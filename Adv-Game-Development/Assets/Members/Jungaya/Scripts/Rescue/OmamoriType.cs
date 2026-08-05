namespace Toufuku.Rescue
{
    /// <summary>
    /// お守りの種類（ボタン5個に対応）。企画書v3 §3 で確定。
    ///   名称が変わったらここを 1 箇所直すだけで済むよう enum に集約しておく。
    ///
    /// ※ この enum の順序＝ボタン箱の物理配置順（左→右）。
    ///   変更する場合はハード側の配置も同時に変えること（v3 §3：5ボタンの色は
    ///   御守り5種の色・輪郭発光・弾の色と完全統一）。
    /// ※ 値は int としてアセットに焼かれるため、並べ替え・追加時は
    ///   シリアライズ済みデータ（*.asset / *.prefab / *.unity）の移行が必要。
    /// </summary>
    public enum OmamoriType
    {
        Kenkou,   // 健康
        Gakugyou, // 学業成就
        Yakuyoke, // 厄除け安全
        Enmusubi, // 縁結び
        Kinun     // 金運
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
