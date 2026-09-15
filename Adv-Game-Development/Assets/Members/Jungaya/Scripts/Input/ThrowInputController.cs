using System;
using UnityEngine;
using UnityEngine.Events;
using Toufuku.Rescue;
using Toufuku.Aim;

namespace Toufuku.GameInput
{
    // T0-CD（#50）でくらべるクールダウンの値。値は int でシーンに保存されるので、順番を変えないこと
    public enum CooldownPreset
    {
        Sec040 = 0,
        Sec050 = 1,
        Sec060 = 2,
        Sec065 = 3,
        Custom = 4
    }

    /*
        有効スイングが決まった瞬間に出すフィードバック（投げる音・発射の振動）を受け取る側（#64）

        ThrowInputController は、発射とかほかの受け取り手に SwingAccepted を配る前に
        これを呼ぶ（振りピークを見つけたのと同じフレームで、ほかの処理より先に鳴らすため）
    */
    public interface IThrowFeedbackSink
    {
        void OnThrowAccepted(SwingAcceptedArgs e);
    }

    /*
        入力の状態機械とシーンをつなぐ窓口のクラス（#51）

        入力そのもの（IControllerRawSource）を「押した」「はなした」に直して
        InputStateMachine に流して、結果をイベントとして配る

        ・発射: SwingAccepted が発射のただ1つのきっかけ。IInputProvider も持っているので、
          TestShooter / OmamoriSelector の inputProviderSource にこれをドラッグすれば、
          FireTriggered / OmamoriSelectTriggered がこの状態機械の結果になる
        ・投げる音: SwingAccepted を受け取ったその場（振りピークを見つけたのと同じフレーム）で鳴らす
        ・キャリブレーション: 正面ボタン1秒長押しで、基準のヨー角を取りなおす。向きは RelativeYaw を読む
        ・発射する側より先にこのフレームの入力を決めておきたいので、実行の順番を早くしている
    */
    [DefaultExecutionOrder(-100)]
    public class ThrowInputController : MonoBehaviour, IInputProvider
    {
        [Header("生入力の供給元（KeyboardMouseRawSource / Esp32RawSource をドラッグ）。未設定なら同じ GameObject から探す")]
        [SerializeField] MonoBehaviour rawSourceSource;

        [Header("クールダウン（T0-CD #50 で 0.40 / 0.50 / 0.60 / 0.65 秒を比較）")]
        [SerializeField] CooldownPreset cooldownPreset = CooldownPreset.Sec050;
        [Tooltip("cooldownPreset = Custom のときの秒数")]
        [SerializeField, Range(0.1f, 1.5f)] float customCooldownSeconds = 0.5f;
        [Tooltip("F1〜F4 で 0.40 / 0.50 / 0.60 / 0.65 秒へ切り替える（Inspector のないビルドでの T0-CD 用）")]
        [SerializeField] bool cooldownHotkeys = true;

        [Header("正面ボタン長押しのヨー角キャリブレーション")]
        [SerializeField, Min(0.1f)] float frontHoldSeconds = 1.0f;
        [Tooltip("キャリブレーション前の基準ヨー角（従来の PlayerDirect と同じ 180）")]
        [SerializeField] float initialYawOffset = 180f;

        [Header("大祓（長押しチャージ）— T5 通過後に実装。現在は分岐のみ")]
        [SerializeField] bool oharaeEnabled = false;
        [SerializeField, Min(0.1f)] float oharaeChargeSeconds = 0.8f;

        [Header("選択の初期値（OmamoriSelector の初期値と揃える）")]
        [SerializeField] OmamoriType initialSelection = OmamoriType.Kenkou;

        [Header("投擲SE（振りピーク検出と同じフレームで鳴らす）")]
        [Tooltip("#64: 投擲SE・発射振動の受け手（GameFeedbackDirector をドラッグ）。未設定ならシーン内から探す。見つかればそちらで鳴らし、下の throwSe は使わない")]
        [SerializeField] MonoBehaviour throwFeedbackSource;
        [SerializeField] AudioSource seSource;
        [SerializeField] AudioClip throwSe;
        [Tooltip("クールダウン中の有効スイングで返す短い低音（#60）。未設定なら synthesizeRejectSe に従う")]
        [SerializeField] AudioClip cooldownRejectSe;
        [Tooltip("cooldownRejectSe が未設定のとき、仮の低音（110Hz・0.12 秒）を合成して鳴らす（正式素材は #64）")]
        [SerializeField] bool synthesizeRejectSe = true;

