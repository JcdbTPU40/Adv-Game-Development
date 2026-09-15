using System.Collections.Generic;
using NUnit.Framework;
using Toufuku.Playtest;
using UnityEngine;

namespace Toufuku.Rescue.Tests
{
    /// <summary>
    /// 180秒の時間割（企画書 v8 8章「時間割」「負荷ウェーブ」、10章「解禁スケジュール」「出現比率」、付録B）— Issue #57
    /// </summary>
    public class SpawnTimetableTests
    {
        const float Eps = 0.001f;

        static void AssertWeights(CustomerKindWeights actual, float normal, float moving, float distant, float greedy, float boss)
        {
            float total = actual.Total;
            Assert.Greater(total, 0f);
            Assert.AreEqual(normal, actual.normal / total * 100f, 0.01f, "通常");
            Assert.AreEqual(moving, actual.moving / total * 100f, 0.01f, "移動");
            Assert.AreEqual(distant, actual.distant / total * 100f, 0.01f, "遠方");
            Assert.AreEqual(greedy, actual.greedy / total * 100f, 0.01f, "欲張り");
            Assert.AreEqual(boss, actual.boss / total * 100f, 0.01f, "ボス");
        }

        // Inspector で値を変えたときと同じように、シリアライズされた値だけを書きかえる
        static SpawnTimetable With(string json)
        {
            var t = new SpawnTimetable();
            JsonUtility.FromJsonOverwrite(json, t);
            return t;
        }

        // ---- 解禁 ----

        [TestCase(CustomerKind.Greedy, 38f)]
        [TestCase(CustomerKind.Distant, 48f)]
        [TestCase(CustomerKind.Moving, 60f)]
        public void 解禁時刻の直前は出ず_ちょうどから出る(CustomerKind kind, float unlock)
        {
            var t = new SpawnTimetable();

            Assert.IsFalse(t.IsUnlocked(kind, unlock - Eps));
            Assert.IsTrue(t.IsUnlocked(kind, unlock));
            Assert.AreEqual(0f, t.WeightsAt(unlock - Eps).Get(kind));
        }

        [Test]
        public void 通常客は最初から解禁されている()
        {
            Assert.IsTrue(new SpawnTimetable().IsUnlocked(CustomerKind.Normal, 0f));
        }

        [Test]
        public void ボス客は追加要素なのでOFFなら最後まで出ない_ONなら2分から()
        {
            var t = new SpawnTimetable();
            Assert.IsFalse(t.IsUnlocked(CustomerKind.Boss, 179.9f));
            Assert.AreEqual(0f, t.WeightsAt(179.9f).boss);

            t.BossEnabled = true;
            Assert.IsFalse(t.IsUnlocked(CustomerKind.Boss, 120f - Eps));
            Assert.IsTrue(t.IsUnlocked(CustomerKind.Boss, 120f));
        }

        [Test]
        public void 解禁の順番は通常_欲張り_遠方_移動()
        {
            var t = new SpawnTimetable();
            Assert.Less(t.UnlockSeconds(CustomerKind.Normal), t.UnlockSeconds(CustomerKind.Greedy));
            Assert.Less(t.UnlockSeconds(CustomerKind.Greedy), t.UnlockSeconds(CustomerKind.Distant));
            Assert.Less(t.UnlockSeconds(CustomerKind.Distant), t.UnlockSeconds(CustomerKind.Moving));
        }

        // ---- 出現比率（付録B SPAWN.TYPE.*） ----

