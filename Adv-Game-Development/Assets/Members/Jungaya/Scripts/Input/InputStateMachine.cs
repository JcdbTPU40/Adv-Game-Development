using System;
using System.Collections.Generic;

namespace Toufuku.GameInput
{
    /// <summary>有効スイングの種類。</summary>
    public enum ThrowKind
    {
        Normal, // 通常投擲
        Oharae  // 大祓（長押しチャージ）— T5 通過後に実装。現在は分岐のみ
    }

    /// <summary>
    /// 振りピークを有効スイングにしなかった理由。判定はこの順で行う（上ほど優先）。
    /// #60 の「クールダウン中は灰色＋低音」などのフィードバックはこの理由で出し分ける。
    /// </summary>
    public enum SwingRejectReason
    {
        Inactive,    // セッション外（リザルト中など）
        FrontHeld,   // 正面ボタン押下中（キャリブレーション操作中）
        NoSelection, // 色が一度も選ばれていない
        Cooldown     // クールダウン中
    }

    /// <summary>デバッグ表示用の現在フェーズ。</summary>
    public enum InputPhase
    {
        Ready,
        CoolingDown,
        FrontHolding,
        Charging
    }

    public readonly struct SwingAcceptedArgs
    {
        public readonly int ColorIndex;
        public readonly ThrowKind Kind;
        public readonly float Strength;
        public readonly double Time;

        public SwingAcceptedArgs(int colorIndex, ThrowKind kind, float strength, double time)
        {
            ColorIndex = colorIndex;
            Kind = kind;
            Strength = strength;
            Time = time;
        }
    }

    public readonly struct SwingRejectedArgs
    {
        public readonly SwingRejectReason Reason;
        public readonly float Strength;
        public readonly double Time;

        public SwingRejectedArgs(SwingRejectReason reason, float strength, double time)
        {
            Reason = reason;
            Strength = strength;
            Time = time;
        }
    }

    /// <summary>
    /// 入力状態機械 — Issue #51（v8 変更点3）
    ///
    /// 色ボタン押下／離す・正面ボタン押下／離す・振りピーク・時間経過の 4 種の入力だけで
    /// 通常投擲／キャンセル／多重押し／正面ボタン長押しキャリブレーション／大祓分岐を決める。
    /// MonoBehaviour に依存しない純粋な C# クラスにして、同じ入力列から必ず同じ結果になるようにしている。
    /// 状態遷移表は Docs/51_入力状態機械.md を正本とする。
    ///
    /// ・<see cref="SwingAccepted"/> が有効スイング確定の唯一の発火点（発射・投擲SE・#55 の対象ID確定はここに繋ぐ）。
    /// ・時刻は呼び出し側が秒で渡す。逆行した時刻は直前の時刻に丸める。
    /// </summary>
    public sealed class InputStateMachine
    {
        public const int ColorCount = 5;
        public const int NoColor = -1;

        // float の秒数（0.8f = 0.80000001…）と double の時刻を比べるときの許容誤差
        const double TimeEpsilon = 1e-6;

        /// <summary>有効スイング確定から次の有効スイングを受け付けるまでの秒数（T0-CD で決定）。</summary>
        public float CooldownSeconds { get; set; } = 0.50f;
        /// <summary>正面ボタンをこの秒数押し続けるとヨー角キャリブレーションを要求する。</summary>
        public float FrontHoldSeconds { get; set; } = 1.0f;
        /// <summary>大祓の分岐を有効にするか。false の間は長押ししても通常投擲になる。</summary>
        public bool OharaeEnabled { get; set; }
        /// <summary>色ボタンをこの秒数単独で押し続けるとチャージ状態になる（大祓）。</summary>
        public float ChargeSeconds { get; set; } = 0.8f;
        /// <summary>false の間は振りピークを <see cref="SwingRejectReason.Inactive"/> で却下する。キャリブレーションは受け付ける。</summary>
        public bool IsActive { get; set; } = true;

        /// <summary>選択中の色（0〜4、OmamoriType の並び）。未選択は <see cref="NoColor"/>。</summary>
        public int SelectedColor { get; private set; }
        public bool IsFrontHeld { get; private set; }
        public bool IsCharging { get; private set; }

        public event Action<int> SelectionChanged;
        public event Action<SwingAcceptedArgs> SwingAccepted;
        public event Action<SwingRejectedArgs> SwingRejected;
        public event Action YawCalibrationRequested;
        public event Action<int> ChargeStarted;
        public event Action<int> ChargeCancelled;

        readonly bool[] _held = new bool[ColorCount];
        readonly double[] _pressTime = new double[ColorCount];
        // 押されている色を押した順に保持する（末尾が最新）
        readonly List<int> _heldOrder = new List<int>(ColorCount);

        int _chargeCandidate = NoColor;
        double _frontPressTime;
        bool _calibrationFired;
        double _cooldownEndTime = double.NegativeInfinity;
        double _lastTime = double.NegativeInfinity;

        public InputStateMachine(int initialColor = 0)
        {
            SelectedColor = IsValidColor(initialColor) ? initialColor : NoColor;
        }

        public static bool IsValidColor(int index) => index >= 0 && index < ColorCount;

        public bool IsColorHeld(int index) => IsValidColor(index) && _held[index];

        public double CooldownRemaining(double now) => Math.Max(0.0, _cooldownEndTime - now);