        [Header("セッション連動（#32）: ON なら GameSession がプレイ中のときだけ投擲を受け付ける")]
        [SerializeField] bool requireSessionPlaying = true;

        [Header("照準（#60）。未設定ならシーン内の OnusaAimController を探す。無ければマウス位置")]
        [SerializeField] OnusaAimController aim;

        [Header("デバッグ")]
        [SerializeField] bool showDebugHud = true;
        [SerializeField] bool logEvents = true;

        [Header("イベント（Inspector で結線する場合）")]
        [Tooltip("通常投擲の有効スイング確定（引数: 色 0〜4）")]
        public UnityEvent<int> onSwingAccepted;
        [Tooltip("選択色が変わった（引数: 色 0〜4）")]
        public UnityEvent<int> onSelectionChanged;
        [Tooltip("キャリブレーション完了（引数: 新しい基準ヨー角）")]
        public UnityEvent<float> onYawCalibrated;

        // 有効スイングが決まった。ふつうに投げるときも大祓のときも呼ばれる（Kind で分ける）
        public event Action<SwingAcceptedArgs> SwingAccepted;
        public event Action<SwingRejectedArgs> SwingRejected;
        public event Action<float> YawCalibrated;
        public event Action<int> SelectionChanged;

        InputStateMachine _machine;
        IControllerRawSource _raw;
        IThrowFeedbackSink _throwFeedback;
        readonly bool[] _prevColorHeld = new bool[InputStateMachine.ColorCount];
        bool _prevFrontHeld;
        float _yawOffset;
        int _fireFrame = -1;
        int _selectFrame = -1;
        AudioClip _synthesizedRejectSe;

        readonly int[] _rejectCounts = new int[Enum.GetValues(typeof(SwingRejectReason)).Length];
        int _acceptedCount;
        string _lastEvent = "-";

        public InputStateMachine Machine => _machine;
        public IControllerRawSource RawSource => _raw;
        public int SelectedColor => _machine != null ? _machine.SelectedColor : (int)initialSelection;
        public float YawOffset => _yawOffset;
        // キャリブレーションした基準からのヨー角（-180〜180度）。キャリブレーションした直後は 0
        public float RelativeYaw => _raw != null ? Mathf.DeltaAngle(_yawOffset, _raw.Yaw) : 0f;
        public int AcceptedCount => _acceptedCount;
        public int GetRejectedCount(SwingRejectReason reason) => _rejectCounts[(int)reason];

        public CooldownPreset Preset
        {
            get => cooldownPreset;
            set => cooldownPreset = value;
        }

        public float CooldownSeconds
        {
            get
            {
                switch (cooldownPreset)
                {
                    case CooldownPreset.Sec040: return 0.40f;
                    case CooldownPreset.Sec050: return 0.50f;
                    case CooldownPreset.Sec060: return 0.60f;
                    case CooldownPreset.Sec065: return 0.65f;
                    default: return customCooldownSeconds;
                }
            }
        }

        // ---- IInputProvider（#20） ----
        public bool FireTriggered => _fireFrame == Time.frameCount;
        // #60: 照準（ヨーとピッチ、またはマウスから出した地面の着弾予測点）の画面上の位置。照準がないシーンではマウスの位置
        public Vector3 AimScreenPosition => aim != null && aim.isActiveAndEnabled && aim.HasAim ? aim.ScreenPosition : Input.mousePosition;
        public int OmamoriSelectTriggered => _selectFrame == Time.frameCount ? _machine.SelectedColor : -1;

