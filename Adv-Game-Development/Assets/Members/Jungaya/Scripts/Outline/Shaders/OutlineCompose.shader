// アウトライン合成パス（#45）。
//
// マスクRTを N px ダイレート（最大値フィルタ）して外周リングを作り、
// Stencil NotEqual(Ref 1) でオブジェクト内側を除外してカメラカラーへ合成する。
// 太さは画面ピクセル固定（#44 で ScreenConstant を採用した経緯に合わせる）。
//
// マスク A の意味: (1 + patternId) / 255。0=無し、それ以外=存在。暗い色でも検出できる。
Shader "Toufuku/Outline/Compose"
{
    Properties
    {
        _OutlineThicknessPx ("Thickness (px)", Float) = 3
        _OutlineIntensity ("Intensity", Float) = 1.4
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "OutlineCompose"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest Always
            Blend One OneMinusSrcAlpha

            // マスクで立てた内側ステンシルを除外 → ダイレート外周の「縁」だけが残る。
            Stencil
            {
                Ref 1
                Comp NotEqual
                Pass Keep
                Fail Keep
                ZFail Keep
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D_X(_OutlineMaskTex);
            SAMPLER(sampler_OutlineMaskTex);

            float4 _OutlineMaskTexelSize; // xy = 1/w,1/h  zw = w,h
            float  _OutlineThicknessPx;
            float  _OutlineIntensity;

            // OutlineWeather がセットする仮パラメータ。
            float _OutlineRainAmount;
            float _OutlineRainIntensityScale;
            float _OutlineNearDistance;
            float _OutlineFarDistance;
            float _OutlineFarIntensity;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                // フルスクリーン三角形（Blitter と同じ）。
                OUT.positionCS = GetFullScreenTriangleVertexPosition(IN.vertexID);
                OUT.uv = GetFullScreenTriangleTexCoord(IN.vertexID);
                return OUT;
            }

            // 色覚対応の拡張点。現状は Solid のみ。
            half ApplyPattern(int patternId, float2 screenUV, half ringMask)
            {
                // TODO(#色覚対応): Dashed / Wavy をここに実装
                // patternId: 0=Solid, 1=Dashed, 2=Wavy（enum 値。A エンコードの +1 は既に剥が済み）
                return ringMask;
            }

            half4 SampleMask(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_OutlineMaskTex, sampler_OutlineMaskTex, uv);
            }

            // 半径 thicknessPx のボックス最大値フィルタでダイレートする。
            // 存在判定は A>0（色の明るさではない）。暗い輪郭色でも同等に検出する。
            // 近傍に複数の客がいる場合は「中心に近いサンプルを優先」。
            //   理由: 先勝ちだと走査順（左上→右下）に依存して縁の色が偏るため。
            //   距離が同じなら先に見つかった方を維持（安定）。
            void DilateMask(float2 uv, out half3 color, out half coverage, out int patternId)
            {
                int radius = (int)max(1.0, round(_OutlineThicknessPx));
                radius = min(radius, 8); // 暴走防止の上限

                half3 bestColor = 0;
                half bestCov = 0;
                int bestPat = 0;
                // 未選択を示すため負の距離。見つかったら dist² を入れる。
                float bestDist2 = -1.0;

                [loop]
                for (int y = -radius; y <= radius; y++)
                {
                    [loop]
                    for (int x = -radius; x <= radius; x++)
                    {
                        float2 offset = float2(x, y) * _OutlineMaskTexelSize.xy;
                        half4 s = SampleMask(uv + offset);
                        // A>0 が存在フラグ。RGB の明るさは見ない。
                        if (s.a <= 0.0h)
                            continue;

                        float dist2 = (float)(x * x + y * y);
                        // 初回、またはより近いサンプルだけ採用。
                        if (bestDist2 < 0.0 || dist2 < bestDist2)
                        {
                            bestDist2 = dist2;
                            bestColor = s.rgb;
                            bestCov = 1.0h;
                            // A = (1 + patternId) / 255 → patternId = round(A*255) - 1
                            bestPat = (int)round((float)s.a * 255.0) - 1;
                        }
                    }
                }

                color = bestColor;
                coverage = bestCov;
                patternId = bestPat;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                half3 dilateColor;
                half coverage;
                int patternId;
                DilateMask(IN.uv, dilateColor, coverage, patternId);

                // 元マスクにも値がある＝内側。ステンシルでも弾くが、解像度スケール時の保険。
                half4 center = SampleMask(IN.uv);
                half centerCov = center.a > 0.0h ? 1.0h : 0.0h;
                half ring = saturate(coverage - centerCov);

                ring = ApplyPattern(patternId, IN.uv, ring);

                if (ring <= 0.001h)
                    return half4(0, 0, 0, 0);

                // 距離減衰（仮）。カメラ深度から視線距離を取る。
                float rawDepth = SampleSceneDepth(IN.uv);
                float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float distT = saturate(
                    (eyeDepth - _OutlineNearDistance) /
                    max(0.001, _OutlineFarDistance - _OutlineNearDistance));
                float distScale = lerp(1.0, _OutlineFarIntensity, distT);

                // 雨天減衰（仮）。
                float rainScale = lerp(1.0, _OutlineRainIntensityScale, saturate(_OutlineRainAmount));

                half intensity = (half)(_OutlineIntensity * distScale * rainScale);
                half3 rgb = dilateColor * intensity * ring;
                return half4(rgb, ring * intensity);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
