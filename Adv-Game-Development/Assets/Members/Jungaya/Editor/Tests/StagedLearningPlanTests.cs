using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using Toufuku.Rescue;

namespace Toufuku.Tutorial.Tests
{
    /// <summary>
    /// 開始30秒の段階学習の進み方 — Issue #58（仕様書 v8 8章「時間割」、18章「開始30秒の段階学習」、17章 T1）
    /// 締切（0:08／0:18／0:30）での時間切れ、早期達成、締切ちょうどの扱い、3秒停止の大幣ゴースト、0:30 の状況別ゴースト。
    /// </summary>
    public class StagedLearningPlanTests
    {
        sealed class Recorder
        {
            public readonly List<string> Events = new List<string>();

            public Recorder(StagedLearningPlan plan)
            {
                plan.StageStarted += (s, t) => Events.Add($"start:{s}@{F(t)}");
                plan.StageEnded += (s, achieved, t) => Events.Add($"end:{s}:{(achieved ? "achieved" : "timeout")}@{F(t)}");
                plan.FreePracticeEnded += (sec, t) => Events.Add($"free:{F(sec)}@{F(t)}");
                plan.CompetitionStarted += t => Events.Add($"competition@{F(t)}");
                plan.GhostRequested += (g, reason, t) => Events.Add($"ghost:{g}:{reason}@{F(t)}");
            }

            static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        static StagedLearningPlan Started(out Recorder recorder)
        {
            var plan = new StagedLearningPlan(new StagedLearningSettings());
            recorder = new Recorder(plan);
            plan.Start(0.0);
            return plan;
        }

        // from から to まで 0.1秒ずつ進める（フレームのかわり）
        static void Run(StagedLearningPlan plan, double from, double to)
        {
            for (int k = 1; from + k * 0.1 <= to + 1e-9; k++) plan.Advance(from + k * 0.1);
            plan.Advance(to);
        }

        // ── 時間切れ ──────────────────────────────────

        [Test]
        public void 何もしなければ0時08分_0時18分_0時30分の締切で順に進む()
        {
            StagedLearningPlan plan = Started(out Recorder r);
            Run(plan, 0.0, 31.0);

            CollectionAssert.AreEqual(new[]
            {
                "start:Deliver@0",
                "ghost:Swing:stage1_idle@3",
                "end:Deliver:timeout@8",
                "start:Choose@8",
                "end:Choose:timeout@18",
                "start:Discern@18",
                "end:Discern:timeout@30",
                "competition@30",
                "ghost:Swing:after_learning@30",
            }, r.Events);
            Assert.AreEqual(LearningStage.Competition, plan.Stage);
            Assert.IsFalse(plan.IsLearning);
        }

        [Test]
        public void おそいフレームで締切をいくつもこえても締切の時刻で順に進む()
        {
            StagedLearningPlan plan = Started(out Recorder r);
            plan.Advance(25.0);

            CollectionAssert.AreEqual(new[]
            {
                "start:Deliver@0",
                "ghost:Swing:stage1_idle@3",
                "end:Deliver:timeout@8",
                "start:Choose@8",
                "end:Choose:timeout@18",
                "start:Discern@18",
            }, r.Events);
            Assert.AreEqual(LearningStage.Discern, plan.Stage);
            Assert.AreEqual(18.0, plan.StageStartSeconds, 1e-9);
        }

        // ── 早期達成 ──────────────────────────────────

        [Test]
        public void 届けるは1人救えた瞬間に選ぶへ進む()
        {
            StagedLearningPlan plan = Started(out Recorder r);
            plan.NoteSwingAccepted(1.5);
            Assert.IsTrue(plan.NoteRescue(2.0, false));

            Assert.AreEqual(LearningStage.Choose, plan.Stage);
            Assert.IsTrue(plan.IsAchieved(LearningStage.Deliver));
            CollectionAssert.AreEqual(new[] { "start:Deliver@0", "end:Deliver:achieved@2", "start:Choose@2" }, r.Events);
            Assert.AreEqual(2.0, plan.FirstRescueSeconds.Value, 1e-9);
        }

        [Test]
        public void 選ぶはその段階で2人救えたら見抜くへ_1人では進まない()
        {
            StagedLearningPlan plan = Started(out _);
            plan.NoteRescue(2.0, false);            // 届けるの1人（選ぶの人数には入らない）

            plan.NoteRescue(5.0, false);
            Assert.AreEqual(LearningStage.Choose, plan.Stage);
            Assert.AreEqual(1, plan.StageRescues);

            plan.NoteRescue(6.0, false);
            Assert.AreEqual(LearningStage.Discern, plan.Stage);
            Assert.IsTrue(plan.IsAchieved(LearningStage.Choose));
            Assert.AreEqual(6.0, plan.StageStartSeconds, 1e-9);
        }

