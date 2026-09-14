using UnityEngine;
using Toufuku.GameInput;

namespace Toufuku.Aim
{
    /// <summary>照準の入力元。値は int でシーンに焼かれるので並べ替えないこと。</summary>
    public enum AimSourceMode
    {
        Auto = 0,     // ESP32 接続中はヨー／ピッチ、それ以外はマウス
        Mouse = 1,    // マウスカーソルが指す地面（机上確認用）
        YawPitch = 2  // 生入力のヨー／ピッチ（KeyboardMouseRawSource なら Q・E とマウスホイール）
    }

    /// <summary>
    /// 照準 — Issue #60（仕様書 v8 4章）
    ///
    /// 毎フレーム、大幣の向きから「地面上の着弾予測点」を 1 つ決める。
    /// ・ヨー（キャリブレーション基準からの相対角）→ 左右、ピッチ → 地面上 3〜18m。
    /// ・マウスのときはカーソルが指す地面を、同じヨー／距離の範囲に収めて使う。
    /// ・予測点が画面外へ出そうなら、画面の内側へ押し戻す（押し戻しは範囲より優先）。
    /// ・振りの強さは使わない。SwingAccepted の瞬間の予測点がそのまま着弾目標点になる（<see cref="GetLockedTarget"/>）。
    /// ・発射側（OnusaThrower）より先に今フレームの照準を確定させるため、ThrowInputController（-100）より前に動かす。
    /// </summary>
    [DefaultExecutionOrder(-150)]
    public class OnusaAimController : MonoBehaviour
    {
        [Header("参照（未設定ならシーン内から探す）")]
        [SerializeField] ThrowInputController input;
        [Tooltip("照準を投影するカメラ。未設定なら Camera.main")]
        [SerializeField] Camera cam;

        [Header("照準の求め方")]
        [SerializeField] AimSourceMode mode = AimSourceMode.Auto;
        [Tooltip("距離と左右の基準点（投げる人の位置。例: ShootPos）。未設定ならこの GameObject")]
        [SerializeField] Transform origin;
        [Tooltip("ヨー 0 度（正面）の向き。未設定なら開始時のカメラ前方（水平成分）")]
        [SerializeField] Transform referenceForward;
        [Tooltip("着弾予測点を置く地面の高さ(Y)")]
        [SerializeField] float groundY = 0f;

        [Header("奥行き（ピッチ → 地面上の距離）")]
        [SerializeField, Min(0f)] float nearDistance = AimSolver.DefaultNearDistance;
        [SerializeField, Min(0f)] float farDistance = AimSolver.DefaultFarDistance;
        [Tooltip("このピッチ角（度）で最短距離。センサーの向きが逆なら pitchAtFar と入れ替える")]
        [SerializeField] float pitchAtNear = -30f;
        [Tooltip("このピッチ角（度）で最長距離")]
        [SerializeField] float pitchAtFar = 20f;

        [Header("左右（ヨー）")]
        [Tooltip("正面からこの角度までに制限する")]
        [SerializeField, Range(0f, 180f)] float maxYaw = 60f;
        [SerializeField] bool invertYaw = false;

        [Header("画面外への押し戻し")]
        [Tooltip("ビューポートの縁からこの割合より内側に照準を留める")]
        [SerializeField, Range(0f, 0.45f)] float viewportMargin = 0.06f;

        [Header("着弾目標点の固定")]
        [Tooltip("SwingAccepted の何秒前の照準を着弾目標点にするか。0 = 確定した瞬間の照準（仕様どおり）。\n" +
                 "実機で振りの途中にピッチが動いて狙いがずれる場合に T0 で調整する")]
        [SerializeField, Range(0f, 0.3f)] float lockLookbackSeconds = 0f;

        const int HistoryCapacity = 64;
        const float PushBackStep = 0.03f;
        const int PushBackMaxSteps = 12;

        readonly double[] _historyTime = new double[HistoryCapacity];
        readonly Vector3[] _historyPoint = new Vector3[HistoryCapacity];
        int _historyHead;
        int _historyCount;

        float _referenceYaw;
        bool _referenceCaptured;

