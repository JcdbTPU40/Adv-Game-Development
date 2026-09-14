using UnityEngine;
using UnityEngine.Rendering;
using Toufuku.GameInput;
using Toufuku.Rescue;

namespace Toufuku.Aim
{
    /// <summary>
    /// 有効スイング確定（SwingAccepted）でお守りを 1 発飛ばす — Issue #60（仕様書 v8 4章）
    ///
    /// ・着弾目標点 = SwingAccepted 時点の照準（<see cref="OnusaAimController.GetLockedTarget"/>）。発射後は動かさない。
    /// ・優先救済の対象ID（二重円の客）も同じ瞬間に弾へ保存する（#55）。飛翔 0.65 秒の間に二重円が
    ///   別の客へ移っても、その弾の +50 の判定は発射時の対象のまま変わらない（v8 4章「通常弾」）。
    /// ・振りの強さ → 飛翔時間（0.25〜0.65 秒、強いほど短い）と軌跡の太さ。着弾点には使わない。
    /// ・弾は無限。クールダウン中の有効スイング（SwingRejected: Cooldown）は弾を作らず、照準を 80ms 灰色にする
    ///   （短い低音は ThrowInputController が鳴らす）。得点イベントも作られない。
    /// ・TestShooter と同時に有効だと二重発射になるので、TestShooter は無効にしておく。
    /// </summary>
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

        /// <summary>発射した弾の数（確認用）。</summary>
        public int ThrowCount { get; private set; }
        /// <summary>最後に発射した弾（着弾後は破棄されて null になる）。</summary>
        public OmamoriProjectile LastProjectile { get; private set; }

        /// <summary>#49: 次に発射する弾が見え始めるまでの秒数。</summary>
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
            if (e.Kind != ThrowKind.Normal) return; // 大祓は T5 通過後に実装
            if (aim == null || !aim.HasAim)
            {
                Debug.LogWarning("[OnusaThrower] 照準（OnusaAimController）が無いので発射できません", this);
                return;
            }

            // 着弾目標点はこの瞬間に固定する。以後の照準の動きや振りの強さでは変わらない
            Vector3 target = aim.GetLockedTarget(e.Time);
            Vector3 start = spawnPoint != null ? spawnPoint.position : aim.Origin;

            // 優先救済の対象（二重円の客）も、色・着弾点と同じ「発射受理の瞬間」に固定する（#55 / v8 4章）
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

        /// <summary>見た目だけ使うため、物理で当たる仕組み（Rigidbody / Collider / OmamoriBullet）を外す。</summary>
        static void StripPhysics(GameObject go)
        {
            foreach (OmamoriBullet bullet in go.GetComponentsInChildren<OmamoriBullet>(true))
            {
                bullet.enabled = false;
                Destroy(bullet);
            }
            // Destroy は フレーム末なので、それまでに衝突しないよう先に無効化しておく
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
