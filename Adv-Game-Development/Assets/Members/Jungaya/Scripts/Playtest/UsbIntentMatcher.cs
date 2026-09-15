using System;
using System.Collections.Generic;

namespace Toufuku.Playtest
{
    /// <summary>意図的 100 投の突き合わせ結果。</summary>
    public readonly struct UsbIntentResult
    {
        public readonly int Cues;
        public readonly int Voided;
        /// <summary>意図した投擲の数（合図 − 無効にした合図）。欠落率・誤発射率の分母。</summary>
        public readonly int Intended;
        public readonly int Matched;
        /// <summary>合図に応える発射が無かった（意図的入力の欠落）。</summary>
        public readonly int Missed;
        /// <summary>どの合図にも当たらない発射、または 1 つの合図への 2 発目以降（誤発射）。</summary>
        public readonly int Extra;
        /// <summary>無効にした合図の窓に入った発射（どちらにも数えない）。</summary>
        public readonly int IgnoredInVoid;

        public UsbIntentResult(int cues, int voided, int matched, int extra, int ignoredInVoid)
        {
            Cues = cues;
            Voided = voided;
            Intended = Math.Max(0, cues - voided);
            Matched = matched;
            Missed = Math.Max(0, Intended - matched);
            Extra = extra;
            IgnoredInVoid = ignoredInVoid;
        }

        public double? MissRate => Intended > 0 ? (double)Missed / Intended : (double?)null;
        public double? FalseFireRate => Intended > 0 ? (double)Extra / Intended : (double?)null;
    }

    /// <summary>
    /// 合図と発射を突き合わせて、意図的入力の欠落と誤発射を数える — Issue #52（T6-USB 意図的 100 投）
    ///
    /// 「意図」の正本は画面と音の合図。合図 k の窓 = [合図 − before, 合図 + after]。
    /// ・発射は、窓に入っている合図のうち時刻がいちばん近いものへの応答とみなす。
    /// ・その合図がすでに応答済みなら 2 発目以降 = 誤発射。どの窓にも入らない発射も誤発射（振り上げなどで出た弾）。
    /// ・応答の無い合図 = 欠落。
    /// ・実施者が「振らなかった」と無効にした合図は分母から外し、その窓に入った発射はどちらにも数えない。
    /// MonoBehaviour 非依存。
    /// </summary>
    public static class UsbIntentMatcher
    {
        /// <param name="cueTimes">合図の秒（昇順）</param>
        /// <param name="voidedCueIndices">無効にした合図の番号（0 始まり）</param>
        /// <param name="fireTimes">発射確定の秒（同じ時間軸）</param>
        public static UsbIntentResult Match(IReadOnlyList<double> cueTimes, ICollection<int> voidedCueIndices,
            IReadOnlyList<double> fireTimes,
            double before = UsbGatePlan.CueWindowBeforeSeconds, double after = UsbGatePlan.CueWindowAfterSeconds)
        {
            int cueCount = cueTimes?.Count ?? 0;
            var matched = new bool[cueCount];
            int voided = 0;
            if (voidedCueIndices != null)
            {
                foreach (int index in voidedCueIndices)
                {
                    if (index >= 0 && index < cueCount) voided++;
                }
            }

            int matchedCount = 0, extra = 0, ignored = 0;
            if (fireTimes != null)
            {
                var fires = new List<double>(fireTimes);
                fires.Sort();
                foreach (double f in fires)
                {
                    int k = NearestCueInWindow(cueTimes, f, before, after);
                    if (k < 0) { extra++; continue; }
                    if (voidedCueIndices != null && voidedCueIndices.Contains(k)) { ignored++; continue; }
                    if (matched[k]) { extra++; continue; }
                    matched[k] = true;
                    matchedCount++;
                }
            }

            return new UsbIntentResult(cueCount, voided, matchedCount, extra, ignored);
        }

        /// <summary>時刻 t を窓に含む合図のうち、いちばん近いものの番号。無ければ -1。</summary>
        public static int NearestCueInWindow(IReadOnlyList<double> cueTimes, double t, double before, double after)
        {
            if (cueTimes == null) return -1;
            int best = -1;
            double bestDistance = double.PositiveInfinity;
            for (int i = 0; i < cueTimes.Count; i++)
            {
                double c = cueTimes[i];
                if (t < c - before - 1e-9 || t > c + after + 1e-9) continue;
                double d = Math.Abs(t - c);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }
            return best;
        }
    }
}
