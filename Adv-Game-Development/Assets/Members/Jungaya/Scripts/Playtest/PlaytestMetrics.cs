using System;
using System.Collections.Generic;
using System.Globalization;

namespace Toufuku.Playtest
{
    /// <summary>集計 CSV の 1 行（section, metric, value）。</summary>
    public readonly struct PlaytestSummaryRow
    {
        public readonly string Section;
        public readonly string Metric;
        public readonly string Value;

        public PlaytestSummaryRow(string section, string metric, string value)
        {
            Section = section;
            Metric = metric;
            Value = value;
        }

        public override string ToString() => $"{Section}.{Metric}={Value}";
    }

    [Serializable]
    public class PlaytestMetricsSettings
    {
        /// <summary>T3 の区間の長さ（秒）。</summary>
        public float segmentSeconds = 60f;
        /// <summary>T3 の区間数（0-60 / 60-120 / 120-180 秒 = 3）。</summary>
        public int segmentCount = 3;
        /// <summary>この秒数以上振らなかったら「停止」とみなす（仕様書 v8 19章: 3 秒以上の停止）。</summary>
        public float idleThresholdSeconds = 3f;
        /// <summary>T1 の色誤り・停止を数える区間（開始からの秒。v8 11章の段階学習 0:00〜0:30）。</summary>
        public float t1WindowSeconds = 30f;
    }

    /// <summary>
    /// 1 プレイのイベント列から T1・T2・T3 の判定に使う数値を出す — Issue #63（仕様書 v8 17章・19章）
    ///
    /// MonoBehaviour に依存しない純粋関数。同じイベント列からは必ず同じ集計になる（EditMode テストで検証）。
    /// 定義は Docs/63_計測ログ基盤.md の「集計の定義」を正本とする。値が求められないもの（分母 0 など）は空欄（欠測）。
    /// </summary>
    public static class PlaytestMetrics
    {
        public const string SectionPlay = "play";
        public const string SectionT1 = "t1";
        public const string SectionT2 = "t2";
        public const string SectionT3 = "t3";
        public const string SectionInput = "input";

        /// <summary>HitZone.Miss の文字列（相性✗の命中・外し）。</summary>
        public const string ZoneMiss = "Miss";
        /// <summary>セッション外の却下（リザルト中など）。停止の判定に含めない。</summary>
        public const string RejectInactive = "Inactive";
        public const string StageAchieved = "achieved";
        public const string StageTimeout = "timeout";
        public const string KindOharae = "Oharae";

        sealed class Counts
        {
            public int Throws, Rejected, Landings, LandingHits, Hits, GoodHits, GoodLandingHits, ColorErrors, Rescues, Conversions;
        }

        /// <param name="endTime">プレイの終わり（セッション開始からの秒）。途中終了なら最後に記録した時刻。</param>
        public static List<PlaytestSummaryRow> Compute(IReadOnlyList<PlaytestEvent> events, PlaytestMetricsSettings settings, double endTime)
        {
            if (events == null) events = Array.Empty<PlaytestEvent>();
            if (settings == null) settings = new PlaytestMetricsSettings();
            endTime = Math.Max(0.0, endTime);

            var rows = new List<PlaytestSummaryRow>();
            List<(double Start, double End)> idle = IdleGaps(SwingTimes(events), 0.0, endTime, settings.idleThresholdSeconds);

            AddPlay(rows, events, idle, endTime);
            AddT1(rows, events, idle, settings);
            AddT2(rows, events);
            AddT3(rows, events, idle, settings, endTime);
            AddInput(rows, events);
            return rows;
        }

        // ---- 全体 ----

