using UnityEngine;

namespace Toufuku.Rescue
{
    /*
        足元の円（危険円・二重円）をシーンの客に配って回るクラス（#55）

        シーンに1つ置くだけで、今いる客にもあとから出てきた客にも CustomerGroundRing が付く
        見た目の設定はここ1か所で持つので、T1（はじめて見てわかるか）の調整はこのコンポーネントだけをさわればいい

        客のプレハブに直接 CustomerGroundRing を付けてもいい。そのときもここに置いた設定で上書きされる
        （applyStyleToExisting を OFF にすると、プレハブのほうの設定をそのまま残す）
    */
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

        // シーンでいっしょに使う見た目の設定
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
            // インスペクターで色や太さを変えたら、再生中でもすぐ全員に反映する（T1 の調整用）
            if (Application.isPlaying && isActiveAndEnabled) Scan();
        }

        // 今いる客ぜんぶに足元の円を付ける（付いていたら設定だけ配る）
        public void Scan()
        {
            var targets = HitZoneTarget.Active;
            for (int i = 0; i < targets.Count; i++)
            {
                HitZoneTarget target = targets[i];
                if (target == null) continue;

                // 円は「救える対象になるかもしれない客」だけに付ける（的だけの検証シーンでは付かない）
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
