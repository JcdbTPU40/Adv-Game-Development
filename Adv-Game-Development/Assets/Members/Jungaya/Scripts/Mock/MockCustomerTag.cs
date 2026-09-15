using UnityEngine;

namespace Toufuku.Rescue.Mock
{
    /*
        視認性モック（#44）用。客1人に決められた「見分けるための情報」だけを持つ、小さな名札みたいなクラス

        ・どのお守りの種類（＝どの輪郭の色）になったか
        ・黒客かどうか（黒い輪郭と黒いゲージ。ほかの5色と見分けられるかが #44 で調べるところ）

        輪郭を描くのは MockCustomerOutline、
        頭の上のゲージを描くのは MockGaugeHud で、どっちもこの名札を読んで描く
        表示の処理とデータを分けておけば、本番のアウトライン（#45）に入れかえるときに
        さわるのが MockCustomerOutline だけですむ

        ※ 検証用の使い捨て。Mock/ フォルダごと消せる
    */
    public class MockCustomerTag : MonoBehaviour
    {
        [Tooltip("お守り5種のインデックス（0:健康 1:学業成就 2:厄除け安全 3:縁結び 4:金運）。黒客は -1。")]
        [SerializeField] private int colorIndex = -1;

        [Tooltip("黒客なら ON。輪郭もゲージも黒系になる。")]
        [SerializeField] private bool isBlack;

        [Tooltip("実際に割り当てられた色（輪郭・ゲージの色分けに使う）。")]
        [SerializeField] private Color assignedColor = Color.white;

        // お守り5種類の番号。黒客は -1
        public int ColorIndex => colorIndex;

        // 黒客かどうか
        public bool IsBlack => isBlack;

        // 決められた色
        public Color AssignedColor => assignedColor;

        /*
            お守りの種類。黒客やまだ決まっていないときはふつうの値（健康）を返すので、
            種類として使いたいときは先に IsBlack を見ること
        */
        public OmamoriType Omamori => (OmamoriType)Mathf.Clamp(colorIndex, 0, 4);

        // 出てきたときに MockCrowdDirector から呼ばれる
        public void Assign(int index, Color color, bool black)
        {
            colorIndex = black ? -1 : index;
            assignedColor = color;
            isBlack = black;
        }
    }
}
