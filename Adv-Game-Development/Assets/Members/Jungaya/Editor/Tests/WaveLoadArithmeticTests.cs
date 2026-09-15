using NUnit.Framework;
using Toufuku.Playtest;
using Toufuku.Rescue;
using UnityEngine;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#53 8章「大負荷ウェーブ候補の処理負荷比較」の表と同じ値が出る。#57 でわりあいからの計算と T0-3M の判定を追加。</summary>
    public class WaveLoadArithmeticTests
    {
        CustomerKindTable _table;

        [SetUp]
        public void SetUp()
        {
            // 付録B B-1 の初期値だけを持った表
            _table = ScriptableObject.CreateInstance<CustomerKindTable>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_table);
        }

        [Test]
        public void T3の比率と付録B_B1から20_94秒と1_094発が出る()
        {
            var t3 = new CustomerKindWeights { normal = 75f, moving = 6.25f, distant = 9.375f, greedy = 9.375f };
            KindMix mix = WaveLoadArithmetic.MixOf(t3, _table);

            Assert.AreEqual(20.9375, mix.SecondsToDanger100, 1e-5);
            Assert.AreEqual(1.09375, mix.HitsPerRescue, 1e-5);
        }

        [TestCase(FinalWaveAdd.Plus3, 1.47)]
        [TestCase(FinalWaveAdd.Plus4, 1.37)]
        [TestCase(FinalWaveAdd.Plus5, 1.28)]
        public void 時間割の大負荷ウェーブから計算しても8章の表と一致する(FinalWaveAdd add, double cycle)
        {
            var timetable = new SpawnTimetable { FinalWaveAdd = add };
            WaveCandidate c = timetable.HeaviestCandidate(2, _table, out _, out _);

            Assert.AreEqual(10 + (int)add, c.TotalCap);
            Assert.AreEqual(cycle, c.AllowedCycleSeconds, 0.005);
        }

        [Test]
        public void 小負荷ウェーブは遠方客が解禁される0時48分がいちばん重く_中負荷ウェーブは大負荷の加4より重い()
        {
            var timetable = new SpawnTimetable();

            // 0:48 以降 70/0/10/20 → 21.8秒・1.2発、上限13人
            WaveCandidate small = timetable.HeaviestCandidate(0, _table, out float smallAt, out _);
            Assert.AreEqual(48f, smallAt, 0.001f);
            Assert.AreEqual(1.0 / (13.0 / 21.8 * 1.2), small.AllowedCycleSeconds, 0.001);

            // 11月 +15 → 60/21.82/10.91/7.27 → 20.8秒・1.0727発（欲張りだけ2発）、上限15人
            WaveCandidate middle = timetable.HeaviestCandidate(1, _table, out _, out _);
            double middleHits = 0.60 + (40.0 / 55.0) * (0.30 + 0.15 + 0.10 * 2);
            Assert.AreEqual(15, middle.TotalCap);
            Assert.AreEqual(1.0 / (15.0 / 20.8 * middleHits), middle.AllowedCycleSeconds, 0.001);
            Assert.AreEqual(1.293, middle.AllowedCycleSeconds, 0.001);

            WaveCandidate final4 = timetable.HeaviestCandidate(2, _table, out _, out _);
            Assert.Less(middle.AllowedCycleSeconds, final4.AllowedCycleSeconds);
        }

        [Test]
        public void p75が1_45秒なら加3だけ残る_53の例()
        {
            Assert.AreEqual(CycleVerdict.Keep, WaveLoadArithmetic.Judge(WaveLoadArithmetic.Of(3), 1.2, 1.45));
            Assert.AreEqual(CycleVerdict.DropSlowQuarter, WaveLoadArithmetic.Judge(WaveLoadArithmetic.Of(4), 1.2, 1.45));
            Assert.AreEqual(CycleVerdict.DropSlowQuarter, WaveLoadArithmetic.Judge(WaveLoadArithmetic.Of(5), 1.2, 1.45));
        }

        [Test]
        public void 中央値でも許容周期より遅ければ中央値で外す()
        {
            Assert.AreEqual(CycleVerdict.DropMedian, WaveLoadArithmetic.Judge(WaveLoadArithmetic.Of(5), 1.30, 1.50));
            // +5 の許容周期は 1.276秒。p75 がそれ以下なら残す
            Assert.AreEqual(CycleVerdict.Keep, WaveLoadArithmetic.Judge(WaveLoadArithmetic.Of(5), 1.10, 1.27));
        }

        [Test]
        public void 未測定なら判定を出さず_測定値があれば全ウェーブぶんの判定が出る()
        {
            var timetable = new SpawnTimetable();

            StringAssert.Contains("未測定", timetable.DescribeLoad(_table, 0.0, 0.0));
            StringAssert.DoesNotContain("T3 候補", timetable.DescribeLoad(_table, 0.0, 0.0));

            string report = timetable.DescribeLoad(_table, 1.2, 1.38);
            StringAssert.Contains("小負荷ウェーブ", report);
            StringAssert.Contains("中負荷ウェーブ", report);
            StringAssert.Contains("大負荷ウェーブ", report);
            StringAssert.Contains("○ 追いつく", report);                      // 小（許容 1.40秒 ≥ p75 1.38）。T3 の候補ではない
            StringAssert.Contains("× 追いつかない → 加算人数を見直す", report); // 中（1.29）
            StringAssert.Contains("× T3 候補から外す", report);               // 大+4（1.37）だけが T3 の候補
            StringAssert.DoesNotContain("○ 残す", report);
        }

        [TestCase(3, 13, 0.62, 0.68, 1.47)]
        [TestCase(4, 14, 0.67, 0.73, 1.37)]
        [TestCase(5, 15, 0.72, 0.78, 1.28)]
        public void 仕様書の表と一致する(int added, int totalCap, double rescues, double hits, double cycle)
        {
            WaveCandidate c = WaveLoadArithmetic.Of(added);

            Assert.AreEqual(totalCap, c.TotalCap);
            Assert.AreEqual(rescues, c.RequiredRescuesPerSecond, 0.005);
            Assert.AreEqual(hits, c.RequiredHitsPerSecond, 0.005);
            Assert.AreEqual(cycle, c.AllowedCycleSeconds, 0.005);
        }

        [Test]
        public void 人数が増えるほど許容される周期は短くなる()
        {
            var list = WaveLoadArithmetic.FinalCandidates();

            Assert.AreEqual(3, list.Count);
            Assert.Greater(list[0].AllowedCycleSeconds, list[1].AllowedCycleSeconds);
            Assert.Greater(list[1].AllowedCycleSeconds, list[2].AllowedCycleSeconds);
        }
    }
}
