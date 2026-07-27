// 視認性モック(#44)専用のアウトラインシェーダ。インバートハル方式。
//
// 仕組み:
//   客メッシュと同じメッシュを、頂点法線方向に _OutlineWidth だけ押し出して描く。
//   Cull Front で「裏面だけ」を描くため、本体に隠されない縁だけが残り輪郭になる。
//   Queue を Geometry+1（本体より後）にしてあるので、本体が先に書いた深度で
//   内側が弾かれ、シルエットのはみ出し分だけが通る。
//
// なぜ Emission/リム発光ではなくこの方式か（案A/案Bの比較結果）:
//   #44 のお題は「輪郭発光5色で客の悩みを識別できるか」。Emission は面全体が光るため
//   (1)そもそも“輪郭”の検証にならない (2)密集時に隣の客と発光が溶け合う
//   (3)Bloom設定に結果が左右され変数が増える、という問題があり、
//   本番アウトライン(#45 ステンシル方式)と見え方が乖離して検証結果を転用できない。
//   インバートハルは「輪郭の太さ」を独立変数として扱えるので #45 の近似として妥当。
//
// 色と太さは MaterialPropertyBlock で客ごとに差し替える想定（マテリアル1枚で全員ぶん）。
// ※ MPB を使うと SRP Batcher からは外れるが、モックの十数体では問題にならない。
Shader "Toufuku/Mock/OutlineHull"
{
    Properties
    {
        [HDR] _OutlineColor ("Outline Color", Color) = (1, 1, 1, 1)
        _OutlineWidth ("Outline Width (World)", Float) = 0.035
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry+1"
        }

        Pass
        {
            Name "MockOutlineHull"
            Tags { "LightMode" = "UniversalForward" }

            Cull Front      // 裏面だけ描く＝輪郭になる
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float  _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                // 法線方向へ押し出す。カプセルは法線が素直なので破綻しない。
                float3 inflated = IN.positionOS.xyz + normalize(IN.normalOS) * _OutlineWidth;
                OUT.positionCS = TransformObjectToHClip(inflated);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return half4(_OutlineColor.rgb, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
