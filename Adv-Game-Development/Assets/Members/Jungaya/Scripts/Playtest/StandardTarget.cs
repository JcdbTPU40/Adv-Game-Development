using UnityEngine;
using Toufuku.Aim;

namespace Toufuku.Playtest
{
    /// <summary>
    /// T0-A/B の標準ターゲット — Issue #49（仕様書 v8 17章）
    ///
    /// 参拝客も得点も置かない検証シーンで「同じ的・同じ判定」を担う。
    /// ・当たり判定は <see cref="HitZoneTarget"/> そのまま（本番と同じ判定半径・同じゾーン分け）。
    /// ・当たったら短く光るだけ。光り方は案A・案B で同じにする（比べるのは 3 つの時刻差だけ）。
    /// ・救済も得点も無いので、当たっても命中SE（根音）が鳴るだけで福の連なりは増えない。
    /// </summary>
    [RequireComponent(typeof(HitZoneTarget))]
    public class StandardTarget : MonoBehaviour
    {
        [Header("当たったときの光り（A/B で共通。比べる対象ではない）")]
        [Tooltip("光らせる見た目。未設定なら子から探す")]
        [SerializeField] Renderer body;
        [SerializeField] Color hitColor = new Color(1f, 0.85f, 0.35f);
        [SerializeField, Min(0f)] float flashSeconds = 0.12f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        HitZoneTarget _zone;
        MaterialPropertyBlock _block;
        Color _restColor = Color.white;
        float _flashUntil;
        bool _flashing;

        /// <summary>この的に当たった回数（確認用）。</summary>
        public int HitCount { get; private set; }

        void Awake()
        {
            _zone = GetComponent<HitZoneTarget>();
            if (body == null) body = GetComponentInChildren<Renderer>();
            if (body != null)
            {
                _block = new MaterialPropertyBlock();
                Material material = body.sharedMaterial;
                if (material != null)
                    _restColor = material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : material.color;
            }
        }

        void OnEnable()
        {
            OmamoriProjectile.AnyLanded += HandleLanded;
        }

        void OnDisable()
        {
            OmamoriProjectile.AnyLanded -= HandleLanded;
            Apply(_restColor);
            _flashing = false;
        }

        void Update()
        {
            if (!_flashing || Time.time < _flashUntil) return;
            Apply(_restColor);
            _flashing = false;
        }

        void HandleLanded(LandingResult result)
        {
            if (result.Hit == null || result.Hit != _zone) return;

            HitCount++;
            if (flashSeconds <= 0f) return;

            Apply(hitColor);
            _flashUntil = Time.time + flashSeconds;
            _flashing = true;
        }

        void Apply(Color color)
        {
            if (body == null || _block == null) return;
            body.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            body.SetPropertyBlock(_block);
        }
    }
}
