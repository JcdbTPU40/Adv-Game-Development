using UnityEngine;

namespace Toufuku.Rescue
{
    /*
        作られた客に「客のタイプの決まり（#16）」を入れるコンポーネント

        役割:
          ・CustomerProfileCatalog からプロフィールを取って、
            CustomerRescue.Setup(CustomerProfile, OmamoriAffinityTable) で
            客のタイプ・正解のお守り・相性の表を入れる
          ・見た目（代表の色 / 仮の Sprite / 見た目の Prefab）を「仮のもの」として反映する
            ＝ 名前じゃなくて見た目で客のなやみをわかってもらう（#16 の方針）

        使い方:
          ・客のプレハブにこのコンポーネントを付けて、catalog を入れるだけ
            pickRandomOnStart が ON なら、作られた瞬間にランダムな客のタイプに自分で変わる
          ・スポナーのほうでタイプを決めたいときは pickRandomOnStart を OFF にして、
            Apply(CustomerProfile, CustomerProfileCatalog) を呼ぶ
    */
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

        // 今使っているプロフィール（まだ入れていなければ null）
        public CustomerProfile Current { get; private set; }

        // 色のプロパティ名（Built-in: _Color / URP: _BaseColor）。両方に書けば、どっちの描画パイプラインでも動く
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
            /*
                外から Apply されていなければ、ランダムに自分で変わる
                #63: 計測プレイ中は、客IDごとに決まったシードの乱数でくじを引く
            */
            if (Current == null && pickRandomOnStart && catalog != null)
                Apply(catalog.GetRandom(Toufuku.Playtest.PlaytestRandom.TryForCustomer(gameObject, Toufuku.Playtest.PlaytestStreams.Profile)));
        }

        // 決めたプロフィールを入れる（スポナーがタイプを決めるとき）
        public void Apply(CustomerProfile profile)
        {
            if (profile == null) return;
            Current = profile;

            CustomerRescue rescue = GetComponent<CustomerRescue>();
            if (rescue != null)
                rescue.Setup(profile, catalog != null ? catalog.AffinityTable : null);

            ApplyVisual(profile);
        }

        // カタログもいっしょに入れるバージョン（スポナーのほうで catalog を渡したいとき）
        public void Apply(CustomerProfile profile, CustomerProfileCatalog fromCatalog)
        {
            if (fromCatalog != null) catalog = fromCatalog;
            Apply(profile);
        }

        private void ApplyVisual(CustomerProfile p)
        {
            // 仮の Sprite（2D）
            if (applySprite && targetSprite != null && p.PlaceholderSprite != null)
                targetSprite.sprite = p.PlaceholderSprite;

            // 代表の色（絵がない間に見分けるため）
            if (applyColor)
            {
                if (targetSprite != null)
                {
                    targetSprite.color = p.RepresentativeColor;
                }
                else if (targetRenderer != null)
                {
                    // MaterialPropertyBlock でぬる＝共有しているマテリアルをコピーしないので、ほかの客にえいきょうしない
                    if (_mpb == null) _mpb = new MaterialPropertyBlock();
                    targetRenderer.GetPropertyBlock(_mpb);
                    _mpb.SetColor(ColorId, p.RepresentativeColor);
                    _mpb.SetColor(BaseColorId, p.RepresentativeColor);
                    targetRenderer.SetPropertyBlock(_mpb);
                }

                // 渋るリアクション（#14）のフラッシュのあとにもどる色を、この代表の色にそろえる
                CustomerReluctance reluctance = GetComponent<CustomerReluctance>();
                if (reluctance != null) reluctance.SetBaseColor(p.RepresentativeColor);
            }

            // 見た目の Prefab（3D や演出付き）
            if (spawnViewPrefab && p.ViewPrefab != null)
            {
                Transform mount = viewMount != null ? viewMount : transform;
                Instantiate(p.ViewPrefab, mount.position, mount.rotation, mount);
            }
        }
    }
}
