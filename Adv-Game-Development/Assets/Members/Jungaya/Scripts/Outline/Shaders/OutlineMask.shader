// アウトライン用マスクパス（#45）。
//
// 役割:
//   - カラー: RGB=輪郭色 / A=存在フラグ＋パターンID の合成値 をマスクRTへ書く
//   - ステンシル: Ref 1 を立てて「オブジェクト内側」をマーク（Compose が NotEqual で除外）
//   - 深度: ZTest LEqual でカメラ深度をテストし、遮蔽物の裏は書かない / ZWrite Off で深度は壊さない
//
// A チャンネルのエンコード:
//   A = (1 + patternId) / 255
//   patternId は OutlinePattern の数値そのもの（Solid=0, Dashed=1, Wavy=2）。
//   → 0 = マスク無し、1/255 = Solid、2/255 = Dashed、3/255 = Wavy。
//   暗い輪郭色でも max(rgb) に頼らず A>0 で存在判定できる（#44 黒客対策）。
//   enum 値は変えず、+1 オフセットはシェーダ載せ時のエンコード専用。
//
// ColorMask について:
//   古典的なステンシルのみ方式では ColorMask 0 だが、本実装はマスクRTへ色を書くため RGBA を出力する。
//   ステンシル書き込みは depth-stencil バインド側で同時に行う。
Shader "Toufuku/Outline/Mask"
{
    Properties
    {
        [HDR] _OutlineColor ("Outline Color", Color) = (1, 1, 1, 1)
        // OutlinePattern の数値（0/1/2）。A へ載せるときはシェーダ内で +1 エンコードする。
        _OutlinePatternId ("Pattern Id (enum 0-2)", Float) = 0
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
            float  _OutlinePatternId; // OutlinePattern enum 値（0/1/2）

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
                // 存在フラグ付きエンコード。Compose は A>0 で存在、round(A*255)-1 で patternId を復元。
                half a = (1.0h + (half)_OutlinePatternId) / 255.0h;
                return half4(_OutlineColor.rgb, a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
