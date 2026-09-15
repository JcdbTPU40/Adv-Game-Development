using System;
using System.Collections.Generic;
using System.Globalization;

namespace Toufuku.Playtest
{
    // 完了条件1つと、不合格だったときにどうするか（企画書 17章 T6-USB の不合格のときの対応）
    public readonly struct UsbGateCriterion
    {
        public readonly AbCriterion Criterion;
        public readonly string Remedy;

        public UsbGateCriterion(AbCriterion criterion, string remedy)
        {
            Criterion = criterion;
            Remedy = remedy;
        }

        public override string ToString() => Criterion.Passed ? Criterion.ToString() : $"{Criterion} → {Remedy}";
    }

    /*
        わざと100投の入力の遅れ（#52）

        1投の遅れ = 弾が画面に出た時刻（visible_time）− 入力時刻を Unity の時計に合わせて出した値（input_unity）
        入力時刻がない投げ（5つ目を送らないファームウェアやマウスのとき）は、受け取った時刻（receive_time）から測る
        そのときは USB で送る時間と ESP32 の中の待ちが入らないので、とりあえずの値（InputBased = false）
    */
    public sealed class UsbLatencyStats
    {
        // 発射が決まった数
        public int Fires { get; private set; }
        // 画面に出た時刻が取れて、遅れを出せた数
        public int Count { get; private set; }
        // そのうち入力時刻から測れた数
        public int WithInput { get; private set; }
        // ぜんぶ入力時刻から測れたかどうか
        public bool InputBased => Count > 0 && WithInput == Count;

        public double? P50Ms { get; private set; }
        public double? P95Ms { get; private set; }
        public double? MaxMs { get; private set; }

        // 中身（p95）: 入力 → Unity が受け取る / 受け取り → 発射が決まる / 発射が決まる → 画面
        public double? InputToReceiveP95Ms { get; private set; }
        public double? ReceiveToFireP95Ms { get; private set; }
        public double? FireToVisibleP95Ms { get; private set; }

        public static UsbLatencyStats Of(IEnumerable<UsbGateEvent> events)
        {
            var s = new UsbLatencyStats();
            var totals = new List<double>();
            var inputToReceive = new List<double>();
            var receiveToFire = new List<double>();
            var fireToVisible = new List<double>();

            if (events != null)
            {
                foreach (UsbGateEvent e in events)
                {
                    if (e == null || !e.Is(UsbGateEventType.Fire)) continue;
                    s.Fires++;

                    if (e.InputUnity.HasValue && e.ReceiveTime.HasValue)
                        inputToReceive.Add((e.ReceiveTime.Value - e.InputUnity.Value) * 1000.0);
                    if (e.ReceiveTime.HasValue && e.FireTime.HasValue)
                        receiveToFire.Add((e.FireTime.Value - e.ReceiveTime.Value) * 1000.0);
                    if (e.FireTime.HasValue && e.VisibleTime.HasValue)
                        fireToVisible.Add((e.VisibleTime.Value - e.FireTime.Value) * 1000.0);

                    if (!e.VisibleTime.HasValue) continue;
                    if (e.InputUnity.HasValue)
                    {
                        totals.Add((e.VisibleTime.Value - e.InputUnity.Value) * 1000.0);
                        s.WithInput++;
                    }
                    else if (e.ReceiveTime.HasValue)
                    {
                        totals.Add((e.VisibleTime.Value - e.ReceiveTime.Value) * 1000.0);
                    }
                    else
                    {
                        continue;
                    }
                    s.Count++;
                }
            }

            s.P50Ms = PlaytestStats.Percentile(totals, 0.50);
            s.P95Ms = PlaytestStats.Percentile(totals, 0.95);
            s.MaxMs = PlaytestStats.Percentile(totals, 1.00);
            s.InputToReceiveP95Ms = PlaytestStats.Percentile(inputToReceive, 0.95);
            s.ReceiveToFireP95Ms = PlaytestStats.Percentile(receiveToFire, 0.95);
            s.FireToVisibleP95Ms = PlaytestStats.Percentile(fireToVisible, 0.95);
            return s;
        }

