using NUnit.Framework;

namespace Toufuku.Scoring.Tests
{
    /// <summary>
    /// 3:00 境界 — Issue #61（仕様書 v8 7章「3:00境界の処理順」／付録B GAME.END）
    ///   入力締切 180.000秒未満、受理済み弾の解決締切 180.650秒、全受理済み弾の解決後にスコアを固定する
    /// </summary>
    public class SessionBoundaryTests
    {
        const double Total = 180.0;
        const double Grace = 0.5;

        [Test]
        public void 入力は180秒未満だけ受理する()
        {
            Assert.IsTrue(SessionBoundary.AcceptsSwing(179.999, Total));
            Assert.IsFalse(SessionBoundary.AcceptsSwing(180.0, Total));
        }

        [Test]
        public void 解決の締切は0点65秒後()
        {
            Assert.AreEqual(0.65f, SessionBoundary.ResolveWindowSeconds);
        }

        [Test]
        public void 三分前は固定しない()
        {
            Assert.IsFalse(SessionBoundary.ShouldLock(179.9, Total, 0, Grace));
        }

        [Test]
        public void 飛んでいる弾がなければ3分ちょうどで固定する()
        {
            Assert.IsTrue(SessionBoundary.ShouldLock(180.0, Total, 0, Grace));
        }

        [Test]
        public void 受理済みの弾が飛んでいる間は固定しない()
        {
            Assert.IsFalse(SessionBoundary.ShouldLock(180.3, Total, 2, Grace));
            // 締切の 180.650 ちょうどでも、同じフレームで弾が落ちる前に固定しないように待つ
            Assert.IsFalse(SessionBoundary.ShouldLock(180.65, Total, 1, Grace));
        }

        [Test]
        public void 弾が落ちたら締切前でも固定する()
        {
            Assert.IsTrue(SessionBoundary.ShouldLock(180.4, Total, 0, Grace));
        }

        [Test]
        public void 弾がこわれて落ちなくても締切のあと猶予で固定する()
        {
            Assert.IsFalse(SessionBoundary.ShouldLock(181.14, Total, 1, Grace));
            Assert.IsTrue(SessionBoundary.ShouldLock(181.15, Total, 1, Grace));
        }

        // ── #65 ─────────────────────────────

        [Test]
        public void 伝播は接触が180秒未満だけ有効()
        {
            Assert.IsTrue(SessionBoundary.AcceptsPropagation(179.999, Total));
            Assert.IsFalse(SessionBoundary.AcceptsPropagation(180.0, Total));
            Assert.IsFalse(SessionBoundary.AcceptsPropagation(180.3, Total), "3:00 以後の退場歩行");
        }

        [Test]
        public void 時計が進んだうち区間と重なる秒()
        {
            Assert.AreEqual(10.0, SessionBoundary.Overlap(170.0, 181.0, 0.0, Total), 1e-9, "3:00 をまたいだフレームは 3:00 まで");
            Assert.AreEqual(5.0, SessionBoundary.Overlap(25.0, 35.0, 30.0, Total), 1e-9, "0:30 をまたいだフレームは 0:30 から");
            Assert.AreEqual(0.0, SessionBoundary.Overlap(181.0, 182.0, 0.0, Total), "3:00 以後");
            Assert.AreEqual(0.0, SessionBoundary.Overlap(10.0, 10.0, 0.0, Total), "止めている");
        }
    }
}
