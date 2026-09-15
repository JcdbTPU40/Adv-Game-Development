using UnityEngine;

namespace Toufuku.Rescue
{
    /// <summary>
    /// 参拝客の「定位置に着いてから消えるまで」の動き — Issue #62
    ///
    ///   ・移動客の往復（<see cref="PatrolPath"/>）。定位置に着いたら歩き出し、active の間だけ往復する。
    ///     速度は <see cref="PatrolSpeed"/> で実行中に変えられる（T2 の変数変更テスト用）。
    ///   ・救済・黒客化したら、その場で往復をやめ、退場秒数（救済3秒／黒客4秒）かけて出口まで歩く（<see cref="ExitRoute"/>）。
    ///     移動客も通常客と同じ扱いで、歩いていた途中の位置から退場経路に入る。rescued／black は終端なので往復へは戻らない。
    ///     黒客の当たり判定は <see cref="CustomerState"/> が残すので、退場中も邪魔になる（企画書 v8 6章）。
    ///   ・出口が設定されていなければ退場歩行はせず、従来どおりその場で消える。
    ///
    /// 入場の歩行（鳥居 → 定位置）はスポーン側（MockCrowdDirector / MockCustomerWalker）の担当で、
    /// 着いたら <see cref="NotifyArrived"/> を呼んでもらう。位置は LateUpdate で書くので、入場歩行の途中で
    /// 救済されても退場経路が勝つ。
    ///
    /// コインプッシャー（#35 Customer_Move）とは両立しない。Customer_Move は Rigidbody の速度を毎 FixedUpdate 上書きして
    /// +Z へ押し続けるが、ここは transform を直接動かす。実際に動かし始めるときに Customer_Move を止め、
    /// Rigidbody を kinematic にする（ApplyVisibilityMockToTestGame の客プレハブ Variant と同じ扱い）。
    /// </summary>
    [DisallowMultipleComponent]
    public class CustomerMotion : MonoBehaviour
    {
        [Header("移動客の往復（企画書 v8 10章／付録B MOVE.SPEED）")]
        [Tooltip("歩行速度（m/秒）。")]
        [Min(0f)]
        [SerializeField] private float patrolSpeed = 1f;
        [Tooltip("往復の幅（m、端から端）。")]
        [Min(0f)]
        [SerializeField] private float patrolWidth = 3f;

        CustomerState _state;

        bool _hasPatrol;
        Vector3 _patrolCenter;
        int _startSign = 1;
        bool _patrolling;
        float _travelled;

        Vector3[] _exitPoints;
        bool _exiting;
        Vector3 _exitFrom;
        Vector3 _exitTo;
        float _exitDuration;
        float _exitElapsed;

        bool _pusherGuarded;

        /// <summary>往復が設定されている（移動客）。</summary>
        public bool HasPatrol => _hasPatrol;
        /// <summary>いま往復している。</summary>
        public bool IsPatrolling => _patrolling;
        /// <summary>いま退場経路を歩いている。</summary>
        public bool IsExiting => _exiting;
        /// <summary>往復の中心（定位置）。</summary>
        public Vector3 PatrolCenter => _patrolCenter;
        /// <summary>往復の幅（m、端から端）。</summary>
        public float PatrolWidth => patrolWidth;
        /// <summary>退場の出口（歩いていなければ意味を持たない）。</summary>
        public Vector3 ExitTarget => _exitTo;

        /// <summary>歩行速度（m/秒）。往復中に変えると次のフレームから反映する。</summary>
        public float PatrolSpeed
        {
            get => patrolSpeed;
            set => patrolSpeed = Mathf.Max(0f, value);
        }

        private void Awake()
        {
            _state = GetComponent<CustomerState>();
        }

        private void OnEnable()
        {
            CustomerState.AnyFinished += HandleFinished;
        }

        private void OnDisable()
        {
            CustomerState.AnyFinished -= HandleFinished;
        }

        /// <summary>移動客の往復を設定する（スポーン直後に呼ぶ）。歩き出すのは <see cref="NotifyArrived"/> から。</summary>
        /// <param name="center">往復の中心（定位置）。</param>
        /// <param name="width">往復の幅（m、端から端）。</param>
        /// <param name="speed">歩行速度（m/秒）。</param>
        /// <param name="startSign">+1 なら右（+X）へ、-1 なら左（-X）へ歩き出す。固定シードから決める。</param>
        public void ConfigurePatrol(Vector3 center, float width, float speed, int startSign)
        {
            _hasPatrol = true;
            _patrolCenter = center;
            patrolWidth = Mathf.Max(0f, width);
            PatrolSpeed = speed;
            _startSign = startSign >= 0 ? 1 : -1;
        }

        /// <summary>退場の出口を設定する。null や空なら退場歩行をしない。</summary>
        public void SetExitPoints(Vector3[] exits)
        {
            _exitPoints = exits;
        }

        /// <summary>定位置に着いた。移動客なら往復を始める。</summary>
        public void NotifyArrived()
        {
            if (!_hasPatrol || _exiting || _patrolling) return;
            if (_state != null && _state.IsFinished) return;

            GuardAgainstPusher();
            _patrolling = true;
            _travelled = 0f;
        }

        void HandleFinished(CustomerState state, CustomerPhase phase)
        {
            if (state == null || state != _state) return;
            BeginExit(state.ExitSecondsFor(phase));
        }

        void BeginExit(float seconds)
        {
            // 終端状態になった瞬間に往復をやめる。以後 active へは戻らない。
            _patrolling = false;

            int index = ExitRoute.Choose(transform.position, _exitPoints);
            if (index < 0) return;

            GuardAgainstPusher();
            _exiting = true;
            _exitFrom = transform.position;
            _exitTo = _exitPoints[index];
            _exitTo.y = _exitFrom.y;
            _exitDuration = Mathf.Max(0.0001f, seconds);
            _exitElapsed = 0f;
        }

        private void LateUpdate()
        {
            if (_exiting)
            {
                _exitElapsed += Time.deltaTime;
                float t = Mathf.Clamp01(_exitElapsed / _exitDuration);
                transform.position = Vector3.Lerp(_exitFrom, _exitTo, t);
                return;
            }

            if (!_patrolling) return;

            // 入場中・終端状態では往復しない（終端は HandleFinished で止まるが、念のため状態でも見る）。
            if (_state != null && !_state.IsActive) return;

            _travelled += patrolSpeed * Time.deltaTime;
            Vector3 p = _patrolCenter;
            p.x += PatrolPath.Offset(patrolWidth, _travelled, _startSign);
            transform.position = p;
        }

        void GuardAgainstPusher()
        {
            if (_pusherGuarded) return;
            _pusherGuarded = true;

            var move = GetComponent<Customer_Move>();
            if (move != null && move.enabled)
            {
                move.enabled = false;
                Debug.LogWarning($"[CustomerMotion] {name}: Customer_Move（#35 コインプッシャー）が有効だったので止めました。" +
                                 "往復・退場歩行は transform を直接動かすため、Rigidbody の押し出しと両立しません。", this);
            }

            var rb = GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (_hasPatrol)
            {
                Gizmos.color = new Color(0.3f, 0.9f, 1f, 1f);
                Vector3 half = new Vector3(patrolWidth * 0.5f, 0f, 0f);
                Gizmos.DrawLine(_patrolCenter - half, _patrolCenter + half);
            }
            if (_exiting)
            {
                Gizmos.color = new Color(1f, 0.9f, 0.3f, 1f);
                Gizmos.DrawLine(_exitFrom, _exitTo);
            }
        }
    }
}
