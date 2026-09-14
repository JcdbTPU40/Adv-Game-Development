using System.Collections.Generic;
using NUnit.Framework;

namespace Toufuku.Rescue.Tests
{
    /// <summary>
    /// 優先対象（二重円）の同値順 — Issue #55（仕様書 v8 6章「最危険マーク＝優先救済候補」／7章 得点表）
    ///
    /// 仕様の順序は <b>D 最大 → 遠い方 → active 化が早い方 → 生成ID が小さい方</b>。
    /// 最後が生成ID（重複しない）なので、同時刻に同じ D の客が何人いても必ず 1 人に定まる。
    /// </summary>
    public class PriorityTargetTests
    {
        [Test]
        public void 候補が無ければnull()
        {
            Assert.IsNull(PriorityTarget.Select(new List<PriorityCandidate>()));
            Assert.IsNull(PriorityTarget.Select(null));
        }

        [Test]
        public void Dが最大の客()
        {
            var c = new List<PriorityCandidate>
            {
                new PriorityCandidate(1, 40f, 3f),
                new PriorityCandidate(2, 90f, 15f),
                new PriorityCandidate(3, 60f, 5f)
            };
            Assert.AreEqual(2, PriorityTarget.Select(c));
        }

        [Test]
        public void D同値なら遠い客()
        {
            var c = new List<PriorityCandidate>
            {
                new PriorityCandidate(1, 70f, 12f),
                new PriorityCandidate(2, 70f, 6f)
            };
            Assert.AreEqual(1, PriorityTarget.Select(c));
        }

        [Test]
        public void D閾値は無い_全員が低くても1人に決まる()
        {
            // 付録B PRIORITY.MARK：対象数 1 人／D 閾値なし。D<50 でも二重円は必ず 1 人に出る。
            var c = new List<PriorityCandidate>
            {
                new PriorityCandidate(1, 5f, 3f),
                new PriorityCandidate(2, 12f, 4f)
            };
            Assert.AreEqual(2, PriorityTarget.Select(c));
        }

        [Test]
        public void Dも距離も同値ならactive化が早い客()
        {
            var c = new List<PriorityCandidate>
            {
                new PriorityCandidate(1, 50f, 8f, 12.0f),
                new PriorityCandidate(2, 50f, 8f, 3.5f)
            };
            Assert.AreEqual(2, PriorityTarget.Select(c));
        }

        [Test]
        public void Dも距離もactive化時刻も同値ならIDが小さい客_並び順によらない()
        {
            var a = new PriorityCandidate(5, 50f, 8f, 2f);
            var b = new PriorityCandidate(3, 50f, 8f, 2f);
            var c = new PriorityCandidate(9, 50f, 8f, 2f);

            Assert.AreEqual(3, PriorityTarget.Select(new List<PriorityCandidate> { a, b, c }));
            Assert.AreEqual(3, PriorityTarget.Select(new List<PriorityCandidate> { c, a, b }));
            Assert.AreEqual(3, PriorityTarget.Select(new List<PriorityCandidate> { b, c, a }));
        }

        [Test]
        public void 同じDの客が3人いても対象は必ず1人に定まる()
        {
            // 「同時刻に同じ D の客が複数いても二重円が必ず 1 人に定まる」（#55 完了条件）
            var c = new List<PriorityCandidate>
            {
                new PriorityCandidate(7, 64f, 9f, 5f),
                new PriorityCandidate(2, 64f, 9f, 5f),
                new PriorityCandidate(4, 64f, 9f, 5f)
            };

            int? first = PriorityTarget.Select(c);
            c.Reverse();
            int? second = PriorityTarget.Select(c);

            Assert.AreEqual(2, first);
            Assert.AreEqual(first, second, "並び順が変わっても同じ 1 人に決まること");
        }

        [Test]
        public void 距離のわずかな差は同値として扱う()
        {
            // 歩いている客の距離が毎フレーム 0.1mm 単位で揺れても、二重円がちらちら移らないこと。
            var near = new PriorityCandidate(1, 70f, 8.0000f, 1f);
            var far = new PriorityCandidate(2, 70f, 8.0005f, 2f);

            Assert.IsFalse(PriorityTarget.IsBetter(far, near), "誤差程度の差では順位を入れ替えない");
            Assert.AreEqual(1, PriorityTarget.Select(new List<PriorityCandidate> { near, far }),
                "同値扱いなら次の条件（active 化が早い方）で決まる");
        }
    }
}
