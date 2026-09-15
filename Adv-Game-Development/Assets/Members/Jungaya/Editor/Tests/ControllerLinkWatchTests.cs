using NUnit.Framework;
using Toufuku.GameInput;

namespace Toufuku.GameInput.Tests
{
    /// <summary>
    /// コントローラーの通信のとぎれと復帰 — Issue #65（仕様書 v8 3章「接続」、17章 T7「切断0／60秒以内で交換・再開」）
    /// </summary>
    public class ControllerLinkWatchTests
    {
        // from から to まで 20ms ごとに受け取る（ファームウェアの送信周期）
        static void Feed(ControllerLinkWatch w, double from, double to)
        {
            int n = (int)System.Math.Round((to - from) / 0.02);
            for (int i = 0; i <= n; i++) w.Receive(from + i * 0.02);
        }

        static ControllerLinkWatch LostAtOneSecond()
        {
            var w = new ControllerLinkWatch();
            Feed(w, 0.0, 1.0);
            Assert.AreEqual(ControllerLinkEvent.Lost, w.Poll(1.5));
            return w;
        }

        [Test]
        public void 一度も受け取っていなければとぎれない()
        {
            var w = new ControllerLinkWatch();
            Assert.AreEqual(ControllerLinkEvent.None, w.Poll(100.0));
            Assert.IsFalse(w.IsLost);
        }

        [Test]
        public void 受け取りが0点5秒あいたらとぎれる()
        {
            var w = new ControllerLinkWatch();
            Feed(w, 0.0, 1.0);

            Assert.AreEqual(ControllerLinkEvent.None, w.Poll(1.49));
            Assert.AreEqual(ControllerLinkEvent.Lost, w.Poll(1.5));
            Assert.IsTrue(w.IsLost);
            Assert.AreEqual(1.0, w.LostSince, 1e-9);
            Assert.AreEqual(1, w.LossCount);

            Assert.AreEqual(ControllerLinkEvent.None, w.Poll(3.0), "1回のとぎれで1回だけ");
            Assert.AreEqual(1, w.LossCount);
        }

        [Test]
        public void 続けて0点3秒受け取れたら復帰してとぎれていた秒が出る()
        {
            ControllerLinkWatch w = LostAtOneSecond();

            Feed(w, 5.0, 5.2);
            Assert.AreEqual(ControllerLinkEvent.None, w.Poll(5.2), "まだ 0.2秒");

            Feed(w, 5.22, 5.3);
            Assert.AreEqual(ControllerLinkEvent.Recovered, w.Poll(5.3));
            Assert.IsFalse(w.IsLost);
            Assert.AreEqual(4.3, w.LastDownSeconds, 1e-9);
        }

        [Test]
        public void 行が1つだけ来てまた止まったら復帰にしない()
        {
            ControllerLinkWatch w = LostAtOneSecond();

            w.Receive(5.0);
            Assert.AreEqual(ControllerLinkEvent.None, w.Poll(5.2));
            Assert.AreEqual(ControllerLinkEvent.None, w.Poll(5.6), "また止まった");
            Assert.IsTrue(w.IsLost);

            Feed(w, 8.0, 8.3);
            Assert.AreEqual(ControllerLinkEvent.Recovered, w.Poll(8.3));
            Assert.AreEqual(7.3, w.LastDownSeconds, 1e-9, "最初にとぎれた時刻から数える");
        }

        [Test]
        public void 復帰したあとまたとぎれたら2回目として数える()
        {
            ControllerLinkWatch w = LostAtOneSecond();
            Feed(w, 5.0, 5.3);
            Assert.AreEqual(ControllerLinkEvent.Recovered, w.Poll(5.3));

            Assert.AreEqual(ControllerLinkEvent.Lost, w.Poll(5.8));
            Assert.AreEqual(2, w.LossCount);
            Assert.AreEqual(5.3, w.LostSince, 1e-9);
        }
    }
}
