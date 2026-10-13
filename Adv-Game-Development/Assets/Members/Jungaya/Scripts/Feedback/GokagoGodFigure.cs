using DG.Tweening;
using UnityEngine;

namespace Toufuku.Feedback
{
    /*
        ご加護カットインで重なる「神の姿」のモデルを動かす（#29）

        GokagoCutIn が専用カメラで映して、画面の左はしに半透明で重ねる。Play(秒) で1回ぶんの動きをする
        ・仮モデル（GokagoGodPlaceholder.prefab）は、下からせり上がる → 袖が開く → 後光が広がって回る、を DOTween で動かす
        ・本番のモデルに差しかえるときは、Animator を入れて playTrigger のトリガーで動かしてもいい（トランスフォームは空のままでいい）
        ・Tween は SetUpdate(true)。timeScale には触らない
    */
    public class GokagoGodFigure : MonoBehaviour
    {
        [Header("仮モデルの部品（空なら動かさない）")]
        [Tooltip("せり上がる本体")]
        [SerializeField] Transform body;
        [Tooltip("左袖の肩の支点。Z回転で開く")]
        [SerializeField] Transform sleeveLeft;
        [Tooltip("右袖の肩の支点。Z回転で開く")]
        [SerializeField] Transform sleeveRight;
        [Tooltip("後光。広がって回る")]
        [SerializeField] Transform halo;

        [Header("動き")]
        [SerializeField] float riseFrom = -0.5f;
        [SerializeField] float sleeveClosedAngle = 8f;
        [SerializeField] float sleeveOpenAngle = 60f;
        [SerializeField] float turnFromYaw = 25f;
        [Tooltip("後光が1回の演出で回る角度")]
        [SerializeField] float haloSpinDegrees = 120f;

        [Header("本番モデル用（任意）")]
        [SerializeField] Animator animator;
        [SerializeField] string playTrigger = "Play";

        Sequence _sequence;
        Vector3 _bodyPos;
        Vector3 _haloScale;
        Quaternion _rootRot;
        bool _captured;

        void Awake()
        {
            Capture();
        }

        void OnDisable()
        {
            Kill();
        }

        void Capture()
        {
            if (_captured) return;
            _captured = true;
            _rootRot = transform.localRotation;
            if (body != null) _bodyPos = body.localPosition;
            if (halo != null) _haloScale = halo.localScale;
        }

        // seconds: カットイン全体の長さ（0.8秒）。動きはこの長さのわりあいで置く
        public void Play(float seconds)
        {
            Capture();
            Kill();

            if (animator != null && !string.IsNullOrEmpty(playTrigger))
                animator.SetTrigger(playTrigger);

            float s = Mathf.Max(0.05f, seconds);
            _sequence = DOTween.Sequence().SetUpdate(true);

            // 少し斜めから正面へ向きなおる
            transform.localRotation = _rootRot * Quaternion.Euler(0f, turnFromYaw, 0f);
            _sequence.Insert(0f, transform.DOLocalRotateQuaternion(_rootRot, 0.6f * s).SetEase(Ease.OutCubic));

            if (body != null)
            {
                body.localPosition = _bodyPos + Vector3.up * riseFrom;
                _sequence.Insert(0f, body.DOLocalMove(_bodyPos, 0.45f * s).SetEase(Ease.OutCubic));
            }

            InsertSleeve(sleeveLeft, -1f, s);
            InsertSleeve(sleeveRight, 1f, s);

            if (halo != null)
            {
                halo.localScale = Vector3.zero;
                _sequence.Insert(0.15f * s, halo.DOScale(_haloScale, 0.4f * s).SetEase(Ease.OutBack));
                _sequence.Insert(0.15f * s, halo.DOLocalRotate(new Vector3(0f, haloSpinDegrees, 0f), 0.85f * s,
                    RotateMode.LocalAxisAdd).SetEase(Ease.OutQuad));
            }
        }

        // side: 左袖は -1、右袖は +1。下ろした角度から開いた角度へ
        void InsertSleeve(Transform sleeve, float side, float s)
        {
            if (sleeve == null) return;
            sleeve.localRotation = Quaternion.Euler(0f, 0f, side * sleeveClosedAngle);
            _sequence.Insert(0.2f * s, sleeve.DOLocalRotate(new Vector3(0f, 0f, side * sleeveOpenAngle), 0.4f * s)
                .SetEase(Ease.OutBack));
        }

        void Kill()
        {
            if (_sequence != null && _sequence.IsActive()) _sequence.Kill();
            _sequence = null;
        }
    }
}
