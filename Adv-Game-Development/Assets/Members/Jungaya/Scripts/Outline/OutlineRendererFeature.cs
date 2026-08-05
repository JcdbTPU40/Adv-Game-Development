using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Toufuku.Rescue.Outline
{
    /// <summary>
    /// ステンシル＋マスクRT方式のアウトライン Renderer Feature（#45）。
    ///
    /// 構成（3パス）:
    ///   1) OutlineStencilPass … フル解像度のカメラ depth-stencil に内側フラグだけを立てる（ColorMask 0）。
    ///      ZTest LEqual で遮蔽（鳥居・提灯の裏）を従来どおり効かせる。
    ///   2) OutlineMaskPass … 任意解像度のマスクRTへ色＋パターンを書く。深度アタッチメント無し。
    ///      遮蔽は _CameraDepthTexture の手動サンプル＋discard。これで maskResolutionScale&lt;1 が可能。
    ///   3) OutlineComposePass … マスクをダイレートし、Stencil NotEqual で内側を除外して縁だけ合成。
    ///
    /// なぜ色マスクとステンシルを分離したか:
    ///   マスクRTを低解像度にすると RenderGraph がカラーと深度アタッチメントのサイズ一致を要求しエラーになる。
    ///   Docs 4章の本命逃げ道「マスクRT 1/2」を塞がないため、ステンシルはフル解像度のまま残す。
    ///   MRT や追加の R8 存在チャンネルは帯域が増えるので採用しない（A チャンネルに存在フラグを載せている）。
    ///
    /// Native RenderPass について:
    ///   本 Feature はフルスクリーン Compose でカメラの depth-stencil をバインドする。
    ///   Native RenderPass が有効だと depth-stencil のバインドが期待どおりにならないことがあるため、
    ///   PC_Renderer では useNativeRenderPass を OFF にしてある（設定変更のコミット参照）。
    ///
    /// RenderGraph 必須。Compatibility Mode の Execute() は使わない。
    /// </summary>
    public class OutlineRendererFeature : ScriptableRendererFeature
    {
        [Serializable]
        public class Settings
        {
            [Tooltip("OutlineTarget 未設定時の既定色。")]
            public Color defaultColor = Color.white;

            [Tooltip("画面上の輪郭太さ（ピクセル固定）。#44 の ScreenConstant に合わせた既定。")]
            [Range(1f, 12f)]
            public float thicknessPx = 3f;

            [Tooltip("合成時の発光強度。RGB にだけ掛かる（alpha は 0〜1）。")]
            [Range(0f, 4f)]
            public float intensity = 1.4f;

            [Tooltip("マスク描画の対象レイヤー。")]
            public LayerMask layerMask = ~0;

            [Tooltip("マスクRTの解像度スケール。1=フル、0.5=半分（軽量化候補）。ステンシルは常にフル解像度。")]
            [Range(0.25f, 1f)]
            public float maskResolutionScale = 1f;

            [Tooltip("遮蔽判定の深度バイアス（メートル）。低解像度マスク時のエッジ調整用。LinearEyeDepth 空間で比較する。")]
            public float depthBiasEpsilon = 0.03f;

            [Header("雨天減衰（仮パラメータ）")]
            [Tooltip("仮: 雨天時の強度倍率。実機調整前提。")]
            [Range(0.05f, 1f)]
            public float rainIntensityScale = 0.45f;

            [Tooltip("仮: 距離減衰の近端(m)。Mock 帯の遠端付近=12。")]
            public float nearDistance = 12f;

            [Tooltip("仮: 距離減衰の遠端(m)。Mock 帯の近端付近=33。")]
            public float farDistance = 33f;

            [Tooltip("仮: 遠端での強度倍率。")]
            [Range(0.05f, 1f)]
            public float farIntensity = 0.55f;

            [Tooltip("ステンシル専用シェーダ（空なら Toufuku/Outline/Stencil を探す）。")]
            public Shader stencilShader;

            [Tooltip("マスク描画に使うシェーダ（空なら Toufuku/Outline/Mask を探す）。")]
            public Shader maskShader;

            [Tooltip("合成に使うシェーダ（空なら Toufuku/Outline/Compose を探す）。")]
            public Shader composeShader;
        }

        /// <summary>マスクRTを Mask→Compose で共有するためのフレームデータ。</summary>
        public class OutlineFrameData : ContextItem
        {
            public TextureHandle maskTexture;
            public float maskResolutionScale = 1f;

            public override void Reset()
            {
                maskTexture = TextureHandle.nullHandle;
                maskResolutionScale = 1f;
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

        /// <summary>計測HUDなどから Feature の ON/OFF を切り替えるための参照。</summary>
        public static OutlineRendererFeature Instance => s_Instance;

        public Settings CurrentSettings => settings;

        /// <summary>アウトライン描画が有効か。</summary>
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

            // Opaque の後＝カメラ深度が揃った状態でステンシル→マスク→合成。
            _stencilPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            _maskPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            _composePass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;

            // 色マスクは手動深度比較のため Depth テクスチャが必要。Compose も距離減衰で使う。
            _maskPass.ConfigureInput(ScriptableRenderPassInput.Depth);
            _composePass.ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!isActive) return;
            if (renderingData.cameraData.cameraType == CameraType.Preview ||
                renderingData.cameraData.cameraType == CameraType.Reflection)
                return;

            EnsureMaterials();
            if (_stencilMaterial == null || _maskMaterial == null || _composeMaterial == null) return;

            OutlineWeather.RainIntensityScale = settings.rainIntensityScale;
            OutlineWeather.NearDistance = settings.nearDistance;
            OutlineWeather.FarDistance = settings.farDistance;
            OutlineWeather.FarIntensity = settings.farIntensity;
            OutlineWeather.ApplyGlobals();

            _stencilPass.Setup(_stencilMaterial, settings);
            _maskPass.Setup(_maskMaterial, settings);
            _composePass.Setup(_composeMaterial, settings);
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
            {
                var shader = settings.stencilShader != null
                    ? settings.stencilShader
                    : Shader.Find("Toufuku/Outline/Stencil");
                if (shader != null) _stencilMaterial = CoreUtils.CreateEngineMaterial(shader);
            }

            if (_maskMaterial == null)
            {
                var shader = settings.maskShader != null
                    ? settings.maskShader
                    : Shader.Find("Toufuku/Outline/Mask");
                if (shader != null) _maskMaterial = CoreUtils.CreateEngineMaterial(shader);
            }

            if (_composeMaterial == null)
            {
                var shader = settings.composeShader != null
                    ? settings.composeShader
                    : Shader.Find("Toufuku/Outline/Compose");
                if (shader != null) _composeMaterial = CoreUtils.CreateEngineMaterial(shader);
            }
        }

        /// <summary>有効な OutlineTarget を収集する共通処理。</summary>
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

        // ── Stencil Pass ─────────────────────────────────────────

        /// <summary>
        /// フル解像度のカメラ depth-stencil に内側フラグだけを立てる。
        /// カラーアタッチメント無し（ColorMask 0）。遮蔽は ZTest LEqual。
        /// </summary>
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

                    // ステンシル書き込みのため depth-stencil を ReadWrite。
                    // カラーは ColorMask 0 だが、RenderGraph はカラーアタッチメントを要求することがあるため
                    // 既存のカメラカラーをバインドする（書き込まない）。
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

        // ── Mask Pass ────────────────────────────────────────────

        /// <summary>
        /// 任意解像度のマスクRTへ色＋パターンを書く。深度アタッチメント無し。
        /// 遮蔽はシェーダ内で _CameraDepthTexture を比較して discard。
        /// </summary>
        sealed class OutlineMaskPass : ScriptableRenderPass
        {
            static readonly int ColorId = Shader.PropertyToID("_OutlineColor");
            static readonly int PatternId = Shader.PropertyToID("_OutlinePatternId");
            static readonly int DepthBiasId = Shader.PropertyToID("_OutlineDepthBiasEpsilon");
            static readonly int MaskTexelSizeId = Shader.PropertyToID("_OutlineMaskTexelSize");

            static readonly MaterialPropertyBlock s_Mpb = new MaterialPropertyBlock();
            static readonly List<OutlineTarget> s_Scratch = new List<OutlineTarget>(32);

            Material _material;
            Settings _settings;

            class PassData
            {
                public Material material;
                public List<DrawItem> items;
                public float depthBias;
                public Vector4 maskTexelSize;
            }

            public struct DrawItem
            {
                public Renderer renderer;
                public Color color;
                public float patternId;
            }

            public OutlineMaskPass()
            {
                profilingSampler = new ProfilingSampler("OutlineMask");
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

                CollectTargets(_settings, s_Scratch);
                if (s_Scratch.Count == 0) return;

                var camColor = resourceData.activeColorTexture;
                var desc = camColor.IsValid()
                    ? renderGraph.GetTextureDesc(camColor)
                    : new TextureDesc(Screen.width, Screen.height);

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
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "_OutlineMask",
                };
                TextureHandle mask = renderGraph.CreateTexture(maskDesc);

                var outlineData = frameData.GetOrCreate<OutlineFrameData>();
                outlineData.maskTexture = mask;
                outlineData.maskResolutionScale = scale;

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Outline Mask", out var passData, profilingSampler))
                {
                    passData.material = _material;
                    passData.depthBias = _settings.depthBiasEpsilon;
                    // フラグメントで SV_POSITION.xy * texelSize からスクリーンUVを作るために渡す。
                    passData.maskTexelSize = new Vector4(1f / w, 1f / h, w, h);
                    passData.items = new List<DrawItem>(s_Scratch.Count);
                    for (int i = 0; i < s_Scratch.Count; i++)
                    {
                        var t = s_Scratch[i];
                        passData.items.Add(new DrawItem
                        {
                            renderer = t.TargetRenderer,
                            color = t.Color,
                            // OutlinePattern の数値そのもの（0/1/2）。A への +1 エンコードは Mask シェーダ側。
                            patternId = (float)(int)t.Pattern,
                        });
                    }

                    // 深度アタッチメントは付けない → 任意解像度のマスクRTが可能。
                    builder.SetRenderAttachment(mask, 0, AccessFlags.Write);

                    if (resourceData.cameraDepthTexture.IsValid())
                        builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                    {
                        for (int i = 0; i < data.items.Count; i++)
                        {
                            var item = data.items[i];
                            if (item.renderer == null) continue;

                            item.renderer.GetPropertyBlock(s_Mpb);
                            s_Mpb.SetColor(ColorId, item.color);
                            s_Mpb.SetFloat(PatternId, item.patternId);
                            s_Mpb.SetFloat(DepthBiasId, data.depthBias);
                            s_Mpb.SetVector(MaskTexelSizeId, data.maskTexelSize);
                            item.renderer.SetPropertyBlock(s_Mpb);

                            int sub = GetSubMeshCount(item.renderer);
                            for (int s = 0; s < sub; s++)
                                ctx.cmd.DrawRenderer(item.renderer, data.material, s, 0);
                        }
                    });
                }
            }
        }

        // ── Compose Pass ─────────────────────────────────────────

        /// <summary>
        /// マスクRTをダイレートして外周リングを作り、ステンシルで内側を除外してカメラカラーへ合成する。
        /// 太さは画面ピクセル固定（#44 ScreenConstant の経緯に合わせる）。
        /// </summary>
        sealed class OutlineComposePass : ScriptableRenderPass
        {
            static readonly int MaskTexId = Shader.PropertyToID("_OutlineMaskTex");
            static readonly int MaskTexelSizeId = Shader.PropertyToID("_OutlineMaskTexelSize");
            static readonly int ThicknessId = Shader.PropertyToID("_OutlineThicknessPx");
            static readonly int IntensityId = Shader.PropertyToID("_OutlineIntensity");
            static readonly MaterialPropertyBlock s_Mpb = new MaterialPropertyBlock();

            Material _material;
            Settings _settings;

            class PassData
            {
                public Material material;
                public TextureHandle mask;
                public float thicknessInMaskPx;
                public float intensity;
                public Vector4 maskTexelSize;
            }

            public OutlineComposePass()
            {
                profilingSampler = new ProfilingSampler("OutlineCompose");
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
                if (!resourceData.activeColorTexture.IsValid()) return;
                if (resourceData.isActiveTargetBackBuffer) return;

                if (!frameData.Contains<OutlineFrameData>()) return;
                var outlineData = frameData.Get<OutlineFrameData>();
                if (!outlineData.maskTexture.IsValid()) return;

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Outline Compose", out var passData, profilingSampler))
                {
                    passData.material = _material;
                    passData.mask = outlineData.maskTexture;
                    passData.intensity = _settings.intensity;

                    // thicknessPx は「画面上のピクセル」。ダイレート半径はマスクRTのテクセル単位。
                    // maskResolutionScale=0.5 ならマスク1px が画面上≈2px なので、
                    // radius_mask = thicknessPx * scale に換算して見かけの太さを揃える。
                    float scale = Mathf.Clamp(outlineData.maskResolutionScale, 0.25f, 1f);
                    passData.thicknessInMaskPx = Mathf.Max(1f, _settings.thicknessPx * scale);

                    var maskDesc = renderGraph.GetTextureDesc(outlineData.maskTexture);
                    passData.maskTexelSize = new Vector4(
                        1f / Mathf.Max(1, maskDesc.width),
                        1f / Mathf.Max(1, maskDesc.height),
                        maskDesc.width,
                        maskDesc.height);

                    builder.UseTexture(passData.mask, AccessFlags.Read);

                    if (resourceData.cameraDepthTexture.IsValid())
                        builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);

                    builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
                    if (resourceData.activeDepthTexture.IsValid())
                        builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                    {
                        s_Mpb.Clear();
                        s_Mpb.SetTexture(MaskTexId, data.mask);
                        s_Mpb.SetVector(MaskTexelSizeId, data.maskTexelSize);
                        s_Mpb.SetFloat(ThicknessId, data.thicknessInMaskPx);
                        s_Mpb.SetFloat(IntensityId, data.intensity);
                        s_Mpb.SetVector(Shader.PropertyToID("_BlitScaleBias"), new Vector4(1f, 1f, 0f, 0f));

                        ctx.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1, s_Mpb);
                    });
                }
            }
        }
    }
}
