using System;

namespace Toufuku.Rescue
{
    // 客の状態（企画書 v8 6章「参拝客の一意な状態遷移」）
    public enum CustomerPhase
    {
        // 予告中: 参道を歩いて入ってくる途中。輪郭の色は見えるけど、当たり判定・二重円・危険度が進むのはまだない
        Entering,
        // active（救われるのを待っている）: 定位置に着いて、D が時間で増えている
        Active,
        // rescued（救えた）: R=0 になった終わりの状態。active にはもどらない
        Rescued,
        // black（黒客になった）: D=100 になった終わりの状態。救える状態にはもどらない
        Black
    }

    // 正しい色が当たったのを処理した結果
    public enum CorrectHitResult
    {
        // active じゃないので何も起きなかった（入ってくる途中・救われたあと・黒客）
        Ignored,
        // R が1減ったけど、まだ 0 じゃない（何発も必要な客のとちゅう）。得点は入らない
        Progressed,
        // R が 0 になって救えた
        Rescued
    }

    /*
        危険度 D と残りの必要な発数 R の状態の変わり方（企画書 v8 6章・付録B）（#54）

        D（0〜100）＝残り時間、R（1〜3）＝救うまでの手数。1本のゲージとして増やしたり減らしたりしない（v8 の変更点1）

        できごとごとの変わり方:
        ・入ってくる途中: D は 0 で止まる / R は種類ごとの最初の値 / 状態は予告中（Entering）
        ・定位置に着いた: D は 0 からスタート / R はそのまま / 状態は active
        ・active の間に時間がたつ: D += 100 × たった秒 / 満タンの秒数 / R は変わらない / active のまま（D は 0〜100 におさめる）
        ・正しい色が当たった: D は変わらない / R を1減らす / R>0 なら active のまま、R=0 なら rescued
        ・まちがった色が当たった: D も R も変わらない / active のまま（C と G のリセットだけ）
        ・笑顔が伝わってきた: D = max(0, D−5) / R は変わらない / active のまま
        ・D が100になった: D は 100 / R はもう使わない / black（救える状態にはもどらない）

        同じ時刻に起きたときの順番（v8 6章）:
          正しい色の着弾と有効な笑顔の伝わりを先に処理 → R と D を更新 → R=0 なら救えた → それでも active で D が 100 以上なら黒客
          このクラスでは TickDanger は D を進めるだけで黒客にはしなくて、
          フレームの中の当たりと伝わりを処理したあとに ResolveBlackout を呼ぶことで、この順番を守っている
          （MonoBehaviour 側は Update で TickDanger、LateUpdate で ResolveBlackout を呼ぶ）

        MonoBehaviour は使っていない。状態の変わり方はぜんぶエディタのテストで確かめる（CustomerStateMachineTests）
    */
    public class CustomerStateMachine
    {
        // 危険度 D の上限。ここに届いたら黒客（付録B STATE.DANGER）
        public const float MaxDanger = 100f;
        // 笑顔が1回伝わったときに減る危険度（企画書 v8 6章）
        public const float SmileDangerRelief = 5f;

        int _remaining;
        float _danger;
        float _dangerFullSeconds;
        CustomerPhase _phase = CustomerPhase.Entering;

        // 状態が変わった（引数: 変わったあとの状態）
        public event Action<CustomerPhase> PhaseChanged;
        // D が変わった（引数: 変わったあとの D。0〜100）
        public event Action<float> DangerChanged;
        // R が変わった（引数: 変わったあとの R）
        public event Action<int> RemainingChanged;

        /*
            initialRemaining: 最初のR（種類ごと。付録B B-1）。1より小さかったら1にする
            dangerFullSeconds: D が 0 から 100 になるまでの秒数（種類ごと。付録B B-1）
            startActive: true なら最初から active（定位置に置かれた客）。false なら入ってくる途中から始める
        */
        public CustomerStateMachine(int initialRemaining, float dangerFullSeconds, bool startActive = false)
        {
            _remaining = initialRemaining < 1 ? 1 : initialRemaining;
            _dangerFullSeconds = dangerFullSeconds > 0.0001f ? dangerFullSeconds : 0.0001f;
            _danger = 0f;
            _phase = startActive ? CustomerPhase.Active : CustomerPhase.Entering;
        }

        // 今の状態
        public CustomerPhase Phase => _phase;
        // 危険度 D（0〜100）
        public float Danger => _danger;
        // 危険度 D を 0〜1 に直したもの（HUD の足元の円用）
        public float DangerNormalized => _danger / MaxDanger;
        // 残りの必要な発数 R。黒客になったあとはもう使わない（値は最後の R のまま残しておく）
        public int Remaining => _remaining;
        // D が 0 から 100 になるまでの秒数
        public float DangerFullSeconds => _dangerFullSeconds;

