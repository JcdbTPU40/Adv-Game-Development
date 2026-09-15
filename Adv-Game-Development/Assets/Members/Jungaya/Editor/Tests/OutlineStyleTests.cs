using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Toufuku.Rescue.Outline.Tests
{
    /// <summary>
    /// 輪郭の5パターン・欲張り客の2本の輪・もようをならべる座標 — Issue #59（企画書 v8 6章「5色とパターン」）
    ///
    /// Compose シェーダーと同じ式を OutlineStyle に置いているので、ここで式の性質を確かめる。
    /// 見た目（グレースケールで見分けられるか、2秒で読めるか）は T1 の観察で確かめる。
    /// </summary>
    public class OutlineStyleTests
    {
        const float Band = 6f;
        const float Gap = 2f;

        [TestCase(OmamoriType.Kenkou, OutlinePattern.Solid)]
        [TestCase(OmamoriType.Gakugyou, OutlinePattern.Dotted)]
        [TestCase(OmamoriType.Yakuyoke, OutlinePattern.Dashed)]
        [TestCase(OmamoriType.Enmusubi, OutlinePattern.Wavy)]
        [TestCase(OmamoriType.Kinun, OutlinePattern.Double)]
        public void お守りごとのもようは企画書の表どおり(OmamoriType type, OutlinePattern expected)
        {
            Assert.AreEqual(expected, OutlineStyle.PatternFor(type));
        }

        [Test]
        public void 五種類のもようはぜんぶちがう()
        {
            var seen = new HashSet<OutlinePattern>();
            foreach (OmamoriType type in System.Enum.GetValues(typeof(OmamoriType)))
                seen.Add(OutlineStyle.PatternFor(type));
            Assert.AreEqual(5, seen.Count);
        }

        [Test]
        public void 欲張り客はR2のあいだだけ輪が2本()
        {
            Assert.AreEqual(2, OutlineStyle.RingCount(2, true));
            Assert.AreEqual(1, OutlineStyle.RingCount(1, true));
        }

        [Test]
        public void 次の色がない客は何発残っていても輪は1本()
        {
            // ボス客は「輪郭は同色のまま」（v8 8章）
            Assert.AreEqual(1, OutlineStyle.RingCount(3, false));
            Assert.AreEqual(1, OutlineStyle.RingCount(1, false));
        }

        [Test]
        public void 輪1本はシルエットのふちから輪のはばまで()
        {
            Assert.AreEqual(OutlineStyle.Band.Current, OutlineStyle.BandAt(0f, 1, Band, Gap, out _));
            Assert.AreEqual(OutlineStyle.Band.Current, OutlineStyle.BandAt(6.4f, 1, Band, Gap, out _));
            Assert.AreEqual(OutlineStyle.Band.None, OutlineStyle.BandAt(6.6f, 1, Band, Gap, out _));
        }

        [Test]
        public void 輪2本は内側が次の色で外側が今の色()
        {
            Assert.AreEqual(OutlineStyle.Band.Next, OutlineStyle.BandAt(1f, 2, Band, Gap, out float innerLocal));
            Assert.AreEqual(1f, innerLocal, 1e-5f);

            // すきま（6.5〜7.5）はどちらの輪でもない
            Assert.AreEqual(OutlineStyle.Band.None, OutlineStyle.BandAt(7f, 2, Band, Gap, out _));

            Assert.AreEqual(OutlineStyle.Band.Current, OutlineStyle.BandAt(9f, 2, Band, Gap, out float outerLocal));
            Assert.AreEqual(1f, outerLocal, 1e-5f);

            Assert.AreEqual(OutlineStyle.Band.None, OutlineStyle.BandAt(14.6f, 2, Band, Gap, out _));
        }

        [Test]
        public void 輪ぜんぶの太さ()
        {
            Assert.AreEqual(6f, OutlineStyle.TotalWidthPx(1, Band, Gap));
            Assert.AreEqual(14f, OutlineStyle.TotalWidthPx(2, Band, Gap));
        }

        [TestCase(1, 1f)]
        [TestCase(2, 1f)]
        [TestCase(1, 0.5f)]
        [TestCase(2, 0.5f)]
        [TestCase(1, 0.25f)]
        [TestCase(2, 0.25f)]
        public void さがす半径はいちばん外の輪のふちまでとどく(int rings, float scale)
        {
            float total = OutlineStyle.TotalWidthPx(rings, Band, Gap);
            int radius = OutlineStyle.SearchRadiusMaskTexels(total, scale);
            // マスクのテクセルを画面px に直して、外側のふち＋ぼかしまで入っているか
            Assert.GreaterOrEqual(radius / scale, total + OutlineStyle.EdgeSoftPx);
            Assert.LessOrEqual(radius, OutlineStyle.MaxSearchRadius);
        }

        [Test]
        public void さがす半径は上限でとまる()
        {
            Assert.AreEqual(OutlineStyle.MaxSearchRadius, OutlineStyle.SearchRadiusMaskTexels(100f, 1f));
        }

        [Test]
        public void まわりの位置は0から1で一周する()
        {
            Assert.AreEqual(0f, OutlineStyle.EllipseArc01(-Mathf.PI, 10f, 25f), 1e-5f);
            Assert.AreEqual(1f, OutlineStyle.EllipseArc01(Mathf.PI, 10f, 25f), 1e-5f);
        }

        [Test]
        public void まわりの位置は一周のあいだずっと増える()
        {
            const int steps = 720;
            float prev = OutlineStyle.EllipseArc01(-Mathf.PI, 10f, 30f);
            for (int i = 1; i <= steps; i++)
            {
                float angle = -Mathf.PI + 2f * Mathf.PI * i / steps;
                float v = OutlineStyle.EllipseArc01(angle, 10f, 30f);
                Assert.Greater(v, prev, $"angle={angle}");
                prev = v;
            }
        }

        [Test]
        public void 円ならただの角度のわりあい()
        {
            Assert.AreEqual(0.5f, OutlineStyle.EllipseArc01(0f, 20f, 20f), 1e-5f);
            Assert.AreEqual(0.75f, OutlineStyle.EllipseArc01(Mathf.PI * 0.5f, 20f, 20f), 1e-5f);
        }

        [Test]
        public void たて長の客でも点の間隔のむらが小さい()
        {
            // 客のカプセルくらいのたて長（1:2.5）。ただの角度だと間隔のむらは b/a = 2.5 倍になる
            const float a = 10f;
            const float b = 25f;
            float naive = Mathf.Max(a, b) / Mathf.Min(a, b);

            float min = float.MaxValue;
            float max = float.MinValue;
            const int steps = 360;
            const float h = 1e-3f;
            for (int i = 0; i < steps; i++)
            {
                float t = -Mathf.PI + 2f * Mathf.PI * (i + 0.5f) / steps;
                // 楕円の上を実際に進む速さ ÷ まわりの位置の進む速さ ＝ 位置 1 あたりの長さ
                float speed = Mathf.Sqrt(a * a * Mathf.Sin(t) * Mathf.Sin(t) + b * b * Mathf.Cos(t) * Mathf.Cos(t));
                float d01 = (OutlineStyle.EllipseArc01(t + h, a, b) - OutlineStyle.EllipseArc01(t - h, a, b)) / (2f * h);
                float lengthPerUnit = speed / d01;
                min = Mathf.Min(min, lengthPerUnit);
                max = Mathf.Max(max, lengthPerUnit);
            }

            float corrected = max / min;
            Assert.Less(corrected, 1.3f);
            Assert.Less(corrected, naive);
        }

        [Test]
        public void 円のまわりの長さ()
        {
            Assert.AreEqual(2f * Mathf.PI * 10f, OutlineStyle.EllipsePerimeter(10f, 10f), 1e-3f);
        }

        [Test]
        public void 楕円のまわりの長さは数値でたどった長さとほぼ同じ()
        {
            const float a = 10f;
            const float b = 25f;
            const int steps = 4000;
            float sum = 0f;
            Vector2 prev = new Vector2(a, 0f);
            for (int i = 1; i <= steps; i++)
            {
                float t = 2f * Mathf.PI * i / steps;
                var p = new Vector2(a * Mathf.Cos(t), b * Mathf.Sin(t));
                sum += Vector2.Distance(prev, p);
                prev = p;
            }
            Assert.AreEqual(sum, OutlineStyle.EllipsePerimeter(a, b), sum * 0.001f);
        }

        [Test]
        public void 小さい客でももようは最低回数くり返す()
        {
            Assert.AreEqual(6, OutlineStyle.RepeatCount(30f, 16f, 6));
            Assert.AreEqual(20, OutlineStyle.RepeatCount(320f, 16f, 6));
        }

        [Test]
        public void マスクに入れた番号はもどせる()
        {
            Assert.AreEqual(-1, OutlineStyle.DecodeSlot(0f));
            for (int slot = 0; slot < OutlineStyle.MaxTargets; slot++)
            {
                float a = OutlineStyle.EncodeSlot(slot);
                Assert.LessOrEqual(a, 1f);
                // RGBA8 に入れたときと同じように 1/255 刻みにまるめてからもどす
                float stored = Mathf.Round(a * 255f) / 255f;
                Assert.AreEqual(slot, OutlineStyle.DecodeSlot(stored));
            }
        }
    }
}
