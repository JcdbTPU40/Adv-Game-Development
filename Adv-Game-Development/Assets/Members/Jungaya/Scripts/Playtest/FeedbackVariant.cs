using System;
using UnityEngine;

namespace Toufuku.Playtest
{
    // A/B でくらべるフィードバックの案。値は int でシーンに保存されるので、順番を変えないこと
    public enum VariantId
    {
        A = 0,
        B = 1
    }

    // 1人に見せる順番（順番のえいきょうを打ち消すために、半分ずつ入れかえる）
    public enum AbOrder
    {
        AB = 0,
        BA = 1
    }

    /*
        フィードバックの案1つぶん（#49 / 企画書 v8 17章）

        T0-A/B でくらべるのは「振りピークからの3つの時間差」だけ。ほかは A と B でまったく変えない
        ・投げる音: 振りピークから何 ms あとに鳴らすか
        ・軌跡が出る: 振りピークから何 ms あとに弾（と軌跡）を見せ始めるか
        ・振動が始まる: 振りピークから何 ms あとにモーターを回し始めるか

        テストの前に値を決めておいて、Describe の中身を 19章のテスト記録に書きうつす
        MonoBehaviour は使っていない（Inspector に出したいので Serializable にしている）
    */
    [Serializable]
    public class FeedbackVariant
    {
        // これより大きい時間差は「遅れてる」と気づかれる（#64 の投げる音の目標 80ms の2倍）
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

        // 3つの中でいちばん遅い時間差（ms）
        public float MaxDelayMs => Math.Max(throwSeDelayMs, Math.Max(trailDelayMs, hapticDelayMs));

        // 3つとも同じ時刻に出るかどうか
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

        // 記録やログ用の1行
        public string Describe() =>
            $"{name}（SE {throwSeDelayMs:0} / 軌跡 {trailDelayMs:0} / 振動 {hapticDelayMs:0} ms）";

        public FeedbackVariant Clone() => new FeedbackVariant(name, throwSeDelayMs, trailDelayMs, hapticDelayMs);

        /*
            2つの案でちがっている項目の数
            不合格のあと作りなおす A/B は「1つずつ直す」（1つのテストで調べることは1つ）ので、2回目からは 1 になっているか確かめる
        */
        public static int CountDifferences(FeedbackVariant a, FeedbackVariant b)
        {
            if (a == null || b == null) return 0;
            int n = 0;
            if (!Approximately(a.throwSeDelayMs, b.throwSeDelayMs)) n++;
            if (!Approximately(a.trailDelayMs, b.trailDelayMs)) n++;
            if (!Approximately(a.hapticDelayMs, b.hapticDelayMs)) n++;
            return n;
        }

        // 1回目の案A（3つとも振りピークと同時）
        public static FeedbackVariant DefaultA() => new FeedbackVariant("同時", 0f, 0f, 0f);

        // 1回目の案B（音 → 振動 → 軌跡 の順にずらす。大幣が空気を切ってから札が飛び出す感じ）
        public static FeedbackVariant DefaultB() => new FeedbackVariant("音→振動→軌跡", 0f, 45f, 20f);

        static bool Approximately(float a, float b) => Math.Abs(a - b) < 0.5f;
    }

    // 時間差を付ける3つの出口
    public enum FeedbackChannel
    {
        ThrowSe = 0,
        Trail = 1,
        Haptic = 2
    }
}
