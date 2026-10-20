using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Toufuku.Rescue.Tests
{
    /// <summary>
    /// 集団客の当てる相手の決め方と並び方（Issue #93: グループ全体で1つの当たり判定・危険度Dが高い人から・合う人がいなければ誤投擲）
    /// </summary>
    public class CustomerGroupTests
    {
        static GroupHitCandidate Good(float danger) => new GroupHitCandidate(true, true, danger);
        static GroupHitCandidate Bad(float danger) => new GroupHitCandidate(true, false, danger);
        static GroupHitCandidate Done(float danger) => new GroupHitCandidate(false, false, danger);

        [Test]
        public void 合う悩みの人が1人なら当たった人に関係なくその人に当てる()
        {
            var c = new List<GroupHitCandidate> { Bad(80f), Good(10f), Bad(50f) };
            Assert.AreEqual(1, CustomerGroup.ChooseTarget(c, struckIndex: 0));
            Assert.AreEqual(1, CustomerGroup.ChooseTarget(c, struckIndex: 2));
        }

        [Test]
        public void 合う悩みの人が2人以上なら危険度Dが高い人から()
        {
            var c = new List<GroupHitCandidate> { Good(30f), Good(70f), Good(50f) };
            Assert.AreEqual(1, CustomerGroup.ChooseTarget(c, struckIndex: 0));
        }

        [Test]
        public void 危険度Dが同じなら当たった本人を優先する()
        {
            var c = new List<GroupHitCandidate> { Good(40f), Good(40f) };
            Assert.AreEqual(1, CustomerGroup.ChooseTarget(c, struckIndex: 1));
            Assert.AreEqual(0, CustomerGroup.ChooseTarget(c, struckIndex: 0));
        }

        [Test]
        public void 悩みが消えた人は合っていても選ばない()
        {
            var c = new List<GroupHitCandidate> { Done(90f), Good(20f) };
            Assert.AreEqual(1, CustomerGroup.ChooseTarget(c, struckIndex: 0));
        }

        [Test]
        public void 合う人がいなければ誤投擲として当たった本人に当てる()
        {
            var c = new List<GroupHitCandidate> { Bad(10f), Bad(90f) };
            Assert.AreEqual(0, CustomerGroup.ChooseTarget(c, struckIndex: 0));
        }

        [Test]
        public void 当たった本人の悩みが消えていたら残っている人に誤投擲を当てる()
        {
            var c = new List<GroupHitCandidate> { Done(0f), Bad(60f), Bad(30f) };
            Assert.AreEqual(1, CustomerGroup.ChooseTarget(c, struckIndex: 0));
        }

        [Test]
        public void だれも残っていなければマイナス1()
        {
            var c = new List<GroupHitCandidate> { Done(0f), Done(0f) };
            Assert.AreEqual(-1, CustomerGroup.ChooseTarget(c, struckIndex: 0));
            Assert.AreEqual(-1, CustomerGroup.ChooseTarget(new List<GroupHitCandidate>(), struckIndex: -1));
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void 並びは定位置を真ん中にして横にならぶ(int count)
        {
            float sumX = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector3 offset = CustomerGroup.FormationOffset(i, count, 0.65f);
                sumX += offset.x;
                Assert.AreEqual(0f, offset.y, 0.0001f);
                Assert.LessOrEqual(Mathf.Abs(offset.z), 0.65f * 0.25f + 0.0001f);
                if (i > 0)
                    Assert.AreEqual(0.65f, offset.x - CustomerGroup.FormationOffset(i - 1, count, 0.65f).x, 0.0001f);
            }
            Assert.AreEqual(0f, sumX, 0.0001f);
        }

        [Test]
        public void 一人なら並びのずれはない()
        {
            Assert.AreEqual(Vector3.zero, CustomerGroup.FormationOffset(0, 1, 0.65f));
        }

        // ── 補充1回ぶんを集団客にするか（MockCrowdDirector.SpawnNext） ──

        [Test]
        public void 空きが2人以上あって確率に当たれば集団客にする()
        {
            Assert.IsTrue(CustomerGroup.TryPlan(0.4f, room: 3, chanceRoll: 0.1f, kindRoll: 0.5f, sizeRoll: 0f, out _, out int size));
            Assert.GreaterOrEqual(size, 2);
            Assert.LessOrEqual(size, 3);
        }

        [Test]
        public void 確率に当たらなければ一人客()
        {
            Assert.IsFalse(CustomerGroup.TryPlan(0.4f, room: 5, chanceRoll: 0.4f, kindRoll: 0f, sizeRoll: 0f, out _, out _));
            Assert.IsFalse(CustomerGroup.TryPlan(0f, room: 5, chanceRoll: 0f, kindRoll: 0f, sizeRoll: 0f, out _, out _));
        }

        [TestCase(1)]
        [TestCase(0)]
        [TestCase(-2)]
        public void 空きが2人未満なら一人客(int room)
        {
            Assert.IsFalse(CustomerGroup.TryPlan(1f, room, chanceRoll: 0f, kindRoll: 0f, sizeRoll: 0f, out _, out _));
        }

        [Test]
        public void 人数は空きまでにおさえて家族にとどかなければ友達にする()
        {
            // 家族（kindRoll が上の3分の1）は3〜4人だけど、空きが2人
            Assert.IsTrue(CustomerGroup.TryPlan(1f, room: 2, chanceRoll: 0f, kindRoll: 0.99f, sizeRoll: 0.99f,
                out CustomerGroupKind kind, out int size));
            Assert.AreEqual(2, size);
            Assert.AreEqual(CustomerGroupKind.Friends, kind);
        }

        [Test]
        public void 人数は種類ごとのはばの両はしまで出る()
        {
            CustomerGroup.TryPlan(1f, room: 10, 0f, kindRoll: 0.99f, sizeRoll: 0f, out CustomerGroupKind k1, out int small);
            CustomerGroup.TryPlan(1f, room: 10, 0f, kindRoll: 0.99f, sizeRoll: 1f, out CustomerGroupKind k2, out int large);
            Assert.AreEqual(CustomerGroupKind.Family, k1);
            Assert.AreEqual(CustomerGroupKind.Family, k2);
            Assert.AreEqual(3, small);
            Assert.AreEqual(4, large);

            CustomerGroup.TryPlan(1f, room: 10, 0f, kindRoll: 0f, sizeRoll: 1f, out CustomerGroupKind couple, out int two);
            Assert.AreEqual(CustomerGroupKind.Couple, couple);
            Assert.AreEqual(2, two);
        }

        [Test]
        public void 並んだはばは人数マイナス1かける間()
        {
            Assert.AreEqual(0f, CustomerGroup.Span(1, 0.65f), 0.0001f);
            Assert.AreEqual(1.95f, CustomerGroup.Span(4, 0.65f), 0.0001f);
        }

        [Test]
        public void 種類ごとの人数のはば()
        {
            Assert.AreEqual(new Vector2Int(2, 2), CustomerGroup.SizeRangeFor(CustomerGroupKind.Couple));
            Assert.AreEqual(new Vector2Int(3, 4), CustomerGroup.SizeRangeFor(CustomerGroupKind.Family));
            Assert.AreEqual(new Vector2Int(2, 4), CustomerGroup.SizeRangeFor(CustomerGroupKind.Friends));
        }
    }
}
