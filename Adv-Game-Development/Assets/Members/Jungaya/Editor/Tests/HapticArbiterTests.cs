using NUnit.Framework;

namespace Toufuku.Feedback.Tests
{
    /// <summary>#64 振動は波形を足さず、100ms 以内の重なりは上位 1 つへ置き換える（救済成功 ＞ 命中 ＞ 発射）。</summary>
    public class HapticArbiterTests
    {
        static readonly HapticPulse Throw = new HapticPulse(HapticKind.Throw, 0.040f, 0.3f);
        static readonly HapticPulse Hit = new HapticPulse(HapticKind.Hit, 0.080f, 0.6f);
        static readonly HapticPulse Rescue = new HapticPulse(HapticKind.Rescue, 0.120f, 1.0f);

        HapticArbiter _a;

        [SetUp]
        public void SetUp()
        {
            _a = new HapticArbiter();
        }

        [Test]
        public void 単発の振動は長さの間だけ出る()
        {
            Assert.IsTrue(_a.Request(Throw, 1.0));
            Assert.AreEqual(0.3f, _a.AmplitudeAt(1.0));
            Assert.AreEqual(0.3f, _a.AmplitudeAt(1.039));
            Assert.AreEqual(0f, _a.AmplitudeAt(1.040));
        }

        [Test]
        public void 命中中に救済が来たら救済へ置き換わり_強さは足されない()
        {
            _a.Request(Hit, 1.0);
            Assert.IsTrue(_a.Request(Rescue, 1.0));

            Assert.AreEqual(1.0f, _a.AmplitudeAt(1.0));
            Assert.IsTrue(_a.TryGetActive(1.1, out HapticPulse p));
            Assert.AreEqual(HapticKind.Rescue, p.Kind);
            Assert.AreEqual(0f, _a.AmplitudeAt(1.12));
        }

        [Test]
        public void 救済中100ms以内の命中や発射は捨てられる()
        {
            _a.Request(Rescue, 1.0);
            Assert.IsFalse(_a.Request(Hit, 1.05));
            Assert.IsFalse(_a.Request(Throw, 1.099));

            Assert.IsTrue(_a.TryGetActive(1.099, out HapticPulse p));
            Assert.AreEqual(HapticKind.Rescue, p.Kind);
            Assert.AreEqual(1.0f, _a.AmplitudeAt(1.099));
        }

        [Test]
        public void 窓を過ぎた下位の要求は新しいほうへ置き換わる()
        {
            _a.Request(Rescue, 1.0);
            Assert.IsTrue(_a.Request(Throw, 1.101));
            Assert.AreEqual(0.3f, _a.AmplitudeAt(1.101));
        }

        [Test]
        public void 振動が終わった後の下位の要求は採用される()
        {
            _a.Request(Hit, 1.0);
            Assert.IsTrue(_a.Request(Throw, 1.09));
            Assert.AreEqual(0.3f, _a.AmplitudeAt(1.09));
        }

        [Test]
        public void 同じ優先度は新しいほうで始め直す()
        {
            var longHit = new HapticPulse(HapticKind.Hit, 0.120f, 0.6f);
            _a.Request(Hit, 1.0);
            Assert.IsTrue(_a.Request(longHit, 1.05));
            Assert.AreEqual(0.6f, _a.AmplitudeAt(1.16));
            Assert.AreEqual(0f, _a.AmplitudeAt(1.17));
        }

        [Test]
        public void 発射直後の命中は命中へ置き換わる()
        {
            _a.Request(Throw, 1.0);
            Assert.IsTrue(_a.Request(Hit, 1.02));
            Assert.AreEqual(0.6f, _a.AmplitudeAt(1.02));
        }

        [Test]
        public void Stopで止まる()
        {
            _a.Request(Rescue, 1.0);
            _a.Stop();
            Assert.AreEqual(0f, _a.AmplitudeAt(1.0));
            Assert.IsTrue(_a.Request(Throw, 1.01));
        }
    }
}
