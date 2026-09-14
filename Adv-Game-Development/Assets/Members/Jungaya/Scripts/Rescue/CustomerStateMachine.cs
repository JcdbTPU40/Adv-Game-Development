using System;

namespace Toufuku.Rescue
{
    /// <summary>参拝客の状態（企画書 v8 6章「参拝客の一意な状態遷移」）。</summary>
    public enum CustomerPhase
    {
        /// <summary>予告中：参道を歩いて入場中。輪郭色は見えるが、当たり判定・二重円・危険度進行はまだない。</summary>
        Entering,
        /// <summary>active（救済待ち）：定位置に到着し、Dが時間で増えている。</summary>
        Active,
        /// <summary>rescued（救済成功）：R=0 に到達した終端状態。activeへは戻らない。</summary>
        Rescued,
        /// <summary>black（黒客化）：D=100 に到達した終端状態。救済へ戻らない。</summary>
        Black
    }

    /// <summary>正色命中を処理した結果。</summary>
    public enum CorrectHitResult
    {
        /// <summary>active でないので何も起きなかった（入場中・救済済み・黒客）。</summary>
        Ignored,
        /// <summary>R が1減ったが、まだ 0 ではない（複数発客の途中）。得点は入らない。</summary>
        Progressed,
        /// <summary>R が 0 になり救済完了。</summary>
        Rescued
    }

    /// <summary>
    /// 危険度 D と残り必要発数 R の状態遷移（企画書 v8 6章・付録B）— Issue #54
    ///
    /// D（0〜100）＝残り時間、R（1〜3）＝救済までの手数。<b>同じゲージとして増減させない</b>（v8変更点1）。
    ///
    /// | 出来事 | D | R | 次状態 |
    /// |---|---|---|---|
    /// | 入場中 | 0で停止 | 客種ごとの初期値 | 予告中(Entering) |
    /// | 定位置へ到着 | 0から開始 | 維持 | active |
    /// | active中の時間経過 | D += 100 × 経過秒 / 満タン秒数 | 変化なし | active（Dは0〜100に制限） |
    /// | 正色が命中 | 変化なし | R を1減らす | R&gt;0ならactive継続、R=0なら rescued |
    /// | 誤色が命中 | 変化なし | 変化なし | active継続（C と G のリセットのみ） |
    /// | 笑顔が伝播 | max(0, D−5) | 変化なし | active継続 |
    /// | D が100に到達 | 100 | 以後使用しない | black（救済へ戻らない） |
    ///
    /// 同時刻の解決順（v8 6章）:
    ///   正色着弾と有効な笑顔伝播を先に解決 → R と D を更新 → R=0 の救済判定 → なおactiveで D≥100 なら黒客化。
    ///   この型では <see cref="TickDanger"/> が D を進めるだけで黒客化せず、
    ///   フレーム内の命中・伝播を解決したあとに <see cref="ResolveBlackout"/> を呼ぶことでこの順序を保証する
    ///   （MonoBehaviour 側は Update で TickDanger、LateUpdate で ResolveBlackout を呼ぶ）。
    ///
    /// MonoBehaviour 非依存。遷移の全ケースはエディタテストで検証する（CustomerStateMachineTests）。
    /// </summary>
    public class CustomerStateMachine
    {
        /// <summary>危険度 D の上限。到達で黒客化（付録B STATE.DANGER）。</summary>
        public const float MaxDanger = 100f;
        /// <summary>笑顔の伝播1回で減る危険度（企画書 v8 6章）。</summary>
        public const float SmileDangerRelief = 5f;

        int _remaining;
        float _danger;
        float _dangerFullSeconds;
        CustomerPhase _phase = CustomerPhase.Entering;

        /// <summary>状態が変わった（引数: 変化後の状態）。</summary>
        public event Action<CustomerPhase> PhaseChanged;
        /// <summary>D が変わった（引数: 変化後の D。0〜100）。</summary>
        public event Action<float> DangerChanged;
        /// <summary>R が変わった（引数: 変化後の R）。</summary>
        public event Action<int> RemainingChanged;

        /// <param name="initialRemaining">初期R（客種ごと。付録B B-1）。1未満は1に切り上げる。</param>
        /// <param name="dangerFullSeconds">D が 0→100 になるまでの秒数（客種ごと。付録B B-1）。</param>
        /// <param name="startActive">true なら最初から active（定位置に置かれた客）。false なら入場中から始める。</param>
        public CustomerStateMachine(int initialRemaining, float dangerFullSeconds, bool startActive = false)
        {
            _remaining = initialRemaining < 1 ? 1 : initialRemaining;
            _dangerFullSeconds = dangerFullSeconds > 0.0001f ? dangerFullSeconds : 0.0001f;
            _danger = 0f;
            _phase = startActive ? CustomerPhase.Active : CustomerPhase.Entering;
        }

        /// <summary>現在の状態。</summary>
        public CustomerPhase Phase => _phase;
        /// <summary>危険度 D（0〜100）。</summary>
        public float Danger => _danger;
        /// <summary>危険度 D の 0〜1 正規化（HUD の足元円用）。</summary>
        public float DangerNormalized => _danger / MaxDanger;
        /// <summary>残り必要発数 R。黒客化後は以後使用しない（値は最後の R のまま残す）。</summary>
        public int Remaining => _remaining;
        /// <summary>D が 0→100 になるまでの秒数。</summary>
        public float DangerFullSeconds => _dangerFullSeconds;

