using System;
using System.Collections.Generic;
using System.Text;

namespace Toufuku.Playtest
{
    /// <summary>
    /// 参加者 1 人分の記録 — Issue #53（仕様書 v8 17章 T0-3M・19章）
    ///
    /// | 系統 | 項目 | 誰が入れるか |
    /// |---|---|---|
    /// | 自動 | 1 分ごとの投数・着弾・命中、実操作周期、実施秒・完走 | ゲーム |
    /// | 観察（試技中にキーで） | 持ち替え、腕の下がり（回数と初回の秒）、終了希望 | 実施者 |
    /// | 直後の聞き取り | 疲労 5 段階、痛み・恐怖・ストラップ逸脱、実際の再挑戦選択、所見 | 実施者 |
    ///
    /// 疲労と再挑戦が入っていない行は合否の集計に入らない（安全事象だけは入力を待たずに数える）。
    /// MonoBehaviour 非依存。
    /// </summary>
    [Serializable]
    public class EnduranceRecord
    {
        public string testId = "T0-3M-1";
        /// <summary>実施日（yyyy-MM-dd）。別日の 2 回目はこの日付で見分ける。</summary>
        public string date = "";
        public string owner = "";
        /// <summary>参加者番号（1 始まり）。氏名は書かない。</summary>
        public int participantNo = 1;

        // ── 固定した条件（記録に残して、途中で変わっていないことを確かめる）──
        /// <summary>実際に適用していたクールダウン秒（T0-CD の採用値）。</summary>
        public float cooldownSeconds = 0.50f;
        /// <summary>T0-A/B の採用案の名前。</summary>
        public string feedbackLabel = "";

        // ── 自動 ──
        public float plannedSeconds = EnduranceTestPlan.TrialSeconds;
        /// <summary>実際に振っていた秒（途中終了ならそこまで）。</summary>
        public float endSeconds;
        /// <summary>3:00 まで続けたか。</summary>
        public bool completed;
        /// <summary>3:00 より前に終了した（終了希望・中止）。</summary>
        public bool stopRequested;

        public int[] throwsPerMinute = new int[EnduranceTestPlan.MinuteCount];
        public int[] landingsPerMinute = new int[EnduranceTestPlan.MinuteCount];
        public int[] hitsPerMinute = new int[EnduranceTestPlan.MinuteCount];
        /// <summary>クールダウン中の振り（参考値）。</summary>
        public int rejected;
        /// <summary>実操作周期（発射と発射の間隔、秒）。全員分を束ねて中央値・p75 を出すので個々の値を持つ。</summary>
        public List<float> cycles = new List<float>();

        // ── 観察 ──
        /// <summary>大幣を持ち替えた回数。</summary>
        public int gripChanges;
        /// <summary>腕が下がった（振り上げが明らかに低くなった）と見た回数。</summary>
        public int armDrops;
        /// <summary>初めて腕の下がりを見た秒。無ければ負。</summary>
        public float firstArmDropSec = -1f;

        // ── 直後の聞き取り ──
        /// <summary>疲労 5 段階（1〜5）。0 は未回答。</summary>
        public int fatigue;
        public bool pain;
        public bool fear;
        public bool strapDeviation;
        /// <summary>再挑戦の選択を記録したか（未記入と「別の遊び」を区別する）。</summary>
        public bool retryAnswered;
        /// <summary>実際に「もう一度」を選んだか（口頭の希望ではなく行動）。</summary>
        public bool retry;
        public string note = "";

        public int MinuteCount => EnduranceTestPlan.MinuteCount;

        /// <summary>その分に入ったか（途中終了で届かなかった分は欠測）。</summary>
        public bool ReachedMinute(int minute) =>
            minute == 0 || endSeconds > minute * EnduranceTestPlan.MinuteSeconds;

        public int? ThrowsOf(int minute) => Valid(minute) && ReachedMinute(minute) ? throwsPerMinute[minute] : (int?)null;
        public int? LandingsOf(int minute) => Valid(minute) && ReachedMinute(minute) ? landingsPerMinute[minute] : (int?)null;
        public int? HitsOf(int minute) => Valid(minute) && ReachedMinute(minute) ? hitsPerMinute[minute] : (int?)null;

        public double? HitRateOf(int minute)
        {
            int? landings = LandingsOf(minute);
            int? hits = HitsOf(minute);
            return landings.HasValue && hits.HasValue && landings.Value > 0 ? (double)hits.Value / landings.Value : (double?)null;
        }

