using UnityEngine;

namespace Toufuku.Rescue.Outline
{
    /*
        輪郭の見た目の決まり（#59）。Compose シェーダーと同じ式を C# にも置いて、テストで確かめられるようにしたもの
        シェーダーの式を変えたら、ここもいっしょに直すこと（逆も）

        ・お守りの種類 → もよう（企画書 v8 6章の表）
        ・輪の数: 欲張り客で R=2 のあいだは2本（内側が次にほしい2色目、外側が今ほしい1色目）。それ以外は1本
        ・太さは画面のピクセルで決める。カメラからの距離に関係なく、近／中／遠で同じ太さに見える
        ・点や破線の間隔も画面のピクセルで決めて、輪のまわりの長さでちょうど割り切れる回数にまるめる
          （つなぎ目で点が半分に切れないように）
    */
    public static class OutlineStyle
    {
        // 1フレームで輪郭を出せる客の最大数。マスクの A に (1 + 番号) / 255 で入れて、シェーダーの配列の長さにもなる
        public const int MaxTargets = 64;

        // ダイレートでさがす半径の上限（マスクのテクセル）。シェーダーの OUTLINE_MAX_SEARCH と同じ
        public const int MaxSearchRadius = 16;

        // 輪のふちのアンチエイリアスのはば（画面ピクセル）
        public const float EdgeSoftPx = 0.5f;

        // その場所がどの輪に入るか
        public enum Band
        {
            None,
            // 今ほしい色の輪（1本のときはこれだけ。2本のときは外側）
            Current,
            // 次にほしい色の輪（欲張り客の内側）
            Next,
        }

        // お守りの種類からもようを引く（企画書 v8 6章「5色とパターン」）
        public static OutlinePattern PatternFor(OmamoriType type)
        {
            switch (type)
            {
                case OmamoriType.Kenkou: return OutlinePattern.Solid;
                case OmamoriType.Gakugyou: return OutlinePattern.Dotted;
                case OmamoriType.Yakuyoke: return OutlinePattern.Dashed;
                case OmamoriType.Enmusubi: return OutlinePattern.Wavy;
                case OmamoriType.Kinun: return OutlinePattern.Double;
                default: return OutlinePattern.Solid;
            }
        }

        /*
            輪の数。残りの必要な発数 R が2以上で、次にほしい色がある（欲張り客）ときだけ2本
            ボス客は「輪郭は同色のまま」（v8 8章）なので、次の色がない＝1本になる
        */
        public static int RingCount(int remaining, bool hasNextRequest)
            => remaining >= 2 && hasNextRequest ? 2 : 1;

        // 輪郭ぜんぶの太さ（画面ピクセル）
        public static float TotalWidthPx(int rings, float bandPx, float gapPx)
            => rings >= 2 ? bandPx * 2f + gapPx : bandPx;

        /*
            ダイレートでさがす半径（マスクのテクセル）
            いちばん外の輪のふち＋アンチエイリアスまでとどくようにする。上限は MaxSearchRadius
        */
        public static int SearchRadiusMaskTexels(float totalWidthPx, float maskResolutionScale)
        {
            float scale = Mathf.Clamp(maskResolutionScale, 0.25f, 1f);
            int r = Mathf.CeilToInt((totalWidthPx + 1f) * scale);
            return Mathf.Clamp(r, 1, MaxSearchRadius);
        }

        /*
            シルエットのふちからの距離 edgePx（画面ピクセル）が、どの輪に入るかを返す
            local には、その輪の内側のふちからの距離を入れる（もようの計算に使う。ふちのぼかしで少しマイナスになることがある）
        */
        public static Band BandAt(float edgePx, int rings, float bandPx, float gapPx, out float local)
        {
            local = edgePx;
            if (edgePx < 0f) return Band.None;

            if (rings < 2)
                return edgePx < bandPx + EdgeSoftPx ? Band.Current : Band.None;

            if (edgePx < bandPx + EdgeSoftPx)
                return Band.Next;

            float outerStart = bandPx + gapPx;
            local = edgePx - outerStart;
            if (local < -EdgeSoftPx || local >= bandPx + EdgeSoftPx) return Band.None;
            return Band.Current;
        }

        /*
            楕円のまわりの位置を 0〜1 にする（もようの点や線をならべる座標）
            angle: 中心から見た楕円の角度 atan2(y/b, x/a)。-π〜π
            a, b: 楕円の横と縦の半径（画面ピクセル）

            ただの角度のわりあいだと、たて長の客では横はらのほうが点の間隔がつまって見える
            楕円の弧の長さの近似 s(t) ≈ √A (t + B/(4A)·sin 2t)（A=(a²+b²)/2, B=(b²−a²)/2）でならす
        */
        public static float EllipseArc01(float angle, float a, float b)
        {
            a = Mathf.Max(a, 1f);
            b = Mathf.Max(b, 1f);
            float t = angle + Mathf.PI;
            float A = (a * a + b * b) * 0.5f;
            float B = (b * b - a * a) * 0.5f;
            return (t + B / (4f * A) * Mathf.Sin(2f * t)) / (2f * Mathf.PI);
        }

        // 楕円のまわりの長さ（ラマヌジャンの近似。画面ピクセル）
        public static float EllipsePerimeter(float a, float b)
        {
            a = Mathf.Max(a, 1f);
            b = Mathf.Max(b, 1f);
            return Mathf.PI * (3f * (a + b) - Mathf.Sqrt((3f * a + b) * (a + 3f * b)));
        }

        // まわりにもようを何回くり返すか。つなぎ目で切れないように整数にする。遠くて小さい客でも最低 minRepeats 回
        public static int RepeatCount(float perimeterPx, float periodPx, int minRepeats)
        {
            int n = Mathf.RoundToInt(perimeterPx / Mathf.Max(periodPx, 1f));
            return Mathf.Max(Mathf.Max(minRepeats, 1), n);
        }

        // マスクの A に入れる値（0 はだれもいない）
        public static float EncodeSlot(int slot) => (1f + slot) / 255f;

        // マスクの A から番号にもどす（-1 はだれもいない）
        public static int DecodeSlot(float a) => Mathf.RoundToInt(a * 255f) - 1;
    }
}
