using System;

/*
    縁の計算式（#61 / 企画書 v8 7章「縁の計算式（通常弾）」、付録B B-2・PROPAGATE）

      救済得点 = round((基礎点 + 最終弾の命中精度加点 + 優先救済加点) × 救済時の福の連なり倍率 × 発射時のご加護倍率)
      伝播得点 = round(20 × 救済時に保存した福の連なり倍率 × 救済時に保存したご加護倍率)（有効な伝播1回ごと）

    ・小数をぜんぶ掛けた最後に1回だけ四捨五入して整数にする（7章「計算は小数を掛けた最後に四捨五入して整数化する」）
    ・Mathf.RoundToInt は 0.5 を偶数へ丸める（32.5 → 32）ので使わない。ここは 0.5 を必ず切り上げる（32.5 → 33）
    ・float のまま掛けると 1.1f が 1.10000002 になって、ちょうど .5 になるはずの値がずれることがある。
      倍率は decimal に直してから掛ける（float → decimal の変換は有効数字7けたで丸まるので 1.1f は 1.1 になる）
    ・危険度 D とランクは倍率に入れない（7章「危険度は倍率にも閾値にもしない」、付録B「ランク すべて×1.0」）
*/
public static class EnFormula
{
    // PROPAGATE: 伝播1人ぶんの縁
    public const int DefaultPropagationPoints = 20;

    // 救済得点
    public static int RescueScore(int baseScore, int accuracyBonus, int priorityBonus, float chainMultiplier, float blessingMultiplier)
    {
        decimal raw = baseScore + accuracyBonus + priorityBonus;
        return RoundHalfUp(raw * ToDecimal(chainMultiplier) * ToDecimal(blessingMultiplier));
    }

    // 伝播得点（1回ぶん）。倍率は救済したときに保存した値を渡す
    public static int PropagationScore(float chainMultiplier, float blessingMultiplier, int points = DefaultPropagationPoints)
    {
        return RoundHalfUp(points * ToDecimal(chainMultiplier) * ToDecimal(blessingMultiplier));
    }

    // 四捨五入（0.5 は 0 から遠いほうへ）
    public static int RoundHalfUp(decimal value)
    {
        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    static decimal ToDecimal(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return 1m;
        return (decimal)value;
    }
}

/*
    救済したときに確定した2つの倍率（#61 / 7章「倍率の保存順」）
    救済得点を足した直後にこの値を救済客へ保存して、あとから起きる伝播得点（#56）にも同じ値を使う
*/
public readonly struct EnMultiplierSnapshot
{
    // 救済時の福の連なり倍率
    public readonly float ChainMultiplier;
    // 発射時のご加護倍率
    public readonly float BlessingMultiplier;
    // 救済で作られた値かどうか（とちゅうの当たりや外れでは false）
    public readonly bool IsValid;

    public EnMultiplierSnapshot(float chainMultiplier, float blessingMultiplier)
    {
        ChainMultiplier = chainMultiplier;
        BlessingMultiplier = blessingMultiplier;
        IsValid = true;
    }

    // 救済していないことを表す値
    public static EnMultiplierSnapshot None => default;

    public override string ToString() => IsValid ? $"連なり×{ChainMultiplier:0.00} ご加護×{BlessingMultiplier:0.00}" : "なし";
}