        void Awake()
        {
            _raw = rawSourceSource as IControllerRawSource;
            if (rawSourceSource != null && _raw == null)
                Debug.LogWarning("[ThrowInputController] rawSourceSource が IControllerRawSource を実装していません", this);
            if (_raw == null)
                _raw = GetComponent<IControllerRawSource>();
            if (_raw == null)
                Debug.LogWarning("[ThrowInputController] 生入力の供給元がありません（KeyboardMouseRawSource か Esp32RawSource を付けてください）", this);

            _yawOffset = initialYawOffset;

            if (aim == null)
                aim = FindAnyObjectByType<OnusaAimController>();

            _throwFeedback = throwFeedbackSource as IThrowFeedbackSink;
            if (throwFeedbackSource != null && _throwFeedback == null)
                Debug.LogWarning("[ThrowInputController] throwFeedbackSource が IThrowFeedbackSink を実装していません", this);
            if (_throwFeedback == null)
                _throwFeedback = FindThrowFeedbackSink();

            _machine = new InputStateMachine((int)initialSelection);
            _machine.SwingAccepted += HandleSwingAccepted;
            _machine.SwingRejected += HandleSwingRejected;
            _machine.YawCalibrationRequested += HandleYawCalibration;
            _machine.SelectionChanged += HandleSelectionChanged;
            _machine.ChargeStarted += c => Log($"大祓チャージ開始 色={c}（未実装）");
            _machine.ChargeCancelled += c => Log($"大祓チャージ取り消し 色={c}");
            ApplySettings();
        }

        void OnDestroy()
        {
            if (_synthesizedRejectSe != null) Destroy(_synthesizedRejectSe);
        }

        void OnDisable()
        {
            if (_machine == null) return;
            // 押しっぱなしのまま無効になっても、再開したときに押したことになったままにならないようにする
            double now = Time.realtimeSinceStartupAsDouble;
            for (int i = 0; i < _prevColorHeld.Length; i++)
            {
                if (_prevColorHeld[i]) _machine.ReleaseColor(i, now);
                _prevColorHeld[i] = false;
            }
            if (_prevFrontHeld) _machine.ReleaseFront(now);
            _prevFrontHeld = false;
        }

        void Update()
        {
            if (_raw == null) return;

            if (cooldownHotkeys) HandleCooldownHotkeys();
            ApplySettings();

            double now = Time.realtimeSinceStartupAsDouble;
            _machine.IsActive = !requireSessionPlaying || GameSession.Instance == null || GameSession.Instance.IsPlaying;

            // 同じフレームの中では「ボタン → 振りピーク → 時間」の順で見る（色を押してから振った、という気持ちを優先する）
            for (int i = 0; i < InputStateMachine.ColorCount; i++)
            {
                bool held = _raw.IsColorHeld(i);
                if (held == _prevColorHeld[i]) continue;
                if (held) _machine.PressColor(i, now);
                else _machine.ReleaseColor(i, now);
                _prevColorHeld[i] = held;
            }

            bool front = _raw.IsFrontHeld;
            if (front != _prevFrontHeld)
            {
                if (front) _machine.PressFront(now);
                else _machine.ReleaseFront(now);
                _prevFrontHeld = front;
            }

            while (_raw.TryConsumeSwingPeak(out float strength, out double time))
                _machine.SwingPeak(strength, time);

            _machine.Tick(now);
        }

        void ApplySettings()
        {
            _machine.CooldownSeconds = CooldownSeconds;
            _machine.FrontHoldSeconds = frontHoldSeconds;
            _machine.OharaeEnabled = oharaeEnabled;
            _machine.ChargeSeconds = oharaeChargeSeconds;
        }

        void HandleCooldownHotkeys()
        {
            CooldownPreset before = cooldownPreset;
            if (Input.GetKeyDown(KeyCode.F1)) cooldownPreset = CooldownPreset.Sec040;
            else if (Input.GetKeyDown(KeyCode.F2)) cooldownPreset = CooldownPreset.Sec050;
            else if (Input.GetKeyDown(KeyCode.F3)) cooldownPreset = CooldownPreset.Sec060;
            else if (Input.GetKeyDown(KeyCode.F4)) cooldownPreset = CooldownPreset.Sec065;
            if (cooldownPreset != before)
                Log($"クールダウン切替: {CooldownSeconds:0.00} 秒");
        }

