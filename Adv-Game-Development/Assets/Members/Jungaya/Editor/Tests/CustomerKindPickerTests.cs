using System.Collections.Generic;
using NUnit.Framework;
using Toufuku.Playtest;

namespace Toufuku.Rescue.Tests
{
    /// <summary>
    /// 客種の抽選（企画書 v8 10章「出現比率」「同時上限」／付録B SPAWN.TYPE.11）— Issue #62
    /// </summary>
    public class CustomerKindPickerTests
    {
        static readonly CustomerKindWeights November = CustomerKindWeights.November;

        [TestCase(0.000f, CustomerKind.Normal)]
        [TestCase(0.449f, CustomerKind.Normal)]
        [TestCase(0.451f, CustomerKind.Moving)]
        [TestCase(0.749f, CustomerKind.Moving)]
        [TestCase(0.751f, CustomerKind.Distant)]
        [TestCase(0.899f, CustomerKind.Distant)]
        [TestCase(0.901f, CustomerKind.Greedy)]
        [TestCase(0.999f, CustomerKind.Greedy)]
        [TestCase(1.000f, CustomerKind.Greedy)]
        public void 十一月の比率45_30_15_10の累積どおりに決まる(float unit, CustomerKind expected)
        {
            Assert.AreEqual(expected, CustomerKindPicker.Pick(November, unit, greedyAlive: 0, bossAlive: 0));
        }

        [Test]
        public void 欲張り客が同時上限2人なら除外して残り90で再正規化する()
        {
            // 通常45／移動30／遠方15 → 0.95×90=85.5 は遠方（75〜90）
            Assert.AreEqual(CustomerKind.Distant, CustomerKindPicker.Pick(November, 0.95f, greedyAlive: 2, bossAlive: 0));

            for (int i = 0; i <= 1000; i++)
            {
                Assert.AreNotEqual(CustomerKind.Greedy,
                    CustomerKindPicker.Pick(November, i / 1000f, greedyAlive: 2, bossAlive: 0));
            }
        }

        [Test]
        public void 欲張り客が1人なら上限未満なので抽選に残る()
        {
            Assert.AreEqual(CustomerKind.Greedy, CustomerKindPicker.Pick(November, 0.95f, greedyAlive: 1, bossAlive: 0));
        }

        [Test]
        public void ボス客は上限1人に達したら出ない()
        {
            var weights = new CustomerKindWeights { normal = 55f, moving = 10f, distant = 15f, greedy = 15f, boss = 5f };

            Assert.AreEqual(CustomerKind.Boss, CustomerKindPicker.Pick(weights, 0.99f, greedyAlive: 0, bossAlive: 0));
            Assert.AreNotEqual(CustomerKind.Boss, CustomerKindPicker.Pick(weights, 0.99f, greedyAlive: 0, bossAlive: 1));
        }

        [Test]
        public void 比率がすべて0なら通常客()
        {
            Assert.AreEqual(CustomerKind.Normal, CustomerKindPicker.Pick(default, 0.7f, 0, 0));
        }

        [Test]
        public void 同じシードの列なら同じ客種の並びになる()
        {
            List<CustomerKind> Draw(int seed)
            {
                var kinds = new List<CustomerKind>();
                for (int id = 1; id <= 50; id++)
                {
                    float unit = PlaytestRandom.Value(DeterministicRandom.Derive(seed, PlaytestStreams.Kind, id));
                    kinds.Add(CustomerKindPicker.Pick(November, unit, 0, 0));
                }
                return kinds;
            }

            CollectionAssert.AreEqual(Draw(42), Draw(42));
            CollectionAssert.AreNotEqual(Draw(42), Draw(43));
        }

        [Test]
        public void 固定シードで多数引くと比率に近づく()
        {
            const int n = 20000;
            var counts = new Dictionary<CustomerKind, int>();
            for (int id = 1; id <= n; id++)
            {
                float unit = DeterministicRandom.Derive(7, PlaytestStreams.Kind, id).Value();
                CustomerKind kind = CustomerKindPicker.Pick(November, unit, 0, 0);
                counts[kind] = counts.TryGetValue(kind, out int c) ? c + 1 : 1;
            }

            Assert.AreEqual(0.45, Share(counts, CustomerKind.Normal, n), 0.02);
            Assert.AreEqual(0.30, Share(counts, CustomerKind.Moving, n), 0.02);
            Assert.AreEqual(0.15, Share(counts, CustomerKind.Distant, n), 0.02);
            Assert.AreEqual(0.10, Share(counts, CustomerKind.Greedy, n), 0.02);
        }

        static double Share(Dictionary<CustomerKind, int> counts, CustomerKind kind, int n) =>
            counts.TryGetValue(kind, out int c) ? (double)c / n : 0.0;
    }
}
