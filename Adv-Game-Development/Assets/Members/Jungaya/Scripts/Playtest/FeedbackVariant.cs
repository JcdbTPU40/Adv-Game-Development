using System;
using UnityEngine;

namespace Toufuku.Playtest
{
    /// <summary>A/B で比べるフィードバック案。値は int でシーンに焼かれるので並べ替えないこと。</summary>
    public enum VariantId
    {
        A = 0,
        B = 1
    }

    /// <summary>1 人に見せる順番（順番の効果を打ち消すため半数ずつ入れ替える）。</summary>
    public enum AbOrder
    {
        AB = 0,
        BA = 1
    }

    /// <summary>
    /// フィードバック案 1 つ分 — Issue #49（仕様書 v8 17章）
    ///
    /// T0-A/B で比べるのは「振りピークからの 3 つの時刻差」だけ。ほかは A/B で一切変えない。
    /// ・投擲SE: 振りピークから何 ms 後に鳴らすか
    /// ・軌跡出現: 振りピークから何 ms 後に弾（と軌跡）を見せ始めるか
    /// ・振動開始: 振りピークから何 ms 後にモーターを回し始めるか
    ///
    /// テスト前に値を固定し、<see cref="Describe"/> の内容を 19章のテスト記録へ書き写す。
    /// MonoBehaviour 非依存（Inspector に出すため Serializable）。
    /// </summary>
    [Serializable]
    public class FeedbackVariant
    {
        /// <summary>これを超える時刻差は「遅れ」として気づかれる（#64 の投擲SE 予算 80ms の 2 倍）。</summary>
        public const float WarnDelayMs = 160f;

        [Tooltip("記録用の短い名前。参加者には見せない（参加者には 案1 / 案2 と伝える）")]
        public string name = "同時";

        [Tooltip("振りピーク → 投擲SE（ms）")]
        [Min(0f)] public float throwSeDelayMs;

        [Tooltip("振りピーク → 軌跡（弾の見た目）が出る（ms）")]
        [Min(0f)] public float trailDelayMs;

        [Tooltip("振りピーク → 振動開始（ms）")]
        [Min(0f)] public float hapticDelayMs;

        public FeedbackVariant()
        {
        }

        public FeedbackVariant(string name, float throwSeDelayMs, float trailDelayMs, float hapticDelayMs)
        {
            this.name = name;
            this.throwSeDelayMs = throwSeDelayMs;
            this.trailDelayMs = trailDelayMs;
            this.hapticDelayMs = hapticDelayMs;
        }

        /// <summary>3 つのうち最も遅い時刻差（ms）。</summary>
        public float MaxDelayMs => Math.Max(throwSeDelayMs, Math.Max(trailDelayMs, hapticDelayMs));

        /// <summary>3 つとも同じ時刻に出るか。</summary>
        public bool IsSimultaneous =>
            Approximately(throwSeDelayMs, trailDelayMs) && Approximately(trailDelayMs, hapticDelayMs);

        public float DelayMsOf(FeedbackChannel channel)
        {
            switch (channel)
            {
                case FeedbackChannel.ThrowSe: return throwSeDelayMs;
                case FeedbackChannel.Trail: return trailDelayMs;
                default: return hapticDelayMs;
            }
        }

        /// <summary>記録・ログ用の 1 行。</summary>
        public string Describe() =>
            $"{name}（SE {throwSeDelayMs:0} / 軌跡 {trailDelayMs:0} / 振動 {hapticDelayMs:0} ms）";

        public FeedbackVariant Clone() => new FeedbackVariant(name, throwSeDelayMs, trailDelayMs, hapticDelayMs);

        /// <summary>
        /// 2 つの案で違っている項目の数。
        /// 不合格のあと作り直す A/B は「1 つずつ直す」（1 テスト 1 仮説）ので、2 回目以降は 1 であることを確かめる。
        /// </summary>
        public static int CountDifferences(FeedbackVariant a, FeedbackVariant b)
        {
            if (a == null || b == null) return 0;
            int n = 0;
            if (!Approximately(a.throwSeDelayMs, b.throwSeDelayMs)) n++;
            if (!Approximately(a.trailDelayMs, b.trailDelayMs)) n++;
            if (!Approximately(a.hapticDelayMs, b.hapticDelayMs)) n++;
            return n;
        }

        /// <summary>1 回目の案A（3 つとも振りピークと同時）。</summary>
        public static FeedbackVariant DefaultA() => new FeedbackVariant("同時", 0f, 0f, 0f);

        /// <summary>1 回目の案B（音 → 振動 → 軌跡 の順にずらす。大幣が空気を切ってから札が飛び出す感じ）。</summary>
        public static FeedbackVariant DefaultB() => new FeedbackVariant("音→振動→軌跡", 0f, 45f, 20f);

        static bool Approximately(float a, float b) => Math.Abs(a - b) < 0.5f;
    }

    /// <summary>時刻差を付ける 3 つの出口。</summary>
    public enum FeedbackChannel
    {
        ThrowSe = 0,
        Trail = 1,
        Haptic = 2
    }
}
