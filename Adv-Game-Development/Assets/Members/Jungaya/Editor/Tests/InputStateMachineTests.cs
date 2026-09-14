using System.Collections.Generic;
using NUnit.Framework;
using Toufuku.GameInput;

namespace Toufuku.GameInput.Tests
{
    /// <summary>
    /// #51 入力状態機械の遷移表（Docs/51_入力状態機械.md）を 1 行ずつ確かめる EditMode テスト。
    /// </summary>
    public class InputStateMachineTests
    {
        InputStateMachine _m;
        List<SwingAcceptedArgs> _accepted;
        List<SwingRejectedArgs> _rejected;
        int _calibrations;
        List<int> _chargeStarted;
        List<int> _chargeCancelled;

        [SetUp]
        public void SetUp()
        {
            _m = new InputStateMachine(0) { CooldownSeconds = 0.5f, FrontHoldSeconds = 1.0f, ChargeSeconds = 0.8f };
            _accepted = new List<SwingAcceptedArgs>();
            _rejected = new List<SwingRejectedArgs>();
            _chargeStarted = new List<int>();
            _chargeCancelled = new List<int>();
            _calibrations = 0;
            _m.SwingAccepted += e => _accepted.Add(e);
            _m.SwingRejected += e => _rejected.Add(e);
            _m.YawCalibrationRequested += () => _calibrations++;
            _m.ChargeStarted += c => _chargeStarted.Add(c);
            _m.ChargeCancelled += c => _chargeCancelled.Add(c);
        }

        // ---- 通常投擲 ----

        [Test]
        public void 色を押して離してから振ると選択色で通常投擲になる()
        {
            _m.PressColor(2, 0.0);
            _m.ReleaseColor(2, 0.1);
            _m.SwingPeak(300f, 0.5);

            Assert.AreEqual(1, _accepted.Count);
            Assert.AreEqual(2, _accepted[0].ColorIndex);
            Assert.AreEqual(ThrowKind.Normal, _accepted[0].Kind);
            Assert.AreEqual(300f, _accepted[0].Strength);
        }

        [Test]
        public void 初期選択色のまま振れば投擲できる()
        {
            _m.SwingPeak(1f, 0.0);
            Assert.AreEqual(1, _accepted.Count);
            Assert.AreEqual(0, _accepted[0].ColorIndex);
        }

        [Test]
        public void 未選択で開始した場合は振っても却下される()
        {
            var m = new InputStateMachine(InputStateMachine.NoColor);
            var rejected = new List<SwingRejectedArgs>();
            m.SwingRejected += e => rejected.Add(e);

            m.SwingPeak(1f, 0.0);

            Assert.AreEqual(1, rejected.Count);
            Assert.AreEqual(SwingRejectReason.NoSelection, rejected[0].Reason);
        }

        [Test]
        public void 色ボタンを押している間に振っても通常投擲になる_大祓無効時()
        {
            _m.PressColor(1, 0.0);
            _m.Tick(2.0);
            _m.SwingPeak(1f, 2.0);

            Assert.AreEqual(1, _accepted.Count);
            Assert.AreEqual(1, _accepted[0].ColorIndex);
            Assert.AreEqual(ThrowKind.Normal, _accepted[0].Kind);
            Assert.AreEqual(0, _chargeStarted.Count);
        }

        // ---- クールダウン ----

        [Test]
        public void クールダウン中の振りは却下され_終了時刻ちょうどから受け付ける()
        {
            _m.SwingPeak(1f, 1.0);
            _m.SwingPeak(1f, 1.3);
            _m.SwingPeak(1f, 1.5);

            Assert.AreEqual(2, _accepted.Count);
            Assert.AreEqual(1, _rejected.Count);
            Assert.AreEqual(SwingRejectReason.Cooldown, _rejected[0].Reason);
            Assert.AreEqual(1.5, _accepted[1].Time, 1e-9);
        }

        [Test]
        public void 却下された振りはクールダウンを延長しない()
        {
            _m.SwingPeak(1f, 0.0);
            _m.SwingPeak(1f, 0.49);
            _m.SwingPeak(1f, 0.5);

            Assert.AreEqual(2, _accepted.Count);
        }

        [TestCase(0.40f, 0.45, true)]
        [TestCase(0.50f, 0.45, false)]
        [TestCase(0.60f, 0.55, false)]
        [TestCase(0.65f, 0.65, true)]
        public void クールダウン値を差し替えると受付時刻が変わる(float cooldown, double secondSwing, bool expectAccepted)
        {
            _m.CooldownSeconds = cooldown;
            _m.SwingPeak(1f, 0.0);
            _m.SwingPeak(1f, secondSwing);

            Assert.AreEqual(expectAccepted ? 2 : 1, _accepted.Count);
        }

