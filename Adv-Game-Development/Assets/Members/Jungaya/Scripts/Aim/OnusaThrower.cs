using UnityEngine;
using UnityEngine.Rendering;
using Toufuku.GameInput;
using Toufuku.Rescue;

namespace Toufuku.Aim
{
    /*
        有効スイングが決まったら（SwingAccepted）お守りを1発飛ばすクラス（#60 / 企画書 v8 4章）

        ・着弾目標点は SwingAccepted のときの照準（OnusaAimController.GetLockedTarget）。発射したあとは動かさない
        ・優先救済の相手のID（二重円の客）も同じ瞬間に弾に保存する（#55）
          飛んでいる 0.65 秒の間に二重円が別の客に移っても、その弾の +50 の判定は発射したときの相手のまま（v8 4章「通常弾」）
        ・振りの強さは、飛ぶ時間（0.25〜0.65秒、強いほど短い）と軌跡の太さに使う。落ちる場所には使わない
        ・弾は無限。クールダウン中に振ったとき（SwingRejected: Cooldown）は弾を出さずに、照準を 80ms 灰色にする
          （短い低い音は ThrowInputController が鳴らす）。スコアのイベントも出ない
        ・TestShooter といっしょに有効になっていると2発出てしまうので、TestShooter は切っておく
    */
    public class OnusaThrower : MonoBehaviour
    {
        [Header("参照（未設定ならシーン内から探す）")]
        [SerializeField] ThrowInputController input;
        [SerializeField] OnusaAimController aim;
        [SerializeField] AimReticleView reticle;

        [Header("弾の見た目")]
        [Tooltip("見た目に使うプレハブ（例: Bullet.prefab）。Rigidbody / Collider / OmamoriBullet は生成時に外す。未設定なら小さな球")]
        [SerializeField] GameObject projectilePrefab;
        [Tooltip("発射位置。未設定なら照準の基準点")]
        [SerializeField] Transform spawnPoint;
        [SerializeField, Min(0.01f)] float fallbackSphereSize = 0.3f;

        [Header("飛翔時間（#60: 0.25〜0.65 秒。強いほど短い。距離には依存しない）")]
        [Tooltip("この強さ（ピーク角速度・度/秒）以下は最も遅い。マウスのクリックは 1")]
        [SerializeField] float slowStrength = 180f;
        [Tooltip("この強さ以上は最も速い")]
        [SerializeField] float fastStrength = 720f;
        [SerializeField, Range(ThrowFlight.MinFlightSeconds, ThrowFlight.MaxFlightSeconds)]
        float slowestSeconds = ThrowFlight.MaxFlightSeconds;
        [SerializeField, Range(ThrowFlight.MinFlightSeconds, ThrowFlight.MaxFlightSeconds)]
        float fastestSeconds = ThrowFlight.MinFlightSeconds;

        [Header("軌跡（強いほど太い）")]
        [SerializeField, Min(0f)] float thinTrailWidth = 0.05f;
        [SerializeField, Min(0f)] float thickTrailWidth = 0.22f;
        [SerializeField, Min(0.01f)] float trailSeconds = 0.18f;
        [SerializeField] Color trailColor = new Color(1f, 0.95f, 0.8f, 0.9f);
        [Tooltip("#49 T0-A/B: 発射から弾（と軌跡）が見え始めるまでの秒数。0 なら発射と同時。" +
                 "飛翔時間（最短 0.25 秒）より十分短くすること。FeedbackTimingShifter が案ごとに書き換える")]
        [SerializeField, Min(0f)] float visualDelaySeconds = 0f;

        [Header("弧の見た目（着弾点には影響しない）")]
        [SerializeField, Min(0f)] float arcHeightPerMeter = 0.12f;
        [SerializeField, Min(0f)] float maxArcHeight = 2f;

        [Header("デバッグ")]
        [SerializeField] bool logThrows = true;

        Material _trailMaterial;
        bool _subscribed;

        // 発射した弾の数（確認用）
        public int ThrowCount { get; private set; }
        // 最後に発射した弾（落ちたあとは消えるので null になる）
        public OmamoriProjectile LastProjectile { get; private set; }

        // #49: 次に発射する弾が見え始めるまでの秒数
        public float VisualDelaySeconds
        {
            get => visualDelaySeconds;
            set => visualDelaySeconds = Mathf.Max(0f, value);
        }

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<ThrowInputController>();
            if (aim == null) aim = FindAnyObjectByType<OnusaAimController>();
            if (reticle == null) reticle = FindAnyObjectByType<AimReticleView>();
        }

        void OnEnable()
        {
            Subscribe();
        }