        public int TotalThrows
        {
            get
            {
                int n = 0;
                foreach (int t in throwsPerMinute) n += t;
                return n;
            }
        }

        /// <summary>最終 1 分の投数低下（1 − 最終分 ÷ 初分）。完走していない・最終分に届いていない・初分が 0 なら欠測。</summary>
        public double? ThrowsDropRatio
        {
            get
            {
                int last = EnduranceTestPlan.MinuteCount - 1;
                // 試技を短くした確認プレイでは「完走」でも最終分に入っていないので、0 投の最終分と区別する
                if (!completed || !ReachedMinute(last) || throwsPerMinute[0] <= 0) return null;
                return 1.0 - (double)throwsPerMinute[last] / throwsPerMinute[0];
            }
        }

        public int CycleCount => cycles.Count;
        public double? CycleMedian => PlaytestStats.Percentile(CyclesAsDouble(), 0.50);
        public double? CycleP75 => PlaytestStats.Percentile(CyclesAsDouble(), 0.75);

        public IEnumerable<double> CyclesAsDouble()
        {
            foreach (float c in cycles) yield return c;
        }

        /// <summary>痛み・恐怖・ストラップ逸脱の件数。</summary>
        public int SafetyIncidents => (pain ? 1 : 0) + (fear ? 1 : 0) + (strapDeviation ? 1 : 0);
        public bool HasSafetyIncident => SafetyIncidents > 0;

        public bool FatigueAnswered => EnduranceTestPlan.IsValidScale(fatigue);

        /// <summary>合否の集計に入れられる記録か。</summary>
        public bool IsComplete => participantNo > 0 && FatigueAnswered && retryAnswered;

        /// <summary>足りない項目（保存ボタンの横に出す）。すべてそろっていれば空文字。</summary>
        public string MissingFields()
        {
            var sb = new StringBuilder();
            if (participantNo <= 0) Append(sb, "参加者番号");
            if (!FatigueAnswered) Append(sb, "疲労");
            if (!retryAnswered) Append(sb, "再挑戦の選択");
            return sb.ToString();
        }

        public void SetRetry(bool value)
        {
            retry = value;
            retryAnswered = true;
        }

        /// <summary>腕の下がりを 1 回記録する。</summary>
        public void MarkArmDrop(double seconds)
        {
            armDrops++;
            if (firstArmDropSec < 0f) firstArmDropSec = (float)Math.Max(0.0, seconds);
        }

        /// <summary>試技が終わったときに自動の値を写す。</summary>
        public void SetFromLog(EnduranceThrowLog log, double endSec, bool completedTrial)
        {
            endSeconds = (float)endSec;
            completed = completedTrial;
            stopRequested = !completedTrial;

            EnsureArrays();
            for (int k = 0; k < EnduranceTestPlan.MinuteCount; k++)
            {
                throwsPerMinute[k] = log != null ? log.ThrowsIn(k) : 0;
                landingsPerMinute[k] = log != null ? log.LandingsIn(k) : 0;
                hitsPerMinute[k] = log != null ? log.HitsIn(k) : 0;
            }

            rejected = log != null ? log.Rejected : 0;
            cycles.Clear();
            if (log != null)
            {
                foreach (double d in log.Intervals()) cycles.Add((float)d);
            }
        }

        public void EnsureArrays()
        {
            int n = EnduranceTestPlan.MinuteCount;
            if (throwsPerMinute == null || throwsPerMinute.Length != n) throwsPerMinute = new int[n];
            if (landingsPerMinute == null || landingsPerMinute.Length != n) landingsPerMinute = new int[n];
            if (hitsPerMinute == null || hitsPerMinute.Length != n) hitsPerMinute = new int[n];
            if (cycles == null) cycles = new List<float>();
        }

        public EnduranceRecord Clone()
        {
            var copy = (EnduranceRecord)MemberwiseClone();
            copy.throwsPerMinute = (int[])throwsPerMinute?.Clone();
            copy.landingsPerMinute = (int[])landingsPerMinute?.Clone();
            copy.hitsPerMinute = (int[])hitsPerMinute?.Clone();
            copy.cycles = cycles != null ? new List<float>(cycles) : new List<float>();
            copy.EnsureArrays();
            return copy;
        }

        bool Valid(int minute) => minute >= 0 && throwsPerMinute != null && minute < throwsPerMinute.Length;

        static void Append(StringBuilder sb, string label)
        {
            if (sb.Length > 0) sb.Append('・');
            sb.Append(label);
        }
    }
}
