namespace Toufuku.Rescue
{
    /// <summary>
    /// 参拝客のタイプ（＝抱えている悩みの種類）。
    /// ※ 暫定定義。正式には #16「客タイプ→悩み→正解お守りの紐付けデータ構造を確定」で
    ///   確定・共有する想定。名称や数が変わってもここ1箇所を直すだけで済むよう enum に集約。
    ///
    ///   5つの悩みは企画書の「お守り5種」と1:1で対応する想定:
    ///     健康・学業成就・縁結び・金運・厄除け安全
    ///   （対応するお守りは <see cref="OmamoriType"/> を参照）
    /// </summary>
    public enum CustomerType
    {
        Kenkou,   // 健康を願う客      → 正解お守り: OmamoriType.Kenkou
        Gakugyou, // 学業成就を願う客  → 正解お守り: OmamoriType.Gakugyou
        Renai,    // 縁結びを願う客    → 正解お守り: OmamoriType.Renai
        Kinun,    // 金運を願う客      → 正解お守り: OmamoriType.Kinun
        Yakuyoke  // 厄除け安全を願う客 → 正解お守り: OmamoriType.Yakuyoke
    }
}
