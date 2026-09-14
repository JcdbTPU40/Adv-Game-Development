using UnityEngine;

/// <summary>
/// 命中精度の判定 — Issue #60（仕様書 v8 5章）
///
/// 当たった位置の「判定半径に対する中心からの距離の割合」で 3 段階に分ける。
///   中心 40% 以内 … Center（+50）
///   40〜70%       … Inner （+20）
///   70〜100%      … Outer （+0）
///   100% 超       … Miss
/// ボーナス点そのものは ScoreManager が持つ。ここは割合 → ゾーンの変換だけ。
/// </summary>
public static class HitAccuracy
{
    public const float CenterRatio = 0.40f;
    public const float InnerRatio = 0.70f;
    public const float OuterRatio = 1.00f;

    // 0.36 / 0.9 のような割り算の丸めで「ちょうど 40%」が外側に落ちないための許容誤差
    const float Epsilon = 1e-5f;

    /// <summary>判定半径に対する距離の割合（0 = 中心、1 = 判定の縁）からゾーンを返す。</summary>
    public static HitZone ZoneOf(float normalizedDistance)
    {
        if (float.IsNaN(normalizedDistance)) return HitZone.Miss;
        if (normalizedDistance <= CenterRatio + Epsilon) return HitZone.Center;
        if (normalizedDistance <= InnerRatio + Epsilon) return HitZone.Inner;
        if (normalizedDistance <= OuterRatio + Epsilon) return HitZone.Outer;
        return HitZone.Miss;
    }

    /// <summary>
    /// 地面上の着弾点と判定中心の水平距離を、判定半径で割った値。高さの差は無視する。
    /// 半径が 0 以下なら必ず外れ（+∞）。
    /// </summary>
    public static float NormalizedDistance(Vector3 center, Vector3 point, float radius)
    {
        if (radius <= 0f) return float.PositiveInfinity;
        float dx = point.x - center.x;
        float dz = point.z - center.z;
        return Mathf.Sqrt(dx * dx + dz * dz) / radius;
    }
}
