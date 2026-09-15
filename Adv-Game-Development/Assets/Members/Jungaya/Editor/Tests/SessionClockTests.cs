using NUnit.Framework;

namespace Toufuku.Session.Tests
{
    /// <summary>
    /// セッションの時計 — Issue #65（仕様書 v8 7章「3:00境界の処理順」: 単調増加時計で、フレームレートではなくイベント時刻で判定する）
    /// 実時間は「起動してからの秒」を Origin から進めて渡す。
    /// </summary>
    public class SessionClockTests
    {
        const double Total = 180.0;
        const double Learning = 30.0;
        const double Origin = 1000.0;
        const int Pause = 1;
        const int Link = 2;

        static SessionClock Started()
        {
            var clock = new SessionClock();
            clock.Start(Origin);
            return clock;
        }

        // from から to まで frameSeconds ごとに進める（引っかかりの上限 0.5秒をこえないように細かく進める）
        static void Run(SessionClock clock, double from, double to, double frameSeconds = 0.1)
        {
            for (int k = 1; from + k * frameSeconds <= to + 1e-9; k++)
                clock.Advance(from + k * frameSeconds);
            clock.Advance(to);
        }

        [Test]
        public void 始める前は進まない()
        {
            var clock = new SessionClock();
            Assert.AreEqual(0.0, clock.Advance(Origin + 5.0));
            Assert.AreEqual(0.0, clock.Elapsed);
            Assert.AreEqual(0.0, clock.ElapsedAt(Origin + 5.0));
        }

        [Test]
        public void 実時間の差だけ進み時刻がもどっても減らない()
        {
            SessionClock clock = Started();
            clock.Advance(Origin + 0.25);
            Assert.AreEqual(0.25, clock.Elapsed, 1e-9);
            clock.Advance(Origin + 0.2);
            Assert.AreEqual(0.25, clock.Elapsed, 1e-9);
            clock.Advance(Origin + 0.5);
            Assert.AreEqual(0.5, clock.Elapsed, 1e-9);
        }

        [Test]
        public void 止めている間は進まず再開した時刻から続く()
        {
            SessionClock clock = Started();
            clock.Advance(Origin + 0.4);

            Assert.IsTrue(clock.Hold(Pause, Origin + 0.5));
            Assert.AreEqual(0.5, clock.Elapsed, 1e-9, "止めた瞬間までは足す");

            clock.Advance(Origin + 30.0);
            Assert.AreEqual(0.5, clock.Elapsed, 1e-9);

            Assert.IsTrue(clock.Release(Pause, Origin + 30.0));
            clock.Advance(Origin + 30.3);
            Assert.AreEqual(0.8, clock.Elapsed, 1e-9);
            Assert.AreEqual(0.0, clock.StalledSeconds, 1e-9, "止めていた時間は引っかかりに数えない");
        }

        [Test]
        public void 理由がひとつでも残っていれば止まったまま()
        {
            SessionClock clock = Started();
            Assert.IsTrue(clock.Hold(Pause, Origin));
            Assert.IsTrue(clock.Hold(Link, Origin + 1.0));
            Assert.IsFalse(clock.Hold(Link, Origin + 1.5), "同じ理由を2回止めても1回ぶん");

            Assert.IsTrue(clock.Release(Pause, Origin + 2.0));
            clock.Advance(Origin + 3.0);
            Assert.IsTrue(clock.IsHeld);
            Assert.AreEqual(0.0, clock.Elapsed, 1e-9);

            Assert.IsTrue(clock.Release(Link, Origin + 3.0));
            clock.Advance(Origin + 3.1);
            Assert.IsFalse(clock.IsHeld);
            Assert.AreEqual(0.1, clock.Elapsed, 1e-9);
        }

        [Test]
        public void 大きな引っかかりは足さない()
        {
            SessionClock clock = Started();
            clock.Advance(Origin + 0.1);
            clock.Advance(Origin + 3.1);
            Assert.AreEqual(0.6, clock.Elapsed, 1e-9, "3秒の引っかかりのうち、0.5秒だけ足す");
            Assert.AreEqual(2.5, clock.StalledSeconds, 1e-9);
        }

