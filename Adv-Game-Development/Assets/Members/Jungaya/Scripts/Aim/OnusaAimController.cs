using UnityEngine;
using Toufuku.GameInput;

namespace Toufuku.Aim
{
    // 照準の入力をどこから取るか。値は int でシーンに保存されるので、順番を変えないこと
    public enum AimSourceMode
    {
        Auto = 0,     // ESP32 がつながっているときはヨーとピッチ、それ以外はマウス
        Mouse = 1,    // マウスカーソルが指している地面（机の上で確認する用）
        YawPitch = 2  // 入力そのままのヨーとピッチ（KeyboardMouseRawSource なら Q・E とマウスホイール）
    }

    /*
        照準を決めるクラス（#60 / 企画書 v8 4章）

        毎フレーム、大幣の向きから「弾が落ちる予定の地面の点」を1つ決める
        ・ヨー（キャリブレーションした正面からの角度）で左右、ピッチで地面の 3〜18m を決める
        ・マウスのときは、カーソルが指す地面を同じヨーと距離の範囲におさめて使う
        ・予測点が画面の外に出そうなときは、画面の内側に押しもどす（範囲より押しもどしを優先する）
        ・振りの強さは使わない。SwingAccepted の瞬間の予測点が、そのまま着弾目標点になる（GetLockedTarget）
        ・発射する側（OnusaThrower）より先にこのフレームの照準を決めておきたいので、ThrowInputController（-100）より前に動かしている
    */
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

        // 1回でも照準を計算できたかどうか
        public bool HasAim { get; private set; }
        // 地面の上の着弾予測点（ワールド座標）
        public Vector3 TargetPoint { get; private set; }
        // 着弾予測点のスクリーン座標（z はカメラからの奥行き）
        public Vector3 ScreenPosition { get; private set; }
        // 基準点から着弾予測点までの水平距離（押しもどしたあとの値）
        public float Distance { get; private set; }
        // 正面からの左右の角度（度、押しもどしたあとの値）
        public float RelativeYaw { get; private set; }
        // このフレームの照準がヨーとピッチから出したものかどうか（false ならマウス）
        public bool UsingYawPitch { get; private set; }
        // このフレームの照準が画面の内側に押しもどされたかどうか
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

        // 正面（ヨー 0 度）の向きを取りなおす。カメラの演出で向きが変わる前に1回だけ取る
        public void CaptureReferenceYaw()
        {
            Vector3 forward = referenceForward != null ? referenceForward.forward
                : cam != null ? cam.transform.forward
                : transform.forward;
            _referenceYaw = AimSolver.YawOf(forward, 0f);
            _referenceCaptured = true;
        }

        /*
            有効スイングが決まった時刻（SwingAcceptedArgs.Time）のときの着弾目標点を返す
            lockLookbackSeconds が 0 なら、今の着弾予測点をそのまま返す
        */
        public Vector3 GetLockedTarget(double swingTime)
        {
            if (lockLookbackSeconds <= 0f || _historyCount == 0) return TargetPoint;

            double want = swingTime - lockLookbackSeconds;
            for (int i = 1; i <= _historyCount; i++)
            {
                int idx = (_historyHead - i + HistoryCapacity) % HistoryCapacity;
                if (_historyTime[idx] <= want) return _historyPoint[idx];
            }
            // 残っている記録より古い時刻を聞かれたら、いちばん古い照準を返す
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
                // 基準点より手前（カメラ側）を指したときに左右へ飛んでいかないように、前向きの成分を少しだけ残しておく
                float f = Mathf.Max(Vector3.Dot(d, forward), 0.01f);
                float r = Vector3.Dot(d, right);
                relativeYaw = Mathf.Atan2(r, f) * Mathf.Rad2Deg;
                distance = d.magnitude;
            }
            else
            {
                // 地平線より上を指しているときは、その向きのいちばん遠い場所にする
                relativeYaw = Mathf.DeltaAngle(_referenceYaw, AimSolver.YawOf(ray.direction, _referenceYaw));
                distance = Mathf.Max(nearDistance, farDistance);
            }
        }

        Vector3 PushInsideScreen(Vector3 point, out bool pushed)
        {
            pushed = false;
            Vector3 vp = cam.WorldToViewportPoint(point);
            if (AimSolver.IsInsideViewport(vp, viewportMargin)) return point;

            // カメラのうしろに回った点は、画面の下のはしの左右反対側としてあつかう
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
                // 押しもどした先が地平線より上だったら、地面に届くまで少しずつ下げる
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
