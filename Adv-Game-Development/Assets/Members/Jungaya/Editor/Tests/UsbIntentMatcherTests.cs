using System.Collections.Generic;
using NUnit.Framework;
using Toufuku.Playtest;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#52 合図と発射の突き合わせ（欠落・誤発射・無効にした合図）。</summary>
    public class UsbIntentMatcherTests
    {
        static List<double> Cues(int n)
        {
            var cues = new List<double>();
            for (int k = 0; k < n; k++) cues.Add(3.0 + k * 2.0);
            return cues;
        }

        static List<double> FiresFor(List<double> cues, double reaction = 0.4)
        {
            var fires = new List<double>();
            foreach (double c in cues) fires.Add(c + reaction);
            return fires;
        }

        [Test]
        public void 全部の合図に一発ずつなら欠落も誤発射も零()
        {
            List<double> cues = Cues(100);
            UsbIntentResult r = UsbIntentMatcher.Match(cues, null, FiresFor(cues));

            Assert.AreEqual(100, r.Intended);
            Assert.AreEqual(100, r.Matched);
            Assert.AreEqual(0, r.Missed);
            Assert.AreEqual(0, r.Extra);
            Assert.AreEqual(0.0, r.MissRate.Value, 1e-12);
        }

        [Test]
        public void 応答の無い合図は欠落()
        {
            List<double> cues = Cues(100);
            List<double> fires = FiresFor(cues);
            fires.RemoveAt(10);
            fires.RemoveAt(50);

            UsbIntentResult r = UsbIntentMatcher.Match(cues, null, fires);
            Assert.AreEqual(2, r.Missed);
            Assert.AreEqual(0.02, r.MissRate.Value, 1e-12);
            Assert.IsFalse(UsbGatePlan.MissRateOk(r.MissRate.Value), "2% ちょうどは「2% 未満」を満たさない");
        }

        [Test]
        public void 一つの合図への二発目と窓の外の発射は誤発射()
        {
            List<double> cues = Cues(100);
            List<double> fires = FiresFor(cues);
            fires.Add(cues[5] + 0.9);   // 同じ合図への 2 発目（振り戻しで出た弾）
            fires.Add(cues[20] + 1.5);  // どの窓にも入らない

            UsbIntentResult r = UsbIntentMatcher.Match(cues, null, fires);
            Assert.AreEqual(100, r.Matched);
            Assert.AreEqual(2, r.Extra);
            Assert.IsTrue(UsbGatePlan.FalseFireRateOk(r.FalseFireRate.Value), "2% ちょうどは「2% 以下」で合格");
        }

        [Test]
        public void 合図の直前の発射も窓の中なら応答()
        {
            var cues = new List<double> { 3.0 };
            Assert.AreEqual(1, UsbIntentMatcher.Match(cues, null, new List<double> { 2.7 }).Matched);
            Assert.AreEqual(1, UsbIntentMatcher.Match(cues, null, new List<double> { 4.2 }).Matched);
            Assert.AreEqual(1, UsbIntentMatcher.Match(cues, null, new List<double> { 2.69 }).Extra);
            Assert.AreEqual(1, UsbIntentMatcher.Match(cues, null, new List<double> { 4.21 }).Extra);
        }

        [Test]
        public void 無効にした合図は分母から外し窓の発射も数えない()
        {
            List<double> cues = Cues(102);
            List<double> fires = FiresFor(cues);
            fires.RemoveAt(7);          // 合図 7 は振らなかった（無効にする）
            fires.Add(cues[30] + 0.8);  // 合図 30 は無効にした合図への 2 発目 → 数えない

            UsbIntentResult r = UsbIntentMatcher.Match(cues, new HashSet<int> { 7, 30 }, fires);
            Assert.AreEqual(100, r.Intended);
            Assert.AreEqual(2, r.Voided);
            Assert.AreEqual(100, r.Matched);
            Assert.AreEqual(0, r.Missed);
            Assert.AreEqual(0, r.Extra);
            Assert.AreEqual(2, r.IgnoredInVoid);
        }

        [Test]
        public void 合図が無ければ率は欠測()
        {
            UsbIntentResult r = UsbIntentMatcher.Match(new List<double>(), null, new List<double> { 1.0 });
            Assert.AreEqual(0, r.Intended);
            Assert.AreEqual(1, r.Extra);
            Assert.IsNull(r.MissRate);
            Assert.IsNull(r.FalseFireRate);
        }
    }
}