        [Test]
        public void 締切ちょうどの救済は次の段階として数える()
        {
            StagedLearningPlan plan = Started(out Recorder r);
            plan.NoteSwingAccepted(7.0);
            plan.NoteRescue(8.0, false);

            Assert.IsFalse(plan.IsAchieved(LearningStage.Deliver), "0:08.000 は届けるの締切");
            Assert.AreEqual(LearningStage.Choose, plan.Stage);
            Assert.AreEqual(1, plan.StageRescues, "選ぶの1人目");
            CollectionAssert.Contains(r.Events, "end:Deliver:timeout@8");
        }

        [Test]
        public void 見抜くは二重円の客を救ったときだけ達成して自由練習の秒を出す()
        {
            StagedLearningPlan plan = Started(out Recorder r);
            plan.NoteSwingAccepted(1.0);
            plan.Advance(18.0);

            plan.NoteRescue(20.0, isPriorityCustomer: false);
            Assert.AreEqual(LearningStage.Discern, plan.Stage, "二重円ではない客を救っても進まない");

            plan.NoteRescue(22.0, isPriorityCustomer: true);
            Assert.AreEqual(LearningStage.FreePractice, plan.Stage);
            Assert.IsTrue(plan.PriorityRescued);

            plan.Advance(30.0);
            CollectionAssert.IsSubsetOf(new[] { "end:Discern:achieved@22", "start:FreePractice@22", "free:8@30", "competition@30" }, r.Events);
            Assert.IsFalse(r.Events.Exists(e => e.Contains("after_learning")), "見抜くを達成したら 0:30 のゴーストは出さない");
            Assert.AreEqual(LearningGhost.None, plan.AfterLearningGhost);
        }

        // ── ゴースト ──────────────────────────────────

        [Test]
        public void 届けるで最後の振りから3秒止まったら大幣ゴーストを1回だけ出す()
        {
            StagedLearningPlan plan = Started(out Recorder r);
            plan.NoteSwingAccepted(1.0);
            Run(plan, 1.0, 7.9);

            List<string> ghosts = r.Events.FindAll(e => e.StartsWith("ghost:"));
            CollectionAssert.AreEqual(new[] { "ghost:Swing:stage1_idle@4" }, ghosts);

            plan.NoteSwingAccepted(7.95);
            Assert.AreEqual(1, r.Events.FindAll(e => e.StartsWith("ghost:")).Count, "1プレイに1回");
        }

        [Test]
        public void 届けるで振りつづけていれば大幣ゴーストは出ない()
        {
            StagedLearningPlan plan = Started(out Recorder r);
            for (double t = 2.0; t < 8.0; t += 2.0) plan.NoteSwingAccepted(t);
            plan.Advance(8.5);

            Assert.IsFalse(plan.IsIdleGhostRequestedForTest());
            Assert.IsFalse(r.Events.Exists(e => e.Contains("stage1_idle")));
        }

        [Test]
        public void 届けるを達成したら止まってもゴーストは出ない()
        {
            StagedLearningPlan plan = Started(out Recorder r);
            plan.NoteSwingAccepted(0.5);
            plan.NoteRescue(1.0, false);
            Run(plan, 1.0, 7.0);

            Assert.IsFalse(r.Events.Exists(e => e.Contains("stage1_idle")));
        }

        [Test]
        public void 足りない操作は振る_狙う_色_二重円の順に1つだけ選ぶ()
        {
            Assert.AreEqual(LearningGhost.Swing, StagedLearningPlan.DecideGhost(0, 0, 0, false));
            Assert.AreEqual(LearningGhost.Aim, StagedLearningPlan.DecideGhost(3, 0, 0, false));
            Assert.AreEqual(LearningGhost.Color, StagedLearningPlan.DecideGhost(3, 2, 0, false));
            Assert.AreEqual(LearningGhost.Priority, StagedLearningPlan.DecideGhost(3, 2, 1, false));
            Assert.AreEqual(LearningGhost.None, StagedLearningPlan.DecideGhost(3, 2, 1, true));
        }