        /// <summary>1 回以上照準を計算できたか。</summary>
        public bool HasAim { get; private set; }
        /// <summary>地面上の着弾予測点（ワールド座標）。</summary>
        public Vector3 TargetPoint { get; private set; }
        /// <summary>着弾予測点のスクリーン座標（z はカメラからの奥行き）。</summary>
        public Vector3 ScreenPosition { get; private set; }
        /// <summary>基準点から着弾予測点までの水平距離（押し戻し後）。</summary>
        public float Distance { get; private set; }
        /// <summary>正面からの左右角（度、押し戻し後）。</summary>
        public float RelativeYaw { get; private set; }
        /// <summary>今フレームの照準がヨー／ピッチから求めたものか（false ならマウス）。</summary>
        public bool UsingYawPitch { get; private set; }
        /// <summary>今フレームの照準が画面の内側へ押し戻されたか。</summary>
        public bool PushedBack { get; private set; }

        public float GroundY => groundY;
        public Vector3 Origin => origin != null ? origin.position : transform.position;
        public Camera ViewCamera => cam;

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<ThrowInputController>();
            if (cam == null) cam = Camera.main;
        }

        void Update()
        {
            if (cam == null)
            {
                cam = Camera.main;
                if (cam == null) return;
            }
            if (!_referenceCaptured) CaptureReferenceYaw();

            Recompute();
        }

        /// <summary>正面（ヨー 0 度）の向きを取り直す。カメラ演出で向きが変わる前に 1 回だけ取る。</summary>
        public void CaptureReferenceYaw()
        {
            Vector3 forward = referenceForward != null ? referenceForward.forward
                : cam != null ? cam.transform.forward
                : transform.forward;
            _referenceYaw = AimSolver.YawOf(forward, 0f);
            _referenceCaptured = true;
        }

        /// <summary>
        /// 有効スイング確定時刻（SwingAcceptedArgs.Time）に対応する着弾目標点。
        /// lockLookbackSeconds = 0 なら現在の着弾予測点そのもの。
        /// </summary>
        public Vector3 GetLockedTarget(double swingTime)
        {
            if (lockLookbackSeconds <= 0f || _historyCount == 0) return TargetPoint;

            double want = swingTime - lockLookbackSeconds;
            for (int i = 1; i <= _historyCount; i++)
            {
                int idx = (_historyHead - i + HistoryCapacity) % HistoryCapacity;
                if (_historyTime[idx] <= want) return _historyPoint[idx];
            }
            // 履歴より古い時刻を求められたら、残っている最古の照準
            return _historyPoint[(_historyHead - _historyCount + HistoryCapacity) % HistoryCapacity];
        }

        void Recompute()
        {
            UsingYawPitch = ShouldUseYawPitch();

            float relativeYaw;
            float distance;
            if (UsingYawPitch)
            {
                relativeYaw = input.RelativeYaw * (invertYaw ? -1f : 1f);
                distance = AimSolver.PitchToDistance(input.RawSource.Pitch, pitchAtNear, pitchAtFar, nearDistance, farDistance);
            }
            else
            {
                MouseToPolar(out relativeYaw, out distance);
            }

            relativeYaw = Mathf.Clamp(relativeYaw, -maxYaw, maxYaw);
            distance = Mathf.Clamp(distance, Mathf.Min(nearDistance, farDistance), Mathf.Max(nearDistance, farDistance));

            Vector3 point = AimSolver.GroundPoint(Origin, _referenceYaw + relativeYaw, distance, groundY);
            point = PushInsideScreen(point, out bool pushed);

            AimSolver.ToPolar(Origin, point, out float worldYaw, out float finalDistance);
            TargetPoint = point;
            PushedBack = pushed;
            Distance = finalDistance;
            RelativeYaw = pushed ? Mathf.DeltaAngle(_referenceYaw, worldYaw) : relativeYaw;
            ScreenPosition = cam.WorldToScreenPoint(point);
            HasAim = true;

            Record(Time.realtimeSinceStartupAsDouble, point);
        }

