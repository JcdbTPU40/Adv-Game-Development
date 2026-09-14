using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Toufuku.Aim.Tests
{
    /// <summary>#60 着弾点での対象決定（救済演出中の客は通過して後方で判定）。</summary>
    public class LandingJudgeTests
    {
        [Test]
        public void 判定円の中にいる客に当たる()
        {
            var list = new List<LandingCandidate> { new LandingCandidate(new Vector3(0f, 1f, 5f), 0.9f, true) };
            int i = LandingJudge.FindBest(list, new Vector3(0.18f, 0f, 5f), out float n);
            Assert.AreEqual(0, i);
            Assert.AreEqual(0.2f, n, 1e-4f);
        }

        [Test]
        public void 判定円の外なら誰にも当たらない()
        {
            var list = new List<LandingCandidate> { new LandingCandidate(new Vector3(0f, 1f, 5f), 0.9f, true) };
            Assert.AreEqual(-1, LandingJudge.FindBest(list, new Vector3(0.95f, 0f, 5f), out _));
        }

        [Test]
        public void 救済演出中の客は通過して後方の客で判定する()
        {
            var list = new List<LandingCandidate>
            {
                new LandingCandidate(new Vector3(0f, 1f, 5.0f), 0.9f, false), // 手前・当たり判定なし
                new LandingCandidate(new Vector3(0f, 1f, 5.6f), 0.9f, true),  // 後方
            };
            int i = LandingJudge.FindBest(list, new Vector3(0f, 0f, 5.0f), out float n);
            Assert.AreEqual(1, i);
            Assert.AreEqual(HitZone.Inner, HitAccuracy.ZoneOf(n)); // 0.6 / 0.9 ≒ 67%
        }

        [Test]
        public void 救済演出中の客しかいなければ外し()
        {
            var list = new List<LandingCandidate> { new LandingCandidate(new Vector3(0f, 1f, 5f), 0.9f, false) };
            Assert.AreEqual(-1, LandingJudge.FindBest(list, new Vector3(0f, 0f, 5f), out _));
        }

        [Test]
        public void 判定円が重なっていれば中心に近い客に当たる()
        {
            var list = new List<LandingCandidate>
            {
                new LandingCandidate(new Vector3(0f, 1f, 5.0f), 0.9f, true),
                new LandingCandidate(new Vector3(0f, 1f, 5.6f), 0.9f, true),
            };
            Assert.AreEqual(1, LandingJudge.FindBest(list, new Vector3(0f, 0f, 5.45f), out _));
            Assert.AreEqual(0, LandingJudge.FindBest(list, new Vector3(0f, 0f, 5.15f), out _));
        }

        [Test]
        public void 候補が空なら外し()
        {
            Assert.AreEqual(-1, LandingJudge.FindBest(new List<LandingCandidate>(), Vector3.zero, out _));
            Assert.AreEqual(-1, LandingJudge.FindBest(null, Vector3.zero, out _));
        }
    }
}
