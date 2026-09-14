using NUnit.Framework;
using Toufuku.GameInput;
using Toufuku.Playtest;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#50 操作経験の異なる 8 人を 4 条件ラテン方格の各順序へ 2 人ずつ。母数は投数で数える。</summary>
    public class CooldownTestPlanTests
    {
        [Test]
        public void 比べるのは四条件()
        {
            Assert.AreEqual(4, CooldownTestPlan.Conditions.Length);
            Assert.AreEqual(0.40f, CooldownTestPlan.SecondsOf(CooldownPreset.Sec040), 0.001f);
            Assert.AreEqual(0.50f, CooldownTestPlan.SecondsOf(CooldownPreset.Sec050), 0.001f);
            Assert.AreEqual(0.60f, CooldownTestPlan.SecondsOf(CooldownPreset.Sec060), 0.001f);
            Assert.AreEqual(0.65f, CooldownTestPlan.SecondsOf(CooldownPreset.Sec065), 0.001f);
        }

        [Test]
        public void 候補は秒の小さい順に並ぶ()
        {
            // 採用は「両方を満たす最小値」なので、この順に探せることが前提
            for (int i = 1; i < CooldownTestPlan.Conditions.Length; i++)
            {
                float previous = CooldownTestPlan.SecondsOf(CooldownTestPlan.Conditions[i - 1]);
                float current = CooldownTestPlan.SecondsOf(CooldownTestPlan.Conditions[i]);
                Assert.Less(previous, current);
            }
        }

        [Test]
        public void 各順序に四条件が一回ずつ出る()
        {
            for (int order = 0; order < CooldownTestPlan.ConditionCount; order++)
            {
                var seen = new bool[CooldownTestPlan.ConditionCount];
                for (int trial = 0; trial < CooldownTestPlan.ConditionCount; trial++)
                {
                    int index = System.Array.IndexOf(
                        CooldownTestPlan.Conditions, CooldownTestPlan.ConditionAt(order, trial));
                    Assert.GreaterOrEqual(index, 0);
                    Assert.IsFalse(seen[index], $"順序 {order} に同じ条件が 2 回出ている");
                    seen[index] = true;
                }
            }
        }

        [Test]
        public void 各回に四条件が一回ずつ出る()
        {
            // 列方向。1 回目に必ず 0.40 秒が来るような偏りが無いこと
            for (int trial = 0; trial < CooldownTestPlan.ConditionCount; trial++)
            {
                var seen = new bool[CooldownTestPlan.ConditionCount];
                for (int order = 0; order < CooldownTestPlan.ConditionCount; order++)
                {
                    int index = System.Array.IndexOf(
                        CooldownTestPlan.Conditions, CooldownTestPlan.ConditionAt(order, trial));
                    Assert.IsFalse(seen[index], $"{trial + 1} 回目に同じ条件が 2 回出ている");
                    seen[index] = true;
                }
            }
        }

        [Test]
        public void 直前の条件の引きずりも打ち消される()
        {
            // ウィリアムズ計画: 「A のあとに B」という並びが 12 通りすべて 1 回ずつ出る
            var pairs = new bool[CooldownTestPlan.ConditionCount, CooldownTestPlan.ConditionCount];
            int count = 0;

            for (int order = 0; order < CooldownTestPlan.ConditionCount; order++)
            {
                for (int trial = 1; trial < CooldownTestPlan.ConditionCount; trial++)
                {
                    int from = System.Array.IndexOf(
                        CooldownTestPlan.Conditions, CooldownTestPlan.ConditionAt(order, trial - 1));
                    int to = System.Array.IndexOf(
                        CooldownTestPlan.Conditions, CooldownTestPlan.ConditionAt(order, trial));
                    Assert.IsFalse(pairs[from, to], $"{from}→{to} が 2 回出ている");
                    pairs[from, to] = true;
                    count++;
                }
            }

            Assert.AreEqual(12, count);
        }

        [Test]
        public void 八人なら各順序に二人ずつ()
        {
            for (int order = 0; order < CooldownTestPlan.ConditionCount; order++)
                Assert.AreEqual(2, CooldownTestPlan.CountOf(CooldownTestPlan.DefaultParticipants, order));

            Assert.AreEqual(0, CooldownTestPlan.OrderIndexOf(1));
            Assert.AreEqual(3, CooldownTestPlan.OrderIndexOf(4));
            Assert.AreEqual(0, CooldownTestPlan.OrderIndexOf(5));
            Assert.AreEqual(3, CooldownTestPlan.OrderIndexOf(8));
        }

        [Test]
        public void 投数は人数で割って余りを若い番号へ足す()
        {
            int singles = 0;
            int pairs = 0;
            for (int no = 1; no <= CooldownTestPlan.DefaultParticipants; no++)
            {
                singles += CooldownTestPlan.SinglesFor(no);
                pairs += CooldownTestPlan.PairsFor(no);
            }

            Assert.AreEqual(CooldownTestPlan.SinglesPerCondition, singles, "単発が合計 100 回にならない");
            Assert.AreEqual(CooldownTestPlan.PairsPerCondition, pairs, "連投が合計 50 組にならない");

            Assert.AreEqual(13, CooldownTestPlan.SinglesFor(1));
            Assert.AreEqual(12, CooldownTestPlan.SinglesFor(8));
            Assert.AreEqual(7, CooldownTestPlan.PairsFor(1));
            Assert.AreEqual(6, CooldownTestPlan.PairsFor(8));
        }

        [Test]
        public void 人数を変えても合計は変わらない()
        {
            for (int participants = 1; participants <= 12; participants++)
            {
                int total = 0;
                for (int i = 0; i < participants; i++)
                    total += CooldownTestPlan.ShareOf(CooldownTestPlan.SinglesPerCondition, participants, i);
                Assert.AreEqual(CooldownTestPlan.SinglesPerCondition, total, $"{participants} 人で合計が合わない");
            }
        }

        [Test]
        public void 誤発射率の母数は単発と連投の合計()
        {
            // 単発 100 回 + 連投 50 組 × 2 回 = 意図した振り 200 回
            Assert.AreEqual(200, CooldownTestPlan.IntendedSwingsPerCondition);
        }

        [Test]
        public void 秒数から条件を引き戻せる()
        {
            Assert.AreEqual(CooldownPreset.Sec040, CooldownTestPlan.PresetOf(0.40f));
            Assert.AreEqual(CooldownPreset.Sec065, CooldownTestPlan.PresetOf(0.65f));
            Assert.AreEqual(CooldownPreset.Custom, CooldownTestPlan.PresetOf(0.55f));
        }
    }
}
