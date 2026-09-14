using NUnit.Framework;

namespace Toufuku.Rescue.Tests
{
    /// <summary>
    /// 足元の円の表示条件 — Issue #55（付録B UI.DANGER：D=50 で表示・D=85 で脈打ち／PRIORITY.MARK：1人・D閾値なし）
    /// </summary>
    public class DangerRingDisplayTests
    {
        [Test]
        public void 危険円はD50未満では出ない()
        {
            Assert.IsFalse(DangerRingDisplay.ShowsDangerRing(49.9f, isRescueTarget: true));
        }

        [Test]
        public void 危険円はD50で出る()
        {
            Assert.IsTrue(DangerRingDisplay.ShowsDangerRing(50f, isRescueTarget: true));
            Assert.IsTrue(DangerRingDisplay.ShowsDangerRing(99f, isRescueTarget: true));
        }

        [Test]
        public void 救済対象でない客には危険円を出さない()
        {
            // 入場中・退場中の救済客・黒客（v8 6章）。
            Assert.IsFalse(DangerRingDisplay.ShowsDangerRing(90f, isRescueTarget: false));
        }

        [Test]
        public void 二重円はD閾値を持たない()
        {
            // 「D<50 でも細い二重の輪だけを表示する」（v8 6章）。ここに閾値を作ると「待って稼ぐ」が復活する。
            Assert.IsTrue(DangerRingDisplay.ShowsPriorityRing(isPriorityTarget: true, isRescueTarget: true));
        }

        [Test]
        public void 二重円は優先対象でなければ出ない()
        {
            Assert.IsFalse(DangerRingDisplay.ShowsPriorityRing(isPriorityTarget: false, isRescueTarget: true));
            Assert.IsFalse(DangerRingDisplay.ShowsPriorityRing(isPriorityTarget: true, isRescueTarget: false));
        }

        [Test]
        public void 危険円の濃さはD50で0_D100で1()
        {
            Assert.AreEqual(0f, DangerRingDisplay.DangerAmount01(50f), 1e-4f);
            Assert.AreEqual(0.5f, DangerRingDisplay.DangerAmount01(75f), 1e-4f);
            Assert.AreEqual(1f, DangerRingDisplay.DangerAmount01(100f), 1e-4f);
            Assert.AreEqual(0f, DangerRingDisplay.DangerAmount01(10f), 1e-4f);
        }

        [Test]
        public void 脈打ちはD85から()
        {
            Assert.IsFalse(DangerRingDisplay.Pulses(84.9f));
            Assert.IsTrue(DangerRingDisplay.Pulses(85f));
        }

        [Test]
        public void 脈打たないDでは明るさを動かさない()
        {
            Assert.AreEqual(1f, DangerRingDisplay.PulseAmount01(60f, 1.234f, 2f), 1e-4f);
        }

        [Test]
        public void 脈打つDでは0から1を往復する()
        {
            float low = DangerRingDisplay.PulseAmount01(90f, 0.75f, 1f);   // sin(1.5π) = -1
            float high = DangerRingDisplay.PulseAmount01(90f, 0.25f, 1f);  // sin(0.5π) = +1

            Assert.AreEqual(0f, low, 1e-3f);
            Assert.AreEqual(1f, high, 1e-3f);
        }
    }
}
