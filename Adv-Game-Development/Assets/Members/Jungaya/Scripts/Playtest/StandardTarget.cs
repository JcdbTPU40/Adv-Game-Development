using UnityEngine;
using Toufuku.Aim;

namespace Toufuku.Playtest
{
    /*
        T0-A/B で使うふつうの的（#49 / 企画書 v8 17章）

        客も得点も置かない検証のシーンで「同じ的・同じ判定」にするためのクラス
        ・当たり判定は HitZoneTarget をそのまま使う（本番と同じ判定半径・同じゾーンの分け方）
        ・当たったら短く光るだけ。光り方は案Aも案Bも同じにする（くらべるのは3つの時間差だけ）
        ・救済も得点もないので、当たっても命中音（根音）が鳴るだけで福の連なりは増えない
    */
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

        // この的に当たった回数（確認用）
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