        [Test]
        public void 付録Bの6月3段階_11月_1月MVPと一致する()
        {
            var t = new SpawnTimetable();

            // 06A 0:30〜0:38
            AssertWeights(t.UnlockedWeightsAt(30f), 100f, 0f, 0f, 0f, 0f);
            AssertWeights(t.UnlockedWeightsAt(38f - Eps), 100f, 0f, 0f, 0f, 0f);
            // 06B 0:38〜0:48
            AssertWeights(t.UnlockedWeightsAt(38f), 70f, 0f, 0f, 30f, 0f);
            AssertWeights(t.UnlockedWeightsAt(48f - Eps), 70f, 0f, 0f, 30f, 0f);
            // 06C 0:48〜1:00
            AssertWeights(t.UnlockedWeightsAt(48f), 55f, 0f, 15f, 30f, 0f);
            AssertWeights(t.WeightsAt(57f), 55f, 0f, 15f, 30f, 0f);
            // 11月
            AssertWeights(t.WeightsAt(60f), 45f, 30f, 15f, 10f, 0f);
            AssertWeights(t.WeightsAt(119.9f), 45f, 30f, 15f, 10f, 0f);
            // 1月 MVP（ボス5を通常へ戻す）
            AssertWeights(t.WeightsAt(120f), 60f, 10f, 15f, 15f, 0f);
            AssertWeights(t.WeightsAt(139.9f), 60f, 10f, 15f, 15f, 0f);
        }

        [Test]
        public void ボスを採用したビルドの1月は55_10_15_15_5()
        {
            var t = new SpawnTimetable { BossEnabled = true };
            AssertWeights(t.WeightsAt(130f), 55f, 10f, 15f, 15f, 5f);
        }

        [Test]
        public void 段階学習の0時00分から0時30分は通常客だけ()
        {
            var t = new SpawnTimetable();
            AssertWeights(t.WeightsAt(0f), 100f, 0f, 0f, 0f, 0f);
            AssertWeights(t.WeightsAt(29.9f), 100f, 0f, 0f, 0f, 0f);
        }

        [Test]
        public void 負荷ウェーブ中は通常客を15ポイント上げて残りを元の比率で按分する()
        {
            var t = new SpawnTimetable();

            // 大負荷ウェーブ: 1月 MVP 60/10/15/15 → 75/6.25/9.375/9.375（8章 T3 の算術の比率）
            AssertWeights(t.WeightsAt(150f), 75f, 6.25f, 9.375f, 9.375f, 0f);
            // 中負荷ウェーブ: 11月 45/30/15/10 → 60、残り40を 30:15:10 で按分
            AssertWeights(t.WeightsAt(90f), 60f, 40f * 30f / 55f, 40f * 15f / 55f, 40f * 10f / 55f, 0f);
            // 小負荷ウェーブ: 0:38〜0:48 は 70/0/0/30 → 85/0/0/15、0:48 以降は 55/0/15/30 → 70/0/10/20
            AssertWeights(t.WeightsAt(45f), 85f, 0f, 0f, 15f, 0f);
            AssertWeights(t.WeightsAt(50f), 70f, 0f, 10f, 20f, 0f);
        }

        [Test]
        public void 通常客の按分は合計100を保ち_通常客100なら変わらない()
        {
            var raw = new CustomerKindWeights { normal = 9f, moving = 6f, distant = 3f, greedy = 2f };   // 合計20
            CustomerKindWeights w = SpawnTimetable.WithNormalBonus(raw, 15f);

            Assert.AreEqual(100f, w.Total, 0.001f);
            AssertWeights(w, 60f, 40f * 6f / 11f, 40f * 3f / 11f, 40f * 2f / 11f, 0f);   // 45+15=60、残り40 を 6:3:2 で

            CustomerKindWeights onlyNormal = SpawnTimetable.WithNormalBonus(new CustomerKindWeights { normal = 1f }, 15f);
            AssertWeights(onlyNormal, 100f, 0f, 0f, 0f, 0f);
        }

        // ---- 同時上限と負荷ウェーブ ----

        [TestCase(0f)]
        [TestCase(39.9f)]
        [TestCase(55f)]
        [TestCase(79.9f)]
        [TestCase(105f)]
        [TestCase(139.9f)]
        public void ウェーブの外は基準の10人(float at)
        {
            var t = new SpawnTimetable();
            Assert.AreEqual(10, t.CapAt(at));
            Assert.IsFalse(t.WaveAt(at).IsActive);
        }

        [TestCase(40f, 10)]
        [TestCase(41.7f, 11)]
        [TestCase(43.4f, 12)]
        [TestCase(45f, 13)]
        [TestCase(54.9f, 13)]
        public void 小負荷ウェーブは0時40分から5秒かけて13人まで増える(float at, int cap)
        {
            Assert.AreEqual(cap, new SpawnTimetable().CapAt(at));
        }

