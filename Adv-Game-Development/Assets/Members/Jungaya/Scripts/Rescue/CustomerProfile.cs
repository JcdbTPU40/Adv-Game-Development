using UnityEngine;

namespace Toufuku.Rescue
{
    /// <summary>
    /// 1客タイプぶんの「定義データ」— Issue #16
    /// 「客タイプ → 悩み → 正解お守り」の紐付けを 1 アセットに束ねた“受け皿”。
    ///
    /// 企画方針（#16）:
    ///   ・ラベル（文字）ではなく “見た目で察させる”。客が何を求めているかは
    ///     表示（Sprite / Prefab / 代表カラー）から推測させる。
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

        [Header("見た目の受け皿（プレースホルダーOK／あとで差し替え）")]
        [Tooltip("客の見た目プレースホルダー画像(2D)。デザイン陣が本番アートに差し替える。")]
        [SerializeField] private Sprite placeholderSprite;

        [Tooltip("客の見た目プレハブ(3D/演出付き)。3Dや専用アニメで出す場合はこちらを差し替える。")]
        [SerializeField] private GameObject viewPrefab;

        [Tooltip("絵が無い間でも見分けられる“仮の代表カラー”。プレースホルダー表示の着色に使う。")]
        [SerializeField] private Color representativeColor = Color.white;

        [Header("エディタ/デバッグ用（※ゲーム内ラベルには使わない）")]
        [Tooltip("Inspector で見分けるための表示名。日本語可。ゲーム画面には出さない前提（見た目で察させる方針）。")]
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

        /// <summary>仮の代表カラー（絵が無い間の見分け用）。</summary>
        public Color RepresentativeColor => representativeColor;

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
        }
#endif
    }
}
