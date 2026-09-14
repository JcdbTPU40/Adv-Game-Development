using System.Collections.Generic;
using UnityEngine;
using Toufuku.Aim;
using Toufuku.Feedback;
using Toufuku.GameInput;

namespace Toufuku.Playtest
{
    /// <summary>
    /// 投擲SE・軌跡出現・振動開始に時刻差を付ける — Issue #49（仕様書 v8 17章）
    ///
    /// <see cref="ThrowInputController.throwFeedbackSource"/> にこれを挿し、GameFeedbackDirector の代わりに受け取る。
    /// 受けた <see cref="OnThrowAccepted"/> で 3 つの出口をそれぞれの時刻差で出す。
    /// ・投擲SE: <see cref="GameFeedbackDirector.PlayThrowSe"/>
    /// ・振動  : <see cref="GameFeedbackDirector.PlayThrowHaptic"/>
    /// ・軌跡  : <see cref="OnusaThrower.VisualDelaySeconds"/>（この直後に作られる弾へ効く）
    ///
    /// 時刻差 0 の出口はその場で（＝本番と同じ呼び出しの中で）出す。
    /// 0 より大きい出口は Update で出すのでフレーム単位に丸まる（60fps なら最大 16ms）。
    /// 実測値は <see cref="LastOffsetMs"/> に残るので、HUD と記録で確かめる。
    /// </summary>
    [DefaultExecutionOrder(-90)] // ThrowInputController(-100) の直後
    public class FeedbackTimingShifter : MonoBehaviour, IThrowFeedbackSink
    {
        [Header("参照（未設定ならシーン内から探す）")]
        [SerializeField] GameFeedbackDirector director;
        [SerializeField] OnusaThrower thrower;

        [Header("デバッグ")]
        [SerializeField] bool logShifts = false;

        struct Pending
        {
            public FeedbackChannel Channel;
            public double Due;
            public SwingAcceptedArgs Args;
        }

        readonly List<Pending> _pending = new List<Pending>(8);
        readonly float[] _lastOffsetMs = new float[3];
        readonly float[] _maxErrorMs = new float[3];

        FeedbackVariant _variant = FeedbackVariant.DefaultA();

        /// <summary>今あてているフィードバック案。</summary>
        public FeedbackVariant Variant
        {
            get => _variant;
            set
            {
                _variant = value ?? FeedbackVariant.DefaultA();
                if (thrower != null) thrower.VisualDelaySeconds = _variant.trailDelayMs / 1000f;
            }
        }

        /// <summary>受け取った有効スイングの数。</summary>
        public int ThrowCount { get; private set; }

        /// <summary>直近の実測時刻差（ms）。振りピークから実際に出すまで。軌跡は設定値をそのまま返す。</summary>
        public float LastOffsetMs(FeedbackChannel channel) =>
            channel == FeedbackChannel.Trail ? _variant.trailDelayMs : _lastOffsetMs[(int)channel];

        /// <summary>設定値からのずれの最大（ms）。フレーム単位の丸めがどれくらいかを見る。</summary>
        public float MaxErrorMs(FeedbackChannel channel) => _maxErrorMs[(int)channel];

        static double Now => Time.realtimeSinceStartupAsDouble;

        void Awake()
        {
            if (director == null) director = FindAnyObjectByType<GameFeedbackDirector>();
            if (thrower == null) thrower = FindAnyObjectByType<OnusaThrower>();
            if (director == null)
                Debug.LogWarning("[TimingShifter] GameFeedbackDirector がありません（投擲SE と振動が出ません）", this);
            Variant = _variant;
        }

        void OnDisable()
        {
            _pending.Clear();
        }

        /// <summary>ThrowInputController が SwingAccepted を配る前に呼ぶ。</summary>
        public void OnThrowAccepted(SwingAcceptedArgs e)
        {
            ThrowCount++;

            // 軌跡はこの直後に作られる弾へ効く（ThrowInputController → OnusaThrower の順に呼ばれる）
            if (thrower != null) thrower.VisualDelaySeconds = _variant.trailDelayMs / 1000f;

            Dispatch(FeedbackChannel.ThrowSe, _variant.throwSeDelayMs, e);
            Dispatch(FeedbackChannel.Haptic, _variant.hapticDelayMs, e);
        }

        void Dispatch(FeedbackChannel channel, float delayMs, SwingAcceptedArgs e)
        {
            if (delayMs <= 0f)
            {
                Fire(channel, e, Now);
                return;
            }
            _pending.Add(new Pending { Channel = channel, Due = Now + delayMs / 1000.0, Args = e });
        }

        void Update()
        {
            if (_pending.Count == 0) return;

            double now = Now;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].Due > now) continue;
                Pending p = _pending[i];
                _pending.RemoveAt(i);
                Fire(p.Channel, p.Args, now);
            }
        }

        void Fire(FeedbackChannel channel, SwingAcceptedArgs e, double now)
        {
            if (director != null)
            {
                if (channel == FeedbackChannel.ThrowSe) director.PlayThrowSe(e);
                else director.PlayThrowHaptic();
            }

            float offsetMs = (float)((now - e.Time) * 1000.0);
            _lastOffsetMs[(int)channel] = offsetMs;

            float error = Mathf.Abs(offsetMs - _variant.DelayMsOf(channel));
            if (error > _maxErrorMs[(int)channel]) _maxErrorMs[(int)channel] = error;

            if (logShifts)
                Debug.Log($"[TimingShifter] {channel} 設定 {_variant.DelayMsOf(channel):0} ms → 実測 {offsetMs:0} ms", this);
        }

        /// <summary>試技の切り替えで数え直す。</summary>
        public void ResetCounters()
        {
            ThrowCount = 0;
            _pending.Clear();
            for (int i = 0; i < _lastOffsetMs.Length; i++)
            {
                _lastOffsetMs[i] = 0f;
                _maxErrorMs[i] = 0f;
            }
        }
    }
}