        [Test]
        public void 同時刻に2回ピークが来ても1発しか出ない()
        {
            _m.SwingPeak(1f, 3.0);
            _m.SwingPeak(1f, 3.0);

            Assert.AreEqual(1, _accepted.Count);
            Assert.AreEqual(SwingRejectReason.Cooldown, _rejected[0].Reason);
        }

        // ---- 多重押し ----

        [Test]
        public void 多重押しは最後に押した色を採用する()
        {
            _m.PressColor(0, 0.0);
            _m.PressColor(3, 0.1);
            _m.SwingPeak(1f, 0.2);

            Assert.AreEqual(3, _accepted[0].ColorIndex);
        }

        [Test]
        public void 後から押した色を先に離しても選択は変わらない()
        {
            _m.PressColor(1, 0.0);
            _m.PressColor(4, 0.1);
            _m.ReleaseColor(4, 0.2);
            _m.SwingPeak(1f, 0.3);

            Assert.AreEqual(4, _accepted[0].ColorIndex);
        }

        [Test]
        public void 押している色をもう一度押し直すとその色が最新になる()
        {
            _m.PressColor(1, 0.0);
            _m.PressColor(2, 0.1);
            _m.ReleaseColor(1, 0.2);
            _m.PressColor(1, 0.3);

            Assert.AreEqual(1, _m.SelectedColor);
        }

        [Test]
        public void 選択が変わったときだけSelectionChangedが発火する()
        {
            var changes = new List<int>();
            _m.SelectionChanged += c => changes.Add(c);

            _m.PressColor(0, 0.0); // 初期値 0 と同じ
            _m.ReleaseColor(0, 0.1);
            _m.PressColor(2, 0.2);
            _m.PressColor(2, 0.3); // 押しっぱなしの再押下は無視

            CollectionAssert.AreEqual(new[] { 2 }, changes);
        }

        [Test]
        public void 範囲外の色番号は無視する()
        {
            _m.PressColor(-1, 0.0);
            _m.PressColor(5, 0.0);
            Assert.AreEqual(0, _m.SelectedColor);
        }

        // ---- 正面ボタン（キャンセル／キャリブレーション）----

        [Test]
        public void 正面ボタンを押している間の振りは却下される()
        {
            _m.PressFront(0.0);
            _m.SwingPeak(1f, 0.2);
            _m.ReleaseFront(0.3);
            _m.SwingPeak(1f, 0.4);

            Assert.AreEqual(SwingRejectReason.FrontHeld, _rejected[0].Reason);
            Assert.AreEqual(1, _accepted.Count);
        }

        [Test]
        public void 正面ボタン1秒長押しでキャリブレーションが1回だけ発火する()
        {
            _m.PressFront(0.0);
            _m.Tick(0.99);
            Assert.AreEqual(0, _calibrations);

            _m.Tick(1.0);
            _m.Tick(1.5);
            _m.Tick(3.0);
            _m.ReleaseFront(3.1);

            Assert.AreEqual(1, _calibrations);
        }

        [Test]
        public void 正面ボタンを1秒未満で離すとキャリブレーションしない()
        {
            _m.PressFront(0.0);
            _m.ReleaseFront(0.9);
            _m.Tick(2.0);

            Assert.AreEqual(0, _calibrations);
        }

        [Test]
        public void Tickが来なくても離した時点で1秒に達していればキャリブレーションする()
        {
            _m.PressFront(0.0);
            _m.ReleaseFront(1.0);

            Assert.AreEqual(1, _calibrations);
        }

        [Test]
        public void 長押しを繰り返すたびにキャリブレーションする()
        {
            _m.PressFront(0.0);
            _m.Tick(1.0);
            _m.ReleaseFront(1.1);
            _m.PressFront(2.0);
            _m.Tick(3.0);

            Assert.AreEqual(2, _calibrations);
        }

        [Test]
        public void 正面ボタン押下中の色ボタンは選択を変えるがキャリブレーションは継続する()
        {
            _m.PressFront(0.0);
            _m.PressColor(3, 0.5);
            _m.Tick(1.0);

            Assert.AreEqual(3, _m.SelectedColor);
            Assert.AreEqual(1, _calibrations);
        }

        [Test]
        public void 正面ボタンの却下はクールダウン判定より優先する()
        {
            _m.SwingPeak(1f, 0.0);
            _m.PressFront(0.1);
            _m.SwingPeak(1f, 0.2);

            Assert.AreEqual(SwingRejectReason.FrontHeld, _rejected[0].Reason);
        }

        // ---- セッション外 ----

