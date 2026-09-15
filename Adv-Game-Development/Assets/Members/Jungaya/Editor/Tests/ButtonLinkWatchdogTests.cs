using System;
using NUnit.Framework;
using Toufuku.GameInput;

namespace Toufuku.GameInput.Tests
{
    /// <summary>
    /// ボタン箱の受信の見張り — Issue #65（仕様書 v8 3章「Unityは100ms受信がなければ全ボタン解放として入力・チャージをキャンセルする」）
    /// </summary>
    public class ButtonLinkWatchdogTests
    {
        const int Color2 = 1 << 2;
        const int Front = 1 << ControllerSample.FrontButtonBit;

        [Test]
        public void 一度も受け取っていなければ見張らない()
        {
            var w = new ButtonLinkWatchdog();
            Assert.IsFalse(w.IsLost(100.0));
            Assert.AreEqual(0, w.EffectiveMask(100.0));
            Assert.IsTrue(double.IsNaN(w.LostTime));
        }

        [Test]
        public void 受け取ってから99msは押したままで100msで全解放()
        {
            var w = new ButtonLinkWatchdog();
            w.Receive(1.0, Color2 | Front);

            Assert.IsFalse(w.IsLost(1.099));
            Assert.AreEqual(Color2 | Front, w.EffectiveMask(1.099));

            Assert.IsTrue(w.IsLost(1.1));
            Assert.AreEqual(0, w.EffectiveMask(1.1));
            Assert.AreEqual(1.1, w.LostTime, 1e-9);
        }

        [Test]
        public void ハートビートが30Hzで続けばとぎれない()
        {
            var w = new ButtonLinkWatchdog();
            for (int i = 0; i < 90; i++)
            {
                double t = i / 30.0;
                w.Receive(t, Color2);
                Assert.IsFalse(w.IsLost(t + 1.0 / 30.0 - 1e-6), $"{t:0.000}秒");
            }
        }

        [Test]
        public void また受け取ったら押下マスクにもどる()
        {
            var w = new ButtonLinkWatchdog();
            w.Receive(1.0, Color2);
            Assert.AreEqual(0, w.EffectiveMask(1.5));

            w.Receive(1.6, Front);
            Assert.IsFalse(w.IsLost(1.6));
            Assert.AreEqual(Front, w.EffectiveMask(1.65));
        }

        [Test]
        public void とぎれた時刻ではなすのでフレームがおそくてもためが始まらない()
        {
            var m = new InputStateMachine(0) { OharaeEnabled = true, ChargeSeconds = 0.8f };
            int started = 0, cancelled = 0;
            m.ChargeStarted += _ => started++;
            m.ChargeCancelled += _ => cancelled++;

            var w = new ButtonLinkWatchdog();
            m.PressColor(2, 0.0);
            w.Receive(0.0, Color2);
            w.Receive(0.6, Color2); // 最後のハートビート
            m.Tick(0.65);

            // 次のフレームが 0.95秒まで来なかった。0.7秒（0.6 + 100ms）ではなしたことにする
            double now = 0.95;
            Assert.IsTrue(w.IsLost(now));
            m.ReleaseColor(2, Math.Min(now, w.LostTime));

            Assert.AreEqual(0, started, "0.7秒ではなしたので、0.8秒のためは始まらない");
            Assert.AreEqual(0, cancelled);
            Assert.IsFalse(m.IsColorHeld(2));
            Assert.IsFalse(m.IsCharging);
        }

        [Test]
        public void ためている途中でとぎれたらためをキャンセルする()
        {
            var m = new InputStateMachine(0) { OharaeEnabled = true, ChargeSeconds = 0.8f };
            int cancelled = 0;
            m.ChargeCancelled += _ => cancelled++;

            var w = new ButtonLinkWatchdog();
            m.PressColor(1, 0.0);
            w.Receive(1.0, 1 << 1);
            m.Tick(1.0);
            Assert.IsTrue(m.IsCharging);

            m.ReleaseColor(1, Math.Min(1.3, w.LostTime));
            Assert.IsFalse(m.IsCharging);
            Assert.AreEqual(1, cancelled);
        }

        [Test]
        public void とぎれた時刻ではなすので正面の長押しが1秒にとどかない()
        {
            var m = new InputStateMachine(0) { FrontHoldSeconds = 1.0f };
            int calibrations = 0;
            m.YawCalibrationRequested += () => calibrations++;

            var w = new ButtonLinkWatchdog();
            m.PressFront(0.0);
            w.Receive(0.0, Front);
            w.Receive(0.85, Front);

            double now = 1.2;
            Assert.IsTrue(w.IsLost(now));
            m.ReleaseFront(Math.Min(now, w.LostTime));

            Assert.AreEqual(0, calibrations, "0.95秒ではなしたので、キャリブレーションしない");
            Assert.IsFalse(m.IsFrontHeld);
        }
    }
}
