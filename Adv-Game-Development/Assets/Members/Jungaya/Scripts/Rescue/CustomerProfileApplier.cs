using UnityEngine;

namespace Toufuku.Rescue
{
    /// <summary>
    /// 生成された客に「客タイプ定義(#16)」を適用するコンポーネント。
    ///
    /// 役割:
    ///   ・<see cref="CustomerProfileCatalog"/> からプロフィールを引き、
    ///     <see cref="CustomerRescue.Setup(CustomerProfile, OmamoriAffinityTable)"/> で
    ///     客タイプ・正解お守り・相性テーブルを注入する。
    ///   ・見た目（代表カラー / プレースホルダーSprite / 見た目Prefab）を“プレースホルダー”として反映。
    ///     ＝ ラベルではなく見た目で客の悩みを察させる（#16の方針）。
    ///
    /// 使い方:
    ///   ・客プレハブにこのコンポーネントを付け、catalog を割り当てるだけ。
    ///     pickRandomOnStart が ON なら、生成された瞬間にランダムな客タイプへ自分で化ける。
    ///   ・スポナー側でタイプを決めたい場合は pickRandomOnStart を OFF にして
    ///     <see cref="Apply(CustomerProfile, CustomerProfileCatalog)"/> を呼ぶ。
    /// </summary>
    [RequireComponent(typeof(CustomerRescue))]
    public class CustomerProfileApplier : MonoBehaviour
    {
        [Header("データ元")]
        [Tooltip("客タイプ定義のカタログ(#16)。ここからプロフィールを引く。")]
        [SerializeField] private CustomerProfileCatalog catalog;
        [Tooltip("ON なら生成時にカタログからランダムな客タイプを自分で選んで適用する。")]
        [SerializeField] private bool pickRandomOnStart = true;

        [Header("見た目の適用")]
        [Tooltip("代表カラーをレンダラ/スプライトに塗るか。")]
        [SerializeField] private bool applyColor = true;
        [Tooltip("プレースホルダーSprite を targetSprite に差し込むか。")]
        [SerializeField] private bool applySprite = true;
        [Tooltip("ON なら viewPrefab を viewMount 配下に生成する（3D見た目の差し替え用）。")]
        [SerializeField] private bool spawnViewPrefab = false;
        [Tooltip("代表カラーを塗る3Dレンダラ。未設定なら子から自動取得。")]
        [SerializeField] private Renderer targetRenderer;
        [Tooltip("Sprite差し替え・着色先。未設定なら子から自動取得。")]
        [SerializeField] private SpriteRenderer targetSprite;
        [Tooltip("viewPrefab を生成する親。未設定ならこの GameObject。")]
        [SerializeField] private Transform viewMount;

        /// <summary>適用中のプロフィール（未適用なら null）。</summary>
        public CustomerProfile Current { get; private set; }

        // 色プロパティ名（Built-in: _Color / URP: _BaseColor）。両方に書けば描画パイプライン非依存。
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock _mpb;

        private void Awake()
        {
            if (targetSprite == null) targetSprite = GetComponentInChildren<SpriteRenderer>();
            if (targetSprite == null && targetRenderer == null)
                targetRenderer = GetComponentInChildren<Renderer>();
        }

        private void Start()
        {
            // 外部から Apply 済みでなければ、ランダムに自分で化ける。
            if (Current == null && pickRandomOnStart && catalog != null)
                Apply(catalog.GetRandom());
        }

        /// <summary>
        /// 指定プロフィールを適用する（スポナーがタイプを決める場合）。
        /// </summary>
        public void Apply(CustomerProfile profile)
        {
            if (profile == null) return;
            Current = profile;

            CustomerRescue rescue = GetComponent<CustomerRescue>();
            if (rescue != null)
                rescue.Setup(profile, catalog != null ? catalog.AffinityTable : null);

            ApplyVisual(profile);
        }

        /// <summary>
        /// カタログ参照ごと差し込む版（スポナー側で catalog を渡したいとき）。
        /// </summary>
        public void Apply(CustomerProfile profile, CustomerProfileCatalog fromCatalog)
        {
            if (fromCatalog != null) catalog = fromCatalog;
            Apply(profile);
        }

        private void ApplyVisual(CustomerProfile p)
        {
            // プレースホルダーSprite（2D）。
            if (applySprite && targetSprite != null && p.PlaceholderSprite != null)
                targetSprite.sprite = p.PlaceholderSprite;

            // 代表カラー（絵が無い間の見分け）。
            if (applyColor)
            {
                if (targetSprite != null)
                {
                    targetSprite.color = p.RepresentativeColor;
                }
                else if (targetRenderer != null)
                {
                    // MaterialPropertyBlock で塗る＝共有マテリアルを複製せず他インスタンスに影響しない。
                    if (_mpb == null) _mpb = new MaterialPropertyBlock();
                    targetRenderer.GetPropertyBlock(_mpb);
                    _mpb.SetColor(ColorId, p.RepresentativeColor);
                    _mpb.SetColor(BaseColorId, p.RepresentativeColor);
                    targetRenderer.SetPropertyBlock(_mpb);
                }

                // 渋り演出(#14)のフラッシュ戻り先をこの代表カラーへそろえる。
                CustomerReluctance reluctance = GetComponent<CustomerReluctance>();
                if (reluctance != null) reluctance.SetBaseColor(p.RepresentativeColor);
            }

            // 見た目Prefab（3D/演出付き）。
            if (spawnViewPrefab && p.ViewPrefab != null)
            {
                Transform mount = viewMount != null ? viewMount : transform;
                Instantiate(p.ViewPrefab, mount.position, mount.rotation, mount);
            }
        }
    }
}
