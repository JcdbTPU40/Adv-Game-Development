namespace Toufuku.Rescue.Outline
{
    /// <summary>
    /// アウトラインの線種。色覚対応で「色＋パターン」の出し分けを想定した拡張点。
    ///
    /// 現状は <see cref="Solid"/> のみ実装。Dashed / Wavy は Compose シェーダの
    /// ApplyPattern にフックを切ってあるだけで中身は未実装（#色覚対応）。
    /// </summary>
    public enum OutlinePattern
    {
        /// <summary>実線（既定）。</summary>
        Solid = 0,
        /// <summary>点線。未実装。</summary>
        Dashed = 1,
        /// <summary>波線。未実装。</summary>
        Wavy = 2,
    }
}
