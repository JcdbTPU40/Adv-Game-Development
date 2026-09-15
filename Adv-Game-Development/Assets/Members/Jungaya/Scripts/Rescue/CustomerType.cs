namespace Toufuku.Rescue
{
    /*
        客のタイプ（＝かかえているなやみの種類）。企画書 v3 §3 で決まった
          名前や数が変わっても、ここ1か所を直すだけですむように enum にまとめている

          5つのなやみは、企画書の「お守り5種」と1対1で対応している:
            健康・学業成就・厄除け安全・縁結び・金運
          （対応するお守りは OmamoriType を見る。順番も名前も必ず同じにそろえること）
    */
    public enum CustomerType
    {
        Kenkou,   // 健康を願う客 → 正解のお守り: OmamoriType.Kenkou
        Gakugyou, // 学業成就を願う客 → 正解のお守り: OmamoriType.Gakugyou
        Yakuyoke, // 厄除け安全を願う客 → 正解のお守り: OmamoriType.Yakuyoke
        Enmusubi, // 縁結びを願う客 → 正解のお守り: OmamoriType.Enmusubi
        Kinun     // 金運を願う客 → 正解のお守り: OmamoriType.Kinun
    }
}