        public string Describe()
        {
            if (Count == 0) return "入力遅延: データなし";
            return $"入力遅延 n={Count}/{Fires}（{(InputBased ? "入力時刻から" : $"受信時刻から・暫定 入力時刻 {WithInput} 件")}）" +
                   $" p50 {Ms(P50Ms)} p95 {Ms(P95Ms)} 最大 {Ms(MaxMs)}" +
                   $"　内訳 p95: 入力→受信 {Ms(InputToReceiveP95Ms)} / 受信→発射 {Ms(ReceiveToFireP95Ms)} / 発射→画面 {Ms(FireToVisibleP95Ms)}";
        }

        static string Ms(double? value) => value.HasValue ? $"{value.Value:0.0}ms" : "-";
    }

    /*
        T6-USB 1回ぶんの合格・不合格を出すクラス（#52 / 企画書 v8 17章）

        記録の CSV（1イベント1行）から、区間ごとに「最後までやった最後の回」を選んで判定する
        子どもは参加者ごとに最後までやった最後の回を使う。やめた回は合格かどうかに使わない（安全のことだけは、やめた回も数える）

        完了条件と判定のしかた:
        ・安全チェックがぜんぶ OK → 最後の安全チェックでぜんぶの項目が OK
        ・ぶつかった・はみ出たが0件 → その日のすべての回の安全のこと（やめた回も入れる）
        ・遅れの p95 が 80ms 以下、最大が 100ms 以下 → わざと100投のそれぞれの「入力 → 画面に弾」（UsbLatencyStats）
        ・わざと入力の抜けが 2% 未満、まちがい発射が 2% 以下 → 合図と照らし合わせる（UsbIntentMatcher）。振ろうとした投げが100より少なければ不合格
        ・切断0 → 実機を使うぜんぶの区間の受け取りのとぎれ。実機につながっていない区間が1つでもあれば不合格
        ・3分のドリフトが画面のはばの 5% 以下 → 止めたままと、さわったあとの両方。置き台での照準の画面 X の差 ÷ 画面のはば
        ・60fps、1% low が 55fps 以上 → 30人の負荷を測った区間。描いている人数のいちばん少ないのが30より少なければ、負荷の条件を満たしていない
        ・近いのは 7/10、遠いのは 6/10 当たる → 子ども全員の合計（5人 × 10投 = 50投で、近いのは35、遠いのは30以上）。参加者が5人より少なければ不合格

        「2回連続合格してから MVP を決める」（19章）は1回ぶんでは判定できないので、テスト記録に2回ならべる
        MonoBehaviour は使っていない
    */
    public sealed class UsbGateSummary
    {
        public const string RemedySafety = "該当項目を直してからチェックし直す。安全事象はその場で止め、原因を除くまで再開しない（12章）";
        public const string RemedyLatency = "描画を簡略化する（#45 の軽量化）。発射→画面が小さいのに遅い場合は受信の間隔（送信周期）を見直す";
        public const string RemedyInput = "3章: T0-CD の採用クールダウンは変えずに、有効スイングの角度閾値だけを独立実験する";
        public const string RemedyLink = "USB ケーブル・端子を交換し、ストレインリリーフを取り直して再計測する。赤のまま MVP を固定しない";
        public const string RemedyDrift = "照準を「振り始めの姿勢からの相対値」（相対姿勢）へ切り替える（3章 ヨー角ドリフト対策の最終手段）";
        public const string RemedyFps = "描画を簡略化する（#45: ダイレート早期棄却・マスク解像度 1/2・SSAO を切る など）";
        public const string RemedyAim = "当たり判定・照準補正を見直す（判定半径・照準の押し戻し・着弾目標点の固定タイミング）";
        public const string RemedyLoadCondition = "負荷条件（黒客・退場者込み 30 体）を満たすよう体数の設定を直して計測し直す";