        static void AddPlay(List<PlaytestSummaryRow> rows, IReadOnlyList<PlaytestEvent> events, List<(double Start, double End)> idle, double endTime)
        {
            const string s = SectionPlay;
            Counts c = CountIn(events, 0.0, double.PositiveInfinity);
            int oharae = 0;
            foreach (PlaytestEvent e in events)
            {
                if (e.Type == PlaytestEventType.Fire && e.Detail == KindOharae) oharae++;
            }

            Add(rows, s, "duration_sec", endTime);
            Add(rows, s, "throws", c.Throws);
            Add(rows, s, "normal_throws", c.Throws - oharae);
            Add(rows, s, "oharae_throws", oharae);
            Add(rows, s, "rejected_swings", c.Rejected);
            Add(rows, s, "landings", c.Landings);
            Add(rows, s, "hits", c.Hits);
            Add(rows, s, "hit_rate", Ratio(c.LandingHits, c.Landings));
            Add(rows, s, "good_hits", c.GoodHits);
            Add(rows, s, "good_hit_rate", Ratio(c.GoodLandingHits, c.Landings));
            Add(rows, s, "color_errors", c.ColorErrors);
            Add(rows, s, "rescues", c.Rescues);
            Add(rows, s, "black_conversions", c.Conversions);
            Add(rows, s, "black_conversion_rate", Ratio(c.Conversions, c.Rescues + c.Conversions));
            Add(rows, s, "max_simultaneous_black", MaxSimultaneousBlack(events, 1, double.PositiveInfinity)[0]);
            Add(rows, s, "idle3s_count", idle.Count);
            Add(rows, s, "idle3s_total_sec", SumOverlap(idle, 0.0, double.PositiveInfinity));

            PlaytestEvent last = LastWith(events, e => e.En.HasValue);
            Add(rows, s, "en_final", last != null ? last.En : null);

            int? maxChain = null;
            foreach (PlaytestEvent e in events)
            {
                if (e.FukuChain.HasValue) maxChain = Math.Max(maxChain ?? 0, e.FukuChain.Value);
            }
            Add(rows, s, "max_fuku_chain", maxChain);

            PlaytestEvent rating = LastWith(events, e => e.Type == PlaytestEventType.Rating && e.Rating.HasValue);
            Add(rows, s, "rating_final", rating?.Rating);
            AddText(rows, s, "rank_final", rating?.Rank);

            // プレイ中の最高ランクと、そこへ最初に到達した秒
            PlaytestEvent best = null;
            foreach (PlaytestEvent e in events)
            {
                if (e.Type != PlaytestEventType.Rating || RankOrder(e.Rank) < 0) continue;
                if (best == null || RankOrder(e.Rank) > RankOrder(best.Rank)) best = e;
            }
            AddText(rows, s, "max_rank", best?.Rank);
            Add(rows, s, "max_rank_sec", best != null ? best.T : (double?)null);

            // 実操作周期（T0-3M #53 と共用）
            var intervals = new List<double>();
            double? prev = null;
            foreach (PlaytestEvent e in events)
            {
                if (e.Type != PlaytestEventType.Fire) continue;
                if (prev.HasValue) intervals.Add(e.T - prev.Value);
                prev = e.T;
            }
            AddPercentiles(rows, s, "throw_interval_sec", intervals, "0.###");
        }

        // ---- T1 ----

