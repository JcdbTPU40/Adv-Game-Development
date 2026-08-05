using UnityEngine;

namespace Toufuku.Rescue.Mock
{
    /// <summary>
    /// 視認性モック(#44)用。1体の客に割り当てられた「識別情報」だけを持つ小さな札。
    ///
    /// ・どのお守り種別（＝どの輪郭色）に割り当てられたか
    /// ・黒客かどうか（黒輪郭・黒ゲージ。他5色と識別できるかが #44 の検証ポイント）
    ///
    /// 輪郭の描画そのものは <see cref="MockCustomerOutline"/>、
    /// 頭上ゲージの描画は <see cref="MockGaugeHud"/> が、この札を読んで行う。
    /// 表示ロジックとデータを分けておくことで、本番アウトライン(#45)へ差し替えるときに
    /// 触るのが MockCustomerOutline だけで済む。
    ///
    /// ※ 検証用の使い捨て。Mock/ ごと削除できる。
    /// </summary>
    public class MockCustomerTag : MonoBehaviour
    {
        [Tooltip("お守り5種のインデックス（0:健康 1:学業成就 2:厄除け安全 3:縁結び 4:金運）。黒客は -1。")]
        [SerializeField] private int colorIndex = -1;

        [Tooltip("黒客なら ON。輪郭もゲージも黒系になる。")]
        [SerializeField] private bool isBlack;

        [Tooltip("実際に割り当てられた色（輪郭・ゲージの色分けに使う）。")]
        [SerializeField] private Color assignedColor = Color.white;

        /// <summary>お守り5種のインデックス。黒客は -1。</summary>
        public int ColorIndex => colorIndex;

        /// <summary>黒客か。</summary>
        public bool IsBlack => isBlack;

        /// <summary>割り当て色。</summary>
        public Color AssignedColor => assignedColor;

        /// <summary>
        /// お守り種別。黒客や未割り当ての場合は既定値(健康)を返すので、
        /// 種別として意味を持たせたい場合は <see cref="IsBlack"/> を先に見ること。
        /// </summary>
        public OmamoriType Omamori => (OmamoriType)Mathf.Clamp(colorIndex, 0, 4);

        /// <summary>スポーン時に <see cref="MockCrowdDirector"/> から呼ばれる。</summary>
        public void Assign(int index, Color color, bool black)
        {
            colorIndex = black ? -1 : index;
            assignedColor = color;
            isBlack = black;
        }
    }
}
