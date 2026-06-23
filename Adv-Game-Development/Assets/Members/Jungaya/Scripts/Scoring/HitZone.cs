/// <summary>
/// 命中ゾーン（的の輪）。中心に近いほど高得点。
/// 値が大きいほど中心寄り＝高評価。
/// </summary>
public enum HitZone
{
    Miss   = 0, // 外した／相性が合わない＝得点なし・コンボ途切れ
    Outer  = 1, // 外周（かすった）
    Inner  = 2, // 中
    Center = 3, // ど真ん中
}
