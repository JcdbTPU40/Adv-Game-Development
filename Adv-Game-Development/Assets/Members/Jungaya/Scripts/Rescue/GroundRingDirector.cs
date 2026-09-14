using UnityEngine;

namespace Toufuku.Rescue
{
    /// <summary>
    /// 足元の円（危険円・二重円）をシーン中の客へ配って回る — Issue #55
    ///
    /// シーンに 1 つ置くだけで、いま居る客と後から湧いた客の両方に <see cref="CustomerGroundRing"/> が付く。
    /// 見た目の設定はここ 1 か所で持つので、T1（初見で読めるか）の調整はこのコンポーネントだけを触ればよい。
    ///
    /// 客のプレハブへ直接 <see cref="CustomerGroundRing"/> を付けてもよい。その場合もここに置いた設定で上書きされる
    /// （<see cref="applyStyleToExisting"/> を OFF にすると、プレハブ側の設定をそのまま残す）。
    /// </summary>
    [DisallowMultipleComponent]
    public class GroundRingDirector : MonoBehaviour
    {
        [Header("見た目（シーン共通）")]
        [SerializeField] CustomerGroundRing.Style style = new CustomerGroundRing.Style();

        [Header("配り方")]
        [Tooltip("客を探し直す間隔（秒）。0 なら毎フレーム。")]
        [SerializeField, Min(0f)] float scanIntervalSeconds = 0.25f;
        [Tooltip("ON なら、すでに円を持っている客の設定もここの値で上書きする。")]
        [SerializeField] bool applyStyleToExisting = true;

        float _nextScan;

        /// <summary>シーン共通の見た目の設定。</summary>
        public CustomerGroundRing.Style Style => style;

        void OnEnable()
        {
            _nextScan = 0f;
            Scan();
        }

        void Update()
        {
            if (Time.time < _nextScan) return;
            _nextScan = Time.time + scanIntervalSeconds;
            Scan();
        }

        void OnValidate()
        {
            // インスペクタで色・太さを動かしたら、再生中でもすぐ全員へ反映する（T1 の調整用）。
            if (Application.isPlaying && isActiveAndEnabled) Scan();
        }

        /// <summary>いま居る客すべてに足元の円を付ける（付いていれば設定だけ配る）。</summary>
        public void Scan()
        {
            var targets = HitZoneTarget.Active;
            for (int i = 0; i < targets.Count; i++)
            {
                HitZoneTarget target = targets[i];
                if (target == null) continue;

                // 円は「救済の対象になりうる客」だけに付ける（的だけの検証シーンでは付かない）。
                if (target.GetComponent<CustomerState>() == null) continue;

                CustomerGroundRing ring = target.GetComponent<CustomerGroundRing>();
                if (ring == null)
                    CustomerGroundRing.EnsureOn(target.gameObject, style);
                else if (applyStyleToExisting)
                    ring.SetStyle(style);
            }
        }
    }
}