        [Test]
        public void イベントの時刻を時計の秒に直す()
        {
            SessionClock clock = Started();
            Run(clock, Origin, Origin + 10.0);

            Assert.AreEqual(10.02, clock.ElapsedAt(Origin + 10.02), 1e-6, "最後に進めたあとの時刻");
            Assert.AreEqual(9.97, clock.ElapsedAt(Origin + 9.97), 1e-6, "最後に進める前の時刻");

            clock.Hold(Pause, Origin + 10.0);
            Assert.AreEqual(10.0, clock.ElapsedAt(Origin + 12.0), 1e-6, "止めている間はずっと止めた秒");
            clock.Release(Pause, Origin + 20.0);

            Assert.AreEqual(10.0, clock.ElapsedAt(Origin + 15.0), 1e-6, "止めていた間の時刻は、止めた秒より前にさかのぼらない");
            Assert.AreEqual(10.05, clock.ElapsedAt(Origin + 20.05), 1e-6);
        }

        /*
            GameSession（先に動く）がフレームの始めに時計を進めて 3:00 をこえていたら締め切る。
            そのあと入力が、このフレームの間に受け取った振りを、受け取った時刻で判定する。
            フレームの長さを変えても、受理される振りが同じになることを確かめる。
        */
        [TestCase(1.0 / 60.0)]
        [TestCase(1.0 / 20.0)]
        [TestCase(0.1)]
        [TestCase(0.25)]
        [TestCase(0.5)]
        public void フレームの粗さで3分の受理が変わらない(double frameSeconds)
        {
            double[] swings = { 150.0, 179.95, 179.9995, 180.0005, 180.02, 180.4 };
            bool[] expected = { true, true, true, false, false, false };

            var clock = new SessionClock();
            clock.Start(Origin);
            var accepted = new bool[swings.Length];
            bool playing = true;
            int next = 0;

            for (int k = 0; next < swings.Length; k++)
            {
                double frameStart = Origin + k * frameSeconds;
                clock.Advance(frameStart);
                if (playing && !SessionBoundary.AcceptsSwing(clock.Elapsed, Total)) playing = false;

                double frameEnd = frameStart + frameSeconds;
                while (next < swings.Length && Origin + swings[next] < frameEnd)
                {
                    accepted[next] = playing && SessionBoundary.AcceptsSwing(clock.ElapsedAt(Origin + swings[next]), Total);
                    next++;
                }
            }

            Assert.AreEqual(expected, accepted);
        }

        [TestCase(1.0 / 60.0)]
        [TestCase(0.1)]
        [TestCase(0.5)]
        public void 危険度と停滞タイマーに足す秒はフレームの粗さで変わらない(double frameSeconds)
        {
            var clock = new SessionClock();
            clock.Start(Origin);
            double last = 0.0, play = 0.0, competition = 0.0;

            for (int k = 0; k * frameSeconds <= 190.0; k++)
            {
                clock.Advance(Origin + k * frameSeconds);
                play += SessionBoundary.Overlap(last, clock.Elapsed, 0.0, Total);
                competition += SessionBoundary.Overlap(last, clock.Elapsed, Learning, Total);
                last = clock.Elapsed;
            }

            Assert.AreEqual(180.0, play, 1e-6, "3:00.000 ちょうどまで");
            Assert.AreEqual(150.0, competition, 1e-6, "0:30.000 から 3:00.000 まで");
        }

        [Test]
        public void 止めている間は危険度にも停滞タイマーにも足さない()
        {
            var clock = new SessionClock();
            clock.Start(Origin);
            double last = 0.0, play = 0.0, competition = 0.0;

            void Step(double realtime)
            {
                clock.Advance(realtime);
                play += SessionBoundary.Overlap(last, clock.Elapsed, 0.0, Total);
                competition += SessionBoundary.Overlap(last, clock.Elapsed, Learning, Total);
                last = clock.Elapsed;
            }

            for (int k = 1; k <= 400; k++) Step(Origin + k * 0.1);  // 40秒
            clock.Hold(Link, Origin + 40.0);
            for (int k = 401; k <= 700; k++) Step(Origin + k * 0.1); // 通信の復帰中 30秒
            clock.Release(Link, Origin + 70.0);
            for (int k = 701; k <= 800; k++) Step(Origin + k * 0.1); // 10秒

            Assert.AreEqual(50.0, clock.Elapsed, 1e-6);
            Assert.AreEqual(50.0, play, 1e-6);
            Assert.AreEqual(20.0, competition, 1e-6, "学習の 30秒と復帰中の 30秒は入らない");
        }
    }
}
