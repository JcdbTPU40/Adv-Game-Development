using UnityEngine;

/*
    命中精度を判定するクラス（#60 / 企画書 v8 5章）

    当たった位置の「中心からの距離が判定半径の何割か」で3段階に分ける
      中心から 40% 以内: Center（+50）
      40〜70%: Inner（+20）
      70〜100%: Outer（+0）
      100% より外: Miss
    ボーナスの点数そのものは ScoreManager が持っている。ここはわりあいをゾーンに変えるだけ
*/
public static class HitAccuracy
{
    public const float CenterRatio = 0.40f;
    public const float InnerRatio = 0.70f;
    public const float OuterRatio = 1.00f;

    // 0.36 / 0.9 みたいなわり算の丸めで「ちょうど 40%」が外側になってしまわないように、これくらいのズレは許す
    const float Epsilon = 1e-5f;

    // 中心からの距離が判定半径の何割か（0 = 中心、1 = 判定のふち）から、ゾーンを返す
    public static HitZone ZoneOf(float normalizedDistance)
    {
        if (float.IsNaN(normalizedDistance)) return HitZone.Miss;
        if (normalizedDistance <= CenterRatio + Epsilon) return HitZone.Center;
        if (normalizedDistance <= InnerRatio + Epsilon) return HitZone.Inner;
        if (normalizedDistance <= OuterRatio + Epsilon) return HitZone.Outer;
        return HitZone.Miss;
    }

    /*
        地面の上の着弾点と判定の中心の水平距離を、判定半径でわった値。高さの差は見ない
        半径が 0 以下なら必ず外れ（+∞）
    */
    public static float NormalizedDistance(Vector3 center, Vector3 point, float radius)
    {
        if (radius <= 0f) return float.PositiveInfinity;
        float dx = point.x - center.x;
        float dz = point.z - center.z;
        return Mathf.Sqrt(dx * dx + dz * dz) / radius;
    }
}