        static void AddT1(List<PlaytestSummaryRow> rows, IReadOnlyList<PlaytestEvent> events, List<(double Start, double End)> idle, PlaytestMetricsSettings settings)
        {
            const string s = SectionT1;
            double window = settings.t1WindowSeconds;
            Add(rows, s, "window_sec", window);

            var stages = new SortedSet<int>();
            foreach (PlaytestEvent e in events)
            {
                if ((e.Type == PlaytestEventType.T1StageStart || e.Type == PlaytestEventType.T1StageEnd) && e.Stage.HasValue)
                    stages.Add(e.Stage.Value);
            }

            int unachieved = 0;
            PlaytestEvent lastStageEnd = null;
            foreach (int stage in stages)
            {
                string label = "stage" + stage.ToString(CultureInfo.InvariantCulture) + ".";
                PlaytestEvent start = FirstWith(events, e => e.Type == PlaytestEventType.T1StageStart && e.Stage == stage);
                PlaytestEvent end = FirstWith(events, e => e.Type == PlaytestEventType.T1StageEnd && e.Stage == stage);
                if (end != null && end.Detail == StageTimeout) unachieved++;
                if (end != null) lastStageEnd = end;

                AddText(rows, s, label + "result", end?.Detail);
                Add(rows, s, label + "start_sec", start != null ? start.T : (double?)null);
                Add(rows, s, label + "end_sec", end != null ? end.T : (double?)null);
                Add(rows, s, label + "duration_sec", start != null && end != null ? end.T - start.T : (double?)null);
            }
            Add(rows, s, "stages_recorded", stages.Count);
            Add(rows, s, "unachieved_stages", unachieved);

            // 自由練習: 明示された秒が優先。無ければ最後の段階を達成してから学習区間の終わりまで
            PlaytestEvent practice = LastWith(events, e => e.Type == PlaytestEventType.T1FreePractice && e.Value.HasValue);
            double? practiceSec = practice?.Value;
            if (!practiceSec.HasValue && lastStageEnd != null && lastStageEnd.Detail == StageAchieved && lastStageEnd.T < window)
                practiceSec = window - lastStageEnd.T;
            Add(rows, s, "free_practice_sec", practiceSec);

            PlaytestEvent reset = FirstWith(events, e => e.Type == PlaytestEventType.T1CounterReset);
            Add(rows, s, "counter_reset_sec", reset != null ? reset.T : (double?)null);

            PlaytestEvent firstRescue = FirstWith(events, e => e.Type == PlaytestEventType.Rescue);
            Add(rows, s, "first_rescue_sec", firstRescue != null ? firstRescue.T : (double?)null);

            // 何投目で初めて正しい色を当てたか（T1 合格値: 2 投目まで）
            PlaytestEvent firstCorrect = FirstWith(events, e => IsCustomerHit(e) && e.ColorError == false && e.IsBlack != true && e.ThrowNo.HasValue);
            Add(rows, s, "first_correct_color_throw", firstCorrect?.ThrowNo);

            Counts c = CountIn(events, 0.0, window);
            Add(rows, s, "color_errors", c.ColorErrors);

            int idleCount = 0;
            foreach ((double start, double _) in idle)
            {
                if (start < window) idleCount++;
            }
            Add(rows, s, "idle3s_count", idleCount);
            Add(rows, s, "idle3s_total_sec", SumOverlap(idle, 0.0, window));

            Add(rows, s, "ghosts", CountType(events, PlaytestEventType.T1Ghost));
            Add(rows, s, "interventions", CountType(events, PlaytestEventType.T1Intervention));
        }

        // ---- T2 ----

        static void AddT2(List<PlaytestSummaryRow> rows, IReadOnlyList<PlaytestEvent> events)
        {
            const string s = SectionT2;

            PlaytestEvent trial = FirstWith(events, e => e.Type == PlaytestEventType.T2TrialStart);
            double start = trial != null ? trial.T : 0.0;
            Add(rows, s, "trial_start_sec", start);

            PlaytestEvent firstFire = FirstWith(events, e => e.Type == PlaytestEventType.Fire && e.T >= start);
            Add(rows, s, "decision_sec", firstFire != null ? firstFire.T - start : (double?)null);

            PlaytestEvent first = FirstWith(events, e => IsCustomerHit(e) && e.T >= start);
            Add(rows, s, "first_target_id", first?.TargetId);
            AddText(rows, s, "first_target_category", first?.Category);
            AddText(rows, s, "first_target_color", first?.Color);
            Add(rows, s, "first_target_was_priority", first != null && first.PriorityTargetId.HasValue
                ? (first.PriorityTargetId == first.TargetId ? 1 : 0)
                : (int?)null);

            var byCategory = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var byColor = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int total = 0, priority = 0, known = 0;
            foreach (PlaytestEvent e in events)
            {
                if (!IsCustomerHit(e) || e.T < start) continue;
                total++;
                Increment(byCategory, e.Category);
                Increment(byColor, e.Color);
                if (e.PriorityTargetId.HasValue)
                {
                    known++;
                    if (e.PriorityTargetId == e.TargetId) priority++;
                }
            }

            Add(rows, s, "choice.total", total);
            foreach (KeyValuePair<string, int> pair in byCategory) Add(rows, s, "choice.category." + pair.Key, pair.Value);
            foreach (KeyValuePair<string, int> pair in byColor) Add(rows, s, "choice.color." + pair.Key, pair.Value);
            Add(rows, s, "choice.priority", priority);
            Add(rows, s, "choice.non_priority", known - priority);
            Add(rows, s, "choice.priority_share", Ratio(priority, known));
        }