        /*
            #58: D をスクリプトで決めた値に固定しているか（段階学習 0:00〜0:30）
            固定している間は、時間・笑顔の伝わり・デバッグ用の設定のどれでも D が動かない
        */
        public bool IsDangerLocked { get; private set; }

        // 救われるのを待っている（時間が進んで、弾を受け付ける）
        public bool IsActive => _phase == CustomerPhase.Active;
        // 救えた
        public bool IsRescued => _phase == CustomerPhase.Rescued;
        // 黒客になった（救えなかった）
        public bool IsBlack => _phase == CustomerPhase.Black;
        // 終わりの状態（rescued / black）。どっちからも active にはもどらない
        public bool IsFinished => _phase == CustomerPhase.Rescued || _phase == CustomerPhase.Black;

        /*
            笑顔の伝わりや優先救済（二重円）の対象になれるか
            企画書 v8 6章・7章: 「active で、まだ救われていない・黒客じゃない・R>0」の客だけ。入ってくる途中や、帰っている途中の救われた客や、黒客は対象外
            黒客を入れると「黒客が多いほど縁が増える」ずるができてしまうので、ここで必ず外す
        */
        public bool IsRescueTarget => _phase == CustomerPhase.Active && _remaining > 0;

        // 定位置に着いた。入ってくる途中 → active。D は 0 から進み始める
        public bool Arrive()
        {
            if (_phase != CustomerPhase.Entering) return false;
            SetPhase(CustomerPhase.Active);
            return true;
        }

        /*
            active の間に時間がたった。D を進めるだけで黒客にはしない（同じ時刻の当たりと伝わりを先に処理したいから）
            黒客になるかの判定は、フレームの最後に ResolveBlackout でやる
        */
        public void TickDanger(float deltaSeconds)
        {
            if (_phase != CustomerPhase.Active || deltaSeconds <= 0f || IsDangerLocked) return;
            SetDanger(_danger + MaxDanger * deltaSeconds / _dangerFullSeconds);
        }

        /*
            D が 100 以上なら、この時点で黒客に決める。フレームの中の当たりと伝わりをぜんぶ処理したあとに呼ぶ
            返す値: この呼び出しで黒客になったら true
        */
        public bool ResolveBlackout()
        {
            if (_phase != CustomerPhase.Active || _danger < MaxDanger) return false;
            SetDanger(MaxDanger);
            SetPhase(CustomerPhase.Black);
            return true;
        }

        // 今ほしがっている正しい色が当たった。R を1減らして、D は変えない。R=0 で救えた
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

        /*
            ちがう色が当たった。D も R も変えない（v8 の変更点2）。active のまま続ける
            福の連なり C とご加護の進み G のリセットは呼ぶ側（スコアのほう）の担当
            まちがった色で D を下げないので、弾が無限なのを使ってまちがった色を連打して黒客になるのを遅らせる、というずるはできない
            返す値: active で受け付けたら true（渋るリアクションなどを出す合図）
        */
        public bool HitWrongColor()
        {
            return _phase == CustomerPhase.Active;
        }

        /*
            笑顔が伝わってきた。D を 5 減らす（0 より小さくはしない）。R は変えない
            対象の条件（active・まだ救われていない・黒客じゃない・R>0）を満たさなければ何もしない
            返す値: 本当に伝わったら true（縁+20 を数えていい）
        */
        public bool ReceiveSmile()
        {
            if (!IsRescueTarget) return false;
            // #58: スクリプトで固定している D は動かさない（伝わったこと自体は数えてよい）
            if (!IsDangerLocked) SetDanger(_danger - SmileDangerRelief);
            return true;
        }

        /*
            D を直接決める（モック・デバッグ・検証のシーン用。ふつうのゲームの進み方では使わない）
            黒客になるかはここでは決めないで、ResolveBlackout にまかせる
            #58: スクリプトで固定している間は何もしない（LockDanger で決めた値を守る）
        */
        public void SetDangerForDebug(float danger)
        {
            if (IsFinished || IsDangerLocked) return;
            SetDanger(danger);
        }

        /*
            #58: D を danger に固定する（企画書 v8 18章「段階学習の危険円はスクリプト固定値」）
            0:00〜0:18 は D を増やさず、0:18〜0:30 の二重円は時間経過ではなく D=65 で表す。この30秒は黒客にしない
            100 以上を渡すと黒客になってしまうので、100 未満におさめる
        */
        public void LockDanger(float danger)
        {
            if (IsFinished) return;
            IsDangerLocked = true;
            SetDanger(Math.Min(danger, MaxDanger - 0.001f));
        }

        // #58: 固定を外す。resetTo を渡したらその D から、渡さなければ今の D から、時間で進みはじめる
        public void UnlockDanger(float? resetTo = null)
        {
            if (!IsDangerLocked) return;
            IsDangerLocked = false;
            if (resetTo.HasValue && !IsFinished) SetDanger(resetTo.Value);
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
