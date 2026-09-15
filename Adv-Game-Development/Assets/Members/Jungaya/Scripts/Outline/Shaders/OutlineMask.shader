// アウトライン用の番号マスクパス（#45 / #59）。
//
// 役割:
//   - カラー: A = (1 + 客の番号) / 255 をマスクRTへ書く。RGB は使わない（0）
//     色・もよう・輪の数は番号で配列からひく（OutlineRendererFeature が Compose に渡す）
//   - 深度アタッチメントは持たない（任意解像度のマスクRTにするため）
//   - 遮蔽は _CameraDepthTexture を手動サンプルして discard で行う
//
// 番号にしたのは #59 から。#45 は RGB=色 / A=もようだったが、欲張り客の2色目や
// もようをならべる座標（画面での楕円）がマスクの4チャンネルに入りきらないため。
// A>0 を存在フラグにするのは #45 と同じ（暗い輪郭色でも max(rgb) に頼らない。#44 黒客対策）。
//
// ステンシル書き込みは OutlineStencil パスが担当。ここには Stencil ブロックを置かない。
Shader "Toufuku/Outline/Mask"
{
    Properties
    {
        // OutlineTarget の番号（0〜OutlineStyle.MaxTargets-1）。A へ載せるときはシェーダ内で +1 する。
        _OutlineSlot ("Target Slot", Float) = 0
        _OutlineDepthBiasEpsilon ("Depth Bias (meters)", Float) = 0.03
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry+50"
        }

        Pass
        {
            Name "OutlineMask"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite Off
            // 深度アタッチメントが無いのでハードウェア ZTest は使えない。常に通し、frag で手動判定。
            ZTest Always
            ColorMask RGBA

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float  _OutlineSlot;
            float  _OutlineDepthBiasEpsilon;
            // Mask RT のテクセルサイズ。xy = 1/width, 1/height（Compose と同名プロパティ）。
            float4 _OutlineMaskTexelSize;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                // screenUV はフラグメントの SV_POSITION から作る。
                // （頂点で w 除算済み UV を補間するとパースペクティブ補正が二重になり誤る）
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // フラグメントの SV_POSITION.xy は現在のレンダーターゲット（マスクRT）のピクセル座標。
                // マスクRT とカメラ深度テクスチャは同じビューポートを覆うので、
                // それぞれの解像度で正規化すれば同一の [0,1] UV になる。
                // → 低解像度マスクでも _CameraDepthTexture を正しくサンプルできる。
                float2 screenUV = IN.positionCS.xy * _OutlineMaskTexelSize.xy;
                // GetNormalizedScreenSpaceUV と同じ Y 補正。DirectX 系で UV 原点が上のとき深度テクスチャと揃える。
                // （不要にすると遮蔽境界が上下反転してずれるため残す）
                TransformNormalizedScreenUV(screenUV);

                // スクリーンUVでカメラ深度を取り、自分が奥なら discard（遮蔽）。
                // バイアスはメートル単位（Settings.depthBiasEpsilon）。raw depth は非線形なので使わない。
                float sceneRaw = SampleSceneDepth(screenUV);
                float sceneEye = LinearEyeDepth(sceneRaw, _ZBufferParams);
                float fragEye = LinearEyeDepth(IN.positionCS.z, _ZBufferParams);

                if (fragEye > sceneEye + _OutlineDepthBiasEpsilon)
                    discard;

                // OutlineStyle.EncodeSlot と同じ。Compose 側は round(A*255)-1 で番号にもどす。
                // 番号は点サンプリングで読むので、マスクRT は Point フィルタにしてある（まざると別の客の番号になる）。
                half a = (1.0h + (half)_OutlineSlot) / 255.0h;
                return half4(0, 0, 0, a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