        // ---- T3 ----

        static void AddT3(List<PlaytestSummaryRow> rows, IReadOnlyList<PlaytestEvent> events, List<(double Start, double End)> idle,
            PlaytestMetricsSettings settings, double endTime)
        {
            const string s = SectionT3;
            int n = Math.Max(1, settings.segmentCount);
            double length = Math.Max(0.001, settings.segmentSeconds);
            int[] maxBlack = MaxSimultaneousBlack(events, n, length);

            for (int k = 0; k < n; k++)
            {
                double from = k * length;
                double to = k == n - 1 ? double.PositiveInfinity : (k + 1) * length;
                string label = $"{Sec(from)}-{Sec((k + 1) * length)}s.";
                bool reached = k == 0 || endTime > from;

                Counts c = CountIn(events, from, to);
                Add(rows, s, label + "throws", reached ? c.Throws : (int?)null);
                Add(rows, s, label + "landings", reached ? c.Landings : (int?)null);
                Add(rows, s, label + "hit_rate", reached ? Ratio(c.LandingHits, c.Landings) : null);
                Add(rows, s, label + "good_hit_rate", reached ? Ratio(c.GoodLandingHits, c.Landings) : null);
                Add(rows, s, label + "idle_sec", reached ? SumOverlap(idle, from, to) : (double?)null);
                Add(rows, s, label + "rescues", reached ? c.Rescues : (int?)null);
                Add(rows, s, label + "black_conversions", reached ? c.Conversions : (int?)null);
                Add(rows, s, label + "black_conversion_rate", reached ? Ratio(c.Conversions, c.Rescues + c.Conversions) : null);
                Add(rows, s, label + "max_simultaneous_black", reached ? maxBlack[k] : (int?)null);

                PlaytestEvent rating = reached ? LastWith(events, e => e.Type == PlaytestEventType.Rating && e.Rating.HasValue && e.T < to) : null;
                Add(rows, s, label + "rating_end", rating?.Rating);
                AddText(rows, s, label + "rank_end", rating?.Rank);
            }

            // 合格値（v8 17章 T3）と照合するための値
            Counts first = CountIn(events, 0.0, length);
            Counts last = CountIn(events, (n - 1) * length, double.PositiveInfinity);
            bool lastReached = n == 1 || endTime > (n - 1) * length;
            double? firstRate = Ratio(first.LandingHits, first.Landings);
            double? lastRate = Ratio(last.LandingHits, last.Landings);

            Add(rows, s, "total.idle_sec", SumOverlap(idle, 0.0, double.PositiveInfinity));
            Add(rows, s, "total.max_simultaneous_black", MaxSimultaneousBlack(events, 1, double.PositiveInfinity)[0]);
            Add(rows, s, "last_segment.hit_rate_drop_pt",
                lastReached && firstRate.HasValue && lastRate.HasValue ? (firstRate.Value - lastRate.Value) * 100.0 : (double?)null);
            Add(rows, s, "last_segment.throws_drop_ratio",
                lastReached && first.Throws > 0 ? 1.0 - (double)last.Throws / first.Throws : (double?)null);
            Add(rows, s, "last_segment.black_conversions_le_rescues",
                lastReached ? (last.Conversions <= last.Rescues ? 1 : 0) : (int?)null);
        }

        // ---- 入力遅延（T6-USB #52 と共用）----

