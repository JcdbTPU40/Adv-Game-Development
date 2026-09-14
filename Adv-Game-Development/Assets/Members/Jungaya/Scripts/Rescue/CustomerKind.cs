namespace Toufuku.Rescue
{
    /// <summary>
    /// 参拝客の「客種」（企画書 v8 付録B B-1）— Issue #54
    ///
    /// <see cref="CustomerType"/>（＝悩みの種類＝欲しいお守りの色）とは別の軸。
    /// こちらは「何発で救済できるか／どれくらいで危険度Dが100になるか／救済完了の基礎点」を決める。
    ///   ・色（CustomerType）   … 輪郭発光の色、相性判定に使う
    ///   ・客種（CustomerKind） … 初期R・D満タン秒数・基礎点・評価増減に使う
    ///
    /// 数値は <see cref="CustomerKindTable"/>（付録B B-1 の写し）が正本。enum はキーだけを持つ。
    /// </summary>
    public enum CustomerKind
    {
        /// <summary>通常客：初期R=1 / D満タン15〜25秒 / 基礎点100。</summary>
        Normal,
        /// <summary>移動客：初期R=1 / D満タン20秒 / 基礎点150。activeのまま横移動する。</summary>
        Moving,
        /// <summary>遠方客：初期R=1 / D満タン22秒 / 基礎点200。</summary>
        Distant,
        /// <summary>欲張り客：初期R=2 / D満タン28秒 / 基礎点300。途中点なし（R=0の救済完了時だけ得点）。</summary>
        Greedy,
        /// <summary>ボス客：初期R=3 / D満タン30秒 / 基礎点500。頭上ゲージは3分割の目盛り。</summary>
        Boss
    }
}
