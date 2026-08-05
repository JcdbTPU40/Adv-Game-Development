namespace Toufuku.Rescue
{
    /// <summary>
    /// 参拝客のタイプ（＝抱えている悩みの種類）。企画書v3 §3 で確定。
    ///   名称や数が変わってもここ1箇所を直すだけで済むよう enum に集約。
    ///
    ///   5つの悩みは企画書の「お守り5種」と1:1で対応する:
    ///     健康・学業成就・厄除け安全・縁結び・金運
    ///   （対応するお守りは <see cref="OmamoriType"/> を参照。順序・識別子も必ず同じに揃えること）
    /// </summary>
    public enum CustomerType
    {
        Kenkou,   // 健康を願う客        → 正解お守り: OmamoriType.Kenkou
        Gakugyou, // 学業成就を願う客    → 正解お守り: OmamoriType.Gakugyou
        Yakuyoke, // 厄除け安全を願う客  → 正解お守り: OmamoriType.Yakuyoke
        Enmusubi, // 縁結びを願う客      → 正解お守り: OmamoriType.Enmusubi
        Kinun     // 金運を願う客        → 正解お守り: OmamoriType.Kinun
    }
}
