using NUnit.Framework;

namespace Toufuku.Session.Tests
{
    /// <summary>
    /// 展示ビルドの生存確認ファイルの起動引数 — Issue #65（Tools/exhibit_watchdog.ps1 が -toufukuHeartbeat を付けて起動する）
    /// </summary>
    public class ExhibitHeartbeatTests
    {
        [Test]
        public void キーのすぐうしろの値を返す()
        {
            string[] args = { "Toufuku.exe", "-screen-fullscreen", "1", "-toufukuHeartbeat", @"C:\Temp\beat.txt" };
            Assert.AreEqual(@"C:\Temp\beat.txt", ExhibitHeartbeat.FindArgument(args, ExhibitHeartbeat.CommandLineKey));
        }

        [Test]
        public void 大文字小文字は区別しない()
        {
            string[] args = { "Toufuku.exe", "-TOUFUKUHEARTBEAT", "beat.txt" };
            Assert.AreEqual("beat.txt", ExhibitHeartbeat.FindArgument(args, ExhibitHeartbeat.CommandLineKey));
        }

        [Test]
        public void キーがないか値がなければnull()
        {
            Assert.IsNull(ExhibitHeartbeat.FindArgument(new[] { "Toufuku.exe" }, ExhibitHeartbeat.CommandLineKey));
            Assert.IsNull(ExhibitHeartbeat.FindArgument(new[] { "Toufuku.exe", "-toufukuHeartbeat" }, ExhibitHeartbeat.CommandLineKey));
            Assert.IsNull(ExhibitHeartbeat.FindArgument(null, ExhibitHeartbeat.CommandLineKey));
        }
    }
}
