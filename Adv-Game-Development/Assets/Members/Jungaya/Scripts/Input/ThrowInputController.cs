using System;
using UnityEngine;
using UnityEngine.Events;
using Toufuku.Rescue;

namespace Toufuku.GameInput
{
    /// <summary>T0-CD（#50）で比較するクールダウン値。値は int でシーンに焼かれるので並べ替えないこと。</summary>
    public enum CooldownPreset
    {
        Sec040 = 0,
        Sec050 = 1,
        Sec060 = 2,
        Sec065 = 3,
        Custom = 4
    }

    /// <summary>
    /// 入力状態機械のシーン側の窓口 — Issue #51
    ///
    /// 生入力（<see cref="IControllerRawSource"/>）を押した／離したのエッジに直して
    /// <see cref="InputStateMachine"/> へ流し、結果をイベントとして配る。
    ///
    /// ・発射: <see cref="SwingAccepted"/> が唯一の発火点。IInputProvider も実装しているので、
    ///   TestShooter / OmamoriSelector の inputProviderSource にこれをドラッグすれば
    ///   FireTriggered / OmamoriSelectTriggered がこの状態機械の結果になる。
    /// ・投擲SE: SwingAccepted を受けたその場（振りピーク検出と同じフレーム）で鳴らす。
    /// ・キャリブレーション: 正面ボタン 1 秒長押しで基準ヨー角を取り直す。向きは <see cref="RelativeYaw"/> を読む。
    /// ・発射側より先に今フレームの入力を確定させるため、実行順を早めている。
    /// </summary>
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
        [SerializeField] AudioSource seSource;
        [SerializeField] AudioClip throwSe;
        [Tooltip("クールダウン中の有効スイングで返す短い低音（#60）。未設定なら鳴らさない")]
        [SerializeField] AudioClip cooldownRejectSe;

        [Header("セッション連動（#32）: ON なら GameSession がプレイ中のときだけ投擲を受け付ける")]
        [SerializeField] bool requireSessionPlaying = true;

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

        /// <summary>有効スイング確定。通常投擲・大祓の両方で発火する（Kind で分岐）。</summary>
        public event Action<SwingAcceptedArgs> SwingAccepted;
        public event Action<SwingRejectedArgs> SwingRejected;
        public event Action<float> YawCalibrated;
        public event Action<int> SelectionChanged;

        InputStateMachine _machine;
        IControllerRawSource _raw;
        readonly bool[] _prevColorHeld = new bool[InputStateMachine.ColorCount];
        bool _prevFrontHeld;
        float _yawOffset;
        int _fireFrame = -1;
        int _selectFrame = -1;

        readonly int[] _rejectCounts = new int[Enum.GetValues(typeof(SwingRejectReason)).Length];
        int _acceptedCount;
        string _lastEvent = "-";

        public InputStateMachine Machine => _machine;
        public IControllerRawSource RawSource => _raw;
        public int SelectedColor => _machine != null ? _machine.SelectedColor : (int)initialSelection;
        public float YawOffset => _yawOffset;
        /// <summary>キャリブレーション基準からのヨー角（-180〜180 度）。キャリブレーション直後は 0。</summary>
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

        // ---- IInputProvider（#20）----
        public bool FireTriggered => _fireFrame == Time.frameCount;
        // 照準の実機化は #60。それまではマウス位置を返す
        public Vector3 AimScreenPosition => Input.mousePosition;
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

            _machine = new InputStateMachine((int)initialSelection);
            _machine.SwingAccepted += HandleSwingAccepted;
            _machine.SwingRejected += HandleSwingRejected;
            _machine.YawCalibrationRequested += HandleYawCalibration;
            _machine.SelectionChanged += HandleSelectionChanged;
            _machine.ChargeStarted += c => Log($"大祓チャージ開始 色={c}（未実装）");
            _machine.ChargeCancelled += c => Log($"大祓チャージ取り消し 色={c}");
            ApplySettings();
        }

        void OnDisable()
        {
            if (_machine == null) return;
            // 押しっぱなしのまま無効化されても、再開時に押下が残らないようにする
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

            // 同一フレーム内は「ボタン → 振りピーク → 時間経過」の順で評価する（色を押して振った意図を優先）
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
                // 大祓は分岐だけ用意（T5 通過後に実装）。通常弾は出さない
                Log($"大祓 確定 色={e.ColorIndex}（未実装のため発射なし）");
                SwingAccepted?.Invoke(e);
                return;
            }

            // 投擲SE は他の処理より先に鳴らす（振りピーク検出と同時刻にするため）
            if (seSource != null && throwSe != null)
                seSource.PlayOneShot(throwSe);

            _fireFrame = Time.frameCount;
            Log($"SwingAccepted 色={e.ColorIndex} 強さ={e.Strength:0.0} t={e.Time:0.000}");
            SwingAccepted?.Invoke(e);
            onSwingAccepted?.Invoke(e.ColorIndex);
        }

        void HandleSwingRejected(SwingRejectedArgs e)
        {
            _rejectCounts[(int)e.Reason]++;

            if (e.Reason == SwingRejectReason.Cooldown && seSource != null && cooldownRejectSe != null)
                seSource.PlayOneShot(cooldownRejectSe);

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
