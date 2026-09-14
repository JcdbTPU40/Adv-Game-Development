using System.Collections.Generic;
using NUnit.Framework;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#63 / #55 優先対象の同値順（D 最大 → 近い → ID 昇順）。</summary>
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
        public void D同値なら近い客()
        {
            var c = new List<PriorityCandidate>
            {
                new PriorityCandidate(1, 70f, 12f),
                new PriorityCandidate(2, 70f, 6f)
            };
            Assert.AreEqual(2, PriorityTarget.Select(c));
        }

        [Test]
        public void Dも距離も同値ならIDが小さい客_並び順によらない()
        {
            var a = new PriorityCandidate(5, 50f, 8f);
            var b = new PriorityCandidate(3, 50f, 8f);
            var c = new PriorityCandidate(9, 50f, 8f);

            Assert.AreEqual(3, PriorityTarget.Select(new List<PriorityCandidate> { a, b, c }));
            Assert.AreEqual(3, PriorityTarget.Select(new List<PriorityCandidate> { c, a, b }));
            Assert.AreEqual(3, PriorityTarget.Select(new List<PriorityCandidate> { b, c, a }));
        }
    }
}