        /// <summary>救済待ち（時間が進み、弾を受け付ける）。</summary>
        public bool IsActive => _phase == CustomerPhase.Active;
        /// <summary>救済成功。</summary>
        public bool IsRescued => _phase == CustomerPhase.Rescued;
        /// <summary>黒客化（救済失敗）。</summary>
        public bool IsBlack => _phase == CustomerPhase.Black;
        /// <summary>終端状態（rescued / black）。どちらからも active へは戻らない。</summary>
        public bool IsFinished => _phase == CustomerPhase.Rescued || _phase == CustomerPhase.Black;

        /// <summary>
        /// 笑顔の伝播・優先救済（二重円）の対象になれるか。
        /// 企画書 v8 6章／7章：「active かつ 未救済・非黒客・R&gt;0」だけ。入場中・退場中の救済客・黒客は対象外。
        /// 黒客を含めると「黒客が多いほど縁が増える」抜け道になるため、ここで必ず外す。
        /// </summary>
        public bool IsRescueTarget => _phase == CustomerPhase.Active && _remaining > 0;

        /// <summary>定位置へ到着。入場中 → active。D は 0 から進み始める。</summary>
        public bool Arrive()
        {
            if (_phase != CustomerPhase.Entering) return false;
            SetPhase(CustomerPhase.Active);
            return true;
        }

        /// <summary>
        /// active 中の時間経過。D を進めるだけで黒客化はしない（同時刻の命中・伝播を先に解決するため）。
        /// 黒客化の判定はフレーム末に <see cref="ResolveBlackout"/> で行う。
        /// </summary>
        public void TickDanger(float deltaSeconds)
        {
            if (_phase != CustomerPhase.Active || deltaSeconds <= 0f) return;
            SetDanger(_danger + MaxDanger * deltaSeconds / _dangerFullSeconds);
        }

        /// <summary>
        /// D≥100 の黒客化をこの時点で確定する。フレーム内の命中・伝播をすべて解決したあとに呼ぶ。
        /// </summary>
        /// <returns>この呼び出しで黒客化したら true。</returns>
        public bool ResolveBlackout()
        {
            if (_phase != CustomerPhase.Active || _danger < MaxDanger) return false;
            SetDanger(MaxDanger);
            SetPhase(CustomerPhase.Black);
            return true;
        }

        /// <summary>
        /// 現在要求中の正しい色が命中した。R を1減らし、<b>D は変えない</b>。R=0 で救済完了。
        /// </summary>
        public CorrectHitResult HitCorrectColor()
        {
            if (_phase != CustomerPhase.Active) return CorrectHitResult.Ignored;

            SetRemaining(_remaining - 1);
            if (_remaining <= 0)
            {
                SetPhase(CustomerPhase.Rescued);
                return CorrectHitResult.Rescued;
            }
            return CorrectHitResult.Progressed;
        }

        /// <summary>
        /// 違う色が命中した。<b>D も R も変えない</b>（v8変更点2）。active のまま継続する。
        /// 福の連なり C とご加護進捗 G のリセットは呼び出し側（スコア側）の担当。
        /// 誤色で D を下げないので、無限弾の誤色連打で黒客化を遅らせる抜け道は成立しない。
        /// </summary>
        /// <returns>active で受け付けたら true（渋るリアクション等を出す合図）。</returns>
        public bool HitWrongColor()
        {
            return _phase == CustomerPhase.Active;
        }

        /// <summary>
        /// 笑顔の伝播を受けた。D を 5 減らす（0 未満にはしない）。R は変えない。
        /// 対象条件（active・未救済・非黒客・R&gt;0）を満たさなければ何もしない。
        /// </summary>
        /// <returns>実際に伝播が成立したら true（縁+20 を数えてよい）。</returns>
        public bool ReceiveSmile()
        {
            if (!IsRescueTarget) return false;
            SetDanger(_danger - SmileDangerRelief);
            return true;
        }

        /// <summary>
        /// D を直接設定する（モック・デバッグ・検証シーン用。正規のゲーム進行では使わない）。
        /// 黒客化はここでは確定せず、<see cref="ResolveBlackout"/> に委ねる。
        /// </summary>
        public void SetDangerForDebug(float danger)
        {
            if (IsFinished) return;
            SetDanger(danger);
        }

        void SetDanger(float value)
        {
            float clamped = value < 0f ? 0f : (value > MaxDanger ? MaxDanger : value);
            if (Math.Abs(clamped - _danger) < 0.0001f) return;
            _danger = clamped;
            DangerChanged?.Invoke(_danger);
        }

        void SetRemaining(int value)
        {
            int next = value < 0 ? 0 : value;
            if (next == _remaining) return;
            _remaining = next;
            RemainingChanged?.Invoke(_remaining);
        }

        void SetPhase(CustomerPhase next)
        {
            if (_phase == next) return;
            _phase = next;
            PhaseChanged?.Invoke(_phase);
        }
    }
}
