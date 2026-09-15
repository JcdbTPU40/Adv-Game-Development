using UnityEngine;

namespace Toufuku.Aim
{
    /*
        弾の飛び方（演出用のカーブ）を計算するクラス（#60 / 企画書 v8 4章）

        ・弾の道すじは、スタート地点と着弾目標点をつなぐ2次ベジェ曲線。放物線の運動はまねしていない
        ・t = 1 のときは必ず着弾目標点そのものを返すので、予測点と実際に落ちた場所が同じになる
        ・振りの強さは飛ぶ時間（0.25〜0.65秒）と軌跡の太さにだけ使って、落ちる場所には使わない
    */
    public static class ThrowFlight
    {
        // いちばん速いときの飛ぶ時間（秒）
        public const float MinFlightSeconds = 0.25f;
        // いちばん遅いときの飛ぶ時間（秒）。いちばん遠くてもこれより長くならない
        public const float MaxFlightSeconds = 0.65f;

        // 振りの強さ（ピークの角速度、度/秒）を 0〜1 に直す
        public static float Strength01(float strength, float slowStrength, float fastStrength)
        {
            if (float.IsNaN(strength)) return 0f;
            if (Mathf.Approximately(slowStrength, fastStrength)) return strength >= fastStrength ? 1f : 0f;
            return Mathf.InverseLerp(slowStrength, fastStrength, strength);
        }

        /*
            強さ（0〜1）から飛ぶ時間を返す。強いほど短い。距離は関係ない
            slowestSeconds や fastestSeconds を範囲の外にしても、0.25〜0.65 秒の中におさめる
        */
        public static float FlightSeconds(float strength01,
            float slowestSeconds = MaxFlightSeconds, float fastestSeconds = MinFlightSeconds)
        {
            float slow = Mathf.Clamp(slowestSeconds, MinFlightSeconds, MaxFlightSeconds);
            float fast = Mathf.Clamp(fastestSeconds, MinFlightSeconds, MaxFlightSeconds);
            float seconds = Mathf.Lerp(slow, fast, Mathf.Clamp01(strength01));
            return Mathf.Clamp(seconds, MinFlightSeconds, MaxFlightSeconds);
        }

        // 強さ（0〜1）から軌跡の太さを返す。強いほど太い
        public static float TrailWidth(float strength01, float thinWidth, float thickWidth)
        {
            return Mathf.Lerp(thinWidth, thickWidth, Mathf.Clamp01(strength01));
        }

        // 見た目の弧の高さ。水平距離に比例して、maxHeight で止まる
        public static float ArcHeight(float horizontalDistance, float heightPerMeter, float maxHeight)
        {
            return Mathf.Clamp(horizontalDistance * heightPerMeter, 0f, Mathf.Max(0f, maxHeight));
        }

        /*
            飛んでいる進み具合 t（0〜1）のときの位置を返す。弧のてっぺんは、始点と終点をつないだ線より arcHeight だけ上になる
            t が 1 以上なら end をそのまま返す（計算の誤差で落ちる場所がずれないようにするため）
        */
        public static Vector3 Evaluate(Vector3 start, Vector3 end, float arcHeight, float t)
        {
            if (t >= 1f) return end;
            if (t <= 0f) return start;

            // 2次ベジェのまん中の点は「始点と終点の中点」と「制御点」のまん中になるので、制御点は2倍の高さに上げておく
            Vector3 control = (start + end) * 0.5f + Vector3.up * (arcHeight * 2f);
            float u = 1f - t;
            return u * u * start + 2f * u * t * control + t * t * end;
        }
    }
}
