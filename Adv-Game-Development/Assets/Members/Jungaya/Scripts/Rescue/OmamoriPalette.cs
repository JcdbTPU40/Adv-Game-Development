using UnityEngine;

namespace Toufuku.Rescue
{
    /*
        お守り5色のパレット（ScriptableObject）。色をまとめて管理するためのもの

        企画書 v3 §3: 5つのボタンの色・客の輪郭の光・お守りの弾の色は、ぜんぶ同じにする
        【このアセットだけが正しい色】なので、いろんな場所に色を直接書かないこと
        配列の順番は OmamoriType の enum の順番と必ず同じにすること
        enum をならべかえたら、この配列もいっしょにならべかえる

        5色をこう選んだ理由（前にあった3セットはおたがい近すぎたり、あざやかさがバラバラだったので、新しく色を作った）:
          ・色相をだいたい 130°/215°/285°/340°/45° にちらして、となりの色との色相の差を最低でも 55° あけている
          ・明るさ（見た目の明るさ）を 金運 > 健康 > 縁結び > 厄除け安全 > 学業成就 の順に
            少しずつずらしてあるので、あざやかさが落ちる場所でも明るさの順番で見分けられる
          ・客の本体のグレー (0.72, 0.70, 0.66) と黒客 (0.04, 0.04, 0.06) の
            どっちとも十分ちがう

        【確かめること: #44 の視認性の検証で見ること（ここでは直さない）】
          ・厄除け安全（紫）と縁結び（ピンク）は色相の差が 55° で、5色の中でいちばん近い
            輪郭の光と Bloom でにじむと同じに見えるかもしれないので、#44 でいちばん先に確かめること
          ・健康（緑）と金運（山吹）は、P型/D型の色覚だと近く見える。明るさの差で分けているけど、
            これも #44 で確かめる
    */
    [CreateAssetMenu(
        fileName = "OmamoriPalette",
        menuName = "Toufuku/お守り5色パレット (OmamoriPalette)",
        order = 2)]
    public class OmamoriPalette : ScriptableObject
    {
        [Tooltip("お守り5色。OmamoriType の enum 順（0:健康 1:学業成就 2:厄除け安全 3:縁結び 4:金運）に厳密対応。")]
        [SerializeField]
        private Color[] colors =
        {
            new Color(0.20f, 0.90f, 0.35f), // 健康: 若草色の緑。明るめ
            new Color(0.20f, 0.50f, 1.00f), // 学業成就: 藍色っぽい青。5色の中でいちばん暗い
            new Color(0.65f, 0.25f, 0.95f), // 厄除け安全: 紫。青とピンクのあいだだけど、明るさで分けている
            new Color(1.00f, 0.40f, 0.62f), // 縁結び: ピンク。紫より明るくて赤っぽい
            new Color(1.00f, 0.78f, 0.10f), // 金運: 山吹色。5色の中でいちばん明るい
        };

        [Tooltip("黒客（救済失敗客）の輪郭色。5色すべてと識別できることが要件（v3 §6）。")]
        [SerializeField] private Color blackCustomerColor = new Color(0.04f, 0.04f, 0.06f);

        // 黒客の色
        public Color BlackCustomerColor => blackCustomerColor;

        // お守りの種類の数（＝色の数。いつも5）。色の数から種類の数を知りたいときはこれを見る
        public int Count => colors != null ? colors.Length : 0;

        // お守りの種類から色を返す
        public Color GetColor(OmamoriType type) => GetColor((int)type);

        /*
            番号から色を返す。範囲外や入っていないときは Color.magenta を返して、設定ミスを目立たせる
            （だまって白を返したりしないこと）
        */
        public Color GetColor(int index)
        {
            if (colors == null || index < 0 || index >= colors.Length)
                return Color.magenta;
            return colors[index];
        }

#if UNITY_EDITOR
        /*
            colors の長さを、いつも OmamoriType の種類の数（=5）に直す
            Inspector で要素の数を変えてしまったミスに気づけるように警告を出す
        */
        private void OnValidate()
        {
            int expected = System.Enum.GetValues(typeof(OmamoriType)).Length;
            if (colors != null && colors.Length == expected) return;

            Debug.LogWarning(
                $"[OmamoriPalette] colors の要素数が {expected} ではありません" +
                $"（現在 {(colors != null ? colors.Length : 0)}）。{expected} に矯正します。", this);

            var fixedColors = new Color[expected];
            for (int i = 0; i < expected; i++)
                fixedColors[i] = (colors != null && i < colors.Length) ? colors[i] : Color.magenta;
            colors = fixedColors;
        }
#endif
    }
}