        [Test]
        public void 見抜くが未達なら0時30分に足りない操作のゴーストを合図する()
        {
            StagedLearningPlan plan = Started(out Recorder r);
            plan.NoteSwingAccepted(1.0);
            plan.NoteCustomerHit(1.5, correctColor: false);
            plan.NoteSwingAccepted(2.0);
            plan.NoteCustomerHit(2.5, correctColor: true);
            plan.NoteRescue(2.5, false);
            plan.Advance(30.0);

            Assert.AreEqual(LearningGhost.Priority, plan.AfterLearningGhost);
            CollectionAssert.Contains(r.Events, "ghost:Priority:after_learning@30");
            Assert.AreEqual(1, plan.ColorErrors);
            Assert.AreEqual(1, plan.CorrectColorHits);
        }

        [Test]
        public void 客に当たらないまま0時30分なら狙うゴースト()
        {
            StagedLearningPlan plan = Started(out Recorder r);
            for (double t = 1.0; t < 30.0; t += 1.0) plan.NoteSwingAccepted(t);
            plan.Advance(30.0);

            Assert.AreEqual(LearningGhost.Aim, plan.AfterLearningGhost);
            CollectionAssert.Contains(r.Events, "ghost:Aim:after_learning@30");
        }

        // ── 学習の値は競技に持ちこまない ─────────────────────────

        [Test]
        public void 競技が始まったあとのできごとは学習として数えない()
        {
            StagedLearningPlan plan = Started(out _);
            plan.Advance(30.0);

            Assert.IsFalse(plan.NoteRescue(31.0, true));
            plan.NoteSwingAccepted(31.0);
            plan.NoteCustomerHit(31.5, true);

            Assert.AreEqual(0, plan.Rescues);
            Assert.AreEqual(0, plan.AcceptedSwings);
            Assert.AreEqual(0, plan.CustomerHits);
            Assert.IsFalse(plan.FirstRescueSeconds.HasValue);
        }

        [Test]
        public void リトライで最初からやりなおせる()
        {
            StagedLearningPlan plan = Started(out Recorder r);
            plan.NoteSwingAccepted(1.0);
            plan.NoteRescue(2.0, false);
            plan.Advance(40.0);

            plan.Start(0.0);
            Assert.AreEqual(LearningStage.Deliver, plan.Stage);
            Assert.AreEqual(0, plan.Rescues);
            Assert.AreEqual(0, plan.AcceptedSwings);
            Assert.IsFalse(plan.IsAchieved(LearningStage.Deliver));
            Assert.IsFalse(plan.IsIdleGhostRequestedForTest());
            Assert.AreEqual(LearningGhost.None, plan.AfterLearningGhost);
        }

        // ── 設定 ──────────────────────────────────

        [Test]
        public void 既定の設定は企画書どおりで問題がない()
        {
            var s = new StagedLearningSettings();
            CollectionAssert.IsEmpty(s.Validate());
            CollectionAssert.AreEqual(new[] { OmamoriType.Kenkou }, s.ColorsFor(LearningStage.Deliver));
            CollectionAssert.AreEqual(new[] { OmamoriType.Kenkou, OmamoriType.Gakugyou }, s.ColorsFor(LearningStage.Choose));
            Assert.AreEqual(3, s.ColorsFor(LearningStage.Discern).Length);
            Assert.AreEqual(3, s.ColorsFor(LearningStage.FreePractice).Length, "自由練習は同じ3色");
            Assert.AreEqual(0, s.ColorsFor(LearningStage.Competition).Length);
            Assert.AreEqual(65f, s.priorityDanger);
        }

        [Test]
        public void 設定ミスを警告する()
        {
            var s = new StagedLearningSettings
            {
                deliverEndSeconds = 20f,
                chooseEndSeconds = 18f,
                chooseRequiredRescues = 3,
                priorityColor = OmamoriType.Gakugyou,
                priorityDanger = 40f
            };
            List<string> problems = s.Validate();

            Assert.IsTrue(problems.Exists(p => p.Contains("締切")));
            Assert.IsTrue(problems.Exists(p => p.Contains("必要な救済人数")));
            Assert.IsTrue(problems.Exists(p => p.Contains("教える2の色にも")));
            Assert.IsTrue(problems.Exists(p => p.Contains("危険円が出ません")));
        }
    }

    static class StagedLearningPlanTestExtensions
    {
        public static bool IsIdleGhostRequestedForTest(this StagedLearningPlan plan) => plan.IdleGhostRequested;
    }
}
