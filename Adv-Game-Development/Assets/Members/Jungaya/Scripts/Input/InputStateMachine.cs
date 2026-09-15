using System;
using System.Collections.Generic;

namespace Toufuku.GameInput
{
    // 有効スイングの種類
    public enum ThrowKind
    {
        Normal, // ふつうに投げる
        Oharae  // 大祓（長押ししてためる）。T5 を通ったあとに作る。今は分かれ道だけ
    }

    /*
        振りピークを有効スイングにしなかった理由。この順番で判定する（上ほど優先）
        #60 の「クールダウン中は灰色＋低い音」みたいなフィードバックは、この理由を見て出し分ける
    */
    public enum SwingRejectReason
    {
        Inactive,    // ゲームの時間外（リザルト中など）
        FrontHeld,   // 正面ボタンを押している（キャリブレーション中）
        NoSelection, // 色がまだ一回も選ばれていない
        Cooldown     // クールダウン中
    }

    // デバッグ表示用の、今のフェーズ
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

    /*
        入力の状態機械（#51 / v8 の変更点3）

        色ボタンを押す・はなす、正面ボタンを押す・はなす、振りピーク、時間がたつ、の4種類の入力だけで、
        ふつうに投げる／キャンセル／同時押し／正面ボタン長押しのキャリブレーション／大祓の分かれ道を決める
        MonoBehaviour を使わないただの C# のクラスにして、同じ入力なら必ず同じ結果になるようにしている
        状態がどう変わるかの表は Docs/51_入力状態機械.md が正しいものとする

        ・SwingAccepted が、有効スイングが決まるただ1つの場所（発射・投げる音・#55 の相手ID決めはここにつなぐ）
        ・時刻は呼ぶ側が秒で渡す。時刻が前にもどっていたら、直前の時刻に合わせる
    */
    public sealed class InputStateMachine
    {
        public const int ColorCount = 5;
        public const int NoColor = -1;

        // float の秒（0.8f = 0.80000001…）と double の時刻をくらべるときに、これくらいのズレは許す
        const double TimeEpsilon = 1e-6;

        // 有効スイングが決まってから、次の有効スイングを受け付けるまでの秒数（T0-CD で決める）
        public float CooldownSeconds { get; set; } = 0.50f;
        // 正面ボタンをこの秒数押しつづけると、ヨー角のキャリブレーションをお願いする
        public float FrontHoldSeconds { get; set; } = 1.0f;
        // 大祓の分かれ道を使うかどうか。false の間は長押ししてもふつうに投げる
        public bool OharaeEnabled { get; set; }
        // 色ボタンをこの秒数1つだけ押しつづけると、ためる状態になる（大祓）
        public float ChargeSeconds { get; set; } = 0.8f;
        // false の間は、振りピークを SwingRejectReason.Inactive ではじく。キャリブレーションは受け付ける
        public bool IsActive { get; set; } = true;

        // 今選んでいる色（0〜4、OmamoriType の順番）。選んでいないときは NoColor
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
        // 押されている色を押した順番に覚えておく（最後がいちばん新しい）
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

        // 正面ボタン長押しの進み具合（0〜1）。押していなければ 0
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

            // 別の色が押されたら、ためるのはやめる（同時押しは大祓にしない）
            if (IsCharging) CancelCharge();

            _held[index] = true;
            _pressTime[index] = time;
            _heldOrder.Remove(index);
            _heldOrder.Add(index);

            // 1つだけ押していて、しかも正面ボタンを押していないときだけ、ためる候補にする
            _chargeCandidate = (_heldOrder.Count == 1 && !IsFrontHeld) ? index : NoColor;

            // 同時押しのルール: 最後に押した色を使う
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
                // 振らずにはなした＝ためるのをキャンセル（選んだ色はそのまま）
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
            // Tick を呼ぶ間かくがあらくても、はなした時点で1秒たっていれば発動させる
            Tick(time);
            IsFrontHeld = false;
        }

        // 振りのピークを見つけた瞬間に呼ぶ。有効なら SwingAccepted を呼ぶ
        public void SwingPeak(float strength, double time)
        {
            time = Advance(time);
            Tick(time);

            if (!IsActive) { Reject(SwingRejectReason.Inactive, strength, time); return; }
            if (IsFrontHeld) { Reject(SwingRejectReason.FrontHeld, strength, time); return; }
            if (SelectedColor == NoColor) { Reject(SwingRejectReason.NoSelection, strength, time); return; }
            // はじかれた振りでは、クールダウンをのばさない
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

        // 時間がたつと起きること（長押し1秒、ため開始）を進める。毎フレーム呼ぶ
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
                // プレイ中に大祓を無効にしたら、ためているのをやめる
                CancelCharge();
            }
        }

        // 選んでいる色を外（UI など）から合わせる。SelectionChanged も呼ぶ
        public void SetSelected(int index)
        {
            if (!IsValidColor(index) || SelectedColor == index) return;
            SelectedColor = index;
            SelectionChanged?.Invoke(index);
        }

        // 押している状態・クールダウン・ためをぜんぶ最初にもどす（シーンをやりなおすときなど）
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
