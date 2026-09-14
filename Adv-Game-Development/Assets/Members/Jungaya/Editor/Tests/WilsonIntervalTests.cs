using NUnit.Framework;
using Toufuku.Playtest;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#50 母数と 95% 信頼区間を記録する。0 件でも上限が出ることが要点。</summary>
    public class WilsonIntervalTests
    {
        [Test]
        public void 母数がなければ区間も出ない()
        {
            RateWithInterval r = WilsonInterval.Of(0, 0);
            Assert.IsFalse(r.HasData);
            Assert.AreEqual(0.0, r.Rate, 1e-9);
            Assert.AreEqual("-", r.Describe());
        }

        [Test]
        public void 点推定は件数を母数で割った値()
        {
            RateWithInterval r = WilsonInterval.Of(4, 200);
            Assert.AreEqual(0.02, r.Rate, 1e-9);
            Assert.AreEqual(4, r.Count);
            Assert.AreEqual(200, r.Total);
        }

        [Test]
        public void ゼロ件でも上限が出る()
        {
            // 正規近似だと 0 幅になってしまう場面。ここが信頼区間を記録する意味
            RateWithInterval r = WilsonInterval.Of(0, 200);
            Assert.AreEqual(0.0, r.Lower, 1e-9);
            Assert.Greater(r.Upper, 0.0);
        }

        [Test]
        public void 意図した振り二百回なら上限は二パーセント未満()
        {
            // 誤発射側は母数 200。0 件なら「2% 以下」と区間でも言い切れる
            RateWithInterval r = WilsonInterval.Of(0, CooldownTestPlan.IntendedSwingsPerCondition);
            Assert.Less(r.Upper, CooldownTestPlan.MaxMisfireRate);
            Assert.AreEqual(0.0188, r.Upper, 0.0005);
        }

        [Test]
        public void 連投五十組では上限が二パーセントまで下がらない()
        {
            // 欠落側は母数 50。0 件でも上限は約 7%。だから合否は点推定で見て、区間は記録に残すだけ
            RateWithInterval r = WilsonInterval.Of(0, CooldownTestPlan.PairsPerCondition);
            Assert.Greater(r.Upper, CooldownTestPlan.MaxMissedSecondRate);
            Assert.AreEqual(0.0713, r.Upper, 0.0005);
        }

        [Test]
        public void 区間はゼロから一に収まる()
        {
            RateWithInterval all = WilsonInterval.Of(50, 50);
            Assert.GreaterOrEqual(all.Lower, 0.0);
            Assert.LessOrEqual(all.Upper, 1.0);
            Assert.AreEqual(1.0, all.Rate, 1e-9);

            RateWithInterval none = WilsonInterval.Of(0, 50);
            Assert.GreaterOrEqual(none.Lower, 0.0);
            Assert.LessOrEqual(none.Upper, 1.0);
        }

        [Test]
        public void 件数は母数で頭打ちになる()
        {
            RateWithInterval r = WilsonInterval.Of(80, 50);
            Assert.AreEqual(50, r.Count);
            Assert.AreEqual(1.0, r.Rate, 1e-9);
        }

        [Test]
        public void 区間は点推定を挟む()
        {
            RateWithInterval r = WilsonInterval.Of(3, 200);
            Assert.LessOrEqual(r.Lower, r.Rate);
            Assert.GreaterOrEqual(r.Upper, r.Rate);
        }

        [Test]
        public void 母数が増えると区間は狭くなる()
        {
            RateWithInterval few = WilsonInterval.Of(2, 100);
            RateWithInterval many = WilsonInterval.Of(20, 1000);
            Assert.Less(many.Upper - many.Lower, few.Upper - few.Lower);
        }
    }
}
