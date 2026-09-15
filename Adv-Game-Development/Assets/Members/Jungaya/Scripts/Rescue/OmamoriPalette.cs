using UnityEngine;

namespace Toufuku.Rescue
{
    /*
        お守り5色のパレット（ScriptableObject）。色をまとめて管理するためのもの

        企画書 v3 §3: 5つのボタンの色・客の輪郭の光・お守りの弾の色は、ぜんぶ同じにする
        【このアセットだけが正しい色】なので、いろんな場所に色を直接書かないこと
        配列の順番は OmamoriType の enum の順番と必ず同じにすること
        enum をならべかえたら、この配列もいっしょにならべかえる

        5色は企画書 v8 6章「5色とパターン」の表のカラーコードそのまま（#59 で合わせた。前は #44 のときに作った明るめの色だった）:
          健康 #3FBF5F / 学業成就 #2F7FD8 / 厄除け安全 #8B5FD0 / 縁結び #E85F8F / 金運 #E8B93F
          ボタン箱のボタンと LED もこの値にそろえる（v8 3章）。値を変えるときは企画書といっしょに変えること

        この5色の性質（#59 で計算した）:
          ・色相は 135°/212°/263°/339°/43°。いちばん近いのは学業成就（青）と厄除け安全（紫）で 52°
          ・見た目の明るさは 金運 0.52 > 健康 0.39 > 縁結び 0.27 > 学業成就 0.21 > 厄除け安全 0.18
            青と紫は明るさもほぼ同じなので、グレースケールや色がわかりにくい人には輪郭のもよう（点線と破線）で見分けてもらう
          ・健康（緑）と金運（金）は P型/D型の色覚だと近く見える。明るさの差と、もよう（実線と二重線）で分ける
          ・客の本体のグレー (0.72, 0.70, 0.66) と黒客 (0.04, 0.04, 0.06) のどっちとも十分ちがう
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
            new Color(63 / 255f, 191 / 255f, 95 / 255f),  // 健康: #3FBF5F
            new Color(47 / 255f, 127 / 255f, 216 / 255f), // 学業成就: #2F7FD8
            new Color(139 / 255f, 95 / 255f, 208 / 255f), // 厄除け安全: #8B5FD0
            new Color(232 / 255f, 95 / 255f, 143 / 255f), // 縁結び: #E85F8F
            new Color(232 / 255f, 185 / 255f, 63 / 255f), // 金運: #E8B93F
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
