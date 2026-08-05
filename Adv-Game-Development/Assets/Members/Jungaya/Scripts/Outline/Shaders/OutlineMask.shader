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

            float4 _OutlineColor;
            float  _OutlinePatternId; // OutlinePattern enum 値（0/1/2）
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
                //
                // 代替案の ComputeScreenPos(positionCS) を float4 で渡して /w する方式も正しいが、
                // SV_POSITION は既にラスタライズ後のピクセル座標なので補間誤差が無く、
                // マスクRTサイズが分かっている今はこちらが単純。
                float2 screenUV = IN.positionCS.xy * _OutlineMaskTexelSize.xy;
                // GetNormalizedScreenSpaceUV と同じ Y 補正。DirectX 系で UV 原点が上のとき深度テクスチャと揃える。
                // （不要にすると遮蔽境界が上下反転してずれるため残す）
                TransformNormalizedScreenUV(screenUV);

                // スクリーンUVでカメラ深度を取り、自分が奥なら discard（遮蔽）。
                // 低解像度マスク時は深度テクスチャとの解像度差でエッジが1〜2pxずれることがある。
                // バイアスはメートル単位（Settings.depthBiasEpsilon）。raw depth は非線形なので使わない。
                float sceneRaw = SampleSceneDepth(screenUV);
                float sceneEye = LinearEyeDepth(sceneRaw, _ZBufferParams);
                // SV_POSITION.z はクリップ空間 Z。LinearEyeDepth は raw/デバイス深度を想定するため、
                // フラグメント深度も同じ経路で線形化する（positionCS.z は既に w 除算後の NDC/デバイス深度相当）。
                float fragEye = LinearEyeDepth(IN.positionCS.z, _ZBufferParams);

                // 視線距離で比較。frag が scene より奥（値が大きい）なら遮蔽されて捨てる。
                if (fragEye > sceneEye + _OutlineDepthBiasEpsilon)
                    discard;

                // 存在フラグ付きエンコード。Compose は A>0 で存在、round(A*255)-1 で patternId を復元。
                half a = (1.0h + (half)_OutlinePatternId) / 255.0h;
                return half4(_OutlineColor.rgb, a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
