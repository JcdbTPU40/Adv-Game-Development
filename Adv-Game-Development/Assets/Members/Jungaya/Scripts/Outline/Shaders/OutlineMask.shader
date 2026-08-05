// アウトライン用マスクパス（#45）。
//
// 役割:
//   - カラー: RGB=輪郭色 / A=パターンID/255 をマスクRTへ書く
//   - ステンシル: Ref 1 を立てて「オブジェクト内側」をマーク（Compose が NotEqual で除外）
//   - 深度: ZTest LEqual でカメラ深度をテストし、遮蔽物の裏は書かない / ZWrite Off で深度は壊さない
//
// ColorMask について:
//   古典的なステンシルのみ方式では ColorMask 0 だが、本実装はマスクRTへ色を書くため RGBA を出力する。
//   ステンシル書き込みは depth-stencil バインド側で同時に行う。
Shader "Toufuku/Outline/Mask"
{
    Properties
    {
        [HDR] _OutlineColor ("Outline Color", Color) = (1, 1, 1, 1)
        _OutlinePatternId ("Pattern Id (0-1)", Float) = 0
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
            ZTest LEqual
            ColorMask RGBA

            // 内側フラグ。Compose は Comp NotEqual で縁だけ残す。
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

            float4 _OutlineColor;
            float  _OutlinePatternId;

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
                // A にパターンIDを載せる。Solid=0 でも RGB で存在判定するため Compose は max(rgb) を見る。
                return half4(_OutlineColor.rgb, saturate(_OutlinePatternId));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
