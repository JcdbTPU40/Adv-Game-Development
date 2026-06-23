namespace Toufuku.Rescue
{
    /// <summary>
    /// お守りの種類（ボタン5個に対応）。
    /// ※ 暫定定義。正式には #9 / #12 で確定・共有する想定。
    ///   名称が変わったらここを 1 箇所直すだけで済むよう enum に集約しておく。
    /// </summary>
    public enum OmamoriType
    {
        Kenkou,   // 健康
        Gakugyou, // 学業
        Renai,    // 恋愛
        Kinun,    // 金運
        Yakuyoke  // 厄除け
    }

    /// <summary>
    /// 相性の判定結果。救済判定(#13)はこの 2 値だけを受け取って動く。
    /// </summary>
    public enum Affinity
    {
        Good, // 相性◯（正しいお守り）→ ゲージを大きく減らす
        Bad   // 相性✗（誤投擲）   → ゲージを少しだけ減らす＋コンボ途切れ
    }
}
