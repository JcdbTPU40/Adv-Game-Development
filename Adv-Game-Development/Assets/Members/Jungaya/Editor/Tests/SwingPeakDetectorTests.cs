using System;
using System.Collections.Generic;
using NUnit.Framework;
using Toufuku.GameInput;

namespace Toufuku.GameInput.Tests
{
    /// <summary>#51 振りピーク検出と ESP32 受信行の解釈の EditMode テスト。</summary>
    public class SwingPeakDetectorTests
    {
        const double Dt = 0.02; // ファームウェアの送信周期（delay(20)）

        /// <summary>
        /// 振り下ろし 1 回ぶんのピッチ列を作る。角速度が半周期の正弦波（最大 peakVelocity 度/秒）になる。
        /// </summary>
        static List<float> SwingDown(float startPitch, float peakVelocity, double duration)
        {
            var angles = new List<float>();
            int n = (int)Math.Round(duration / Dt);
            float angle = startPitch;
            angles.Add(angle);
            for (int i = 1; i <= n; i++)
            {
                double tMid = (i - 0.5) * Dt;
                double v = peakVelocity * Math.Sin(Math.PI * tMid / duration);
                angle -= (float)(v * Dt);
                angles.Add(angle);
            }
            return angles;
        }

        static List<(int index, float velocity)> Feed(SwingPeakDetector d, IList<float> angles, double startTime = 0.0)
        {
            var peaks = new List<(int, float)>();
            for (int i = 0; i < angles.Count; i++)
            {
                if (d.AddSample(angles[i], startTime + i * Dt, out float v))
                    peaks.Add((i, v));
            }
            return peaks;
        }

        [Test]
        public void 振り下ろし1回で1回だけピークを返す()
        {
            var d = new SwingPeakDetector();
            var angles = SwingDown(40f, 600f, 0.3);
            angles.AddRange(new[] { angles[angles.Count - 1], angles[angles.Count - 1], angles[angles.Count - 1] });

            var peaks = Feed(d, angles);

            Assert.AreEqual(1, peaks.Count);
            Assert.Greater(peaks[0].velocity, 500f);
            // 角速度が最大になるのは 0.15 秒付近。検出はその次のサンプル
            double detectedAt = peaks[0].index * Dt;
            Assert.That(detectedAt, Is.InRange(0.14, 0.22));
        }

        [Test]
        public void 閾値未満の小さな動きではピークを返さない()
        {
            var d = new SwingPeakDetector();
            var peaks = Feed(d, SwingDown(10f, 120f, 0.3));
            Assert.AreEqual(0, peaks.Count);
        }

        [Test]
        public void 振り上げ方向は検出しない()
        {
            var d = new SwingPeakDetector();
            var down = SwingDown(0f, 600f, 0.3);
            var up = down.ConvertAll(a => -a);
            Assert.AreEqual(0, Feed(d, up).Count);
        }

        [Test]
        public void 向きを反転すると振り上げを検出する()
        {
            var d = new SwingPeakDetector { Direction = 1f };
            var up = SwingDown(0f, 600f, 0.3).ConvertAll(a => -a);
            Assert.AreEqual(1, Feed(d, up).Count);
        }

        [Test]
        public void 間を空けた2回の振りで2回ピークを返す()
        {
            var d = new SwingPeakDetector();
            var angles = SwingDown(40f, 600f, 0.3);
            float end = angles[angles.Count - 1];
            for (int i = 0; i < 10; i++) angles.Add(end);
            var second = SwingDown(40f, 600f, 0.3);
            // 戻す動作は閾値未満の速さで
            float back = end;
            while (back < 40f) { back = Math.Min(40f, back + 1f); angles.Add(back); }
            angles.AddRange(second);

            Assert.AreEqual(2, Feed(d, angles).Count);
        }

        [Test]
        public void 通信が途切れたサンプルでは速度を計算しない()
        {
            var d = new SwingPeakDetector();
            d.AddSample(40f, 0.0, out _);
            // 0.5 秒後に 60 度動いていても、途切れとみなしてピークにしない
            Assert.IsFalse(d.AddSample(-20f, 0.5, out _));
            Assert.IsFalse(d.AddSample(-20f, 0.52, out _));
            Assert.IsFalse(d.AddSample(-20f, 0.54, out _));
        }

        [Test]
        public void 角度の折り返しを大きな速度と誤認しない()
        {
            var d = new SwingPeakDetector { Direction = 1f };
            var angles = new[] { 178f, 179f, -180f, -179f, -178f, -178f };
            Assert.AreEqual(0, Feed(d, angles).Count);
        }

        // ---- 受信行の解釈 ----

        [Test]
        public void 三項目の行はボタンなしとして解釈する()
        {
            Assert.IsTrue(ControllerSample.TryParse("123.5,-10.25,3.0\r", 1.0, out var s));
            Assert.AreEqual(123.5f, s.Yaw);
            Assert.AreEqual(-10.25f, s.Pitch);
            Assert.IsFalse(s.HasButtons);
            Assert.IsFalse(s.IsColorHeld(0));
            Assert.IsFalse(s.IsFrontHeld);
        }

        [Test]
        public void 四項目目をボタンのビットマスクとして解釈する()
        {
            // bit0（色0）＋ bit3（色3）＋ bit5（正面）
            Assert.IsTrue(ControllerSample.TryParse("0,0,0,41", 1.0, out var s));
            Assert.IsTrue(s.HasButtons);
            Assert.IsTrue(s.IsColorHeld(0));
            Assert.IsFalse(s.IsColorHeld(1));
            Assert.IsTrue(s.IsColorHeld(3));
            Assert.IsTrue(s.IsFrontHeld);
        }

        [Test]
        public void 五項目目をコントローラ側のミリ秒として解釈する()
        {
            // #63: 入力時刻（millis()）。Unity 側の受信時刻とは別に持つ
            Assert.IsTrue(ControllerSample.TryParse("0,0,0,1,123456", 2.5, out var s));
            Assert.IsTrue(s.HasButtons);
            Assert.IsTrue(s.IsColorHeld(0));
            Assert.IsTrue(s.HasDeviceTime);
            Assert.AreEqual(123.456, s.DeviceTime, 1e-9);
            Assert.AreEqual(2.5, s.Time, 1e-9);

            Assert.IsTrue(ControllerSample.TryParse("0,0,0,1", 2.5, out var noTime));
            Assert.IsFalse(noTime.HasDeviceTime);
        }

        [TestCase("OONUSA_READY")]
        [TestCase("BNO055 ERROR")]
        [TestCase("1,2")]
        [TestCase("1,2,3,4,5,6")]
        [TestCase("1,2,x")]
        [TestCase("1,2,3,x")]
        [TestCase("1,2,3,4,x")]
        [TestCase("")]
        public void 数値行以外は受け付けない(string line)
        {
            Assert.IsFalse(ControllerSample.TryParse(line, 0.0, out _));
        }
    }
}