        [Test]
        public void 非アクティブ中は振りを却下するがキャリブレーションは受け付ける()
        {
            _m.IsActive = false;
            _m.SwingPeak(1f, 0.0);
            _m.PressFront(0.1);
            _m.Tick(1.1);

            Assert.AreEqual(SwingRejectReason.Inactive, _rejected[0].Reason);
            Assert.AreEqual(0, _accepted.Count);
            Assert.AreEqual(1, _calibrations);
        }

        [Test]
        public void 非アクティブ中の却下はクールダウンを消費しない()
        {
            _m.IsActive = false;
            _m.SwingPeak(1f, 0.0);
            _m.IsActive = true;
            _m.SwingPeak(1f, 0.1);

            Assert.AreEqual(1, _accepted.Count);
        }

        // ---- 大祓（分岐のみ）----

        [Test]
        public void 大祓有効時_単独長押しでチャージし振ると大祓になる()
        {
            _m.OharaeEnabled = true;
            _m.PressColor(2, 0.0);
            _m.Tick(0.79);
            Assert.IsFalse(_m.IsCharging);
            _m.Tick(0.8);
            Assert.IsTrue(_m.IsCharging);

            _m.SwingPeak(1f, 1.0);

            CollectionAssert.AreEqual(new[] { 2 }, _chargeStarted);
            Assert.AreEqual(ThrowKind.Oharae, _accepted[0].Kind);
            Assert.IsFalse(_m.IsCharging);
        }

        [Test]
        public void 大祓有効時_チャージ前に振ると通常投擲()
        {
            _m.OharaeEnabled = true;
            _m.PressColor(2, 0.0);
            _m.SwingPeak(1f, 0.5);

            Assert.AreEqual(ThrowKind.Normal, _accepted[0].Kind);
        }

        [Test]
        public void 大祓有効時_振らずに離すとチャージは取り消される()
        {
            _m.OharaeEnabled = true;
            _m.PressColor(2, 0.0);
            _m.Tick(1.0);
            _m.ReleaseColor(2, 1.2);
            _m.SwingPeak(1f, 1.3);

            CollectionAssert.AreEqual(new[] { 2 }, _chargeCancelled);
            Assert.AreEqual(ThrowKind.Normal, _accepted[0].Kind);
        }

        [Test]
        public void 大祓有効時_チャージ中に別の色を押すと取り消して選択を切り替える()
        {
            _m.OharaeEnabled = true;
            _m.PressColor(2, 0.0);
            _m.Tick(1.0);
            _m.PressColor(4, 1.1);
            _m.Tick(3.0);
            _m.SwingPeak(1f, 3.0);

            CollectionAssert.AreEqual(new[] { 2 }, _chargeCancelled);
            Assert.AreEqual(1, _chargeStarted.Count, "多重押し中は再チャージしない");
            Assert.AreEqual(4, _accepted[0].ColorIndex);
            Assert.AreEqual(ThrowKind.Normal, _accepted[0].Kind);
        }

        [Test]
        public void 大祓有効時_正面ボタンでチャージは取り消される()
        {
            _m.OharaeEnabled = true;
            _m.PressColor(1, 0.0);
            _m.Tick(1.0);
            _m.PressFront(1.1);

            Assert.IsFalse(_m.IsCharging);
            CollectionAssert.AreEqual(new[] { 1 }, _chargeCancelled);
        }

        [Test]
        public void 大祓有効時_正面ボタンを押しながら色を押してもチャージしない()
        {
            _m.OharaeEnabled = true;
            _m.PressFront(0.0);
            _m.PressColor(1, 0.1);
            _m.ReleaseFront(0.2);
            _m.Tick(2.0);

            Assert.IsFalse(_m.IsCharging);
        }

        // ---- 時刻 ----

        [Test]
        public void 逆行した時刻は直前の時刻として扱う()
        {
            _m.SwingPeak(1f, 1.0);
            _m.SwingPeak(1f, 0.2);

            Assert.AreEqual(1, _accepted.Count);
            Assert.AreEqual(SwingRejectReason.Cooldown, _rejected[0].Reason);
            Assert.AreEqual(1.0, _rejected[0].Time, 1e-9);
        }

        [Test]
        public void フェーズは正面_チャージ_クールダウン_待機の優先で返る()
        {
            Assert.AreEqual(InputPhase.Ready, _m.GetPhase(0.0));
            _m.SwingPeak(1f, 0.0);
            Assert.AreEqual(InputPhase.CoolingDown, _m.GetPhase(0.1));
            _m.PressFront(0.2);
            Assert.AreEqual(InputPhase.FrontHolding, _m.GetPhase(0.2));
            Assert.AreEqual(0.5f, _m.FrontHoldProgress(0.7), 1e-4);
        }
    }
}
