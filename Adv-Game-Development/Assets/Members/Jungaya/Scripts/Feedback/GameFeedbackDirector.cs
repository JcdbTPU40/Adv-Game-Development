using System;
using System.Collections.Generic;
using UnityEngine;
using Toufuku.GameInput;
using Toufuku.Rescue;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Toufuku.Feedback
{
    /// <summary>MVP 必須 SE 7 種。</summary>
    public enum FeedbackSe
    {
        Throw = 0,     // 発射
        Hit = 1,       // 命中
        Rescue = 2,    // 救済
        Fail = 3,      // 失敗（黒客化）
        BlackHit = 4,  // 黒客ヒット
        Countdown = 5, // カウントダウン
        Bell = 6       // 鈴
    }

    /// <summary>
    /// MVP 必須 SE 7 種と振動の結線 — Issue #64（仕様書 v8 15章・1章）
    ///
    /// | SE | きっかけ | 鳴らす場所 |
    /// |---|---|---|
    /// | 発射 | 有効スイング確定（振りピーク） | <see cref="ThrowInputController"/> が他の処理より先に <see cref="OnThrowAccepted"/> を呼ぶ |
    /// | 命中 | 相性◯の命中 | <see cref="OmamoriHitResolver.HitResolved"/>。福の連なりで音階を上げ、3・6・10 で和音 |
    /// | 救済 | 命中で救済が確定 | 同上（<see cref="OmamoriHitInfo.Rescued"/>） |
    /// | 失敗（黒客化） | 客のゲージが満タン（怒り） | <see cref="CustomerMood.AnyFinished"/> |
    /// | 黒客ヒット | 黒客に命中 | <see cref="OmamoriHitResolver.HitResolved"/>（命中音の代わりに鳴らす） |
    /// | カウントダウン | ゲーム終了の残り countdownFrom 秒から毎秒 | <see cref="GameSession.RemainingSeconds"/> |
    /// | 鈴 | ゲーム開始・終了 | <see cref="GameSession.IsPlaying"/> の変化 |
    ///
    /// ・振動は <see cref="HapticArbiter"/> で 1 本に絞る（救済成功 ＞ 命中 ＞ 発射、100ms 以内の重なりは上位へ置き換え）。
    ///   出力先は接続中のゲームパッド。ESP32 の振動は <see cref="HapticStarted"/> を購読して送る（ファームウェア未対応）。
    /// ・発音までの遅延は <see cref="FeedbackBudget"/> で計測し、デバッグ HUD（画面右上）に出す。
    /// ・素材が未設定の枠は <see cref="ProceduralSe"/> の仮SE を鳴らす。
    /// </summary>
    public class GameFeedbackDirector : MonoBehaviour, IThrowFeedbackSink
    {
        [Header("SE 素材（未設定の枠は仮SE を合成して鳴らす）")]
        [SerializeField] AudioClip throwSe;
        [Tooltip("命中音。音階は再生速度（pitch）で上げるので、1 連なり目の高さの素材を入れる")]
        [SerializeField] AudioClip hitSe;
        [SerializeField] AudioClip rescueSe;
        [Tooltip("救済失敗（黒客化）")]
        [SerializeField] AudioClip failSe;
        [SerializeField] AudioClip blackHitSe;
        [SerializeField] AudioClip countdownSe;
        [Tooltip("残り accentFrom 秒以下のカウントダウン。未設定なら countdownSe を 5 度高く鳴らす")]
        [SerializeField] AudioClip countdownAccentSe;
        [SerializeField] AudioClip bellSe;
        [SerializeField] bool synthesizePlaceholders = true;
        [SerializeField, Range(0f, 1f)] float seVolume = 0.9f;

        [Header("命中音の音階（福の連なり）")]
        [Tooltip("この段数で音階の上昇を止める（10 段 = 2 オクターブ）")]
        [SerializeField, Min(0)] int maxScaleStep = ComboScale.DefaultMaxStep;
        [Tooltip("和音を足す連なり数")]
        [SerializeField] int[] chordMilestones = { 3, 6, 10 };
        [SerializeField, Range(0f, 1f)] float chordVolume = 0.55f;
        [Tooltip("同時に重ねて鳴らせる数（和音と連打が途切れないように）")]
        [SerializeField, Range(2, 16)] int voiceCount = 8;

        [Header("カウントダウン（ゲーム終了前）")]
        [Tooltip("残り何秒から毎秒鳴らすか。0 で鳴らさない")]
        [SerializeField, Min(0)] int countdownFrom = 10;
        [Tooltip("残りこの秒数以下は強調音")]
        [SerializeField, Min(0)] int accentFrom = 3;

        [Header("鈴")]
        [SerializeField] bool bellOnSessionStart = true;
        [SerializeField] bool bellOnSessionEnd = true;

        [Header("振動（発射 40ms 弱／命中 80ms 中・連なりで 100→120ms／救済 120ms 強）")]
        [SerializeField] bool hapticsEnabled = true;
        [SerializeField, Range(0f, 1f)] float weakAmplitude = 0.3f;
        [SerializeField, Range(0f, 1f)] float mediumAmplitude = 0.6f;
        [SerializeField, Range(0f, 1f)] float strongAmplitude = 1f;
        [SerializeField, Min(0f)] float throwHapticMs = 40f;
        [SerializeField, Min(0f)] float hitHapticMs = 80f;
        [SerializeField, Min(0f)] float hitChainHapticMs = 100f;
        [SerializeField, Min(0f)] float hitLongChainHapticMs = 120f;
        [SerializeField, Min(0f)] float rescueHapticMs = 120f;
        [Tooltip("この連なり数から命中の振動を hitChainHapticMs にする")]
        [SerializeField, Min(1)] int chainFrom = 3;
        [Tooltip("この連なり数から命中の振動を hitLongChainHapticMs にする")]
        [SerializeField, Min(1)] int longChainFrom = 6;
        [Tooltip("この時間内に重なった振動は上位 1 つへ置き換える")]
        [SerializeField, Min(0f)] float overlapWindowMs = 100f;
        [Tooltip("接続中のゲームパッドを振動させる")]
        [SerializeField] bool gamepadRumble = true;

        [Header("デバッグ")]
        [SerializeField] bool showDebugHud = true;
        [SerializeField] bool logEvents = false;
        [Tooltip("予算を超えた発音を Console に警告する")]
        [SerializeField] bool warnBudgetViolation = true;

        /// <summary>SE を鳴らした（和音の追加音では発火しない）。</summary>
        public event Action<FeedbackSe> SePlayed;
        /// <summary>振動が採用された（ESP32 など、ゲームパッド以外の出力先はここを購読する）。</summary>
        public event Action<HapticPulse> HapticStarted;

        static readonly int s_seKinds = Enum.GetValues(typeof(FeedbackSe)).Length;

        readonly HapticArbiter _haptics = new HapticArbiter();
        readonly FeedbackBudget _budget = new FeedbackBudget();
        readonly CountdownTicker _countdown = new CountdownTicker();
        readonly List<AudioClip> _synthesized = new List<AudioClip>();
        readonly int[] _seCounts = new int[s_seKinds];

        AudioSource[] _voices;
        int _nextVoice;
        AudioClip _throw, _hit, _rescue, _fail, _blackHit, _tick, _tickAccent, _bell;
        float _tickAccentPitch = 1f;
        bool _wasPlaying;
        float _motor;
        float _outputBufferMs;
        int _lastCombo;
        string _lastEvent = "-";

        public FeedbackBudget Budget => _budget;
        public HapticArbiter Haptics => _haptics;
        public float CurrentHapticAmplitude => _haptics.AmplitudeAt(Now);
        public int GetSeCount(FeedbackSe se) => _seCounts[(int)se];

        static double Now => Time.realtimeSinceStartupAsDouble;

        void Awake()
        {
            CreateVoices();
            ResolveClips();

            AudioSettings.GetDSPBufferSize(out int bufferLength, out _);
            int rate = AudioSettings.outputSampleRate;
            _outputBufferMs = rate > 0 ? bufferLength * 1000f / rate : 0f;
        }

        void OnEnable()
        {
            OmamoriHitResolver.HitResolved += HandleHitResolved;
            CustomerMood.AnyFinished += HandleCustomerFinished;
        }

        void OnDisable()
        {
            OmamoriHitResolver.HitResolved -= HandleHitResolved;
            CustomerMood.AnyFinished -= HandleCustomerFinished;
            _haptics.Stop();
            ApplyMotor();
        }

        void OnDestroy()
        {
            foreach (AudioClip clip in _synthesized)
            {
                if (clip != null) Destroy(clip);
            }
            _synthesized.Clear();
        }

        void Update()
        {
            UpdateSession();
            ApplyMotor();
        }

        // ---- 発射（IThrowFeedbackSink）----

        /// <summary>有効スイング確定の瞬間。ThrowInputController が発射より先に呼ぶ。</summary>
        public void OnThrowAccepted(SwingAcceptedArgs e)
        {
            Play(FeedbackSe.Throw, _throw);
            RequestHaptic(HapticKind.Throw, throwHapticMs, weakAmplitude);
            Record(FeedbackCategory.Swing, Now - e.Time);
        }

        // ---- 命中・救済・黒客ヒット ----

        void HandleHitResolved(OmamoriHitInfo info)
        {
            double latency = Now - info.ImpactTime;
            _lastCombo = info.Combo;

            if (info.IsBlackCustomer)
            {
                Play(FeedbackSe.BlackHit, _blackHit);
                Record(FeedbackCategory.Hit, latency);
            }
            else if (info.IsGoodHit)
            {
                PlayHit(info.Combo);
                RequestHaptic(HapticKind.Hit, HitHapticMs(info.Combo), mediumAmplitude);
                Record(FeedbackCategory.Hit, latency);
            }

            if (info.Rescued)
            {
                Play(FeedbackSe.Rescue, _rescue);
                RequestHaptic(HapticKind.Rescue, rescueHapticMs, strongAmplitude);
                Record(FeedbackCategory.Rescue, latency);
            }
        }

        void PlayHit(int combo)
        {
            int root = ComboScale.SemitoneOf(combo, maxScaleStep);
            Play(FeedbackSe.Hit, _hit, ComboScale.PitchOf(root));

            foreach (int interval in ComboScale.ChordOf(combo, chordMilestones))
                Play(FeedbackSe.Hit, _hit, ComboScale.PitchOf(root + interval), chordVolume, notify: false);
        }

        float HitHapticMs(int combo)
        {
            if (combo >= longChainFrom) return hitLongChainHapticMs;
            if (combo >= chainFrom) return hitChainHapticMs;
            return hitHapticMs;
        }

        // ---- 失敗（黒客化）----

        void HandleCustomerFinished(CustomerMood mood, CustomerMood.MoodState result)
        {
            if (result == CustomerMood.MoodState.Angry)
                Play(FeedbackSe.Fail, _fail);
        }

        // ---- カウントダウン・鈴 ----

        void UpdateSession()
        {
            GameSession session = GameSession.Instance;
            bool playing = session != null && session.IsPlaying;

            if (playing && !_wasPlaying)
            {
                _countdown.Reset();
                if (bellOnSessionStart) Play(FeedbackSe.Bell, _bell);
            }
            else if (!playing && _wasPlaying && session != null && session.IsFinished)
            {
                if (bellOnSessionEnd) Play(FeedbackSe.Bell, _bell);
            }
            _wasPlaying = playing;

            if (!playing || countdownFrom <= 0) return;

            _countdown.From = countdownFrom;
            int n = _countdown.Advance(session.RemainingSeconds);
            if (n <= 0) return;

            if (n <= accentFrom) Play(FeedbackSe.Countdown, _tickAccent, _tickAccentPitch);
            else Play(FeedbackSe.Countdown, _tick);
        }

        // ---- 音 ----

        void CreateVoices()
        {
            var holder = new GameObject("SE Voices");
            holder.transform.SetParent(transform, false);

            _voices = new AudioSource[voiceCount];
            for (int i = 0; i < _voices.Length; i++)
            {
                AudioSource source = holder.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                _voices[i] = source;
            }
        }

        void ResolveClips()
        {
            _throw = Resolve(throwSe, ProceduralSe.Throw);
            _hit = Resolve(hitSe, ProceduralSe.Hit);
            _rescue = Resolve(rescueSe, ProceduralSe.Rescue);
            _fail = Resolve(failSe, ProceduralSe.Fail);
            _blackHit = Resolve(blackHitSe, ProceduralSe.BlackHit);
            _tick = Resolve(countdownSe, () => ProceduralSe.CountdownTick(false));
            _bell = Resolve(bellSe, ProceduralSe.Bell);

            if (countdownAccentSe != null)
            {
                _tickAccent = countdownAccentSe;
            }
            else if (countdownSe != null)
            {
                _tickAccent = countdownSe;
                _tickAccentPitch = ComboScale.PitchOf(7);
            }
            else
            {
                _tickAccent = Resolve(null, () => ProceduralSe.CountdownTick(true));
            }
        }

        AudioClip Resolve(AudioClip assigned, Func<AudioClip> synthesize)
        {
            if (assigned != null || !synthesizePlaceholders) return assigned;
            AudioClip clip = synthesize();
            _synthesized.Add(clip);
            return clip;
        }

        void Play(FeedbackSe kind, AudioClip clip, float pitch = 1f, float volume = 1f, bool notify = true)
        {
            if (clip == null || _voices == null) return;

            AudioSource voice = AcquireVoice();
            voice.clip = clip;
            voice.pitch = pitch;
            voice.volume = seVolume * volume;
            voice.Play();

            if (!notify) return;
            _seCounts[(int)kind]++;
            _lastEvent = pitch == 1f ? kind.ToString() : $"{kind} pitch={pitch:0.00}";
            if (logEvents) Debug.Log($"[Feedback] SE {_lastEvent}", this);
            SePlayed?.Invoke(kind);
        }

        /// <summary>空いている声を使う。すべて鳴っていれば最も古く割り当てた声を止めて使う。</summary>
        AudioSource AcquireVoice()
        {
            for (int i = 0; i < _voices.Length; i++)
            {
                int index = (_nextVoice + i) % _voices.Length;
                if (!_voices[index].isPlaying)
                {
                    _nextVoice = (index + 1) % _voices.Length;
                    return _voices[index];
                }
            }
            AudioSource stolen = _voices[_nextVoice];
            _nextVoice = (_nextVoice + 1) % _voices.Length;
            return stolen;
        }

        // ---- 振動 ----

        void RequestHaptic(HapticKind kind, float milliseconds, float amplitude)
        {
            if (!hapticsEnabled || milliseconds <= 0f) return;

            _haptics.OverlapWindowSeconds = overlapWindowMs / 1000.0;
            var pulse = new HapticPulse(kind, milliseconds / 1000f, amplitude);
            if (!_haptics.Request(pulse, Now)) return;

            if (logEvents) Debug.Log($"[Feedback] 振動 {kind} {milliseconds:0}ms 強さ {amplitude:0.0}", this);
            HapticStarted?.Invoke(pulse);
            ApplyMotor();
        }

        /// <summary>今の振動の強さをモーターへ反映する（上位 1 本の強さだけ。足さない）。</summary>
        void ApplyMotor()
        {
            float amplitude = _haptics.AmplitudeAt(Now);
            if (Mathf.Approximately(amplitude, _motor)) return;
            _motor = amplitude;

#if ENABLE_INPUT_SYSTEM
            if (gamepadRumble && Gamepad.current != null)
                Gamepad.current.SetMotorSpeeds(amplitude, amplitude);
#endif
        }

        // ---- 予算 ----

        void Record(FeedbackCategory category, double latencySeconds)
        {
            if (_budget.Record(category, latencySeconds) || !warnBudgetViolation) return;
            Debug.LogWarning(
                $"[Feedback] 予算超過 {category}: {latencySeconds * 1000.0:0} ms > {FeedbackBudget.LimitOf(category) * 1000.0:0} ms", this);
        }

        void OnGUI()
        {
            if (!showDebugHud) return;

            string text =
                $"[フィードバック予算 #64]  出力バッファ ≈ {_outputBufferMs:0} ms（下の値に含まない）\n" +
                BudgetLine("スイング", FeedbackCategory.Swing) +
                BudgetLine("命中", FeedbackCategory.Hit) +
                BudgetLine("救済", FeedbackCategory.Rescue) +
                $"振動 {_motor:0.0}  連なり {_lastCombo}  最後の SE: {_lastEvent}";

            const float width = 460f;
            var rect = new Rect(Screen.width - width - 10f, 10f, width, 92f);
            GUI.Box(rect, GUIContent.none);
            GUI.Label(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, rect.height - 8f), text);
        }

        string BudgetLine(string label, FeedbackCategory category)
        {
            return $"{label}: 直近 {_budget.LastSeconds(category) * 1000.0:0} / 最大 {_budget.MaxSeconds(category) * 1000.0:0} ms" +
                   $"（予算 {FeedbackBudget.LimitOf(category) * 1000.0:0}）超過 {_budget.ViolationCount(category)}/{_budget.SampleCount(category)}\n";
        }
    }
}