        readonly List<UsbGateCriterion> _criteria = new List<UsbGateCriterion>();

        public IReadOnlyList<UsbGateCriterion> Criteria => _criteria;
        public bool Passed { get; private set; }
        public int PlannedChildren { get; private set; }

        // ---- くわしい中身（やる人のパネルやテスト記録用） ----
        public bool SafetyChecked { get; private set; }
        public int SafetyItemsOk { get; private set; }
        public int Incidents { get; private set; }

        public UsbLatencyStats Latency { get; private set; } = UsbLatencyStats.Of(null);
        public UsbIntentResult? Intent { get; private set; }
        public int CooldownRejects { get; private set; }

        public int ControllerRuns { get; private set; }
        public int UnconnectedRuns { get; private set; }
        public int Disconnects { get; private set; }

        public double? DriftStatic { get; private set; }
        public double? DriftOperate { get; private set; }

        public double? AverageFps { get; private set; }
        public double? OnePercentLowFps { get; private set; }
        public double? RenderedMin { get; private set; }

        public int Children { get; private set; }
        public int NearHits { get; private set; }
        public int NearThrows { get; private set; }
        public int FarHits { get; private set; }
        public int FarThrows { get; private set; }

        public string FailureSummary()
        {
            var parts = new List<string>();
            foreach (UsbGateCriterion c in _criteria)
            {
                if (!c.Criterion.Passed) parts.Add($"{c.Criterion.Name} {c.Criterion.Actual}");
            }
            return parts.Count == 0 ? "-" : string.Join(" / ", parts);
        }

        public static UsbGateSummary Of(IReadOnlyList<UsbGateEvent> events, int plannedChildren = UsbGatePlan.ChildParticipants)
        {
            var s = new UsbGateSummary { PlannedChildren = plannedChildren };
            events = events ?? new UsbGateEvent[0];

            foreach (UsbGateEvent e in events)
            {
                if (e != null && e.Is(UsbGateEventType.Incident)) s.Incidents++;
            }

            // ---- 安全チェック ----
            List<UsbGateEvent> safety = LatestCompletedRun(events, UsbGateSection.Safety);
            if (safety != null)
            {
                s.SafetyChecked = true;
                foreach (UsbSafetyItem item in UsbGatePlan.SafetyItems)
                {
                    foreach (UsbGateEvent e in safety)
                    {
                        if (!e.Is(UsbGateEventType.SafetyItem) || e.Label != item.Id) continue;
                        if (e.Flag) s.SafetyItemsOk++;
                        break;
                    }
                }
            }

            // ---- わざと100投 ----
            List<UsbGateEvent> throws = LatestCompletedRun(events, UsbGateSection.Throws100);
            if (throws != null)
            {
                s.Latency = UsbLatencyStats.Of(throws);
                var cues = new List<double>();
                var voided = new HashSet<int>();
                var fires = new List<double>();
                foreach (UsbGateEvent e in throws)
                {
                    if (e.Is(UsbGateEventType.Cue)) cues.Add(e.T);
                    else if (e.Is(UsbGateEventType.CueVoid)) voided.Add(e.Seq - 1);
                    else if (e.Is(UsbGateEventType.Fire)) fires.Add(e.T);
                    else if (e.Is(UsbGateEventType.Reject) && e.Label == "Cooldown") s.CooldownRejects++;
                }
                cues.Sort();
                s.Intent = UsbIntentMatcher.Match(cues, voided, fires);
            }

            // ---- 切断（実機を使うぜんぶの区間の最後の回） ----
            var controllerRuns = new List<List<UsbGateEvent>>();
            foreach (UsbGateSection section in new[] { UsbGateSection.Throws100, UsbGateSection.DriftStatic, UsbGateSection.DriftOperate, UsbGateSection.Load })
            {
                List<UsbGateEvent> run = LatestCompletedRun(events, section);
                if (run != null) controllerRuns.Add(run);
            }
            Dictionary<int, List<UsbGateEvent>> children = LatestCompletedRunPerParticipant(events, UsbGateSection.Children);
            controllerRuns.AddRange(children.Values);

            foreach (List<UsbGateEvent> run in controllerRuns)
            {
                s.ControllerRuns++;
                double? connected = MetricOf(run, UsbGateMetric.Connected);
                if (!connected.HasValue || connected.Value < 0.5) s.UnconnectedRuns++;
                foreach (UsbGateEvent e in run)
                {
                    if (e.Is(UsbGateEventType.Disconnect)) s.Disconnects++;
                }
            }

            // ---- ドリフト ----
            s.DriftStatic = DriftOf(LatestCompletedRun(events, UsbGateSection.DriftStatic));
            s.DriftOperate = DriftOf(LatestCompletedRun(events, UsbGateSection.DriftOperate));

            // ---- 30人の負荷 ----
            List<UsbGateEvent> load = LatestCompletedRun(events, UsbGateSection.Load);
            if (load != null)
            {
                s.AverageFps = MetricOf(load, UsbGateMetric.AverageFps);
                s.OnePercentLowFps = MetricOf(load, UsbGateMetric.OnePercentLowFps);
                s.RenderedMin = MetricOf(load, UsbGateMetric.RenderedMin);
            }

            // ---- 子ども ----
            s.Children = children.Count;
            foreach (List<UsbGateEvent> run in children.Values)
            {
                foreach (UsbGateEvent e in run)
                {
                    if (e.Is(UsbGateEventType.Fire))
                    {
                        if (e.Label == UsbGateBlock.Near) s.NearThrows++;
                        else if (e.Label == UsbGateBlock.Far) s.FarThrows++;
                    }
                    else if (e.Is(UsbGateEventType.Landing) && e.Flag)
                    {
                        if (e.Label == UsbGateBlock.Near) s.NearHits++;
                        else if (e.Label == UsbGateBlock.Far) s.FarHits++;
                    }
                }
            }

            s.BuildCriteria();
            return s;
        }

