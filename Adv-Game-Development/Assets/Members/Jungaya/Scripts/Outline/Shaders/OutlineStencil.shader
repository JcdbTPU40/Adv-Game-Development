// アウトライン用ステンシルパス（#45）。
//
// 役割は「内側フラグをカメラ depth-stencil に立てることだけ」。
// ColorMask 0 なのでカラー書き込みが無く軽い。フル解像度の depth-stencil にだけ触る。
//
// 遮蔽:
//   ZTest LEqual で Opaque 後のカメラ深度をテストするため、
//   鳥居・提灯の裏に回った面はステンシルにも書かれない。
//
// なぜ色マスクと分離したか:
//   マスクRTを低解像度にすると RenderGraph がカラーと深度のサイズ一致を要求しエラーになる。
//   ステンシルはフル解像度のまま、色マスクは任意解像度、と分けるのが本命の逃げ道。
Shader "Toufuku/Outline/Stencil"
{
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
            Name "OutlineStencil"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite Off
            ZTest LEqual
            ColorMask 0

            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
                Fail Keep
                ZFail Keep
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
