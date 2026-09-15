using UnityEngine;

namespace Toufuku.Rescue
{
    /*
        客が定位置に着いてから、いなくなるまでの動きを担当するクラス（#62）

          ・移動客の往復（PatrolPath）。定位置に着いたら歩き出して、active の間だけ行ったり来たりする
            速さは PatrolSpeed でプレイ中でも変えられる（T2 で数値を変えるテスト用）
          ・救われたり黒客になったりしたら、その場で往復をやめて、決まった秒数（救済は3秒、黒客は4秒）で出口まで歩く（ExitRoute）
            移動客もふつうの客と同じあつかいで、歩いていたとちゅうの位置から帰り道に入る。rescued と black は終わりの状態なので、往復にはもどらない
            黒客の当たり判定は CustomerState が残すので、帰っている間もじゃまになる（企画書 v8 6章）
          ・出口が設定されていなければ歩いて帰らずに、前と同じでその場で消える

        入ってくるとき（鳥居 → 定位置）に歩かせるのは出す側（MockCrowdDirector / MockCustomerWalker）の仕事で、
        着いたら NotifyArrived を呼んでもらう。位置は LateUpdate で書いているので、入ってくるとちゅうで
        救われても帰り道のほうが優先される

        コインプッシャー（#35 Customer_Move）とはいっしょに使えない。Customer_Move は FixedUpdate のたびに Rigidbody の速さを上書きして
        +Z に押しつづけるけど、こっちは transform を直接動かすから。なので実際に動かし始めるときに Customer_Move を止めて、
        Rigidbody を kinematic にする（ApplyVisibilityMockToTestGame の客プレハブの Variant と同じあつかい）
    */
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

        // 往復が設定されているか（移動客）
        public bool HasPatrol => _hasPatrol;
        // 今往復しているか
        public bool IsPatrolling => _patrolling;
        // 今帰り道を歩いているか
        public bool IsExiting => _exiting;
        // 往復の真ん中（定位置）
        public Vector3 PatrolCenter => _patrolCenter;
        // 往復のはば（m、はしからはしまで）
        public float PatrolWidth => patrolWidth;
        // 帰るときの出口（歩いていなければ意味はない）
        public Vector3 ExitTarget => _exitTo;

        // 歩く速さ（m/秒）。往復している途中に変えると、次のフレームから反映される
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

        /*
            移動客の往復を設定する（出てきた直後に呼ぶ）。歩き出すのは NotifyArrived から
            center: 往復の真ん中（定位置）
            width: 往復のはば（m、はしからはしまで）
            speed: 歩く速さ（m/秒）
            startSign: +1 なら右（+X）へ、-1 なら左（-X）へ歩き出す。決まったシードから決める
        */
        public void ConfigurePatrol(Vector3 center, float width, float speed, int startSign)
        {
            _hasPatrol = true;
            _patrolCenter = center;
            patrolWidth = Mathf.Max(0f, width);
            PatrolSpeed = speed;
            _startSign = startSign >= 0 ? 1 : -1;
        }

        // 帰るときの出口を設定する。null か空なら歩いて帰らない
        public void SetExitPoints(Vector3[] exits)
        {
            _exitPoints = exits;
        }

        // 定位置に着いた。移動客なら往復を始める
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
            // 終わりの状態になった瞬間に往復をやめる。このあと active にはもどらない
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

            // 入ってくる途中や終わりの状態のときは往復しない（終わりは HandleFinished で止まるけど、念のため状態でも見ておく）
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
