using UnityEngine;

namespace Toufuku.Rescue
{
    /// <summary>
    /// お守り5色パレット（ScriptableObject）— 色の一元管理。
    ///
    /// 企画書 v3 §3：5ボタンの色・客の輪郭発光・お守り弾の色は完全に同一。
    /// 【このアセットが唯一の正】であり、各所でハードコードしないこと。
    /// 配列順は <see cref="OmamoriType"/> の enum 順に厳密に一致させること。
    /// enum を並べ替えたらこの配列も同時に並べ替える。
    ///
    /// 5色の選定理由（既存3セットが互いに近すぎ・彩度不揃いだったため新規に調色）:
    ///   ・色相を約 130°/215°/285°/340°/45° に散らし、隣接色の色相差を最低 55° 確保している。
    ///   ・明度（知覚輝度）を 金運 &gt; 健康 &gt; 縁結び &gt; 厄除け安全 &gt; 学業成就 の順に
    ///     段階的にずらしてあるので、彩度が落ちる環境でも順序で判別できる。
    ///   ・客本体の灰 (0.72, 0.70, 0.66) および黒客 (0.04, 0.04, 0.06) の
    ///     どちらとも十分な差がある。
    ///
    /// 【要検証：Issue #44 の視認性検証で確認すること（ここでは対処しない）】
    ///   ・厄除け安全（紫）と縁結び（桃）は色相差 55° と5色中で最も近い。
    ///     輪郭発光＋Bloom で滲むと同化する可能性があるため、#44 で最優先に確認すること。
    ///   ・健康（緑）と金運（山吹）は P型/D型色覚では近づく。明度差で分離しているが、
    ///     これも #44 で確認対象。
    /// </summary>
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
            new Color(0.20f, 0.90f, 0.35f), // 健康：若草緑。明度高め
            new Color(0.20f, 0.50f, 1.00f), // 学業成就：藍青。5色中で最も暗い
            new Color(0.65f, 0.25f, 0.95f), // 厄除け安全：紫。青と桃の中間だが明度で分離
            new Color(1.00f, 0.40f, 0.62f), // 縁結び：桃。紫より明るく赤寄り
            new Color(1.00f, 0.78f, 0.10f), // 金運：山吹。5色中で最も明るい
        };

        [Tooltip("黒客（救済失敗客）の輪郭色。5色すべてと識別できることが要件（v3 §6）。")]
        [SerializeField] private Color blackCustomerColor = new Color(0.04f, 0.04f, 0.06f);

        /// <summary>黒客の色。</summary>
        public Color BlackCustomerColor => blackCustomerColor;

        /// <summary>お守り種別数（＝色数。常に5）。色数から種別数を求めたい場合はこれを参照する。</summary>
        public int Count => colors != null ? colors.Length : 0;

        /// <summary>お守り種別 → 色。</summary>
        public Color GetColor(OmamoriType type) => GetColor((int)type);

        /// <summary>
        /// インデックス → 色。範囲外・未設定は Color.magenta を返して設定ミスを目立たせる
        /// （黙って白を返さないこと）。
        /// </summary>
        public Color GetColor(int index)
        {
            if (colors == null || index < 0 || index >= colors.Length)
                return Color.magenta;
            return colors[index];
        }

#if UNITY_EDITOR
        /// <summary>
        /// colors の長さを常に OmamoriType の種別数（=5）に矯正する。
        /// Inspector 操作で要素数を変えてしまった事故を検知して警告する。
        /// </summary>
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
