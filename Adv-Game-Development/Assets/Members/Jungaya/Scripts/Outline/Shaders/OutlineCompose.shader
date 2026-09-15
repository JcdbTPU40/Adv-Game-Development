// アウトライン合成（#45 / #59）。
//
// 2つのパス:
//   Pass 0 "OutlineDilateH" : 番号マスク（A = (1 + 番号) / 255）を横にだけさがして、
//                              いちばん近い客までの横の距離(R)と、その客の 1+番号(G) を書く（マスクと同じ解像度）
//   Pass 1 "OutlineCompose" : Pass 0 の結果をたてにさがして、いちばん近い客までの距離を出す。
//                              距離から「どの輪か」を決めて、客ごとの色ともよう（配列）でふちだけをカメラのカラーに合成する。
//                              Stencil NotEqual(Ref 1) でオブジェクトの内側は消す。
//
// 横→たての2パスに分けた理由（#59）:
//   もようを見せるには #45 の 3px より太い輪がいる。1パスの (2r+1)² だと、欲張り客の2本の輪（半径15）で 961 サンプルになる。
//   距離の2乗は dx² + dy² に分けられるので、「横でいちばん近い点」を先に取っておけば、たての最小をとるだけで正しい距離になる。
//   サンプル数は (2r+1)×2 = 62 で、#45 の半径3（49 サンプル）とほぼ同じ重さ。
//
// 客ごとのデータ（番号でひく配列。長さは OutlineStyle.MaxTargets と同じ）:
//   _OutlineCurrentColors[i] : rgb = 今ほしい色（linear）  a = 明るさの倍率（照準が乗ると上がる）
//   _OutlineNextColors[i]    : rgb = 次にほしい色（欲張り客の内側の輪）
//   _OutlineStyles[i]        : x = 今のもよう  y = 次のもよう  z = 輪の数(1/2)   （OutlinePattern の数字）
//   _OutlineEllipses[i]      : xy = 画面での中心(px、positionCS と同じ向き)  zw = 横・たての半径(px)
//
// 天候や距離では明るさを変えない（企画書 v8 6章「天候中も輪郭の明度・色・パターンを維持する」）。
// 式は OutlineStyle.cs と同じ。片方を変えたらもう片方も直すこと。
Shader "Toufuku/Outline/Compose"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        #define OUTLINE_MAX_TARGETS 64
        #define OUTLINE_MAX_SEARCH 16

        // 番号をまぜないように点サンプリング（名前に point と clamp が入っているとインラインのサンプラーになる）
        SAMPLER(outline_point_clamp_sampler);

        float4 _OutlineMaskTexelSize; // xy = 1/w,1/h  zw = w,h（マスクRT）
        float  _OutlineSearchRadius;  // さがす半径（マスクのテクセル）

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
        ENDHLSL

        Pass
        {
            Name "OutlineDilateH"

            Cull Off
            ZWrite Off
            ZTest Always
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDilateH

            TEXTURE2D_X(_OutlineMaskTex);

            float4 FragDilateH(Varyings IN) : SV_Target
            {
                int radius = clamp((int)_OutlineSearchRadius, 1, OUTLINE_MAX_SEARCH);

                float bestAbs = 1e5;
                float bestSlot = 0; // 0 = 近くにだれもいない。それ以外は 1 + 番号

                [loop]
                for (int x = -radius; x <= radius; x++)
                {
                    float a = SAMPLE_TEXTURE2D_X_LOD(_OutlineMaskTex, outline_point_clamp_sampler,
                        IN.uv + float2(x, 0) * _OutlineMaskTexelSize.xy, 0).a;
                    // A>0 が存在フラグ。暗い輪郭色でも同じように見つかる。
                    if (a <= 0.0)
                        continue;

                    float ax = abs((float)x);
                    // 同じ距離なら先に見つけたほうを残す（安定）
                    if (ax < bestAbs)
                    {
                        bestAbs = ax;
                        bestSlot = round(a * 255.0);
                    }
                }

                return float4(bestSlot > 0.5 ? bestAbs : 0.0, bestSlot, 0, 0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "OutlineCompose"

            Cull Off
            ZWrite Off
            ZTest Always
            // プリマルチプライド: rgb は alpha を掛け済み、alpha は 0〜1。
            Blend One OneMinusSrcAlpha

            // ステンシルで立てた内側を除外 → ダイレート外周の「縁」だけが残る。
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
            #pragma fragment FragCompose

            TEXTURE2D_X(_OutlineDilateTex);

            float _OutlineMaskScale;      // マスクRT の解像度スケール（テクセル → 画面px の換算）
            float _OutlineIntensity;
            float _OutlineBandPx;         // 輪1本の太さ（画面px）
            float _OutlineGapPx;          // 2本の輪のすきま（画面px）
            float _OutlineDotPeriodPx;
            float _OutlineDashPeriodPx;
            float _OutlineDashDuty;
            float _OutlineWavePeriodPx;
            float _OutlineMinRepeats;

            float4 _OutlineCurrentColors[OUTLINE_MAX_TARGETS];
            float4 _OutlineNextColors[OUTLINE_MAX_TARGETS];
            float4 _OutlineStyles[OUTLINE_MAX_TARGETS];
            float4 _OutlineEllipses[OUTLINE_MAX_TARGETS];

            // OutlineStyle.EllipseArc01 と同じ。楕円のまわりの位置を 0〜1 にする（弧の長さでならす）
            float EllipseArc01(float2 p, float a, float b)
            {
                float t = atan2(p.y / b, p.x / a) + PI;
                float A = (a * a + b * b) * 0.5;
                float B = (b * b - a * a) * 0.5;
                return (t + B / (4.0 * A) * sin(2.0 * t)) / TWO_PI;
            }

            // OutlineStyle.EllipsePerimeter と同じ
            float EllipsePerimeter(float a, float b)
            {
                return PI * (3.0 * (a + b) - sqrt((3.0 * a + b) * (a + 3.0 * b)));
            }

            // OutlineStyle.RepeatCount と同じ
            float Repeats(float perimeter, float period)
            {
                return max(max(_OutlineMinRepeats, 1.0), round(perimeter / max(period, 1.0)));
            }

            // 符号つき距離（中が +、画面px）から 0〜1 のおおい方にする（ふち 1px のアンチエイリアス）
            float Cover(float signedPx)
            {
                return saturate(signedPx + 0.5);
            }

            /*
                輪1本ぶんのもようのおおい方（0〜1）
                  local     : 輪の内側のふちからの距離(px)
                  arc01     : 輪のまわりの位置 0〜1
                  perimeter : この輪のまわりの長さ(px)
                点や線の長さは画面px で決めるので、近／中／遠の客で同じ大きさに見える
            */
            float PatternCoverage(int pattern, float local, float arc01, float perimeter)
            {
                float w = _OutlineBandPx;
                float across = Cover(local + 0.5) * Cover(w - local);

                if (pattern == 1) // 点線: 輪のはばと同じ直径の丸
                {
                    float n = Repeats(perimeter, _OutlineDotPeriodPx);
                    float along = (frac(arc01 * n) - 0.5) * (perimeter / n);
                    float r = w * 0.5;
                    return Cover(r - length(float2(along, local - r)));
                }

                if (pattern == 2) // 破線: 1つぶんの dashDuty だけ線
                {
                    float n = Repeats(perimeter, _OutlineDashPeriodPx);
                    float period = perimeter / n;
                    float along = abs(frac(arc01 * n) - 0.5) * period;
                    return across * Cover(period * _OutlineDashDuty * 0.5 - along);
                }

                if (pattern == 3) // 波線: 輪のはばの中で上下にゆれる線
                {
                    float n = Repeats(perimeter, _OutlineWavePeriodPx);
                    float lineW = max(1.5, w * 0.4);
                    float amp = (w - lineW) * 0.5;
                    float center = w * 0.5 + amp * sin(TWO_PI * arc01 * n);
                    return Cover(lineW * 0.5 - abs(local - center));
                }

                if (pattern == 4) // 二重線: 輪のはばの両はしに細い線
                {
                    float lineW = max(1.0, w * 0.3);
                    float inner = Cover(lineW - local) * Cover(local + 0.5);
                    float outer = Cover(local - (w - lineW)) * Cover(w - local);
                    return max(inner, outer);
                }

                return across; // 実線
            }

            float4 FragCompose(Varyings IN) : SV_Target
            {
                int radius = clamp((int)_OutlineSearchRadius, 1, OUTLINE_MAX_SEARCH);

                float bestD2 = 1e10;
                float bestSlot = 0;

                [loop]
                for (int y = -radius; y <= radius; y++)
                {
                    float4 h = SAMPLE_TEXTURE2D_X_LOD(_OutlineDilateTex, outline_point_clamp_sampler,
                        IN.uv + float2(0, y) * _OutlineMaskTexelSize.xy, 0);
                    if (h.g < 0.5)
                        continue;

                    float d2 = h.r * h.r + (float)(y * y);
                    if (d2 < bestD2)
                    {
                        bestD2 = d2;
                        bestSlot = h.g;
                    }
                }

                // だれも近くにいない、または客の内側（ステンシルでも消すけど、マスクの解像度を下げたときの保険）
                if (bestSlot < 0.5 || bestD2 < 0.5)
                    return 0;

                int slot = (int)round(bestSlot) - 1;
                if (slot < 0 || slot >= OUTLINE_MAX_TARGETS)
                    return 0;

                // シルエットのふちからの距離（画面px）。マスクのテクセルの中心からはかっているので半テクセルぶん引く
                float texelPx = 1.0 / max(_OutlineMaskScale, 0.01);
                float edgePx = sqrt(bestD2) * texelPx - 0.5 * texelPx;

                float4 style = _OutlineStyles[slot];
                float w = _OutlineBandPx;
                int rings = (int)round(style.z);

                // OutlineStyle.BandAt と同じ。2本のときは内側が次にほしい色、外側が今ほしい色
                float local;
                float bandStart;
                int pattern;
                float3 color;
                if (rings >= 2 && edgePx < w + 0.5)
                {
                    bandStart = 0;
                    local = edgePx;
                    pattern = (int)round(style.y);
                    color = _OutlineNextColors[slot].rgb;
                }
                else
                {
                    bandStart = rings >= 2 ? w + _OutlineGapPx : 0;
                    local = edgePx - bandStart;
                    if (local < -0.5 || local >= w + 0.5)
                        return 0;
                    pattern = (int)round(style.x);
                    color = _OutlineCurrentColors[slot].rgb;
                }

                // もようをならべる座標: 客の画面での楕円を、この輪のまん中までふくらませたもの
                float4 ellipse = _OutlineEllipses[slot];
                float bandCenter = bandStart + w * 0.5;
                float a = max(ellipse.z, 1.0) + bandCenter;
                float b = max(ellipse.w, 1.0) + bandCenter;
                float arc01 = EllipseArc01(IN.positionCS.xy - ellipse.xy, a, b);
                float perimeter = EllipsePerimeter(a, b);

                float coverage = PatternCoverage(pattern, local, arc01, perimeter);
                if (coverage <= 0.001)
                    return 0;

                // プリマルチプライド: alpha は 0〜1 に抑え、強度は RGB にだけ掛ける。
                // intensity を alpha に載せると OneMinusSrcAlpha が負になり背景が沈む。
                float brightness = _OutlineCurrentColors[slot].a;
                float3 rgb = color * (_OutlineIntensity * brightness) * coverage;
                return float4(rgb, coverage);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
