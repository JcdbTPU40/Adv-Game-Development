using UnityEngine;

namespace Toufuku.Aim
{
    /// <summary>
    /// 弾の飛翔（演出用スプライン）の計算 — Issue #60（仕様書 v8 4章）
    ///
    /// ・弾道は開始点と着弾目標点を結ぶ 2 次ベジェ。放物運動は再現しない。
    /// ・t = 1 で必ず着弾目標点そのものを返すので、予測点と実着弾点が一致する。
    /// ・振りの強さは飛翔時間（0.25〜0.65 秒）と軌跡の太さにだけ効き、着弾点には効かない。
    /// </summary>
    public static class ThrowFlight
    {
        /// <summary>最も速い飛翔時間（秒）。</summary>
        public const float MinFlightSeconds = 0.25f;
        /// <summary>最も遅い飛翔時間（秒）。最遠距離でもこれを超えない。</summary>
        public const float MaxFlightSeconds = 0.65f;

        /// <summary>振りの強さ（ピーク角速度・度/秒）を 0〜1 に正規化する。</summary>
        public static float Strength01(float strength, float slowStrength, float fastStrength)
        {
            if (float.IsNaN(strength)) return 0f;
            if (Mathf.Approximately(slowStrength, fastStrength)) return strength >= fastStrength ? 1f : 0f;
            return Mathf.InverseLerp(slowStrength, fastStrength, strength);
        }

        /// <summary>
        /// 強さ（0〜1）から飛翔時間を返す。強いほど短い。距離には依存しない。
        /// slowestSeconds / fastestSeconds を範囲外にしても 0.25〜0.65 秒に収める。
        /// </summary>
        public static float FlightSeconds(float strength01,
            float slowestSeconds = MaxFlightSeconds, float fastestSeconds = MinFlightSeconds)
        {
            float slow = Mathf.Clamp(slowestSeconds, MinFlightSeconds, MaxFlightSeconds);
            float fast = Mathf.Clamp(fastestSeconds, MinFlightSeconds, MaxFlightSeconds);
            float seconds = Mathf.Lerp(slow, fast, Mathf.Clamp01(strength01));
            return Mathf.Clamp(seconds, MinFlightSeconds, MaxFlightSeconds);
        }

        /// <summary>強さ（0〜1）から軌跡の太さを返す。強いほど太い。</summary>
        public static float TrailWidth(float strength01, float thinWidth, float thickWidth)
        {
            return Mathf.Lerp(thinWidth, thickWidth, Mathf.Clamp01(strength01));
        }

        /// <summary>見た目の弧の高さ。水平距離に比例し、maxHeight で頭打ち。</summary>
        public static float ArcHeight(float horizontalDistance, float heightPerMeter, float maxHeight)
        {
            return Mathf.Clamp(horizontalDistance * heightPerMeter, 0f, Mathf.Max(0f, maxHeight));
        }

        /// <summary>
        /// 飛翔の進み具合 t（0〜1）での位置。弧の頂点は始点と終点を結ぶ線より arcHeight だけ上になる。
        /// t ≥ 1 では end をそのまま返す（丸め誤差で着弾点がずれないようにするため）。
        /// </summary>
        public static Vector3 Evaluate(Vector3 start, Vector3 end, float arcHeight, float t)
        {
            if (t >= 1f) return end;
            if (t <= 0f) return start;

            // 2 次ベジェの中間点は「始点と終点の中点」と「制御点」の中間になるので、制御点は 2 倍持ち上げる
            Vector3 control = (start + end) * 0.5f + Vector3.up * (arcHeight * 2f);
            float u = 1f - t;
            return u * u * start + 2f * u * t * control + t * t * end;
        }
    }
}