        void BuildCriteria()
        {
            int items = UsbGatePlan.SafetyItems.Length;
            Add("安全チェック全項目適合",
                SafetyChecked ? $"{SafetyItemsOk}/{items} 項目" : "未実施",
                $"{items}/{items} 項目", SafetyChecked && SafetyItemsOk == items, RemedySafety);
            Add("接触／逸脱", $"{Incidents} 件", "0 件", Incidents == 0, RemedySafety);

            UsbLatencyStats l = Latency;
            string latencyNote = l.Count == 0 ? "" : l.InputBased ? "" : "（受信時刻から・暫定）";
            Add("入力遅延 p95", l.P95Ms.HasValue ? $"{l.P95Ms.Value:0.0}ms{latencyNote}" : "データなし",
                $"{UsbGatePlan.LatencyP95LimitMs:0}ms 以下",
                l.P95Ms.HasValue && l.P95Ms.Value <= UsbGatePlan.LatencyP95LimitMs + 1e-9, RemedyLatency);
            Add("入力遅延 最大", l.MaxMs.HasValue ? $"{l.MaxMs.Value:0.0}ms{latencyNote}" : "データなし",
                $"{UsbGatePlan.LatencyMaxLimitMs:0}ms 以下",
                l.MaxMs.HasValue && l.MaxMs.Value <= UsbGatePlan.LatencyMaxLimitMs + 1e-9, RemedyLatency);

            if (Intent.HasValue)
            {
                UsbIntentResult r = Intent.Value;
                bool enough = r.Intended >= UsbGatePlan.IntendedThrows;
                string n = enough ? "" : $"・意図 {r.Intended} 投で不足";
                Add("意図的入力の欠落", $"{r.Missed}/{r.Intended}（{Percent(r.MissRate)}）{n}",
                    $"{UsbGatePlan.MissRateLimit * 100:0}% 未満・{UsbGatePlan.IntendedThrows} 投以上",
                    enough && r.MissRate.HasValue && UsbGatePlan.MissRateOk(r.MissRate.Value), RemedyInput);
                Add("誤発射", $"{r.Extra}/{r.Intended}（{Percent(r.FalseFireRate)}）{n}",
                    $"{UsbGatePlan.FalseFireRateLimit * 100:0}% 以下・{UsbGatePlan.IntendedThrows} 投以上",
                    enough && r.FalseFireRate.HasValue && UsbGatePlan.FalseFireRateOk(r.FalseFireRate.Value), RemedyInput);
            }
            else
            {
                Add("意図的入力の欠落", "未実施", $"{UsbGatePlan.MissRateLimit * 100:0}% 未満", false, RemedyInput);
                Add("誤発射", "未実施", $"{UsbGatePlan.FalseFireRateLimit * 100:0}% 以下", false, RemedyInput);
            }

            string link = ControllerRuns == 0 ? "データなし"
                : UnconnectedRuns > 0 ? $"{Disconnects} 回・未接続の区間 {UnconnectedRuns}"
                : $"{Disconnects} 回（{ControllerRuns} 区間）";
            Add("切断", link, "0 回（全区間 USB 接続）",
                ControllerRuns > 0 && UnconnectedRuns == 0 && Disconnects == 0, RemedyLink);

            bool driftOk = DriftStatic.HasValue && DriftOperate.HasValue &&
                           UsbGatePlan.DriftOk(DriftStatic.Value) && UsbGatePlan.DriftOk(DriftOperate.Value);
            Add("3 分ドリフト（静止／操作）", $"{Percent(Abs(DriftStatic))} / {Percent(Abs(DriftOperate))}",
                $"画面幅の {UsbGatePlan.DriftLimitScreenRatio * 100:0}% 以下（両方）", driftOk, RemedyDrift);

            Add("負荷条件（描画体数の最小）", RenderedMin.HasValue ? $"{RenderedMin.Value:0} 体" : "未実施",
                $"{UsbGatePlan.LoadBodies} 体以上", RenderedMin.HasValue && RenderedMin.Value >= UsbGatePlan.LoadBodies - 1e-9,
                RemedyLoadCondition);
            Add("平均 fps（30 体負荷）", AverageFps.HasValue ? $"{AverageFps.Value:0.0}fps" : "未実施",
                $"{UsbGatePlan.TargetFps:0}fps（{UsbGatePlan.TargetFps - UsbGatePlan.TargetFpsTolerance:0} 以上）",
                AverageFps.HasValue && UsbGatePlan.AverageFpsOk(AverageFps.Value), RemedyFps);
            Add("1% low（30 体負荷）", OnePercentLowFps.HasValue ? $"{OnePercentLowFps.Value:0.0}fps" : "未実施",
                $"{UsbGatePlan.OnePercentLowLimitFps:0}fps 以上",
                OnePercentLowFps.HasValue && UsbGatePlan.OnePercentLowOk(OnePercentLowFps.Value), RemedyFps);

            bool enoughChildren = Children >= PlannedChildren;
            string who = enoughChildren ? $"{Children} 人" : $"{Children}/{PlannedChildren} 人で不足";
            Add("近の命中", $"{NearHits}/{NearThrows}（{Percent(Ratio(NearHits, NearThrows))}・{who}）",
                $"{UsbGatePlan.NearHitRatio * 10:0}/10 以上",
                enoughChildren && UsbGatePlan.HitRatioOk(NearHits, NearThrows, UsbGatePlan.NearHitRatio), RemedyAim);
            Add("遠の命中", $"{FarHits}/{FarThrows}（{Percent(Ratio(FarHits, FarThrows))}・{who}）",
                $"{UsbGatePlan.FarHitRatio * 10:0}/10 以上",
                enoughChildren && UsbGatePlan.HitRatioOk(FarHits, FarThrows, UsbGatePlan.FarHitRatio), RemedyAim);

            bool passed = true;
            foreach (UsbGateCriterion c in _criteria)
            {
                if (!c.Criterion.Passed) passed = false;
            }
            Passed = passed;
        }

