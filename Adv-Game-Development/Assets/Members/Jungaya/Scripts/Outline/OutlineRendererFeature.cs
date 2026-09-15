using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Toufuku.Rescue.Outline
{
    /*
        ステンシル＋マスクRT のやり方で描くアウトラインの Renderer Feature（#45 / #59）

        しくみ（4つのパス）:
          1) OutlineStencilPass: フル解像度のカメラの depth-stencil に、内側のフラグだけを立てる（ColorMask 0）
             ZTest LEqual で、かくれている部分（鳥居や提灯のうしろ）はいつもどおり描かない
          2) OutlineMaskPass: 好きな解像度のマスクRT に「客の番号」を書く（A = (1 + 番号) / 255）。深度のアタッチメントはなし
             かくれているかは _CameraDepthTexture を自分で読んで discard する。これで maskResolutionScale を 1 より小さくできる
          3) Outline Dilate H: マスクを横にだけさがして、いちばん近い客までの横の距離と番号を書く
          4) Outline Compose: 3) をたてにさがして、いちばん近い客までの距離を出す。距離から輪を決めて、
             客ごとの色ともよう（配列）でふちだけをカメラのカラーに合成する。Stencil NotEqual で内側は消す

        #59 で変えたこと:
          ・お守り5種に5つのもよう（実線／点線／破線／波線／二重線。v8 6章）を入れた。色がわからなくても、もようで見分けられる
          ・欲張り客は R=2 のあいだ輪を2本にする（内側が次にほしい2色目）。照準が乗った客は一段明るくする
          ・マスクには色ではなく客の番号を入れて、色・もよう・輪の数・明るさ・画面での楕円は配列で Compose に渡す
            （2色目や、もようをならべる座標がマスクの4チャンネルに入りきらないため）
          ・ダイレートを横→たての2パスにした。もようを見せるために輪を太くしても、サンプル数は (2r+1)² ではなく (2r+1)×2 ですむ
          ・距離はマスクのテクセルから画面ピクセルに直してはかるので、太さは近／中／遠で同じで、
            maskResolutionScale を下げても階段状にならない（#45 の「1/4 だと 4px に張り付く」問題もなくなった）
          ・雨や距離で輪郭を暗くする処理は消した（v8 6章「天候中も輪郭の明度・色・パターンを維持する」）

        なんで色のマスクとステンシルを分けたか:
          マスクRT の解像度を下げると、RenderGraph がカラーと深度のアタッチメントのサイズが同じじゃないとだめと言ってエラーになる
          Docs 4章の本命のにげ道「マスクRT を 1/2 にする」をつぶさないように、ステンシルはフル解像度のままにしている

        Native RenderPass について:
          この Feature はフルスクリーンの Compose でカメラの depth-stencil を使う
          Native RenderPass が有効だと depth-stencil がちゃんとつながらないことがあるので、
          PC_Renderer では useNativeRenderPass を OFF にしてある（設定を変えたコミットを見る）

        RenderGraph が必要。Compatibility Mode の Execute() は使わない
    */
    public class OutlineRendererFeature : ScriptableRendererFeature
    {
        [Serializable]
        public class Settings
        {
            [Header("太さともよう（#59。T1 の視認性で調整する仮の値）")]
            [Tooltip("輪1本の太さ（画面ピクセル）。カメラからの距離に関係なく同じ。もようが見える太さがいる（#45 の 3px では点線と破線を見分けられない）。")]
            [Range(2f, 8f)]
            public float bandWidthPx = 6f;

            [Tooltip("欲張り客の2本の輪のあいだのすきま（画面ピクセル）。")]
            [Range(0f, 4f)]
            public float ringGapPx = 2f;

            [Tooltip("点線の点の間隔（画面ピクセル）。輪のまわりで割り切れる回数にまるめる。")]
            [Range(8f, 48f)]
            public float dotPeriodPx = 16f;

            [Tooltip("破線の1つぶんの長さ（線＋すきま、画面ピクセル）。")]
            [Range(12f, 64f)]
            public float dashPeriodPx = 28f;

            [Tooltip("破線のうち線の部分のわりあい。")]
            [Range(0.3f, 0.85f)]
            public float dashDuty = 0.6f;

            [Tooltip("波線の1波の長さ（画面ピクセル）。")]
            [Range(8f, 64f)]
            public float wavePeriodPx = 20f;

            [Tooltip("遠くて小さい客でも、もようを最低この回数はくり返す。")]
            [Range(1, 16)]
            public int minPatternRepeats = 6;

            [Header("明るさ")]
            [Tooltip("合成時の発光強度。RGB にだけ掛かる（alpha は 0〜1）。天候や距離では変えない（v8 6章・9章）。")]
            [Range(0f, 4f)]
            public float intensity = 1.4f;

            [Tooltip("照準が乗った客の明るさの倍率（v8 3章「一段明るくなる」。ロック表示ではないので形は変えない）。")]
            [Range(1f, 3f)]
            public float highlightBrightness = 1.6f;

            [Header("描画")]
            [Tooltip("マスク描画の対象レイヤー。")]
            public LayerMask layerMask = ~0;

            [Tooltip("マスクRTの解像度スケール。1=フル、0.5=半分（軽量化候補）。ステンシルは常にフル解像度。")]
            [Range(0.25f, 1f)]
            public float maskResolutionScale = 1f;

            [Tooltip("遮蔽判定の深度バイアス（メートル）。低解像度マスク時のエッジ調整用。LinearEyeDepth 空間で比較する。")]
            public float depthBiasEpsilon = 0.03f;

            [Tooltip("ステンシル専用シェーダ（空なら Toufuku/Outline/Stencil を探す）。")]
            public Shader stencilShader;

            [Tooltip("マスク描画に使うシェーダ（空なら Toufuku/Outline/Mask を探す）。")]
            public Shader maskShader;

            [Tooltip("合成に使うシェーダ（空なら Toufuku/Outline/Compose を探す）。")]
            public Shader composeShader;
        }

        // マスクRT を Mask から Compose に渡すためのフレームのデータ
        public class OutlineFrameData : ContextItem
        {
            public TextureHandle maskTexture;
            public float maskResolutionScale = 1f;
            // このフレームで描く客の数（配列の番号 0〜targetCount-1）
            public int targetCount;
            // いちばん多い輪の数。欲張り客がいないフレームはさがす半径を小さくして軽くする
            public int maxRings = 1;

            public override void Reset()
            {
                maskTexture = TextureHandle.nullHandle;
                maskResolutionScale = 1f;
                targetCount = 0;
                maxRings = 1;
            }
        }

        static OutlineRendererFeature s_Instance;

        [SerializeField] Settings settings = new Settings();

        OutlineStencilPass _stencilPass;
        OutlineMaskPass _maskPass;
        OutlineComposePass _composePass;
        Material _stencilMaterial;
        Material _maskMaterial;
        Material _composeMaterial;
        readonly TargetBuffers _buffers = new TargetBuffers();

        // 計測の HUD などから Feature のオンオフを切りかえるための参照
        public static OutlineRendererFeature Instance => s_Instance;

        public Settings CurrentSettings => settings;

        // 輪が rings 本のときに、ダイレートでさがす半径（マスクのテクセル）。計測 HUD の表示用
        public int SearchRadiusMaskTexels(int rings)
        {
            float total = OutlineStyle.TotalWidthPx(rings, settings.bandWidthPx, settings.ringGapPx);
            return OutlineStyle.SearchRadiusMaskTexels(total, settings.maskResolutionScale);
        }

        // アウトラインを描くのが有効かどうか
        public bool OutlineEnabled
        {
            get => isActive;
            set => SetActive(value);
        }

        public override void Create()
        {
            s_Instance = this;

            EnsureMaterials();

            _stencilPass ??= new OutlineStencilPass();
            _maskPass ??= new OutlineMaskPass();
            _composePass ??= new OutlineComposePass();

            // 不透明なものを描いたあと＝カメラの深度がそろった状態で、ステンシル → マスク → 合成 の順にやる
            _stencilPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            _maskPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            _composePass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;

            // 番号のマスクは自分で深度をくらべるので Depth テクスチャがいる（Compose は #59 から深度を使わない）
            _maskPass.ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!isActive) return;
            if (renderingData.cameraData.cameraType == CameraType.Preview ||
                renderingData.cameraData.cameraType == CameraType.Reflection)
                return;

            EnsureMaterials();
            if (_stencilMaterial == null || _maskMaterial == null || _composeMaterial == null) return;

            _stencilPass.Setup(_stencilMaterial, settings);
            _maskPass.Setup(_maskMaterial, settings, _buffers);
            _composePass.Setup(_composeMaterial, settings, _buffers);
            _composePass.requiresIntermediateTexture = true;

            renderer.EnqueuePass(_stencilPass);
            renderer.EnqueuePass(_maskPass);
            renderer.EnqueuePass(_composePass);
        }

        protected override void Dispose(bool disposing)
        {
            if (s_Instance == this) s_Instance = null;

            CoreUtils.Destroy(_stencilMaterial);
            CoreUtils.Destroy(_maskMaterial);
            CoreUtils.Destroy(_composeMaterial);
            _stencilMaterial = null;
            _maskMaterial = null;
            _composeMaterial = null;
            _stencilPass = null;
            _maskPass = null;
            _composePass = null;
        }

        void EnsureMaterials()
        {
            if (_stencilMaterial == null)
                _stencilMaterial = CreateMaterial(settings.stencilShader, "Toufuku/Outline/Stencil");

            if (_maskMaterial == null)
                _maskMaterial = CreateMaterial(settings.maskShader, "Toufuku/Outline/Mask");

            if (_composeMaterial == null)
                _composeMaterial = CreateMaterial(settings.composeShader, "Toufuku/Outline/Compose");
        }

        /*
            シェーダーの参照からマテリアルを作る

            Settings のシェーダーの参照が空だと Shader.Find にたよることになるけど、ビルドでは
            このシェーダーを使っているファイルがほかにないので削られてしまって、Find が null を返して
            アウトラインが「エラーも出ないのに描かれない」状態になる（#45 で実際にこれになった）
            Renderer Feature のインスペクターで3つとも入れておくこと
        */
        Material CreateMaterial(Shader assigned, string fallbackName)
        {
            var shader = assigned != null ? assigned : Shader.Find(fallbackName);
            if (shader == null)
            {
                if (!_shaderWarningLogged)
                {
                    _shaderWarningLogged = true;
                    Debug.LogWarning(
                        $"[Outline] シェーダー '{fallbackName}' を解決できずアウトラインを描画しません。" +
                        "ビルドでのストリップが原因の可能性があります。" +
                        "PC_Renderer の OutlineRendererFeature にシェーダーを割り当ててください。");
                }
                return null;
            }

            return CoreUtils.CreateEngineMaterial(shader);
        }

        bool _shaderWarningLogged;

        // 有効な OutlineTarget を集める、共通の処理
        static void CollectTargets(Settings settings, List<OutlineTarget> dst)
        {
            dst.Clear();
            var active = OutlineTarget.ActiveTargets;
            for (int i = 0; i < active.Count; i++)
            {
                var t = active[i];
                if (t == null || !t.isActiveAndEnabled) continue;
                if (t.TargetRenderer == null || !t.TargetRenderer.enabled) continue;
                if (((1 << t.TargetRenderer.gameObject.layer) & settings.layerMask) == 0) continue;
                dst.Add(t);
            }
        }

        static int GetSubMeshCount(Renderer renderer)
        {
            if (renderer is MeshRenderer mf)
            {
                var filter = mf.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                    return filter.sharedMesh.subMeshCount;
            }
            else if (renderer is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                return smr.sharedMesh.subMeshCount;
            }
            return 1;
        }

        // ---- 客ごとのデータ ----

        /*
            シェーダーに配列で渡す客ごとのデータ。マスクのパスで入れて、Compose のパスで使う
            番号（配列の添え字）はマスクの A に入れた番号と同じ
        */
        sealed class TargetBuffers
        {
            public readonly Vector4[] currentColors = new Vector4[OutlineStyle.MaxTargets];
            public readonly Vector4[] nextColors = new Vector4[OutlineStyle.MaxTargets];
            public readonly Vector4[] styles = new Vector4[OutlineStyle.MaxTargets];
            public readonly Vector4[] ellipses = new Vector4[OutlineStyle.MaxTargets];
            public readonly List<Renderer> renderers = new List<Renderer>(OutlineStyle.MaxTargets);
            public int maxRings = 1;

            static bool s_OverflowLogged;

            public int Count => renderers.Count;

            public void Fill(List<OutlineTarget> source, Settings settings, Camera camera, int width, int height)
            {
                renderers.Clear();
                maxRings = 1;

                int count = source.Count;
                if (count > OutlineStyle.MaxTargets)
                {
                    if (!s_OverflowLogged)
                    {
                        s_OverflowLogged = true;
                        Debug.LogWarning($"[Outline] 輪郭を出す客が {count} 人います。上限 {OutlineStyle.MaxTargets} 人をこえたぶんは描きません。");
                    }
                    count = OutlineStyle.MaxTargets;
                }

                for (int i = 0; i < count; i++)
                {
                    OutlineTarget t = source[i];
                    renderers.Add(t.TargetRenderer);

                    float brightness = t.Highlighted ? settings.highlightBrightness : 1f;
                    currentColors[i] = ToShaderColor(t.Color, brightness);
                    nextColors[i] = ToShaderColor(t.HasNext ? t.NextColor : t.Color, 1f);
                    styles[i] = new Vector4((int)t.Pattern, (int)t.NextPattern, t.RingCount, 0f);
                    ellipses[i] = ScreenEllipse(camera, t.TargetRenderer.bounds, width, height);

                    if (t.RingCount > maxRings) maxRings = t.RingCount;
                }
            }

            // SetVectorArray は色空間を直してくれないので、Linear のときは自分で直す（SetColor と同じ見た目にする）
            static Vector4 ToShaderColor(Color c, float brightness)
            {
                Color v = QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
                return new Vector4(v.r, v.g, v.b, brightness);
            }

            /*
                Renderer の bounds を画面にうつして、楕円（xy=中心、zw=横・たての半径。ピクセル）にする。もようをならべる座標に使う

                y の向き: この Feature はバックバッファには描かない（Compose は isActiveTargetBackBuffer なら何もしない）
                中間テクスチャに描くときは D3D でも投影行列が上下反転しているので、フラグメントの positionCS.y は
                「ビューポートの y（下が 0）× 高さ」になる。OpenGL 系はもともと下が 0 なので同じ式でいい
            */
            static Vector4 ScreenEllipse(Camera camera, Bounds bounds, int width, int height)
            {
                var fallback = new Vector4(width * 0.5f, height * 0.5f, width * 0.5f, height * 0.5f);
                if (camera == null) return fallback;

                Vector3 c = bounds.center;
                Vector3 e = bounds.extents;
                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;
                int inFront = 0;

                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        c.x + ((i & 1) == 0 ? -e.x : e.x),
                        c.y + ((i & 2) == 0 ? -e.y : e.y),
                        c.z + ((i & 4) == 0 ? -e.z : e.z));
                    Vector3 vp = camera.WorldToViewportPoint(corner);
                    if (vp.z <= 0f) continue; // カメラのうしろの角は使わない

                    inFront++;
                    if (vp.x < minX) minX = vp.x;
                    if (vp.y < minY) minY = vp.y;
                    if (vp.x > maxX) maxX = vp.x;
                    if (vp.y > maxY) maxY = vp.y;
                }

                if (inFront == 0) return fallback;

                return new Vector4(
                    (minX + maxX) * 0.5f * width,
                    (minY + maxY) * 0.5f * height,
                    (maxX - minX) * 0.5f * width,
                    (maxY - minY) * 0.5f * height);
            }
        }

        // ---- Stencil Pass ----

        /*
            フル解像度のカメラの depth-stencil に、内側のフラグだけを立てる
            カラーのアタッチメントはなし（ColorMask 0）。かくれているかは ZTest LEqual で見る
        */
        sealed class OutlineStencilPass : ScriptableRenderPass
        {
            static readonly List<OutlineTarget> s_Scratch = new List<OutlineTarget>(32);

            Material _material;
            Settings _settings;

            class PassData
            {
                public Material material;
                public List<Renderer> renderers;
            }

            public OutlineStencilPass()
            {
                profilingSampler = new ProfilingSampler("OutlineStencil");
            }

            public void Setup(Material material, Settings settings)
            {
                _material = material;
                _settings = settings;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_material == null) return;

                var resourceData = frameData.Get<UniversalResourceData>();
                if (!resourceData.activeDepthTexture.IsValid()) return;
                if (!resourceData.activeColorTexture.IsValid()) return;

                CollectTargets(_settings, s_Scratch);
                if (s_Scratch.Count == 0) return;

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Outline Stencil", out var passData, profilingSampler))
                {
                    passData.material = _material;
                    passData.renderers = new List<Renderer>(s_Scratch.Count);
                    for (int i = 0; i < s_Scratch.Count; i++)
                        passData.renderers.Add(s_Scratch[i].TargetRenderer);

                    /*
                        ステンシルに書きこむので depth-stencil は ReadWrite
                        カラーは ColorMask 0 だけど、RenderGraph はカラーのアタッチメントを求めてくることがあるので、
                        今のカメラのカラーをつないでおく（書きこみはしない）
                    */
                    builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                    {
                        for (int i = 0; i < data.renderers.Count; i++)
                        {
                            var r = data.renderers[i];
                            if (r == null) continue;
                            int sub = GetSubMeshCount(r);
                            for (int s = 0; s < sub; s++)
                                ctx.cmd.DrawRenderer(r, data.material, s, 0);
                        }
                    });
                }
            }
        }

        // ---- Mask Pass ----

        /*
            好きな解像度のマスクRT に客の番号を書く。深度のアタッチメントはなし
            かくれているかは、シェーダーの中で _CameraDepthTexture とくらべて discard する
            客ごとのデータ（色・もよう・楕円）もここで TargetBuffers に入れる
        */
        sealed class OutlineMaskPass : ScriptableRenderPass
        {
            static readonly int SlotId = Shader.PropertyToID("_OutlineSlot");
            static readonly int DepthBiasId = Shader.PropertyToID("_OutlineDepthBiasEpsilon");
            static readonly int MaskTexelSizeId = Shader.PropertyToID("_OutlineMaskTexelSize");

            static readonly MaterialPropertyBlock s_Mpb = new MaterialPropertyBlock();
            static readonly List<OutlineTarget> s_Scratch = new List<OutlineTarget>(32);

            Material _material;
            Settings _settings;
            TargetBuffers _buffers;

            class PassData
            {
                public Material material;
                public List<Renderer> renderers;
                public float depthBias;
                public Vector4 maskTexelSize;
            }

            public OutlineMaskPass()
            {
                profilingSampler = new ProfilingSampler("OutlineMask");
            }

            public void Setup(Material material, Settings settings, TargetBuffers buffers)
            {
                _material = material;
                _settings = settings;
                _buffers = buffers;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_material == null) return;

                var resourceData = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();

                CollectTargets(_settings, s_Scratch);
                if (s_Scratch.Count == 0) return;

                var camColor = resourceData.activeColorTexture;
                var desc = camColor.IsValid()
                    ? renderGraph.GetTextureDesc(camColor)
                    : new TextureDesc(Screen.width, Screen.height);

                _buffers.Fill(s_Scratch, _settings, cameraData.camera, desc.width, desc.height);

                float scale = Mathf.Clamp(_settings.maskResolutionScale, 0.25f, 1f);
                int w = Mathf.Max(1, Mathf.RoundToInt(desc.width * scale));
                int h = Mathf.Max(1, Mathf.RoundToInt(desc.height * scale));

                var maskDesc = new TextureDesc(w, h)
                {
                    colorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                    depthBufferBits = DepthBits.None,
                    msaaSamples = MSAASamples.None,
                    clearBuffer = true,
                    clearColor = Color.clear,
                    // 番号をまぜないように Point（Bilinear だととなりの客や 0 とまざって別の番号になる）
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "_OutlineMask",
                };
                TextureHandle mask = renderGraph.CreateTexture(maskDesc);

                var outlineData = frameData.GetOrCreate<OutlineFrameData>();
                outlineData.maskTexture = mask;
                outlineData.maskResolutionScale = scale;
                outlineData.targetCount = _buffers.Count;
                outlineData.maxRings = _buffers.maxRings;

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Outline Mask", out var passData, profilingSampler))
                {
                    passData.material = _material;
                    passData.depthBias = _settings.depthBiasEpsilon;
                    // フラグメントで SV_POSITION.xy * texelSize からスクリーンの UV を作るために渡す
                    passData.maskTexelSize = new Vector4(1f / w, 1f / h, w, h);
                    // 添え字＝番号。TargetBuffers と同じならびを使う
                    passData.renderers = new List<Renderer>(_buffers.renderers);

                    // 深度のアタッチメントは付けない → 好きな解像度のマスクRT にできる
                    builder.SetRenderAttachment(mask, 0, AccessFlags.Write);

                    if (resourceData.cameraDepthTexture.IsValid())
                        builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                    {
                        for (int i = 0; i < data.renderers.Count; i++)
                        {
                            var renderer = data.renderers[i];
                            if (renderer == null) continue;

                            renderer.GetPropertyBlock(s_Mpb);
                            s_Mpb.SetFloat(SlotId, i);
                            s_Mpb.SetFloat(DepthBiasId, data.depthBias);
                            s_Mpb.SetVector(MaskTexelSizeId, data.maskTexelSize);
                            renderer.SetPropertyBlock(s_Mpb);

                            int sub = GetSubMeshCount(renderer);
                            for (int s = 0; s < sub; s++)
                                ctx.cmd.DrawRenderer(renderer, data.material, s, 0);
                        }
                    });
                }
            }
        }

        // ---- Compose Pass ----

        /*
            番号マスクを横→たての2パスでさがして、いちばん近い客までの距離から輪を決めて、カメラのカラーに合成する
            太さともようの長さは画面のピクセルで決める（カメラからの距離に関係なく同じに見える）
        */
        sealed class OutlineComposePass : ScriptableRenderPass
        {
            const int DilateShaderPass = 0;
            const int ComposeShaderPass = 1;

            static readonly int MaskTexId = Shader.PropertyToID("_OutlineMaskTex");
            static readonly int DilateTexId = Shader.PropertyToID("_OutlineDilateTex");
            static readonly int MaskTexelSizeId = Shader.PropertyToID("_OutlineMaskTexelSize");
            static readonly int SearchRadiusId = Shader.PropertyToID("_OutlineSearchRadius");
            static readonly int MaskScaleId = Shader.PropertyToID("_OutlineMaskScale");
            static readonly int IntensityId = Shader.PropertyToID("_OutlineIntensity");
            static readonly int BandPxId = Shader.PropertyToID("_OutlineBandPx");
            static readonly int GapPxId = Shader.PropertyToID("_OutlineGapPx");
            static readonly int DotPeriodId = Shader.PropertyToID("_OutlineDotPeriodPx");
            static readonly int DashPeriodId = Shader.PropertyToID("_OutlineDashPeriodPx");
            static readonly int DashDutyId = Shader.PropertyToID("_OutlineDashDuty");
            static readonly int WavePeriodId = Shader.PropertyToID("_OutlineWavePeriodPx");
            static readonly int MinRepeatsId = Shader.PropertyToID("_OutlineMinRepeats");
            static readonly int CurrentColorsId = Shader.PropertyToID("_OutlineCurrentColors");
            static readonly int NextColorsId = Shader.PropertyToID("_OutlineNextColors");
            static readonly int StylesId = Shader.PropertyToID("_OutlineStyles");
            static readonly int EllipsesId = Shader.PropertyToID("_OutlineEllipses");
            static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");

            static readonly MaterialPropertyBlock s_DilateMpb = new MaterialPropertyBlock();
            static readonly MaterialPropertyBlock s_ComposeMpb = new MaterialPropertyBlock();

            Material _material;
            Settings _settings;
            TargetBuffers _buffers;

            class DilatePassData
            {
                public Material material;
                public TextureHandle mask;
                public Vector4 maskTexelSize;
                public float searchRadius;
            }

            class ComposePassData
            {
                public Material material;
                public TextureHandle dilate;
                public Vector4 maskTexelSize;
                public float searchRadius;
                public float maskScale;
                public float intensity;
                public float bandPx;
                public float gapPx;
                public float dotPeriodPx;
                public float dashPeriodPx;
                public float dashDuty;
                public float wavePeriodPx;
                public float minRepeats;
                public TargetBuffers buffers;
            }

            public OutlineComposePass()
            {
                profilingSampler = new ProfilingSampler("OutlineCompose");
            }

            public void Setup(Material material, Settings settings, TargetBuffers buffers)
            {
                _material = material;
                _settings = settings;
                _buffers = buffers;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_material == null) return;

                var resourceData = frameData.Get<UniversalResourceData>();
                if (!resourceData.activeColorTexture.IsValid()) return;
                if (resourceData.isActiveTargetBackBuffer) return;

                if (!frameData.Contains<OutlineFrameData>()) return;
                var outlineData = frameData.Get<OutlineFrameData>();
                if (!outlineData.maskTexture.IsValid() || outlineData.targetCount == 0) return;

                float scale = Mathf.Clamp(outlineData.maskResolutionScale, 0.25f, 1f);
                float totalPx = OutlineStyle.TotalWidthPx(outlineData.maxRings, _settings.bandWidthPx, _settings.ringGapPx);
                float radius = OutlineStyle.SearchRadiusMaskTexels(totalPx, scale);

                var maskDesc = renderGraph.GetTextureDesc(outlineData.maskTexture);
                var maskTexelSize = new Vector4(
                    1f / Mathf.Max(1, maskDesc.width),
                    1f / Mathf.Max(1, maskDesc.height),
                    maskDesc.width,
                    maskDesc.height);

                // 横にさがした結果（R=横の距離、G=1+番号）。距離も番号も整数なので Half で正確に入る
                var dilateDesc = new TextureDesc(maskDesc.width, maskDesc.height)
                {
                    colorFormat = GraphicsFormat.R16G16_SFloat,
                    depthBufferBits = DepthBits.None,
                    msaaSamples = MSAASamples.None,
                    clearBuffer = false,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "_OutlineDilateH",
                };
                TextureHandle dilate = renderGraph.CreateTexture(dilateDesc);

                using (var builder = renderGraph.AddRasterRenderPass<DilatePassData>("Outline Dilate H", out var passData, profilingSampler))
                {
                    passData.material = _material;
                    passData.mask = outlineData.maskTexture;
                    passData.maskTexelSize = maskTexelSize;
                    passData.searchRadius = radius;

                    builder.UseTexture(passData.mask, AccessFlags.Read);
                    builder.SetRenderAttachment(dilate, 0, AccessFlags.Write);

                    builder.SetRenderFunc(static (DilatePassData data, RasterGraphContext ctx) =>
                    {
                        s_DilateMpb.Clear();
                        s_DilateMpb.SetTexture(MaskTexId, data.mask);
                        s_DilateMpb.SetVector(MaskTexelSizeId, data.maskTexelSize);
                        s_DilateMpb.SetFloat(SearchRadiusId, data.searchRadius);
                        s_DilateMpb.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));

                        ctx.cmd.DrawProcedural(Matrix4x4.identity, data.material, DilateShaderPass, MeshTopology.Triangles, 3, 1, s_DilateMpb);
                    });
                }

                using (var builder = renderGraph.AddRasterRenderPass<ComposePassData>("Outline Compose", out var passData, profilingSampler))
                {
                    passData.material = _material;
                    passData.dilate = dilate;
                    passData.maskTexelSize = maskTexelSize;
                    passData.searchRadius = radius;
                    passData.maskScale = scale;
                    passData.intensity = _settings.intensity;
                    passData.bandPx = _settings.bandWidthPx;
                    passData.gapPx = _settings.ringGapPx;
                    passData.dotPeriodPx = _settings.dotPeriodPx;
                    passData.dashPeriodPx = _settings.dashPeriodPx;
                    passData.dashDuty = _settings.dashDuty;
                    passData.wavePeriodPx = _settings.wavePeriodPx;
                    passData.minRepeats = _settings.minPatternRepeats;
                    passData.buffers = _buffers;

                    builder.UseTexture(dilate, AccessFlags.Read);
                    builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
                    if (resourceData.activeDepthTexture.IsValid())
                        builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);

                    builder.SetRenderFunc(static (ComposePassData data, RasterGraphContext ctx) =>
                    {
                        var mpb = s_ComposeMpb;
                        mpb.Clear();
                        mpb.SetTexture(DilateTexId, data.dilate);
                        mpb.SetVector(MaskTexelSizeId, data.maskTexelSize);
                        mpb.SetFloat(SearchRadiusId, data.searchRadius);
                        mpb.SetFloat(MaskScaleId, data.maskScale);
                        mpb.SetFloat(IntensityId, data.intensity);
                        mpb.SetFloat(BandPxId, data.bandPx);
                        mpb.SetFloat(GapPxId, data.gapPx);
                        mpb.SetFloat(DotPeriodId, data.dotPeriodPx);
                        mpb.SetFloat(DashPeriodId, data.dashPeriodPx);
                        mpb.SetFloat(DashDutyId, data.dashDuty);
                        mpb.SetFloat(WavePeriodId, data.wavePeriodPx);
                        mpb.SetFloat(MinRepeatsId, data.minRepeats);
                        mpb.SetVectorArray(CurrentColorsId, data.buffers.currentColors);
                        mpb.SetVectorArray(NextColorsId, data.buffers.nextColors);
                        mpb.SetVectorArray(StylesId, data.buffers.styles);
                        mpb.SetVectorArray(EllipsesId, data.buffers.ellipses);
                        mpb.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));

                        ctx.cmd.DrawProcedural(Matrix4x4.identity, data.material, ComposeShaderPass, MeshTopology.Triangles, 3, 1, mpb);
                    });
                }
            }
        }
    }
}
