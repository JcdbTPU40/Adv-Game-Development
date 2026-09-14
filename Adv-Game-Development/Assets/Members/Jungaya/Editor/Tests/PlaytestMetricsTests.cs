using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#63 1 プレイのイベント列 → T1・T2・T3 の集計（Docs/63_計測ログ基盤.md の「集計の定義」）。</summary>
    public class PlaytestMetricsTests
    {
        static PlaytestEvent Ev(double t, string type, Action<PlaytestEvent> fill = null)
        {
            var e = new PlaytestEvent(t, type);
            fill?.Invoke(e);
            return e;
        }

        static string Get(List<PlaytestSummaryRow> rows, string section, string metric)
        {
            foreach (PlaytestSummaryRow row in rows)
            {
                if (row.Section == section && row.Metric == metric) return row.Value;
            }
            Assert.Fail($"{section}.{metric} がありません");
            return null;
        }

        static List<PlaytestSummaryRow> Compute(List<PlaytestEvent> events, double end)
        {
            return PlaytestMetrics.Compute(events, new PlaytestMetricsSettings(), end);
        }

        // ---- 停止 ----

        [Test]
        public void 三秒以上振らなかった区間_開始と終わりの端も含む()
        {
            List<(double Start, double End)> gaps = PlaytestMetrics.IdleGaps(new List<double> { 1, 2, 6, 7 }, 0, 12, 3);
            CollectionAssert.AreEqual(new[] { (2.0, 6.0), (7.0, 12.0) }, gaps);

            gaps = PlaytestMetrics.IdleGaps(new List<double> { 4 }, 0, 12, 3);
            CollectionAssert.AreEqual(new[] { (0.0, 4.0), (4.0, 12.0) }, gaps);

            // ちょうど 3 秒は停止
            Assert.AreEqual(1, PlaytestMetrics.IdleGaps(new List<double> { 3 }, 0, 5, 3).Count);
        }

        [Test]
        public void 停止は発射と却下で途切れ_セッション外の却下は数えない()
        {
            var events = new List<PlaytestEvent>
            {
                Ev(1, PlaytestEventType.Fire),
                Ev(2, PlaytestEventType.SwingRejected, e => e.Detail = "Cooldown"),
                Ev(4, PlaytestEventType.SwingRejected, e => e.Detail = PlaytestMetrics.RejectInactive),
                Ev(6, PlaytestEventType.Fire),
                Ev(7, PlaytestEventType.Fire),
            };
            List<PlaytestSummaryRow> rows = Compute(events, 12);

            Assert.AreEqual("2", Get(rows, "play", "idle3s_count"));
            Assert.AreEqual("9", Get(rows, "play", "idle3s_total_sec"));
            Assert.AreEqual("1", Get(rows, "play", "rejected_swings"));
        }

        // ---- 黒客化率・同時黒客 ----

        [Test]
        public void 黒客化率は黒客化数を救済数と黒客化数の和で割る_分母0は欠測()
        {
            var events = new List<PlaytestEvent>
            {
                Ev(5, PlaytestEventType.Rescue, e => e.TargetId = 1),
                Ev(6, PlaytestEventType.Rescue, e => e.TargetId = 2),
                Ev(7, PlaytestEventType.Rescue, e => e.TargetId = 3),
                Ev(8, PlaytestEventType.BlackConversion, e => e.TargetId = 4),
            };
            Assert.AreEqual("0.25", Get(Compute(events, 60), "play", "black_conversion_rate"));
            Assert.AreEqual("", Get(Compute(new List<PlaytestEvent>(), 60), "play", "black_conversion_rate"));
        }

        [Test]
        public void 同時黒客の最大は区間ごと_開始時点の残りも含める()
        {
            var events = new List<PlaytestEvent>
            {
                Ev(10, PlaytestEventType.Spawn, e => { e.TargetId = 1; e.IsBlack = true; }),
                Ev(15, PlaytestEventType.Spawn, e => { e.TargetId = 4; e.IsBlack = false; }),
                Ev(20, PlaytestEventType.BlackConversion, e => e.TargetId = 2),
                Ev(30, PlaytestEventType.Despawn, e => e.TargetId = 1),
                Ev(40, PlaytestEventType.Despawn, e => e.TargetId = 2),
                Ev(70, PlaytestEventType.Spawn, e => { e.TargetId = 3; e.IsBlack = true; }),
            };
            CollectionAssert.AreEqual(new[] { 2, 1, 1 }, PlaytestMetrics.MaxSimultaneousBlack(events, 3, 60));

            List<PlaytestSummaryRow> rows = Compute(events, 180);
            Assert.AreEqual("2", Get(rows, "t3", "0-60s.max_simultaneous_black"));
            Assert.AreEqual("1", Get(rows, "t3", "120-180s.max_simultaneous_black"));
            Assert.AreEqual("2", Get(rows, "t3", "total.max_simultaneous_black"));
        }

        // ---- T3 ----

        static List<PlaytestEvent> T3Events()
        {
            return new List<PlaytestEvent>
            {
                Ev(10, PlaytestEventType.Fire),
                Ev(10.5, PlaytestEventType.Landing, e => { e.TargetId = 1; e.Zone = "Center"; }),
                Ev(11, PlaytestEventType.Landing, e => e.Zone = "Miss"),
                Ev(20, PlaytestEventType.Rescue, e => e.TargetId = 1),
                Ev(70, PlaytestEventType.Fire),
                Ev(130, PlaytestEventType.Fire),
                Ev(130.5, PlaytestEventType.Landing, e => e.Zone = "Miss"),
                Ev(131, PlaytestEventType.Landing, e => e.Zone = "Miss"),
                Ev(140, PlaytestEventType.BlackConversion, e => e.TargetId = 5),
                Ev(150, PlaytestEventType.Rescue, e => e.TargetId = 6),
                Ev(0, PlaytestEventType.Rating, e => { e.Rating = 30; e.Rank = "C"; }),
            };
        }

        [Test]
        public void T3は区間ごとの命中率_投擲数_停止時間_救済数_黒客化率()
        {
            List<PlaytestSummaryRow> rows = Compute(T3Events(), 180);

            Assert.AreEqual("1", Get(rows, "t3", "0-60s.throws"));
            Assert.AreEqual("0.5", Get(rows, "t3", "0-60s.hit_rate"));
            Assert.AreEqual("", Get(rows, "t3", "60-120s.hit_rate"));   // 着弾なし = 欠測
            Assert.AreEqual("0", Get(rows, "t3", "120-180s.hit_rate"));
            Assert.AreEqual("60", Get(rows, "t3", "0-60s.idle_sec"));
            Assert.AreEqual("0", Get(rows, "t3", "0-60s.black_conversion_rate"));
            Assert.AreEqual("0.5", Get(rows, "t3", "120-180s.black_conversion_rate"));

            Assert.AreEqual("180", Get(rows, "t3", "total.idle_sec"));
            Assert.AreEqual("50", Get(rows, "t3", "last_segment.hit_rate_drop_pt"));
            Assert.AreEqual("0", Get(rows, "t3", "last_segment.throws_drop_ratio"));
            Assert.AreEqual("1", Get(rows, "t3", "last_segment.black_conversions_le_rescues"));
        }

        [Test]
        public void 到達していない区間は欠測()
        {
            var events = new List<PlaytestEvent> { Ev(10, PlaytestEventType.Fire) };
            List<PlaytestSummaryRow> rows = Compute(events, 100);

            Assert.AreEqual("0", Get(rows, "t3", "60-120s.throws"));
            Assert.AreEqual("", Get(rows, "t3", "120-180s.throws"));
            Assert.AreEqual("", Get(rows, "t3", "last_segment.hit_rate_drop_pt"));
        }

        [Test]
        public void 評価推移は区間の終わりの値_最高ランクと到達秒()
        {
            var events = new List<PlaytestEvent>
            {
                Ev(0, PlaytestEventType.Rating, e => { e.Rating = 30; e.Rank = "C"; }),
                Ev(50, PlaytestEventType.Rating, e => { e.Rating = 45; e.Rank = "B"; }),
                Ev(100, PlaytestEventType.Rating, e => { e.Rating = 65; e.Rank = "A"; }),
                Ev(110, PlaytestEventType.Rating, e => { e.Rating = 62; e.Rank = "A"; }),
                Ev(150, PlaytestEventType.Rating, e => { e.Rating = 35; e.Rank = "C"; }),
            };
            List<PlaytestSummaryRow> rows = Compute(events, 180);

            Assert.AreEqual("45", Get(rows, "t3", "0-60s.rating_end"));
            Assert.AreEqual("B", Get(rows, "t3", "0-60s.rank_end"));
            Assert.AreEqual("62", Get(rows, "t3", "60-120s.rating_end"));
            Assert.AreEqual("35", Get(rows, "t3", "120-180s.rating_end"));
            Assert.AreEqual("A", Get(rows, "play", "max_rank"));
            Assert.AreEqual("100", Get(rows, "play", "max_rank_sec"));
            Assert.AreEqual("C", Get(rows, "play", "rank_final"));
        }

        // ---- T1 ----

        [Test]
        public void T1は段階ごとの開始_終わり_未達_自由練習_初救済_色誤り_停止()
        {
            var events = new List<PlaytestEvent>
            {
                Ev(0, PlaytestEventType.T1StageStart, e => e.Stage = 1),
                Ev(2, PlaytestEventType.Fire, e => e.ThrowNo = 1),
                Ev(2.4, PlaytestEventType.Landing, e => { e.ThrowNo = 1; e.TargetId = 1; e.Zone = "Miss"; e.ColorError = true; }),
                Ev(4, PlaytestEventType.Fire, e => e.ThrowNo = 2),
                Ev(4.4, PlaytestEventType.Rescue, e => e.TargetId = 1),
                Ev(4.4, PlaytestEventType.Landing, e => { e.ThrowNo = 2; e.TargetId = 1; e.Zone = "Center"; e.ColorError = false; }),
                Ev(5.2, PlaytestEventType.T1StageEnd, e => { e.Stage = 1; e.Detail = "achieved"; }),
                Ev(5.2, PlaytestEventType.T1StageStart, e => e.Stage = 2),
                Ev(18, PlaytestEventType.T1StageEnd, e => { e.Stage = 2; e.Detail = "timeout"; }),
                Ev(18, PlaytestEventType.T1StageStart, e => e.Stage = 3),
                Ev(24, PlaytestEventType.T1StageEnd, e => { e.Stage = 3; e.Detail = "achieved"; }),
                Ev(30, PlaytestEventType.T1CounterReset),
                Ev(31, PlaytestEventType.T1Ghost, e => e.Detail = "color"),
                Ev(31, PlaytestEventType.T1Intervention, e => e.Detail = "ghost"),
                Ev(40, PlaytestEventType.Landing, e => { e.TargetId = 2; e.Zone = "Miss"; e.ColorError = true; }),
            };
            List<PlaytestSummaryRow> rows = Compute(events, 60);

            Assert.AreEqual("achieved", Get(rows, "t1", "stage1.result"));
            Assert.AreEqual("0", Get(rows, "t1", "stage1.start_sec"));
            Assert.AreEqual("5.2", Get(rows, "t1", "stage1.end_sec"));
            Assert.AreEqual("5.2", Get(rows, "t1", "stage1.duration_sec"));
            Assert.AreEqual("timeout", Get(rows, "t1", "stage2.result"));
            Assert.AreEqual("3", Get(rows, "t1", "stages_recorded"));
            Assert.AreEqual("1", Get(rows, "t1", "unachieved_stages"));
            Assert.AreEqual("6", Get(rows, "t1", "free_practice_sec"));
            Assert.AreEqual("30", Get(rows, "t1", "counter_reset_sec"));
            Assert.AreEqual("4.4", Get(rows, "t1", "first_rescue_sec"));
            Assert.AreEqual("2", Get(rows, "t1", "first_correct_color_throw"));
            Assert.AreEqual("1", Get(rows, "t1", "color_errors"));      // 0:40 の色誤りは学習区間の外
            Assert.AreEqual("2", Get(rows, "play", "color_errors"));
            Assert.AreEqual("1", Get(rows, "t1", "idle3s_count"));
            Assert.AreEqual("26", Get(rows, "t1", "idle3s_total_sec"));
            Assert.AreEqual("1", Get(rows, "t1", "ghosts"));
            Assert.AreEqual("1", Get(rows, "t1", "interventions"));
        }

        [Test]
        public void 自由練習秒は明示した値を優先する()
        {
            var events = new List<PlaytestEvent>
            {
                Ev(24, PlaytestEventType.T1StageEnd, e => { e.Stage = 3; e.Detail = "achieved"; }),
                Ev(25, PlaytestEventType.T1FreePractice, e => e.Value = 4.5),
            };
            Assert.AreEqual("4.5", Get(Compute(events, 60), "t1", "free_practice_sec"));
        }

        // ---- T2 ----

        [Test]
        public void T2は試行開始からの決定秒_最初の標的_選択の分布()
        {
            var events = new List<PlaytestEvent>
            {
                Ev(3, PlaytestEventType.Fire),
                Ev(5, PlaytestEventType.T2TrialStart),
                Ev(7.5, PlaytestEventType.Fire),
                Ev(8, PlaytestEventType.Landing, e => { e.TargetId = 3; e.Category = "Far"; e.Color = "Kinun"; e.PriorityTargetId = 3; e.Zone = "Inner"; }),
                Ev(9, PlaytestEventType.Landing, e => { e.TargetId = 4; e.Category = "Normal"; e.Color = "Kenkou"; e.PriorityTargetId = 3; e.Zone = "Outer"; }),
                Ev(10, PlaytestEventType.Hit, e => { e.TargetId = 5; e.Category = "Normal"; e.Color = "Kenkou"; e.Zone = "Center"; }),
            };
            List<PlaytestSummaryRow> rows = Compute(events, 60);

            Assert.AreEqual("5", Get(rows, "t2", "trial_start_sec"));
            Assert.AreEqual("2.5", Get(rows, "t2", "decision_sec"));
            Assert.AreEqual("3", Get(rows, "t2", "first_target_id"));
            Assert.AreEqual("Far", Get(rows, "t2", "first_target_category"));
            Assert.AreEqual("Kinun", Get(rows, "t2", "first_target_color"));
            Assert.AreEqual("1", Get(rows, "t2", "first_target_was_priority"));
            Assert.AreEqual("3", Get(rows, "t2", "choice.total"));
            Assert.AreEqual("1", Get(rows, "t2", "choice.category.Far"));
            Assert.AreEqual("2", Get(rows, "t2", "choice.category.Normal"));
            Assert.AreEqual("2", Get(rows, "t2", "choice.color.Kenkou"));
            Assert.AreEqual("1", Get(rows, "t2", "choice.priority"));
            Assert.AreEqual("1", Get(rows, "t2", "choice.non_priority"));
            Assert.AreEqual("0.5", Get(rows, "t2", "choice.priority_share"));
        }

        // ---- 入力遅延・実操作周期 ----

        [Test]
        public void 受信から発射確定までの分位点_入力時刻は揺らぎだけ()
        {
            var events = new List<PlaytestEvent>
            {
                Ev(1, PlaytestEventType.Fire, e => { e.ReceiveTime = 1.000; e.FireTime = 1.010; e.InputTime = 100.000; }),
                Ev(2, PlaytestEventType.Fire, e => { e.ReceiveTime = 2.000; e.FireTime = 2.030; e.InputTime = 101.005; }),
                Ev(3.5, PlaytestEventType.Fire, e => { e.ReceiveTime = 3.000; e.FireTime = 3.020; e.InputTime = 102.002; }),
            };
            List<PlaytestSummaryRow> rows = Compute(events, 10);

            Assert.AreEqual("3", Get(rows, "input", "receive_to_fire_ms.count"));
            Assert.AreEqual("20.0", Get(rows, "input", "receive_to_fire_ms.p50"));
            Assert.AreEqual("30.0", Get(rows, "input", "receive_to_fire_ms.max"));
            Assert.AreEqual("3.0", Get(rows, "input", "input_to_receive_jitter_ms.p50"));
            Assert.AreEqual("5.0", Get(rows, "input", "input_to_receive_jitter_ms.max"));

            // 実操作周期 = 発射の間隔 1.0 / 1.5
            Assert.AreEqual("1.25", Get(rows, "play", "throw_interval_sec.p50"));
        }

        [Test]
        public void 同じイベント列からは同じ集計()
        {
            List<PlaytestSummaryRow> a = Compute(T3Events(), 180);
            List<PlaytestSummaryRow> b = Compute(T3Events(), 180);
            CollectionAssert.AreEqual(a, b);
        }
    }
}
