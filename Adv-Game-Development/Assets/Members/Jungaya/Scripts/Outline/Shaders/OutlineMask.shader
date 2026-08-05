// アウトライン用色マスクパス（#45）。
//
// 役割:
//   - カラー: RGB=輪郭色 / A=存在フラグ＋パターンID の合成値 をマスクRTへ書く
//   - 深度アタッチメントは持たない（任意解像度のマスクRTにするため）
//   - 遮蔽は _CameraDepthTexture を手動サンプルして discard で行う
//
// A チャンネルのエンコード:
//   A = (1 + patternId) / 255
//   patternId は OutlinePattern の数値そのもの（Solid=0, Dashed=1, Wavy=2）。
//   → 0 = マスク無し、1/255 = Solid、2/255 = Dashed、3/255 = Wavy。
//   暗い輪郭色でも max(rgb) に頼らず A>0 で存在判定できる（#44 黒客対策）。
//
// ステンシル書き込みは OutlineStencil パスが担当。ここには Stencil ブロックを置かない。
Shader "Toufuku/Outline/Mask"
{
    Properties
    {
        [HDR] _OutlineColor ("Outline Color", Color) = (1, 1, 1, 1)
        // OutlinePattern の数値（0/1/2）。A へ載せるときはシェーダ内で +1 エンコードする。
        _OutlinePatternId ("Pattern Id (enum 0-2)", Float) = 0
        _OutlineDepthBiasEpsilon ("Depth Bias", Float) = 0.0001
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

            float4 _OutlineColor;
            float  _OutlinePatternId; // OutlinePattern enum 値（0/1/2）
            float  _OutlineDepthBiasEpsilon;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                // マスクRTが低解像度でも、カメラ深度はフル解像度なので
                // SV_POSITION（RTピクセル座標）からUVを取るとずれる。
                // クリップ空間→NDC からスクリーンUVを作る。
                float4 ndc = OUT.positionCS * rcp(OUT.positionCS.w);
                float2 uv = ndc.xy * 0.5 + 0.5;
            #if UNITY_UV_STARTS_AT_TOP
                if (_ProjectionParams.x < 0)
                    uv.y = 1.0 - uv.y;
            #endif
                OUT.screenUV = uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // スクリーンUVでカメラ深度を取り、自分が奥なら discard（遮蔽）。
                // 低解像度マスク時は深度テクスチャとの解像度差でエッジが1〜2pxずれることがある。
                // _OutlineDepthBiasEpsilon で調整（Settings.depthBiasEpsilon）。
                float sceneRaw = SampleSceneDepth(IN.screenUV);
                float fragRaw = IN.positionCS.z;

#if UNITY_REVERSED_Z
                // Reversed-Z: 値が大きいほど手前。frag が scene より小さい＝奥 → 捨てる。
                if (fragRaw < sceneRaw - _OutlineDepthBiasEpsilon)
                    discard;
#else
                if (fragRaw > sceneRaw + _OutlineDepthBiasEpsilon)
                    discard;
#endif

                // 存在フラグ付きエンコード。Compose は A>0 で存在、round(A*255)-1 で patternId を復元。
                half a = (1.0h + (half)_OutlinePatternId) / 255.0h;
                return half4(_OutlineColor.rgb, a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