        bool ShouldUseYawPitch()
        {
            if (input == null || input.RawSource == null) return false;
            switch (mode)
            {
                case AimSourceMode.Mouse: return false;
                case AimSourceMode.YawPitch: return true;
                default: return input.RawSource is Esp32RawSource && input.RawSource.IsConnected;
            }
        }

        void MouseToPolar(out float relativeYaw, out float distance)
        {
            Vector3 forward = AimSolver.DirectionFromYaw(_referenceYaw);
            Vector3 right = AimSolver.DirectionFromYaw(_referenceYaw + 90f);

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            var ground = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));
            if (ground.Raycast(ray, out float enter))
            {
                Vector3 d = ray.GetPoint(enter) - Origin;
                d.y = 0f;
                // 基準点より手前（カメラ側）を指したときに左右へ飛ばないよう、前方成分は少しだけ残す
                float f = Mathf.Max(Vector3.Dot(d, forward), 0.01f);
                float r = Vector3.Dot(d, right);
                relativeYaw = Mathf.Atan2(r, f) * Mathf.Rad2Deg;
                distance = d.magnitude;
            }
            else
            {
                // 地平線より上を指している → 指している向きの最遠
                relativeYaw = Mathf.DeltaAngle(_referenceYaw, AimSolver.YawOf(ray.direction, _referenceYaw));
                distance = Mathf.Max(nearDistance, farDistance);
            }
        }

        Vector3 PushInsideScreen(Vector3 point, out bool pushed)
        {
            pushed = false;
            Vector3 vp = cam.WorldToViewportPoint(point);
            if (AimSolver.IsInsideViewport(vp, viewportMargin)) return point;

            // カメラの後ろに回った点は、画面下端の左右反対側として扱う
            Vector2 clamped = vp.z > 0f
                ? AimSolver.ClampToViewport(new Vector2(vp.x, vp.y), viewportMargin)
                : AimSolver.ClampToViewport(new Vector2(1f - vp.x, 0f), viewportMargin);

            var ground = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));
            for (int i = 0; i <= PushBackMaxSteps; i++)
            {
                Ray ray = cam.ViewportPointToRay(new Vector3(clamped.x, clamped.y, 0f));
                if (ground.Raycast(ray, out float enter))
                {
                    pushed = true;
                    Vector3 p = ray.GetPoint(enter);
                    p.y = groundY;
                    return p;
                }
                // 押し戻し先が地平線より上なら、地面に届くまで少しずつ下げる
                clamped.y -= PushBackStep;
            }
            return point;
        }

        void Record(double time, Vector3 point)
        {
            _historyTime[_historyHead] = time;
            _historyPoint[_historyHead] = point;
            _historyHead = (_historyHead + 1) % HistoryCapacity;
            if (_historyCount < HistoryCapacity) _historyCount++;
        }

        void OnDrawGizmosSelected()
        {
            Vector3 o = Origin;
            float yaw = Application.isPlaying ? _referenceYaw
                : AimSolver.YawOf(referenceForward != null ? referenceForward.forward
                    : cam != null ? cam.transform.forward
                    : Camera.main != null ? Camera.main.transform.forward : transform.forward);

            Gizmos.color = new Color(1f, 0.9f, 0.4f, 0.8f);
            DrawArc(o, yaw, nearDistance);
            DrawArc(o, yaw, farDistance);
            Gizmos.DrawLine(AimSolver.GroundPoint(o, yaw - maxYaw, nearDistance, groundY), AimSolver.GroundPoint(o, yaw - maxYaw, farDistance, groundY));
            Gizmos.DrawLine(AimSolver.GroundPoint(o, yaw + maxYaw, nearDistance, groundY), AimSolver.GroundPoint(o, yaw + maxYaw, farDistance, groundY));

            if (HasAim)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(TargetPoint, 0.2f);
            }
        }

        void DrawArc(Vector3 o, float yaw, float distance)
        {
            const int segments = 24;
            Vector3 prev = AimSolver.GroundPoint(o, yaw - maxYaw, distance, groundY);
            for (int i = 1; i <= segments; i++)
            {
                Vector3 next = AimSolver.GroundPoint(o, yaw - maxYaw + 2f * maxYaw * i / segments, distance, groundY);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}