        [TestCase(80f, 10)]
        [TestCase(81f, 11)]
        [TestCase(82f, 12)]
        [TestCase(83f, 13)]
        [TestCase(84f, 14)]
        [TestCase(85f, 15)]
        [TestCase(104.9f, 15)]
        [TestCase(105f, 10)]
        public void 中負荷ウェーブは1時20分から1秒に1人ずつ15人まで増え_1時45分で戻る(float at, int cap)
        {
            Assert.AreEqual(cap, new SpawnTimetable().CapAt(at));
        }

        [TestCase(FinalWaveAdd.Plus3, 13)]
        [TestCase(FinalWaveAdd.Plus4, 14)]
        [TestCase(FinalWaveAdd.Plus5, 15)]
        public void 大負荷ウェーブは切り替えた人数で3時00分まで続く(FinalWaveAdd add, int cap)
        {
            var t = new SpawnTimetable { FinalWaveAdd = add };

            Assert.AreEqual(10, t.CapAt(140f));
            Assert.AreEqual(cap, t.CapAt(145f));
            Assert.AreEqual(cap, t.CapAt(179.999f));
            Assert.AreEqual("大負荷ウェーブ", t.WaveAt(179.999f).Label);
            Assert.AreEqual(180f, t.WaveWindow(2).EndSeconds, 0.0001f);
        }

        [Test]
        public void 既定の大負荷ウェーブは基準の加4()
        {
            Assert.AreEqual(FinalWaveAdd.Plus4, new SpawnTimetable().FinalWaveAdd);
        }

        [Test]
        public void プレイ中に加算人数を変えると上限にすぐ反映される()
        {
            var t = new SpawnTimetable { FinalWaveAdd = FinalWaveAdd.Plus3 };
            Assert.AreEqual(13, t.CapAt(160f));

            t.FinalWaveAdd = FinalWaveAdd.Plus5;
            Assert.AreEqual(15, t.CapAt(160f));
        }

        [Test]
        public void 総上限15人をこえない()
        {
            SpawnTimetable t = With("{\"baseCap\":12}");

            Assert.AreEqual(15, t.CapAt(90f));   // 12+5=17 → 15
            t.FinalWaveAdd = FinalWaveAdd.Plus5;
            Assert.AreEqual(15, t.CapAt(170f));

            for (float at = 0f; at < SpawnTimetable.MatchEndSeconds; at += 0.25f)
                Assert.LessOrEqual(new SpawnTimetable { FinalWaveAdd = FinalWaveAdd.Plus5 }.CapAt(at), 15);
        }

        [TestCase(3, 0f, 5f, 0)]
        [TestCase(3, 1.666f, 5f, 0)]
        [TestCase(3, 1.667f, 5f, 1)]
        [TestCase(5, 1f, 5f, 1)]
        [TestCase(5, 4.999f, 5f, 4)]
        [TestCase(5, 5f, 5f, 5)]
        [TestCase(5, 30f, 5f, 5)]
        [TestCase(4, 0f, 0f, 4)]
        [TestCase(4, -0.1f, 5f, 0)]
        public void 増員は5秒かけて1人ずつ(int added, float since, float ramp, int expected)
        {
            Assert.AreEqual(expected, SpawnTimetable.RampedBonus(added, since, ramp));
        }

        // ---- 篝火3つの区切り ----

        [Test]
        public void 月は60秒ごとに6月_11月_1月と進む()
        {
            var t = new SpawnTimetable();
            Assert.AreEqual(1, t.MonthAt(0f));
            Assert.AreEqual(1, t.MonthAt(59.9f));
            Assert.AreEqual(2, t.MonthAt(60f));
            Assert.AreEqual(3, t.MonthAt(120f));
            Assert.AreEqual(3, t.MonthAt(180f));
        }

        [Test]
        public void 篝火1つの区切りにウェーブが1つずつ入り_既定値は仕様の約束を守っている()
        {
            var t = new SpawnTimetable();
            for (int i = 0; i < t.WaveCount; i++)
            {
                LoadWaveWindow w = t.WaveWindow(i);
                Assert.AreEqual(i + 1, t.MonthAt(w.startSeconds), $"{SpawnTimetable.WaveLabels[i]} の開始");
                Assert.AreEqual(i + 1, t.MonthAt(w.EndSeconds - Eps), $"{SpawnTimetable.WaveLabels[i]} の終わり");
            }

            CollectionAssert.IsEmpty(t.Validate());
        }

