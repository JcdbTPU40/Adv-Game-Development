using System;
using System.Collections.Generic;

namespace Toufuku.Playtest
{
    // わざと100投を照らし合わせた結果
    public readonly struct UsbIntentResult
    {
        public readonly int Cues;
        public readonly int Voided;
        // 振ろうとした投げの数（合図 − なしにした合図）。抜けのわりあいとまちがい発射のわりあいのわる数
        public readonly int Intended;
        public readonly int Matched;
        // 合図に反応した発射がなかった（わざと入力したのに抜けた）
        public readonly int Missed;
        // どの合図にも合わない発射、または1つの合図への2発目から（まちがい発射）
        public readonly int Extra;
        // なしにした合図の時間のはんいに入った発射（どっちにも数えない）
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

    /*
        合図と発射を照らし合わせて、わざと入力したのに抜けたのと、まちがい発射を数えるクラス（#52。T6-USB のわざと100投）

        「振ろうとした」の正しい基準は画面と音の合図。合図 k のはんい = [合図 − before, 合図 + after]
        ・発射は、はんいに入っている合図のうち、時刻がいちばん近いものへの反応とする
        ・その合図にもう反応していたら、2発目からはまちがい発射。どのはんいにも入らない発射もまちがい発射（振り上げたときに出た弾とか）
        ・反応がない合図は抜け
        ・やる人が「振らなかった」となしにした合図はわる数から外して、そのはんいに入った発射はどっちにも数えない
        MonoBehaviour は使っていない
    */
    public static class UsbIntentMatcher
    {
        /*
            cueTimes: 合図の秒（小さい順）
            voidedCueIndices: なしにした合図の番号（0から）
            fireTimes: 発射が決まった秒（同じ時間のものさし）
        */
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

        // 時刻 t をはんいに入れている合図のうち、いちばん近いものの番号。なければ -1
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
