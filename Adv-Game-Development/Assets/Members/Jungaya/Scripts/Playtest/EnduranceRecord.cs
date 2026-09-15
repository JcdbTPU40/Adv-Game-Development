using System;
using System.Collections.Generic;
using System.Text;

namespace Toufuku.Playtest
{
    /*
        参加者1人ぶんの記録（#53 / 企画書 v8 17章 T0-3M・19章）

        入れる項目とだれが入れるか:
        ・自動: 1分ごとの投げた数・着弾・命中、実際に振った間かく、振った秒と最後までやったか → ゲーム
        ・見て入れる（テスト中にキーで）: 持ちかえ、腕が下がった（回数と最初の秒）、やめたいと言った → やる人
        ・終わった直後の聞き取り: 疲れの5段階、痛い・こわい・ストラップが外れた、本当にもう一回を選んだか、気づいたこと → やる人

        疲れともう一回が入っていない行は、合格かどうかの集計に入れない（安全のことだけは入力を待たずに数える）
        MonoBehaviour は使っていない
    */
    [Serializable]
    public class EnduranceRecord
    {
        public string testId = "T0-3M-1";
        // やった日（yyyy-MM-dd）。別の日の2回目はこの日付で見分ける
        public string date = "";
        public string owner = "";
        // 参加者の番号（1から）。名前は書かない
        public int participantNo = 1;

        /*
            ---- 決めておいた条件（記録に残して、とちゅうで変わっていないか確かめる） ----
            実際に使っていたクールダウンの秒（T0-CD で選んだ値）
        */
        public float cooldownSeconds = 0.50f;
        // T0-A/B で選んだ案の名前
        public string feedbackLabel = "";

        // ---- 自動 ----
        public float plannedSeconds = EnduranceTestPlan.TrialSeconds;
        // 実際に振っていた秒（とちゅうで終わったらそこまで）
        public float endSeconds;
        // 3:00 まで続けたかどうか
        public bool completed;
        // 3:00 より前に終わった（やめたい・中止）
        public bool stopRequested;

        public int[] throwsPerMinute = new int[EnduranceTestPlan.MinuteCount];
        public int[] landingsPerMinute = new int[EnduranceTestPlan.MinuteCount];
        public int[] hitsPerMinute = new int[EnduranceTestPlan.MinuteCount];
        // クールダウン中の振り（参考の値）
        public int rejected;
        // 実際に振った間かく（発射と発射の間、秒）。全員ぶんをまとめて中央値と p75 を出すので、1つ1つの値を持っておく
        public List<float> cycles = new List<float>();

        /*
            ---- 見て入れる ----
            大幣を持ちかえた回数
        */
        public int gripChanges;
        // 腕が下がった（振り上げがはっきり低くなった）と思った回数
        public int armDrops;
        // はじめて腕が下がったと思った秒。なければマイナス
        public float firstArmDropSec = -1f;

        /*
            ---- 終わった直後の聞き取り ----
            疲れの5段階（1〜5）。0 はまだ答えていない
        */
        public int fatigue;
        public bool pain;
        public bool fear;
        public bool strapDeviation;
        // もう一回やるかの答えを記録したか（書いていないのと「別の遊び」を分けるため）
        public bool retryAnswered;
        // 本当に「もう一回」を選んだか（口で言ったことじゃなくて、行動で見る）
        public bool retry;
        public string note = "";

        public int MinuteCount => EnduranceTestPlan.MinuteCount;

        // その分に入ったかどうか（とちゅうで終わって届かなかった分はデータなし）
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

        // 最後の1分で投げた数がどれだけ下がったか（1 − 最後の分 ÷ 最初の分）。最後までやっていない・最後の分に届いていない・最初の分が0 ならデータなし
        public double? ThrowsDropRatio
        {
            get
            {
                int last = EnduranceTestPlan.MinuteCount - 1;
                // テストを短くした確認プレイだと「最後までやった」でも最後の分に入っていないので、0回の最後の分と分ける
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

        // 痛い・こわい・ストラップが外れた、の件数
        public int SafetyIncidents => (pain ? 1 : 0) + (fear ? 1 : 0) + (strapDeviation ? 1 : 0);
        public bool HasSafetyIncident => SafetyIncidents > 0;

        public bool FatigueAnswered => EnduranceTestPlan.IsValidScale(fatigue);

        // 合格かどうかの集計に入れていい記録かどうか
        public bool IsComplete => participantNo > 0 && FatigueAnswered && retryAnswered;

        // 足りない項目（保存ボタンのとなりに出す）。ぜんぶそろっていれば空の文字
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

        // 腕が下がったのを1回記録する
        public void MarkArmDrop(double seconds)
        {
            armDrops++;
            if (firstArmDropSec < 0f) firstArmDropSec = (float)Math.Max(0.0, seconds);
        }

        // テストが終わったときに、自動の値をうつす
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
