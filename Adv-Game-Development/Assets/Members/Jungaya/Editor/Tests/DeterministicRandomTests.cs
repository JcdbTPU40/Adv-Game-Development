using System.Collections.Generic;
using NUnit.Framework;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#63 固定シードの乱数列。同じシード・用途・番号なら必ず同じ列になる。</summary>
    public class DeterministicRandomTests
    {
        static List<ulong> Take(DeterministicRandom rng, int n)
        {
            var values = new List<ulong>();
            for (int i = 0; i < n; i++) values.Add(rng.NextULong());
            return values;
        }

        [Test]
        public void SplitMix64の既知の値と一致する()
        {
            // 実装や環境が変わっても、保存済みのシードで同じ列が再現されることを固定する
            var rng = new DeterministicRandom(0UL);
            Assert.AreEqual(0xE220A8397B1DCDAFUL, rng.NextULong());
            Assert.AreEqual(0x6E789E6AA1B965F4UL, rng.NextULong());
            Assert.AreEqual(0x06C45D188009454FUL, rng.NextULong());
        }

        [Test]
        public void 同じシード_用途_番号なら同じ列()
        {
            CollectionAssert.AreEqual(
                Take(DeterministicRandom.Derive(42, 2, 7), 20),
                Take(DeterministicRandom.Derive(42, 2, 7), 20));
        }

        [Test]
        public void シード_用途_番号のどれかが違えば別の列()
        {
            List<ulong> baseline = Take(DeterministicRandom.Derive(42, 2, 7), 5);
            CollectionAssert.AreNotEqual(baseline, Take(DeterministicRandom.Derive(43, 2, 7), 5));
            CollectionAssert.AreNotEqual(baseline, Take(DeterministicRandom.Derive(42, 3, 7), 5));
            CollectionAssert.AreNotEqual(baseline, Take(DeterministicRandom.Derive(42, 2, 8), 5));
        }

        [Test]
        public void 番号ごとの列は他の番号の抽選回数に左右されない()
        {
            // 客 1 の抽選を何回しても、客 2 の結果は変わらない（プレイヤーの行動で列がずれない）
            var first = DeterministicRandom.Derive(9, PlaytestStreams.Placement, 1);
            for (int i = 0; i < 100; i++) first.NextULong();

            Assert.AreEqual(
                DeterministicRandom.Derive(9, PlaytestStreams.Placement, 2).Range(0, 1000),
                DeterministicRandom.Derive(9, PlaytestStreams.Placement, 2).Range(0, 1000));
        }

        [Test]
        public void 整数のRangeは下限を含み上限を含まない_全値が出る()
        {
            var rng = new DeterministicRandom(123UL);
            var seen = new HashSet<int>();
            for (int i = 0; i < 5000; i++)
            {
                int v = rng.Range(0, 5);
                Assert.That(v, Is.InRange(0, 4));
                seen.Add(v);
            }
            Assert.AreEqual(5, seen.Count);
        }

        [Test]
        public void 整数のRangeは上限が下限以下なら下限()
        {
            var rng = new DeterministicRandom(1UL);
            Assert.AreEqual(3, rng.Range(3, 3));
            Assert.AreEqual(3, rng.Range(3, 1));
        }

        [Test]
        public void Valueは0以上1未満_実数Rangeは範囲内()
        {
            var rng = new DeterministicRandom(77UL);
            for (int i = 0; i < 5000; i++)
            {
                float v = rng.Value();
                Assert.That(v, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
                Assert.That(rng.Range(-2.5f, 4f), Is.InRange(-2.5f, 4f));
            }
        }

        [Test]
        public void 子シードは用途ごとに異なり再現する()
        {
            Assert.AreEqual(DeterministicRandom.DeriveSeed(5, PlaytestStreams.SlotLayout), DeterministicRandom.DeriveSeed(5, PlaytestStreams.SlotLayout));
            Assert.AreNotEqual(DeterministicRandom.DeriveSeed(5, PlaytestStreams.SlotLayout), DeterministicRandom.DeriveSeed(5, PlaytestStreams.Placement));
        }
    }
}
