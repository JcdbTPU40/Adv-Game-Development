using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#63 CSV の書式・ファイル名・起動引数。</summary>
    public class PlaytestCsvTests
    {
        [Test]
        public void カンマ_引用符_改行を含む値だけ囲む()
        {
            Assert.AreEqual("abc", PlaytestCsv.Escape("abc"));
            Assert.AreEqual("\"a,b\"", PlaytestCsv.Escape("a,b"));
            Assert.AreEqual("\"say \"\"hi\"\"\"", PlaytestCsv.Escape("say \"hi\""));
            Assert.AreEqual("\"a\nb\"", PlaytestCsv.Escape("a\nb"));
            Assert.AreEqual("", PlaytestCsv.Escape(null));
        }

        [Test]
        public void 数値は小数点ピリオド_未設定とNaNは空欄()
        {
            Assert.AreEqual("1.5", PlaytestCsv.Num(1.5));
            Assert.AreEqual("", PlaytestCsv.Num(null));
            Assert.AreEqual("", PlaytestCsv.Num(double.NaN));
            Assert.AreEqual("", PlaytestCsv.Int(null));
            Assert.AreEqual("1", PlaytestCsv.Bool(true));
            Assert.AreEqual("0", PlaytestCsv.Bool(false));
            Assert.AreEqual("", PlaytestCsv.Bool(null));
        }

        [Test]
        public void イベント行の列数は列名と同じ()
        {
            var e = new PlaytestEvent(1.25, PlaytestEventType.Landing)
            {
                ThrowNo = 3, TargetId = 7, Category = "Normal", Color = "Kenkou", Omamori = "Kenkou", IsBlack = false,
                Accuracy = 0.3, Zone = "Center", ColorError = false, Gain = 150, En = 450, Multiplier = 1.2, FukuChain = 3,
                Rescued = true, PriorityTargetId = 7, InputTime = 1.0, ReceiveTime = 2.0, FireTime = 2.01, Strength = 300,
                FlightSeconds = 0.4, Distance = 9.5, PosX = 1, PosZ = 20, Danger = 65, Rating = 35, Rank = "C", Stage = 1,
                Value = 2, Detail = "x"
            };
            Assert.AreEqual(PlaytestCsv.EventColumns.Length, PlaytestCsv.EventRow(e).Split(',').Length);

            var empty = new PlaytestEvent(0, PlaytestEventType.SessionStart);
            Assert.AreEqual(PlaytestCsv.EventColumns.Length, PlaytestCsv.EventRow(empty).Split(',').Length);
        }

        [Test]
        public void メタ行の後に列名とデータ行()
        {
            var meta = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("test_id", "T3"),
                new KeyValuePair<string, string>("params.X", "{\"a\":1,\"b\":2}")
            };
            var rows = new List<PlaytestSummaryRow> { new PlaytestSummaryRow("t3", "0-60s.throws", "12") };

            string[] lines = PlaytestCsv.BuildSummary(meta, rows).Split(new[] { PlaytestCsv.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual("#meta,test_id,T3", lines[0]);
            Assert.AreEqual("#meta,params.X,\"{\"\"a\"\":1,\"\"b\"\":2}\"", lines[1]);
            Assert.AreEqual("section,metric,value", lines[2]);
            Assert.AreEqual("t3,0-60s.throws,12", lines[3]);
        }

        [Test]
        public void ファイル名にテストID_日付_ビルド番号_シード_参加者ID()
        {
            var meta = new PlaytestMeta { testId = "T3", participantId = "P05" };
            string stem = meta.FileStem(new DateTime(2026, 9, 15, 14, 30, 5), "17", 42);
            Assert.AreEqual("T3_20260915-143005_b17_s42_P05", stem);

            meta.participantId = "";
            Assert.AreEqual("T3_20260915-143005_b17_s-8", meta.FileStem(new DateTime(2026, 9, 15, 14, 30, 5), "17", -8));
        }

        [Test]
        public void ファイル名に使えない文字は置き換える()
        {
            Assert.AreEqual("T2-A-b", PlaytestMeta.SanitizeFilePart("T2/A b"));
            Assert.AreEqual("0.1.0", PlaytestMeta.SanitizeFilePart("0.1.0"));
            Assert.AreEqual("NA", PlaytestMeta.SanitizeFilePart("  "));
            Assert.AreEqual("a-b", PlaytestMeta.SanitizeFilePart("a_b"));
        }

        [Test]
        public void 起動引数で上書きする()
        {
            var meta = new PlaytestMeta { seedMode = PlaytestSeedMode.RandomEachPlay };
            List<string> applied = meta.ApplyCommandLine(new[]
            {
                "Game.exe", "-batchmode", "-playtestTestId", "T2", "-PLAYTESTSEED", "99", "-playtestBuild", "17",
                "-playtestParticipant", "P01", "-playtestOperator", "Jungaya", "-playtestUnknown", "x"
            });

            Assert.AreEqual("T2", meta.testId);
            Assert.AreEqual(99, meta.seed);
            Assert.AreEqual(PlaytestSeedMode.Fixed, meta.seedMode);
            Assert.AreEqual("17", meta.buildNumber);
            Assert.AreEqual("P01", meta.participantId);
            Assert.AreEqual("Jungaya", meta.operatorName);
            CollectionAssert.AreEqual(new[] { "testid", "seed", "build", "participant", "operator" }, applied);
        }

        [Test]
        public void シードにrandomを渡すとプレイごとの新しいシード_不正な値は無視()
        {
            var meta = new PlaytestMeta { seed = 5 };
            meta.ApplyCommandLine(new[] { "-playtestSeed", "random" });
            Assert.AreEqual(PlaytestSeedMode.RandomEachPlay, meta.seedMode);

            var bad = new PlaytestMeta { seed = 5 };
            bad.ApplyCommandLine(new[] { "-playtestSeed", "abc", "-playtestSeed" });
            Assert.AreEqual(5, bad.seed);
            Assert.AreEqual(PlaytestSeedMode.Fixed, bad.seedMode);
        }
    }
}
