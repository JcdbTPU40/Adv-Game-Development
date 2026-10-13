using Toufuku.Rescue;
using Toufuku.Rescue.Mock;
using UnityEngine;

namespace Toufuku.Scenery
{
    /*
        客の状態を Animator のパラメータへ渡す（客の仮モデル用。デザイン班のモデルに差しかえても同じパラメータで動く）
          IsWalking（bool） : 鳥居から定位置まで・退場のあいだ
          Danger（float）   : 危険度 D を 0〜1 にしたもの。0.5 から焦りだす（D≥50 で危険円が出るのと同じ）
          Black（bool）     : 黒客になった（終端。もどらない）
          Rescued（trigger）: 救済された（喜んでおじぎしてから帰る）
          Correct（trigger）: 正しい色が当たった（うなずく）
          Wrong（trigger）  : ちがう色が当たった（首を横にふる）
        向き: 歩いているあいだは進む方向、立っているあいだはカメラの方を向く。回すのは見た目（Animator の付いた子）だけで、
              当たり判定や輪郭の帯の計算に使う本体の向きは変えない
    */
    [DisallowMultipleComponent]
    public class CustomerAnimatorBridge : MonoBehaviour
    {
        static readonly int IsWalkingId = Animator.StringToHash("IsWalking");
        static readonly int DangerId = Animator.StringToHash("Danger");
        static readonly int BlackId = Animator.StringToHash("Black");
        static readonly int RescuedId = Animator.StringToHash("Rescued");
        static readonly int CorrectId = Animator.StringToHash("Correct");
        static readonly int WrongId = Animator.StringToHash("Wrong");

        [Tooltip("見た目のモデル（Animator）。未設定なら子から探す。")]
        [SerializeField] Animator animator;
        [Tooltip("1秒に回る角度の上限。")]
        [SerializeField, Min(0f)] float turnSpeed = 540f;
        [Tooltip("この速さ（m/秒）より遅い移動は、歩きの向きに使わない（押し合いの小さなずれで向きが変わらないように）。")]
        [SerializeField, Min(0f)] float minFacingSpeed = 0.3f;
        [Tooltip("客ごとの動きの速さのばらつき（±割合）。全員がそろって動かないように。")]
        [SerializeField, Range(0f, 0.3f)] float speedJitter = 0.1f;

        CustomerState _state;
        MockCustomerWalker _walker;
        Transform _model;
        Transform _camera;
        Vector3 _lastPosition;
        Vector3 _walkDirection = Vector3.back;

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            _state = GetComponent<CustomerState>();
            _walker = GetComponent<MockCustomerWalker>();
            _model = animator != null ? animator.transform : null;
            _lastPosition = transform.position;
        }

        void OnEnable()
        {
            if (_state == null) return;
            _state.onCorrectColorHit.AddListener(OnCorrect);
            _state.onWrongColorHit.AddListener(OnWrong);
            _state.onRescued.AddListener(OnRescued);
        }

        void OnDisable()
        {
            if (_state == null) return;
            _state.onCorrectColorHit.RemoveListener(OnCorrect);
            _state.onWrongColorHit.RemoveListener(OnWrong);
            _state.onRescued.RemoveListener(OnRescued);
        }

        void Start()
        {
            if (animator == null) return;
            animator.speed = 1f + Random.Range(-speedJitter, speedJitter);
            // 待機の呼吸がそろわないように、始まる位置をずらす
            animator.Update(0f);
            var info = animator.GetCurrentAnimatorStateInfo(0);
            animator.Play(info.fullPathHash, 0, Random.value);
            if (Camera.main != null) _camera = Camera.main.transform;
        }

        void Update()
        {
            if (animator == null) return;

            bool walking = _walker != null && _walker.IsWalking;
            animator.SetBool(IsWalkingId, walking);
            if (_state != null)
            {
                animator.SetFloat(DangerId, _state.IsActive ? _state.DangerNormalized : 0f);
                animator.SetBool(BlackId, _state.IsBlack);
            }

            UpdateFacing(walking);
        }

        void UpdateFacing(bool walking)
        {
            if (_model == null) return;

            Vector3 position = transform.position;
            Vector3 delta = position - _lastPosition;
            delta.y = 0f;
            _lastPosition = position;
            float dt = Time.deltaTime;
            if (dt > 0f && delta.magnitude / dt >= minFacingSpeed) _walkDirection = delta.normalized;

            Vector3 forward;
            if (walking)
            {
                forward = _walkDirection;
            }
            else if (_camera != null)
            {
                forward = _camera.position - position;
                forward.y = 0f;
            }
            else
            {
                forward = transform.forward;
            }
            if (forward.sqrMagnitude < 1e-6f) return;

            Quaternion target = Quaternion.LookRotation(forward.normalized, Vector3.up);
            _model.rotation = Quaternion.RotateTowards(_model.rotation, target, turnSpeed * dt);
        }

        void OnCorrect()
        {
            if (animator != null) animator.SetTrigger(CorrectId);
        }

        void OnWrong()
        {
            if (animator != null) animator.SetTrigger(WrongId);
        }

        void OnRescued()
        {
            if (animator != null) animator.SetTrigger(RescuedId);
        }
    }
}
