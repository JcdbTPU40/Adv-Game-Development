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
    // MVP で必要な効果音7種
    public enum FeedbackSe
    {
        Throw = 0,     // 発射
        Hit = 1,       // 命中
        Rescue = 2,    // 救済
        Fail = 3,      // 失敗（黒客になった）
        BlackHit = 4,  // 黒客に当たった
        Countdown = 5, // カウントダウン
        Bell = 6       // 鈴
    }

    /*
        MVP で必要な効果音7種と振動をつなぐクラス（#64 / 企画書 v8 15章・1章）

        効果音ごとの「きっかけ」と「鳴らす場所」:
        ・発射: 有効スイングが決まったとき。ThrowInputController がほかの処理より先に OnThrowAccepted を呼ぶ
        ・命中: 相性◯で当たったとき。OmamoriHitResolver.HitResolved。福の連なりで音を上げて、3・6・10 で和音
        ・救済: 当てて救済が決まったとき。同じく HitResolved（OmamoriHitInfo.Rescued）
        ・失敗（黒客になった）: 客の危険度Dが100になったとき。CustomerState.AnyFinished
        ・黒客ヒット: 黒客に当たったとき。HitResolved（命中音のかわりに鳴らす）
        ・カウントダウン: 残り countdownFrom 秒から毎秒。GameSession.RemainingSeconds
        ・鈴: ゲームの開始と終了。GameSession.IsPlaying が変わったとき

        ・振動は HapticArbiter で1本にしぼる（救済成功 ＞ 命中 ＞ 発射。100ms 以内に重なったら上のほうに入れかえ）
          出す先はつながっているゲームパッド。ESP32 の振動は HapticStarted を受け取って送る形にする（ファームウェアはまだ対応してない）
        ・音が鳴るまでの遅れは FeedbackBudget で測って、デバッグHUD（画面の右上）に出す
        ・素材が入っていない枠は、ProceduralSe の仮の音を鳴らす
    */
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

        // 効果音を鳴らしたときに呼ばれる（和音で足した音では呼ばれない）
        public event Action<FeedbackSe> SePlayed;
        // 振動が決まったときに呼ばれる（ESP32 とか、ゲームパッド以外に出したいときはここを受け取る）
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
            CustomerState.AnyFinished += HandleCustomerFinished;
        }

        void OnDisable()
        {
            OmamoriHitResolver.HitResolved -= HandleHitResolved;
            CustomerState.AnyFinished -= HandleCustomerFinished;
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

        // ---- 発射（IThrowFeedbackSink） ----

        // 有効スイングが決まった瞬間。ThrowInputController が発射より先に呼ぶ
        public void OnThrowAccepted(SwingAcceptedArgs e)
        {
            PlayThrowSe(e);
            PlayThrowHaptic();
        }

        /*
            投げる音だけを鳴らす。振りピークから音が鳴るまでの遅れもここで測る
            #49 の T0-A/B では投げる音と発射の振動に別々の時間差を付けるので、2つに分けてある
        */
        public void PlayThrowSe(SwingAcceptedArgs e)
        {
            Play(FeedbackSe.Throw, _throw);
            Record(FeedbackCategory.Swing, Now - e.Time);
        }

        // 発射の振動だけを出す
        public void PlayThrowHaptic()
        {
            RequestHaptic(HapticKind.Throw, throwHapticMs, weakAmplitude);
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

        // ---- 失敗（黒客になった） ----

        void HandleCustomerFinished(CustomerState state, CustomerPhase result)
        {
            if (result == CustomerPhase.Black)
                Play(FeedbackSe.Fail, _fail);
        }

        // ---- カウントダウンと鈴 ----

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

        // 空いている AudioSource を使う。ぜんぶ鳴っていたら、いちばん古く使ったものを止めて使う
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

        // 今の振動の強さをモーターに反映する（いちばん上の1本の強さだけ。足したりはしない）
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

        // ---- 予算（遅れの計測） ----

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