        /// <summary>正面ボタン長押しの進み具合（0〜1）。押していなければ 0。</summary>
        public float FrontHoldProgress(double now)
        {
            if (!IsFrontHeld || FrontHoldSeconds <= 0f) return 0f;
            return (float)Math.Min(1.0, (now - _frontPressTime) / FrontHoldSeconds);
        }

        public InputPhase GetPhase(double now)
        {
            if (IsFrontHeld) return InputPhase.FrontHolding;
            if (IsCharging) return InputPhase.Charging;
            if (now < _cooldownEndTime) return InputPhase.CoolingDown;
            return InputPhase.Ready;
        }

        public void PressColor(int index, double time)
        {
            if (!IsValidColor(index) || _held[index]) return;
            time = Advance(time);

            // 別の色が押されたらチャージは取り消す（多重押しは大祓にしない）
            if (IsCharging) CancelCharge();

            _held[index] = true;
            _pressTime[index] = time;
            _heldOrder.Remove(index);
            _heldOrder.Add(index);

            // 単独押し かつ 正面ボタンを押していないときだけチャージ候補にする
            _chargeCandidate = (_heldOrder.Count == 1 && !IsFrontHeld) ? index : NoColor;

            // 多重押しの優先規則: 最後に押した色を採用する
            SetSelected(index);
        }

        public void ReleaseColor(int index, double time)
        {
            if (!IsValidColor(index) || !_held[index]) return;
            time = Advance(time);
            Tick(time);

            _held[index] = false;
            _heldOrder.Remove(index);

            if (_chargeCandidate == index)
            {
                // 振らずに離した＝チャージの取り消し（選択は保持）
                if (IsCharging) CancelCharge();
                _chargeCandidate = NoColor;
            }
        }

        public void PressFront(double time)
        {
            if (IsFrontHeld) return;
            time = Advance(time);

            IsFrontHeld = true;
            _frontPressTime = time;
            _calibrationFired = false;

            if (IsCharging) CancelCharge();
            _chargeCandidate = NoColor;
        }

        public void ReleaseFront(double time)
        {
            if (!IsFrontHeld) return;
            time = Advance(time);
            // Tick の間隔が粗くても、離した時点で 1 秒に達していれば発火させる
            Tick(time);
            IsFrontHeld = false;
        }

        /// <summary>振りのピークを検出した瞬間に呼ぶ。有効なら <see cref="SwingAccepted"/> を発火する。</summary>
        public void SwingPeak(float strength, double time)
        {
            time = Advance(time);
            Tick(time);

            if (!IsActive) { Reject(SwingRejectReason.Inactive, strength, time); return; }
            if (IsFrontHeld) { Reject(SwingRejectReason.FrontHeld, strength, time); return; }
            if (SelectedColor == NoColor) { Reject(SwingRejectReason.NoSelection, strength, time); return; }
            // 却下されたスイングはクールダウンを延長しない
            if (time < _cooldownEndTime - TimeEpsilon) { Reject(SwingRejectReason.Cooldown, strength, time); return; }

            ThrowKind kind = ThrowKind.Normal;
            if (IsCharging)
            {
                kind = ThrowKind.Oharae;
                IsCharging = false;
                _chargeCandidate = NoColor;
            }

            _cooldownEndTime = time + CooldownSeconds;
            SwingAccepted?.Invoke(new SwingAcceptedArgs(SelectedColor, kind, strength, time));
        }

        /// <summary>時間経過で起きる遷移（長押し 1 秒・チャージ開始）を進める。毎フレーム呼ぶ。</summary>
        public void Tick(double time)
        {
            time = Advance(time);

            if (IsFrontHeld && !_calibrationFired && time - _frontPressTime >= FrontHoldSeconds - TimeEpsilon)
            {
                _calibrationFired = true;
                YawCalibrationRequested?.Invoke();
            }

            if (OharaeEnabled && !IsCharging && _chargeCandidate != NoColor
                && _held[_chargeCandidate] && time - _pressTime[_chargeCandidate] >= ChargeSeconds - TimeEpsilon)
            {
                IsCharging = true;
                ChargeStarted?.Invoke(_chargeCandidate);
            }
            else if (!OharaeEnabled && IsCharging)
            {
                // 実行中に大祓を無効化したらチャージを畳む
                CancelCharge();
            }
        }

        /// <summary>選択を外部（UI 等）から合わせる。<see cref="SelectionChanged"/> も発火する。</summary>
        public void SetSelected(int index)
        {
            if (!IsValidColor(index) || SelectedColor == index) return;
            SelectedColor = index;
            SelectionChanged?.Invoke(index);
        }

        /// <summary>押下状態・クールダウン・チャージをすべて初期化する（シーン再開時など）。</summary>
        public void Reset(int initialColor = 0)
        {
            for (int i = 0; i < ColorCount; i++) _held[i] = false;
            _heldOrder.Clear();
            _chargeCandidate = NoColor;
            IsCharging = false;
            IsFrontHeld = false;
            _calibrationFired = false;
            _cooldownEndTime = double.NegativeInfinity;
            _lastTime = double.NegativeInfinity;
            SelectedColor = IsValidColor(initialColor) ? initialColor : NoColor;
        }

        void CancelCharge()
        {
            int color = _chargeCandidate;
            IsCharging = false;
            _chargeCandidate = NoColor;
            ChargeCancelled?.Invoke(color);
        }

        void Reject(SwingRejectReason reason, float strength, double time)
        {
            SwingRejected?.Invoke(new SwingRejectedArgs(reason, strength, time));
        }

        double Advance(double time)
        {
            if (time < _lastTime) time = _lastTime;
            _lastTime = time;
            return time;
        }
    }
}
