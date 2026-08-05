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
    /// 構成:
    ///   1) OutlineMaskPass … 登録済み OutlineTarget をオーバーライド材でマスクRTへ描く。
    ///      ZTest LEqual でカメラ深度をテスト → 鳥居・提灯の裏はマスクに書かれない。
    ///      同時にカメラ depth-stencil へ Stencil Ref=1 を立てて「内側」をマークする。
    ///   2) OutlineComposePass … マスクを N px ダイレートし、Stencil NotEqual で内側を除外して縁だけ合成。
    ///
    /// なぜステンシル＋マスクか:
    ///   #44 のインバートハルは検証用として妥当だったが、本番ではドローコール2倍と
    ///   メッシュ依存の太さ制御が展示す規模（16体）で不利。フルスクリーンの縁取りなら
    ///   太さを画面ピクセル固定にでき、#44 で採用した ScreenConstant の見え方を踏襲できる。
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

            [Tooltip("合成時の発光強度。")]
            [Range(0f, 4f)]
            public float intensity = 1.4f;

            [Tooltip("マスク描画の対象レイヤー。")]
            public LayerMask layerMask = ~0;

            [Tooltip("マスクRTの解像度スケール。1=フル、0.5=半分（軽量化候補）。")]
            [Range(0.25f, 1f)]
            public float maskResolutionScale = 1f;

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

            [Tooltip("マスク描画に使うシェーダ（空なら Toufuku/Outline/Mask を探す）。")]
            public Shader maskShader;

            [Tooltip("合成に使うシェーダ（空なら Toufuku/Outline/Compose を探す）。")]
            public Shader composeShader;
        }

        /// <summary>マスクRTを Mask→Compose で共有するためのフレームデータ。</summary>
        public class OutlineFrameData : ContextItem
        {
            public TextureHandle maskTexture;

            public override void Reset()
            {
                maskTexture = TextureHandle.nullHandle;
            }
        }

        static OutlineRendererFeature s_Instance;

        [SerializeField] Settings settings = new Settings();

        OutlineMaskPass _maskPass;
        OutlineComposePass _composePass;
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

            _maskPass ??= new OutlineMaskPass();
            _composePass ??= new OutlineComposePass();

            // Opaque の後＝カメラ深度が揃った状態でマスクを書き、その直後に縁を合成する。
            _maskPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            _composePass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            _composePass.ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!isActive) return;
            if (renderingData.cameraData.cameraType == CameraType.Preview ||
                renderingData.cameraData.cameraType == CameraType.Reflection)
                return;

            EnsureMaterials();
            if (_maskMaterial == null || _composeMaterial == null) return;

            // 雨天・距離減衰の仮パラメータを Feature 設定から Weather へ同期してからグローバルへ流す。
            OutlineWeather.RainIntensityScale = settings.rainIntensityScale;
            OutlineWeather.NearDistance = settings.nearDistance;
            OutlineWeather.FarDistance = settings.farDistance;
            OutlineWeather.FarIntensity = settings.farIntensity;
            OutlineWeather.ApplyGlobals();

            _maskPass.Setup(_maskMaterial, settings);
            _composePass.Setup(_composeMaterial, settings);
            // Compose は depth-stencil を読むため中間カラーが必要。
            _composePass.requiresIntermediateTexture = true;

            renderer.EnqueuePass(_maskPass);
            renderer.EnqueuePass(_composePass);
        }

        protected override void Dispose(bool disposing)
        {
            if (s_Instance == this) s_Instance = null;

            CoreUtils.Destroy(_maskMaterial);
            CoreUtils.Destroy(_composeMaterial);
            _maskMaterial = null;
            _composeMaterial = null;
            _maskPass = null;
            _composePass = null;
        }

        void EnsureMaterials()
        {
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

        // ── Mask Pass ────────────────────────────────────────────

        /// <summary>
        /// 登録済みレンダラをマスクRTへ描き、ステンシルで内側をマークする。
        ///
        /// 遮蔽の仕組み:
        ///   SetRenderAttachmentDepth でカメラの depth-stencil をバインドし、
        ///   シェーダ側 ZTest LEqual / ZWrite Off。Opaque 描画後の深度に対してテストするため、
        ///   鳥居・提灯など手前の遮蔽物の裏に回った面は深度テストで落ち、マスクにもステンシルにも書かれない。
        ///   → 遮蔽越しに輪郭が透けて見えない。
        ///
        /// ステンシル:
        ///   Ref 1 / Comp Always / Pass Replace。内側ピクセルにフラグを立てる。
        ///   Compose 側は Comp NotEqual で内側を除外し、ダイレートした外周リングだけを残す。
        /// </summary>
        sealed class OutlineMaskPass : ScriptableRenderPass
        {
            static readonly int ColorId = Shader.PropertyToID("_OutlineColor");
            static readonly int PatternId = Shader.PropertyToID("_OutlinePatternId");

            static readonly MaterialPropertyBlock s_Mpb = new MaterialPropertyBlock();
            static readonly List<OutlineTarget> s_Scratch = new List<OutlineTarget>(32);

            Material _material;
            Settings _settings;

            class PassData
            {
                public Material material;
                public List<DrawItem> items;
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
                if (!resourceData.activeDepthTexture.IsValid()) return;

                // 有効なターゲットをスナップショット（Execute 中にリストが変わらないように）。
                s_Scratch.Clear();
                var active = OutlineTarget.ActiveTargets;
                for (int i = 0; i < active.Count; i++)
                {
                    var t = active[i];
                    if (t == null || !t.isActiveAndEnabled) continue;
                    if (t.TargetRenderer == null || !t.TargetRenderer.enabled) continue;
                    if (((1 << t.TargetRenderer.gameObject.layer) & _settings.layerMask) == 0) continue;
                    s_Scratch.Add(t);
                }

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

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Outline Mask", out var passData, profilingSampler))
                {
                    passData.material = _material;
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

                    builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                    // カメラ深度でテストしつつステンシルへ内側フラグを書く。深度値自体は書き換えない（シェーダ ZWrite Off）。
                    builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                    {
                        for (int i = 0; i < data.items.Count; i++)
                        {
                            var item = data.items[i];
                            if (item.renderer == null) continue;

                            // 本体の MPB（_BaseColor 等）を壊さないよう Get→追記→Set。
                            item.renderer.GetPropertyBlock(s_Mpb);
                            s_Mpb.SetColor(ColorId, item.color);
                            s_Mpb.SetFloat(PatternId, item.patternId);
                            item.renderer.SetPropertyBlock(s_Mpb);

                            int subMeshCount = 1;
                            var mf = item.renderer as MeshRenderer;
                            if (mf != null)
                            {
                                var filter = mf.GetComponent<MeshFilter>();
                                if (filter != null && filter.sharedMesh != null)
                                    subMeshCount = filter.sharedMesh.subMeshCount;
                            }
                            else if (item.renderer is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                            {
                                subMeshCount = smr.sharedMesh.subMeshCount;
                            }

                            for (int s = 0; s < subMeshCount; s++)
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
                public float thicknessPx;
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
                    passData.thicknessPx = _settings.thicknessPx;
                    passData.intensity = _settings.intensity;

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
                    // ステンシルテストで内側を除外するため depth-stencil をバインドする。
                    if (resourceData.activeDepthTexture.IsValid())
                        builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                    {
                        s_Mpb.Clear();
                        s_Mpb.SetTexture(MaskTexId, data.mask);
                        s_Mpb.SetVector(MaskTexelSizeId, data.maskTexelSize);
                        s_Mpb.SetFloat(ThicknessId, data.thicknessPx);
                        s_Mpb.SetFloat(IntensityId, data.intensity);
                        // Core Blit.hlsl 互換。
                        s_Mpb.SetVector(Shader.PropertyToID("_BlitScaleBias"), new Vector4(1f, 1f, 0f, 0f));

                        ctx.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1, s_Mpb);
                    });
                }
            }
        }
    }
}
