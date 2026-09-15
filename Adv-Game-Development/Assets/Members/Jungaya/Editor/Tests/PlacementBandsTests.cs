using NUnit.Framework;

namespace Toufuku.Rescue.Tests
{
    /// <summary>
    /// 近／中／遠の目標占有率と補充する帯の選び方（企画書 v8 8章「空間配置の比率」／付録B PLACEMENT）— Issue #62
    /// </summary>
    public class PlacementBandsTests
    {
        static readonly float[] Shares = { 40f, 40f, 20f };

        [TestCase(10, 4, 4, 2)]
        [TestCase(15, 6, 6, 3)]
        [TestCase(13, 5, 5, 3)]   // 5.2 / 5.2 / 2.6 → 端数が最大の遠へ1
        [TestCase(14, 6, 5, 3)]   // 5.6 / 5.6 / 2.8 → 遠 .8 → 近 .6（同率は手前）
        [TestCase(11, 5, 4, 2)]   // 4.4 / 4.4 / 2.2 → 同率は手前へ
        [TestCase(1, 1, 0, 0)]
        public void 上限を最大剰余法で整数枠に丸める(int capacity, int near, int middle, int far)
        {
            CollectionAssert.AreEqual(new[] { near, middle, far }, PlacementBands.Quotas(capacity, Shares));
        }

        [Test]
        public void 枠の合計は常に上限と一致する()
        {
            for (int capacity = 0; capacity <= 30; capacity++)
            {
                int[] q = PlacementBands.Quotas(capacity, Shares);
                Assert.AreEqual(capacity, q[0] + q[1] + q[2], $"capacity={capacity}");
            }
        }

        [Test]
        public void 比率が未設定なら枠はすべて0()
        {
            CollectionAssert.AreEqual(new[] { 0, 0, 0 }, PlacementBands.Quotas(15, new[] { 0f, 0f, 0f }));
        }

        [Test]
        public void 遠方客は遠に空きがあれば不足率に関係なく遠へ置く()
        {
            int band = PlacementBands.Choose(
                quotas: new[] { 6, 6, 3 }, occupied: new[] { 0, 0, 3 }, hasFree: new[] { true, true, true },
                requiredBand: 2, out bool fellBack);

            Assert.AreEqual(2, band);
            Assert.IsFalse(fellBack);
        }

        [Test]
        public void 遠に空きがなければ次に不足する帯へ回しフラグを立てる()
        {
            int band = PlacementBands.Choose(
                quotas: new[] { 6, 6, 3 }, occupied: new[] { 5, 2, 3 }, hasFree: new[] { true, true, false },
                requiredBand: 2, out bool fellBack);

            Assert.AreEqual(1, band, "中の不足率 4/6 が近の 1/6 より大きい");
            Assert.IsTrue(fellBack);
        }

        [Test]
        public void 通常の補充は不足率が最大の帯を選ぶ()
        {
            // 近 3/6 不足 .5、中 2/6 不足 .67、遠 0/3 不足 1.0
            Assert.AreEqual(2, PlacementBands.Choose(new[] { 6, 6, 3 }, new[] { 3, 2, 0 }, new[] { true, true, true }, -1, out _));
            // 遠が埋まっていれば中
            Assert.AreEqual(1, PlacementBands.Choose(new[] { 6, 6, 3 }, new[] { 3, 2, 3 }, new[] { true, true, true }, -1, out _));
        }

        [Test]
        public void 不足率が同じなら手前の帯()
        {
            Assert.AreEqual(0, PlacementBands.Choose(new[] { 4, 4, 2 }, new[] { 2, 2, 1 }, new[] { true, true, true }, -1, out _));
        }

        [Test]
        public void どの帯にも空きがなければマイナス1()
        {
            Assert.AreEqual(-1, PlacementBands.Choose(new[] { 6, 6, 3 }, new[] { 6, 6, 3 }, new[] { false, false, false }, 2, out _));
        }

        [TestCase(12f, 0f, 12f)]
        [TestCase(15f, 9f, 12f)]
        [TestCase(5f, 3f, 4f)]
        [TestCase(3f, 4f, 0f)]
        public void 距離と左右のずれから奥行きを求める(float distance, float lateral, float expected)
        {
            Assert.AreEqual(expected, PlacementBands.DepthAtDistance(distance, lateral), 0.0001f);
        }
    }
}