        void Start()
        {
            Subscribe();
            if (input == null)
                Debug.LogWarning("[OnusaThrower] ThrowInputController がありません（Toufuku/Input/Setup Throw Input In Active Scene）", this);

            foreach (TestShooter shooter in FindObjectsByType<TestShooter>(FindObjectsSortMode.None))
            {
                if (shooter.isActiveAndEnabled)
                    Debug.LogWarning("[OnusaThrower] TestShooter も有効です。二重発射になるので無効にしてください", shooter);
            }
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        void OnDestroy()
        {
            if (_trailMaterial != null) Destroy(_trailMaterial);
        }

        void Subscribe()
        {
            if (_subscribed || input == null) return;
            input.SwingAccepted += HandleSwingAccepted;
            input.SwingRejected += HandleSwingRejected;
            _subscribed = true;
        }

        void Unsubscribe()
        {
            if (!_subscribed || input == null) return;
            input.SwingAccepted -= HandleSwingAccepted;
            input.SwingRejected -= HandleSwingRejected;
            _subscribed = false;
        }

        void HandleSwingAccepted(SwingAcceptedArgs e)
        {
            if (e.Kind != ThrowKind.Normal) return; // 大祓は T5 を通ったあとに作る
            if (aim == null || !aim.HasAim)
            {
                Debug.LogWarning("[OnusaThrower] 照準（OnusaAimController）が無いので発射できません", this);
                return;
            }

            // 着弾目標点はこの瞬間に決める。このあと照準が動いても、振りの強さがどうでも変わらない
            Vector3 target = aim.GetLockedTarget(e.Time);
            Vector3 start = spawnPoint != null ? spawnPoint.position : aim.Origin;

            // 優先救済の相手（二重円の客）も、色や着弾点と同じ「発射が決まった瞬間」に決める（#55 / v8 4章）
            int priorityTargetId = PriorityRescue.CurrentTargetIdOrNone;

            float strength01 = ThrowFlight.Strength01(e.Strength, slowStrength, fastStrength);
            float seconds = ThrowFlight.FlightSeconds(strength01, slowestSeconds, fastestSeconds);
            float width = ThrowFlight.TrailWidth(strength01, thinTrailWidth, thickTrailWidth);

            Vector3 flat = target - start;
            flat.y = 0f;
            float arc = ThrowFlight.ArcHeight(flat.magnitude, arcHeightPerMeter, maxArcHeight);

            var type = (OmamoriType)e.ColorIndex;
            GameObject go = CreateProjectileObject(start);
            AttachTrail(go, width);

            var projectile = go.AddComponent<OmamoriProjectile>();
            projectile.Launch(start, target, seconds, arc, type, trailSeconds, visualDelaySeconds, priorityTargetId);

            ThrowCount++;
            LastProjectile = projectile;

            if (logThrows)
                Debug.Log($"[Throw] 発射 {type} 目標=({target.x:0.00}, {target.z:0.00}) 距離={flat.magnitude:0.0}m 強さ={e.Strength:0} → 飛翔 {seconds:0.00}s 太さ {width:0.00}" +
                          $" 優先対象ID={(priorityTargetId > 0 ? priorityTargetId.ToString() : "なし")}", this);
        }

        void HandleSwingRejected(SwingRejectedArgs e)
        {
            if (e.Reason != SwingRejectReason.Cooldown) return;
            if (reticle != null) reticle.FlashRejected();
        }

        GameObject CreateProjectileObject(Vector3 start)
        {
            GameObject go;
            if (projectilePrefab != null)
            {
                go = Instantiate(projectilePrefab, start, Quaternion.identity);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.transform.position = start;
                go.transform.localScale = Vector3.one * fallbackSphereSize;
            }
            StripPhysics(go);
            go.name = "OmamoriProjectile";
            return go;
        }

        // 見た目だけ使いたいので、物理で当たるしくみ（Rigidbody / Collider / OmamoriBullet）を外す
        static void StripPhysics(GameObject go)
        {
            foreach (OmamoriBullet bullet in go.GetComponentsInChildren<OmamoriBullet>(true))
            {
                bullet.enabled = false;
                Destroy(bullet);
            }
            // Destroy はフレームの最後に実行されるので、それまでにぶつからないように先に無効にしておく
            foreach (Collider col in go.GetComponentsInChildren<Collider>(true))
            {
                col.enabled = false;
                Destroy(col);
            }
            foreach (Rigidbody rb in go.GetComponentsInChildren<Rigidbody>(true))
            {
                rb.isKinematic = true;
                rb.detectCollisions = false;
                Destroy(rb);
            }
        }

        void AttachTrail(GameObject go, float width)
        {
            TrailRenderer trail = go.GetComponent<TrailRenderer>();
            if (trail == null) trail = go.AddComponent<TrailRenderer>();

            if (_trailMaterial == null) _trailMaterial = AimRenderUtil.CreateTransparentMaterial("OmamoriTrail");
            if (_trailMaterial != null) trail.sharedMaterial = _trailMaterial;

            trail.time = trailSeconds;
            trail.minVertexDistance = 0.05f;
            trail.widthMultiplier = width;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            Color end = trailColor;
            end.a = 0f;
            trail.startColor = trailColor;
            trail.endColor = end;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.Clear();
            trail.emitting = true;
        }
    }
}
