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
        向き: 歩いているあいだは進む方向、立っているあいだはカメラの方を向く。
              集団客（#93）は立っているあいだ、カメラと仲間のあいだを向いて、ときどき仲間の方に向きなおる（話しているように見せる）
              悩みが消えて仲間を待っているあいだは、ほとんど仲間の方を向く。回すのは見た目（Animator の付いた子）だけで、
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
        [Tooltip("集団客（#93）が立っているときに仲間の方を向く割合のはば（0 ならカメラ、1 なら仲間）。時間でこの中をゆっくり行き来する。")]
        [SerializeField] Vector2 groupFacingRange = new Vector2(0.2f, 0.7f);
        [Tooltip("集団客が悩みが消えて仲間を待っているあいだに、仲間の方を向く割合。")]
        [SerializeField, Range(0f, 1f)] float groupWaitingFacing = 0.85f;
        [Tooltip("集団客が向きを変える速さ（大きいほどひんぱんに向きなおる）。")]
        [SerializeField, Min(0f)] float groupFacingTempo = 0.25f;

        CustomerState _state;
        MockCustomerWalker _walker;
        CustomerGroupMember _groupMember;
        float _facingSeed;
        Transform _model;
        Transform _camera;
        Vector3 _lastPosition;
        Vector3 _walkDirection = Vector3.back;

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            _state = GetComponent<CustomerState>();
            _walker = GetComponent<MockCustomerWalker>();
            _facingSeed = Random.Range(0f, 100f);
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
            // 集団客（#93）は出す側が作った直後に CustomerGroupMember を付けるので、Start の時点ではもう付いている
            _groupMember = GetComponent<CustomerGroupMember>();
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
                forward = BlendTowardGroup(forward, position);
            }
            else
            {
                forward = transform.forward;
            }
            if (forward.sqrMagnitude < 1e-6f) return;

            Quaternion target = Quaternion.LookRotation(forward.normalized, Vector3.up);
            _model.rotation = Quaternion.RotateTowards(_model.rotation, target, turnSpeed * dt);
        }

        // 集団客（#93）なら、カメラの方向 toCamera を仲間の真ん中の方へ寄せる。一人客ならそのまま
        Vector3 BlendTowardGroup(Vector3 toCamera, Vector3 position)
        {
            CustomerGroup group = _groupMember != null ? _groupMember.Group : null;
            if (group == null || toCamera.sqrMagnitude < 1e-6f) return toCamera;

            Vector3 toMates = group.Centroid(position) - position;
            toMates.y = 0f;
            if (toMates.sqrMagnitude < 0.01f) return toCamera;

            float weight = _state != null && _state.IsExitPending
                ? groupWaitingFacing
                : Mathf.Lerp(groupFacingRange.x, groupFacingRange.y, Mathf.PerlinNoise(_facingSeed, Time.time * groupFacingTempo));
            return Vector3.Slerp(toCamera.normalized, toMates.normalized, Mathf.Clamp01(weight));
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