        static void AddInput(List<PlaytestSummaryRow> rows, IReadOnlyList<PlaytestEvent> events)
        {
            const string s = SectionInput;
            var receiveToFire = new List<double>();
            var inputToReceive = new List<double>();

            foreach (PlaytestEvent e in events)
            {
                if (e.Type != PlaytestEventType.Fire) continue;
                if (e.ReceiveTime.HasValue && e.FireTime.HasValue)
                    receiveToFire.Add((e.FireTime.Value - e.ReceiveTime.Value) * 1000.0);
                if (e.InputTime.HasValue && e.ReceiveTime.HasValue)
                    inputToReceive.Add(e.ReceiveTime.Value - e.InputTime.Value);
            }

            AddPercentiles(rows, s, "receive_to_fire_ms", receiveToFire, "0.0");

            // コントローラの時計と Unity の時計は原点が違うので、最小値を 0 とした揺らぎ（ジッタ）だけを出す
            if (inputToReceive.Count > 0)
            {
                double offset = double.PositiveInfinity;
                foreach (double d in inputToReceive) offset = Math.Min(offset, d);
                for (int i = 0; i < inputToReceive.Count; i++)
                    inputToReceive[i] = (inputToReceive[i] - offset) * 1000.0;
            }
            AddPercentiles(rows, s, "input_to_receive_jitter_ms", inputToReceive, "0.0");
        }

        // ---- 部品（テストから使う）----

        /// <summary>停止判定に使う振りの時刻（発射確定と、セッション外以外の却下）。昇順。</summary>
        public static List<double> SwingTimes(IReadOnlyList<PlaytestEvent> events)
        {
            var times = new List<double>();
            foreach (PlaytestEvent e in events)
            {
                if (e.Type == PlaytestEventType.Fire || (e.Type == PlaytestEventType.SwingRejected && e.Detail != RejectInactive))
                    times.Add(e.T);
            }
            times.Sort();
            return times;
        }

        /// <summary>
        /// threshold 秒以上振らなかった区間。開始から最初の振り、最後の振りから終わりまでも含める。
        /// </summary>
        public static List<(double Start, double End)> IdleGaps(IReadOnlyList<double> sortedTimes, double start, double end, double threshold)
        {
            var gaps = new List<(double Start, double End)>();
            double prev = start;
            foreach (double raw in sortedTimes)
            {
                double t = Math.Min(Math.Max(raw, start), end);
                if (t - prev >= threshold) gaps.Add((prev, t));
                prev = Math.Max(prev, t);
            }
            if (end - prev >= threshold) gaps.Add((prev, end));
            return gaps;
        }

        /// <summary>
        /// 同時にいた黒客（黒客として出現 or 黒客化してから退場するまで）の最大数を区間ごとに返す。
        /// 区間の最大には、区間の開始時点で残っている数も含める。
        /// </summary>
        public static int[] MaxSimultaneousBlack(IReadOnlyList<PlaytestEvent> events, int segmentCount, double segmentSeconds)
        {
            int n = Math.Max(1, segmentCount);
            var result = new int[n];
            var black = new HashSet<int>();
            int current = 0;

            foreach (PlaytestEvent e in events)
            {
                int k = SegmentOf(e.T, n, segmentSeconds);
                while (current < k)
                {
                    current++;
                    result[current] = Math.Max(result[current], black.Count);
                }

                if (e.TargetId.HasValue)
                {
                    if ((e.Type == PlaytestEventType.Spawn && e.IsBlack == true) || e.Type == PlaytestEventType.BlackConversion)
                        black.Add(e.TargetId.Value);
                    else if (e.Type == PlaytestEventType.Despawn)
                        black.Remove(e.TargetId.Value);
                }
                result[current] = Math.Max(result[current], black.Count);
            }

            // 最後のイベント以降の区間にも、残っている黒客を持ち越す
            while (current < n - 1)
            {
                current++;
                result[current] = Math.Max(result[current], black.Count);
            }
            return result;
        }

        static int SegmentOf(double t, int count, double length)
        {
            if (double.IsInfinity(length) || count <= 1) return 0;
            int k = (int)Math.Floor(t / length);
            return Math.Max(0, Math.Min(count - 1, k));
        }