        void Add(string name, string actual, string required, bool passed, string remedy) =>
            _criteria.Add(new UsbGateCriterion(new AbCriterion(name, actual, required, passed), remedy));

        // ---- どの回を使うかの選び方（テストから使う） ----

        // その区間で最後までやった（section_end の flag = 1）最後の回の行。なければ null
        public static List<UsbGateEvent> LatestCompletedRun(IReadOnlyList<UsbGateEvent> events, UsbGateSection section)
        {
            int run = -1;
            string key = UsbGatePlan.KeyOf(section);
            foreach (UsbGateEvent e in events)
            {
                if (e != null && e.Section == key && e.Is(UsbGateEventType.SectionEnd) && e.Flag && e.Run > run) run = e.Run;
            }
            return run < 0 ? null : RowsOf(events, key, run);
        }

        // 参加者ごとの、最後までやった最後の回の行
        public static Dictionary<int, List<UsbGateEvent>> LatestCompletedRunPerParticipant(IReadOnlyList<UsbGateEvent> events, UsbGateSection section)
        {
            string key = UsbGatePlan.KeyOf(section);
            var latest = new Dictionary<int, int>();
            foreach (UsbGateEvent e in events)
            {
                if (e == null || e.Section != key || !e.Is(UsbGateEventType.SectionEnd) || !e.Flag || e.Participant <= 0) continue;
                if (!latest.TryGetValue(e.Participant, out int run) || e.Run > run) latest[e.Participant] = e.Run;
            }

            var result = new Dictionary<int, List<UsbGateEvent>>();
            foreach (KeyValuePair<int, int> pair in latest)
                result[pair.Key] = RowsOf(events, key, pair.Value);
            return result;
        }