        void HandleSwingAccepted(SwingAcceptedArgs e)
        {
            _acceptedCount++;

            if (e.Kind == ThrowKind.Oharae)
            {
                // 大祓は分かれ道だけ用意してある（T5 を通ったあとに作る）。ふつうの弾は出さない
                Log($"大祓 確定 色={e.ColorIndex}（未実装のため発射なし）");
                SwingAccepted?.Invoke(e);
                return;
            }

            // 投げる音（と発射の振動）はほかの処理より先に出す（振りピークを見つけたのと同じ時刻にするため）
            if (_throwFeedback != null)
                _throwFeedback.OnThrowAccepted(e);
            else if (seSource != null && throwSe != null)
                seSource.PlayOneShot(throwSe);

            _fireFrame = Time.frameCount;
            Log($"SwingAccepted 色={e.ColorIndex} 強さ={e.Strength:0.0} t={e.Time:0.000}");
            SwingAccepted?.Invoke(e);
            onSwingAccepted?.Invoke(e.ColorIndex);
        }

        static IThrowFeedbackSink FindThrowFeedbackSink()
        {
            foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (behaviour is IThrowFeedbackSink sink) return sink;
            }
            return null;
        }

        void HandleSwingRejected(SwingRejectedArgs e)
        {
            _rejectCounts[(int)e.Reason]++;

            // #60: クールダウン中に振ったときは弾を出さずに、短い低い音だけ返す（灰色の表示は OnusaThrower から AimReticleView でやる）
            if (e.Reason == SwingRejectReason.Cooldown && seSource != null)
            {
                AudioClip clip = cooldownRejectSe;
                if (clip == null && synthesizeRejectSe)
                {
                    if (_synthesizedRejectSe == null)
                        _synthesizedRejectSe = ProceduralTone.Create("CooldownRejectTone", 110f, 0.12f, 0.6f);
                    clip = _synthesizedRejectSe;
                }
                if (clip != null) seSource.PlayOneShot(clip);
            }

            Log($"SwingRejected 理由={e.Reason} t={e.Time:0.000}");
            SwingRejected?.Invoke(e);
        }

        void HandleYawCalibration()
        {
            _yawOffset = _raw != null ? _raw.Yaw : initialYawOffset;
            Log($"ヨー角キャリブレーション 基準={_yawOffset:0.0}");
            YawCalibrated?.Invoke(_yawOffset);
            onYawCalibrated?.Invoke(_yawOffset);
        }

        void HandleSelectionChanged(int color)
        {
            _selectFrame = Time.frameCount;
            Log($"選択 色={color}");
            SelectionChanged?.Invoke(color);
            onSelectionChanged?.Invoke(color);
        }

        void Log(string message)
        {
            if (logEvents) Debug.Log($"[ThrowInput] {message}", this);
            _lastEvent = message;
        }

        void OnGUI()
        {
            if (!showDebugHud || _machine == null) return;

            double now = Time.realtimeSinceStartupAsDouble;
            string held = "";
            for (int i = 0; i < InputStateMachine.ColorCount; i++)
                held += _machine.IsColorHeld(i) ? (i + 1).ToString() : "-";

            string text =
                $"[入力状態機械 #51] {_machine.GetPhase(now)}  選択={_machine.SelectedColor + 1}  押下={held}  正面={(_machine.IsFrontHeld ? $"{_machine.FrontHoldProgress(now) * 100f:0}%" : "-")}\n" +
                $"CD={CooldownSeconds:0.00}s（残り {_machine.CooldownRemaining(now):0.00}s / F1-F4 切替）  相対ヨー={RelativeYaw:0.0}°  接続={(_raw != null && _raw.IsConnected ? "○" : "×")}\n" +
                $"確定={_acceptedCount}  却下: CD={GetRejectedCount(SwingRejectReason.Cooldown)} 正面={GetRejectedCount(SwingRejectReason.FrontHeld)} 未選択={GetRejectedCount(SwingRejectReason.NoSelection)} 停止中={GetRejectedCount(SwingRejectReason.Inactive)}\n" +
                $"最後: {_lastEvent}";

            GUI.Box(new Rect(10, Screen.height - 90, 620, 80), GUIContent.none);
            GUI.Label(new Rect(16, Screen.height - 86, 610, 76), text);
        }
    }
}