        [Test]
        public void 時間割は8章の表と同じ時刻に区切られている()
        {
            CollectionAssert.AreEqual(
                new List<float> { 0f, 38f, 40f, 48f, 55f, 60f, 80f, 105f, 120f, 140f, 180f },
                new SpawnTimetable().ChangePoints());
        }

        [Test]
        public void ウェーブが重なる_月をまたぐ_上限をこえる設定は警告される()
        {
            SpawnTimetable overlap = With("{\"middleWave\":{\"startSeconds\":50,\"durationSeconds\":25,\"added\":5}}");
            Assert.IsTrue(overlap.Validate().Exists(p => p.Contains("重なって")));
            Assert.IsTrue(overlap.Validate().Exists(p => p.Contains("収まっていません")));

            SpawnTimetable tooMany = With("{\"smallWave\":{\"startSeconds\":40,\"durationSeconds\":15,\"added\":6}}");
            Assert.IsTrue(tooMany.Validate().Exists(p => p.Contains("総上限")));

            SpawnTimetable shortFinal = With("{\"finalWaveDurationSeconds\":30}");
            Assert.IsTrue(shortFinal.Validate().Exists(p => p.Contains("3:00")));
        }

        [Test]
        public void 表示用の1行に時刻_月_ウェーブ_上限が入る()
        {
            var t = new SpawnTimetable();
            Assert.AreEqual("1:25 11月 中負荷ウェーブ +5（いま +5） 上限 15人", t.Describe(85f));
            Assert.AreEqual("0:20 6月 通常 上限 10人", t.Describe(20f));
            Assert.AreEqual("通常・欲張り・遠方", t.DescribeUnlocked(50f));
        }

        // ---- 固定シード ----

        // n 人目の客が時刻 at に補充されたときの客種の並び（MockCrowdDirector と同じ「シード × 客種 × 客ID」の列）
        static List<CustomerKind> Draw(SpawnTimetable t, int seed, IList<float> spawnTimes)
        {
            var kinds = new List<CustomerKind>();
            int greedyAlive = 0;
            for (int i = 0; i < spawnTimes.Count; i++)
            {
                int id = i + 1;
                float unit = PlaytestRandom.Value(DeterministicRandom.Derive(seed, PlaytestStreams.Kind, id));
                CustomerKind kind = CustomerKindPicker.Pick(t.WeightsAt(spawnTimes[i]), unit, greedyAlive, 0);
                if (kind == CustomerKind.Greedy) greedyAlive = (greedyAlive + 1) % 3;   // 抽選の上限もからめる（出たり消えたり）
                kinds.Add(kind);
            }
            return kinds;
        }

        [Test]
        public void 同じシードと同じ補充時刻なら180秒ぶんの客種の並びが一致する()
        {
            var times = new List<float>();
            for (float at = 0.5f; at < SpawnTimetable.MatchEndSeconds; at += 1.5f) times.Add(at);
            var t = new SpawnTimetable();

            CollectionAssert.AreEqual(Draw(t, 42, times), Draw(t, 42, times));
            CollectionAssert.AreNotEqual(Draw(t, 42, times), Draw(t, 43, times));
        }

        [Test]
        public void 解禁前の時刻に補充された客はどのシードでも解禁前の種類にならない()
        {
            var t = new SpawnTimetable();
            for (int seed = 1; seed <= 50; seed++)
            {
                var times = new List<float>();
                for (float at = 30f; at < 60f; at += 0.5f) times.Add(at);
                List<CustomerKind> kinds = Draw(t, seed, times);

                for (int i = 0; i < times.Count; i++)
                {
                    Assert.IsTrue(t.IsUnlocked(kinds[i], times[i]),
                        $"seed {seed} の {times[i]:0.0}秒に未解禁の {kinds[i]} が出た");
                }
            }
        }
    }
}