        static List<UsbGateEvent> RowsOf(IReadOnlyList<UsbGateEvent> events, string key, int run)
        {
            var rows = new List<UsbGateEvent>();
            foreach (UsbGateEvent e in events)
            {
                if (e != null && e.Section == key && e.Run == run) rows.Add(e);
            }
            return rows;
        }

        public static double? MetricOf(IReadOnlyList<UsbGateEvent> run, string label)
        {
            if (run == null) return null;
            double? value = null;
            foreach (UsbGateEvent e in run)
            {
                if (e.Is(UsbGateEventType.Metric) && e.Label == label) value = e.Value;
            }
            return value;
        }

        // 置き台での照準の画面 X の差（終わり − 始め、画面のはばに対するわりあい）。どっちかがなければ null
        public static double? DriftOf(IReadOnlyList<UsbGateEvent> run)
        {
            if (run == null) return null;
            double? start = null, end = null;
            foreach (UsbGateEvent e in run)
            {
                if (!e.Is(UsbGateEventType.DriftSample) || !e.Value.HasValue) continue;
                if (e.Label == UsbGateBlock.DriftStart) start = e.Value;
                else if (e.Label == UsbGateBlock.DriftEnd) end = e.Value;
            }
            return start.HasValue && end.HasValue ? end.Value - start.Value : (double?)null;
        }

        static double? Ratio(int hits, int throws) => throws > 0 ? (double)hits / throws : (double?)null;

        static double? Abs(double? value) => value.HasValue ? Math.Abs(value.Value) : (double?)null;

        static string Percent(double? value) =>
            value.HasValue ? (value.Value * 100.0).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "-";
    }

    // label 列に入る区切りの名前
    public static class UsbGateBlock
    {
        public const string Practice = "practice";
        public const string Near = "near";
        public const string Far = "far";
        public const string DriftStart = "start";
        public const string DriftEnd = "end";
        public const string DriftTrack = "track";
        public const string Contact = "contact";
        public const string Deviation = "deviation";
    }
}
