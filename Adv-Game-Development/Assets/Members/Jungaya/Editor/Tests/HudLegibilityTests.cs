using NUnit.Framework;
using Toufuku.Hud;

namespace Toufuku.Scoring.Tests
{
    /// <summary>
    /// 縁と今のランクが「画面外（列のうしろ）から読める大きさ」か — Issue #61（仕様書 v8 13章「観戦体験の設計」）
    ///
    /// 展示モニターは会場備品でサイズ未定なので、小さめの 50インチ・画面から 7m（プレイヤー約2.5m＋観客境界2m＋列）で確かめる。
    /// 読める目安は文字の高さが 22分角以上（ANSI/HFES 100 の推奨）。
    /// </summary>
    public class HudLegibilityTests
    {
        const float Inches = HudLegibility.ReferenceDiagonalInches;
        const float Distance = HudLegibility.SpectatorDistanceMeters;

        [Test]
        public void 画面の高さ_50インチ16対9()
        {
            Assert.AreEqual(62.26f, HudLegibility.ScreenHeightCm(50f), 0.01f);
        }

        [Test]
        public void 文字の高さ1cmは約1点56mから読める()
        {
            Assert.AreEqual(1.5626f, HudLegibility.ReadableDistanceMeters(1f), 0.001f);
        }

        [Test]
        public void 必要なわりあいから読める距離にもどせる()
        {
            float ratio = HudLegibility.RequiredFontSizeRatio(Distance, Inches);
            Assert.AreEqual(Distance, HudLegibility.ReadableDistanceMetersForFont(ratio, Inches), 0.001f);
        }

        [Test]
        public void 縁の数字は列のうしろから読める()
        {
            Assert.GreaterOrEqual(HudLegibility.ReadableDistanceMetersForFont(ScoreBoardHud.DefaultEnFontRatio, Inches), Distance);
        }

        [Test]
        public void 今のランクは列のうしろから読める()
        {
            Assert.GreaterOrEqual(HudLegibility.ReadableDistanceMetersForFont(ScoreBoardHud.DefaultRankFontRatio, Inches), Distance);
        }

        [Test]
        public void 今日のベストも列のうしろから読める()
        {
            Assert.GreaterOrEqual(HudLegibility.ReadableDistanceMetersForFont(ScoreBoardHud.DefaultBestFontRatio, Inches), Distance);
        }

        [Test]
        public void HUDの縁は5桁で止める()
        {
            Assert.AreEqual("26000", HudUi.FormatScore(26000));
            Assert.AreEqual("99999", HudUi.FormatScore(123456));
            Assert.AreEqual("0", HudUi.FormatScore(-5));
        }
    }
}
