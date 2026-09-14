using System.Collections.Generic;
using NUnit.Framework;
using Toufuku.GameInput;
using Toufuku.Playtest;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#50 誤発射 ≤2% かつ 意図的連投の欠落 ≤2% を両方満たす最小値を採用する。</summary>
    public class CooldownTestSummaryTests
    {
        /// <summary>1 条件分の母数（単発 100 回・連投 50 組）をまとめて 1 行で作る。</summary>
        static CooldownConditionRecord Row(CooldownPreset preset, int extraFires, int missedSecond,
            int strapDeviation = 0, int caseContact = 0)
        {
            return new CooldownConditionRecord
            {
                participantNo = 1,
                preset = preset,
                cooldownSeconds = CooldownTestPlan.SecondsOf(preset),
                videoSingleSwings = CooldownTestPlan.SinglesPerCondition,
                videoPairSets = CooldownTestPlan.PairsPerCondition,
                videoExtraFires = extraFires,
                videoMissedSecond = missedSecond,
                strapDeviation = strapDeviation,
                caseContact = caseContact
            };
        }

        /// <summary>短いクールダウンほど誤発射が増え、長いほど欠落が増える——という想定どおりの 4 行。</summary>
        static List<CooldownConditionRecord> TypicalRecords()
        {
            return new List<CooldownConditionRecord>
            {
                Row(CooldownPreset.Sec040, 9, 0), // 誤発射 4.5% — 短すぎる
                Row(CooldownPreset.Sec050, 3, 1), // 誤発射 1.5% / 欠落 2.0% — 両方満たす
                Row(CooldownPreset.Sec060, 1, 4), // 欠落 8.0% — 長すぎる
                Row(CooldownPreset.Sec065, 0, 9)  // 欠落 18.0%
            };
        }

        [Test]
        public void 値ごとに誤発射率と欠落率が出る()
        {
            CooldownTestSummary summary = CooldownTestSummary.Of(TypicalRecords());

            CooldownValueResult fast = summary.ValueOf(CooldownPreset.Sec040);
            Assert.AreEqual(200, fast.IntendedSwings);
            Assert.AreEqual(0.045, fast.Misfire.Rate, 1e-9);
            Assert.AreEqual(50, fast.PairSets);
            Assert.AreEqual(0.0, fast.MissedRate.Rate, 1e-9);
            Assert.IsFalse(fast.MisfireOk);
            Assert.IsTrue(fast.MissedOk);
        }

        [Test]
        public void 両方を満たす最小値が採用される()
        {
            CooldownTestSummary summary = CooldownTestSummary.Of(TypicalRecords());

            Assert.IsTrue(summary.HasAdopted);
            Assert.AreEqual(CooldownPreset.Sec050, summary.Adopted);
            Assert.AreEqual(0.50f, summary.AdoptedSeconds, 0.001f);
            Assert.IsTrue(summary.Passed, summary.FailureSummary());
            Assert.IsFalse(summary.NeedsDetectorChange);
        }

        [Test]
        public void より短い値も満たすならそちらを採る()
        {
            var records = TypicalRecords();
            records[0] = Row(CooldownPreset.Sec040, 4, 0); // 誤発射 2.0% ちょうど

            CooldownTestSummary summary = CooldownTestSummary.Of(records);
            Assert.AreEqual(CooldownPreset.Sec040, summary.Adopted, "2% ちょうどは満たす側");
        }

        [Test]
        public void 片方だけ満たす値は採用しない()
        {
            var records = new List<CooldownConditionRecord>
            {
                Row(CooldownPreset.Sec040, 9, 0),  // 誤発射だけ×
                Row(CooldownPreset.Sec050, 8, 0),  // 誤発射だけ×
                Row(CooldownPreset.Sec060, 0, 5),  // 欠落だけ×
                Row(CooldownPreset.Sec065, 0, 9)   // 欠落だけ×
            };

            CooldownTestSummary summary = CooldownTestSummary.Of(records);

            Assert.IsFalse(summary.HasAdopted);
            Assert.IsFalse(summary.Passed);
            Assert.IsTrue(summary.NeedsDetectorChange,
                "どの値も両方は満たさない → 閾値・ピーク検出・ヒステリシスを変える");
        }

        [Test]
        public void 母数が足りなければ採用できない()
        {
            var records = new List<CooldownConditionRecord>();
            foreach (CooldownPreset preset in CooldownTestPlan.Conditions)
            {
                CooldownConditionRecord r = Row(preset, 0, 0);
                r.videoSingleSwings = 40; // 単発 100 回に届いていない
                records.Add(r);
            }

            CooldownTestSummary summary = CooldownTestSummary.Of(records);

            Assert.IsFalse(summary.ValueOf(CooldownPreset.Sec040).HasEnoughSwings);
            Assert.IsFalse(summary.HasAdopted);
            Assert.IsFalse(summary.Passed);
            Assert.IsFalse(summary.NeedsDetectorChange, "母数が足りないだけなら検出の作り直しではない");
        }

        [Test]
        public void 安全事象があれば不合格()
        {
            var records = TypicalRecords();
            records[2] = Row(CooldownPreset.Sec060, 1, 4, strapDeviation: 1);

            CooldownTestSummary summary = CooldownTestSummary.Of(records);

            Assert.AreEqual(1, summary.SafetyIncidents);
            Assert.IsFalse(summary.Passed);
            StringAssert.Contains("ストラップ", summary.FailureSummary());
        }

        [Test]
        public void 筐体接触も安全事象として数える()
        {
            var records = TypicalRecords();
            records[0] = Row(CooldownPreset.Sec040, 9, 0, caseContact: 2);

            CooldownTestSummary summary = CooldownTestSummary.Of(records);
            Assert.AreEqual(2, summary.SafetyIncidents);
            Assert.AreEqual(2, summary.ValueOf(CooldownPreset.Sec040).SafetyIncidents);
        }

        [Test]
        public void 動画が未入力の行は数えない()
        {
            var records = TypicalRecords();
            records[1].videoExtraFires = CooldownConditionRecord.NotEntered;

            CooldownTestSummary summary = CooldownTestSummary.Of(records);

            Assert.AreEqual(1, summary.Incomplete);
            Assert.AreEqual(3, summary.Rows);
            Assert.AreEqual(0, summary.ValueOf(CooldownPreset.Sec050).IntendedSwings);
            Assert.IsFalse(summary.HasAdopted);
            Assert.IsFalse(summary.Passed);
        }

        [Test]
        public void 未入力でも安全事象だけは数える()
        {
            var records = TypicalRecords();
            records[1].videoExtraFires = CooldownConditionRecord.NotEntered;
            records[1].strapDeviation = 1;

            CooldownTestSummary summary = CooldownTestSummary.Of(records);
            Assert.AreEqual(1, summary.SafetyIncidents, "動画を見る前でも安全は数える");
        }

        [Test]
        public void 複数人の行は値ごとに足し合わせる()
        {
            var records = new List<CooldownConditionRecord>();
            for (int no = 1; no <= 2; no++)
            {
                CooldownConditionRecord r = Row(CooldownPreset.Sec050, 1, 0);
                r.participantNo = no;
                r.videoSingleSwings = 50;
                r.videoPairSets = 25;
                records.Add(r);
            }

            CooldownTestSummary summary = CooldownTestSummary.Of(records);
            CooldownValueResult value = summary.ValueOf(CooldownPreset.Sec050);

            Assert.AreEqual(2, value.Rows);
            Assert.AreEqual(200, value.IntendedSwings);
            Assert.AreEqual(50, value.PairSets);
            Assert.AreEqual(2, value.ExtraFires);
            Assert.IsTrue(value.HasEnoughData);
        }

        [Test]
        public void 候補外の秒数は集計に入らない()
        {
            var records = TypicalRecords();
            CooldownConditionRecord stray = Row(CooldownPreset.Sec050, 0, 0);
            stray.preset = CooldownPreset.Custom;
            records.Add(stray);

            CooldownTestSummary summary = CooldownTestSummary.Of(records);
            Assert.AreEqual(4, summary.Rows);
        }

        [Test]
        public void 記録が無ければ何も採用しない()
        {
            CooldownTestSummary summary = CooldownTestSummary.Of(null);

            Assert.AreEqual(0, summary.Rows);
            Assert.IsFalse(summary.HasAdopted);
            Assert.IsFalse(summary.Passed);
            Assert.IsFalse(summary.NeedsDetectorChange);
            Assert.AreEqual(CooldownTestPlan.ConditionCount, summary.Values.Count);
        }
    }
}