        static Counts CountIn(IReadOnlyList<PlaytestEvent> events, double from, double to)
        {
            var c = new Counts();
            foreach (PlaytestEvent e in events)
            {
                if (e.T < from || e.T >= to) continue;
                switch (e.Type)
                {
                    case PlaytestEventType.Fire: c.Throws++; break;
                    case PlaytestEventType.SwingRejected: if (e.Detail != RejectInactive) c.Rejected++; break;
                    case PlaytestEventType.Rescue: c.Rescues++; break;
                    case PlaytestEventType.BlackConversion: c.Conversions++; break;
                }

                if (e.Type == PlaytestEventType.Landing) c.Landings++;
                if (!IsCustomerHit(e)) continue;

                c.Hits++;
                bool good = e.Zone != null && e.Zone != ZoneMiss;
                if (good) c.GoodHits++;
                if (e.ColorError == true) c.ColorErrors++;
                if (e.Type == PlaytestEventType.Landing)
                {
                    c.LandingHits++;
                    if (good) c.GoodLandingHits++;
                }
            }
            return c;
        }

        static bool IsCustomerHit(PlaytestEvent e)
        {
            return (e.Type == PlaytestEventType.Landing || e.Type == PlaytestEventType.Hit) && e.TargetId.HasValue;
        }

        static int RankOrder(string rank)
        {
            switch (rank)
            {
                case "C": return 0;
                case "B": return 1;
                case "A": return 2;
                case "S": return 3;
                default: return -1;
            }
        }

        static string Sec(double seconds) => seconds.ToString("0", CultureInfo.InvariantCulture);

        static double SumOverlap(List<(double Start, double End)> gaps, double from, double to)
        {
            double sum = 0.0;
            foreach ((double start, double end) in gaps)
                sum += Math.Max(0.0, Math.Min(end, to) - Math.Max(start, from));
            return sum;
        }

        static int CountType(IReadOnlyList<PlaytestEvent> events, string type)
        {
            int n = 0;
            foreach (PlaytestEvent e in events)
            {
                if (e.Type == type) n++;
            }
            return n;
        }

        static PlaytestEvent FirstWith(IReadOnlyList<PlaytestEvent> events, Func<PlaytestEvent, bool> match)
        {
            foreach (PlaytestEvent e in events)
            {
                if (match(e)) return e;
            }
            return null;
        }

        static PlaytestEvent LastWith(IReadOnlyList<PlaytestEvent> events, Func<PlaytestEvent, bool> match)
        {
            for (int i = events.Count - 1; i >= 0; i--)
            {
                if (match(events[i])) return events[i];
            }
            return null;
        }

        static void Increment(SortedDictionary<string, int> counts, string key)
        {
            key = string.IsNullOrEmpty(key) ? "NA" : key;
            counts.TryGetValue(key, out int n);
            counts[key] = n + 1;
        }

        static double? Ratio(int numerator, int denominator)
        {
            return denominator > 0 ? (double)numerator / denominator : (double?)null;
        }

        static void AddPercentiles(List<PlaytestSummaryRow> rows, string section, string name, List<double> values, string format)
        {
            Add(rows, section, name + ".count", values.Count);
            Add(rows, section, name + ".p50", PlaytestStats.Percentile(values, 0.50), format);
            Add(rows, section, name + ".p75", PlaytestStats.Percentile(values, 0.75), format);
            Add(rows, section, name + ".p95", PlaytestStats.Percentile(values, 0.95), format);
            Add(rows, section, name + ".max", PlaytestStats.Percentile(values, 1.0), format);
        }

        static void Add(List<PlaytestSummaryRow> rows, string section, string metric, double? value, string format = "0.###")
        {
            rows.Add(new PlaytestSummaryRow(section, metric, PlaytestCsv.Num(value, format)));
        }

        static void Add(List<PlaytestSummaryRow> rows, string section, string metric, int? value)
        {
            rows.Add(new PlaytestSummaryRow(section, metric, PlaytestCsv.Int(value)));
        }

        static void AddText(List<PlaytestSummaryRow> rows, string section, string metric, string value)
        {
            rows.Add(new PlaytestSummaryRow(section, metric, value ?? ""));
        }
    }
}
