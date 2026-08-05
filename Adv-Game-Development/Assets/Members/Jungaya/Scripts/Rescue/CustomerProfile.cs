using UnityEngine;

namespace Toufuku.Rescue
{
    /// <summary>
    /// 1客タイプぶんの「定義データ」— Issue #16
    /// 「客タイプ → 悩み → 正解お守り」の紐付けを 1 アセットに束ねた“受け皿”。
    ///
    /// 企画方針:
    ///   ・v2 の「見た目で察させる」方針は企画書 v3 §6 で廃止。現在は【客の輪郭発光】で
    ///     求めているお守りを明示する。
    ///   ・このアセットは輪郭発光・弾・ボタンで共有する代表色と、見た目アセットの
    ///     受け皿を保持する（データ構造は v2 から流用）。
    ///   ・いまは絵が無いので「プレースホルダー画像でOK」。デザイン陣があとから
    ///     <see cref="placeholderSprite"/> / <see cref="viewPrefab"/> に本番アートを
    ///     差し替えるだけで済むよう、先に“受け皿”だけ切っておく。
    ///
    /// 役割分担:
    ///   ・このアセットは「データ（紐付け＋見た目）」だけを持つ。挙動は持たない。
    ///   ・相性◯/✗の判定は <see cref="OmamoriAffinityTable"/>(#12) が担当。
    ///     ここの <see cref="correctOmamori"/> は “1:1 の正解（フォールバック）” を表す。
    ///   ・全タイプをまとめて引くのは <see cref="CustomerProfileCatalog"/>。
    ///
    /// 使い方:
    ///   Project で右クリック → Create → Toufuku → 客タイプ定義(CustomerProfile)。
    ///   客タイプ1種につき1アセットを作り、Catalog に登録する。
    /// </summary>
    [CreateAssetMenu(
        fileName = "CustomerProfile",
        menuName = "Toufuku/客タイプ定義 (CustomerProfile)",
        order = 1)]
    public class CustomerProfile : ScriptableObject
    {
        [Header("紐付け（客タイプ → 悩み → 正解お守り）")]
        [Tooltip("この定義が表す客タイプ。＝抱えている悩みの種類。")]
        [SerializeField] private CustomerType customerType = CustomerType.Kenkou;

        [Tooltip("この客に効く“正解お守り”。相性テーブル未設定時のフォールバック判定にも使う。")]
        [SerializeField] private OmamoriType correctOmamori = OmamoriType.Kenkou;

        [Header("色の正（お守り5色パレット v3 §3）")]
        [Tooltip("お守り5色パレット（唯一の正）。設定時は correctOmamori に対応する色が代表色になり、下の representativeColor は無視される。")]
        [SerializeField] private OmamoriPalette palette;

        [Header("見た目の受け皿（プレースホルダーOK／あとで差し替え）")]
        [Tooltip("客の見た目プレースホルダー画像(2D)。デザイン陣が本番アートに差し替える。")]
        [SerializeField] private Sprite placeholderSprite;

        [Tooltip("客の見た目プレハブ(3D/演出付き)。3Dや専用アニメで出す場合はこちらを差し替える。")]
        [SerializeField] private GameObject viewPrefab;

        [Header("フォールバック用（palette 未設定時のみ）")]
        [Tooltip("正は OmamoriPalette。palette 設定時この値は無視される。")]
        [SerializeField] private Color representativeColor = Color.white;

        [Header("エディタ/デバッグ用（※ゲーム内ラベルには使わない）")]
        [Tooltip("Inspector/デバッグ表示用の名前。日本語可。ゲーム内ラベルには使わない（明示は輪郭発光で行う。v3 §6）。")]
        [SerializeField] private string displayName = "";

        [Tooltip("デザイン/企画への申し送りメモ。仕様には影響しない。")]
        [TextArea(2, 4)]
        [SerializeField] private string designerNote = "";

        // ---- 読み取り専用アクセサ（外部はここ経由で参照する）----

        /// <summary>この定義が表す客タイプ（＝悩み）。</summary>
        public CustomerType CustomerType => customerType;

        /// <summary>この客に効く正解お守り（1:1のフォールバック）。</summary>
        public OmamoriType CorrectOmamori => correctOmamori;

        /// <summary>見た目プレースホルダー画像(2D)。未設定なら null。</summary>
        public Sprite PlaceholderSprite => placeholderSprite;

        /// <summary>見た目プレハブ(3D/演出付き)。未設定なら null。</summary>
        public GameObject ViewPrefab => viewPrefab;

        /// <summary>
        /// この客が求めるお守りの代表色。輪郭発光・弾・ボタンの色と完全統一（v3 §3）。
        /// この客の代表色は「求めているお守りの色」と定義上必ず一致するため、
        /// palette 設定時は OmamoriPalette から correctOmamori で引く。
        /// palette 未設定時のみフォールバックの representativeColor を返す。
        /// </summary>
        public Color RepresentativeColor =>
            palette != null ? palette.GetColor(correctOmamori) : representativeColor;

        /// <summary>エディタ/デバッグ表示名（未設定なら客タイプ名を返す）。</summary>
        public string DisplayName =>
            string.IsNullOrEmpty(displayName) ? customerType.ToString() : displayName;

        /// <summary>デザイン/企画向けメモ。</summary>
        public string DesignerNote => designerNote;

#if UNITY_EDITOR
        /// <summary>
        /// 表示名が空のとき、アセット名から補完しておく（Inspector の見やすさ用）。
        /// 仕様には影響しない。
        /// </summary>
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(displayName))
                displayName = name;

            // palette 設定済みなのにフォールバック representativeColor が正と大きく食い違う場合は
            // 警告する（palette を外した瞬間に腐った色へ戻る事故に気づけるように）。
            // 「大きく」の閾値は RGB 差の合計 0.3（体感で明らかに別の色になるライン）。
            if (palette != null)
            {
                Color c = palette.GetColor(correctOmamori);
                float diff = Mathf.Abs(c.r - representativeColor.r)
                           + Mathf.Abs(c.g - representativeColor.g)
                           + Mathf.Abs(c.b - representativeColor.b);
                if (diff > 0.3f)
                    Debug.LogWarning(
                        $"[CustomerProfile] {name}: representativeColor（フォールバック）が OmamoriPalette の正" +
                        $"（{correctOmamori} の色）と大きく食い違っています（RGB差合計 {diff:0.00}）。" +
                        "フォールバック値をパレットと同値に更新してください。", this);
            }
        }
#endif
    }
}
