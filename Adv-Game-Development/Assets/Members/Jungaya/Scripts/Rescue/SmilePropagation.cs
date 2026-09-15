using System.Collections.Generic;

namespace Toufuku.Rescue
{
    // 1回ぶんの伝播をためしてみた結果
    public enum SmilePropagationResult
    {
        // 伝わった（相手の D を5減らして、縁を足していい）
        Applied,
        // 同じ向きの組（救済客 → 相手）ではもう伝わっている。2回目は起こさない
        AlreadyPropagated,
        // この救済からの伝播が上限の人数に届いている
        LimitReached,
        // 相手が条件を満たしていない（入ってくるとちゅう・もう救われた・黒客・R=0・自分自身・IDなし）
        NotEligible,
        // 3:00 以後の接触だった。歩く見た目だけが残って、D も縁も動かさない
        AfterDeadline
    }

    /*
        笑顔の伝播のルール（#56 / 企画書 v8 6章「笑顔の伝播」・7章 得点表・付録B PROPAGATE）

        救われた客1人ぶんの伝播を数えるクラス。救えた瞬間に1つ作って、帰り道のあいだ持ちつづける

        ルール:
        ・相手は「active・まだ救われていない・黒客じゃない・R>0」だけ（CustomerState.IsRescueTarget）
          入ってくるとちゅうの客、ほかの帰りとちゅうの救済客、黒客は相手にしない
          黒客を入れると「黒客が多いほど縁が増える」ずるができてしまう（v8 変更点10）
        ・同じ向きの組（救済客 → 相手）ごとに1回だけ。当たり判定が何フレームか重なっても2回目は起こさない
          1人の相手が「べつべつの救済客」から受け取るのはOK（インスタンスが別なので自然にそうなる）
        ・1回の救済から伝わるのは最大4人（DefaultMaxTargets）
          ＝ 1回の救済で伝播から入る縁は最大 +80（遠方客の「基礎200 ＋ 伝播最大+80」の後ろ半分。付録B B-2）
        ・相手を先に決めておかない。帰り道で実際にすれちがった時点で判定する（呼ぶ側がすれちがいを見つけて呼ぶ）
        ・3:00 以後の接触では起こさない（v8 7章「3:00境界の処理順」）。時刻を見るのは GameSession なので、
          ここには「間に合っているか」を bool で渡してもらう（時計の正本を2つにしないため）
        ・縁は「救えたときに保存した倍率」（EnMultiplierSnapshot）で計算して、伝わった時点の倍率に差しかえない

        人数（4人）と1回ぶんの点（+20）は付録B の写しである ScoreBonusTable から渡す。
        ここの定数はその表がないときの予備（付録B と同じ値）

        MonoBehaviour を使わない。ルールの全部の場合はエディタのテストで確かめる（SmilePropagationTests）。
        Unity 側のすれちがい判定・D の減少・縁の加算は SmileCarrier がやる
    */
    public class SmilePropagation
    {
        // 1回の救済から伝わる人数の上限（付録B PROPAGATE）
        public const int DefaultMaxTargets = 4;

        readonly HashSet<int> _reached = new HashSet<int>();

        /*
            rescuerId: 救われた客のID（CustomerSpawnId）。矢印のはじまり
            snapshot: 救えたときに確定した倍率（#61 の EnMultiplierSnapshot）
            pointsPerContact: 伝播1人ぶんの縁（付録B B-2。ふつうは +20）
            maxTargets: 1回の救済あたりの上限人数（付録B PROPAGATE。ふつうは 4人）
        */
        public SmilePropagation(
            int rescuerId,
            EnMultiplierSnapshot snapshot,
            int pointsPerContact = EnFormula.DefaultPropagationPoints,
            int maxTargets = DefaultMaxTargets)
        {
            RescuerId = rescuerId;
            Snapshot = snapshot;
            PointsPerContact = pointsPerContact < 0 ? 0 : pointsPerContact;
            MaxTargets = maxTargets < 0 ? 0 : maxTargets;
        }

        // 救われた客のID（矢印のはじまり）
        public int RescuerId { get; }
        // 救えたときに確定した倍率。あとから変えない
        public EnMultiplierSnapshot Snapshot { get; }
        // 伝播1人ぶんの縁（倍率を掛ける前）
        public int PointsPerContact { get; }
        // 1回の救済あたりの上限人数
        public int MaxTargets { get; }

        // ここまでに伝わった人数
        public int Count { get; private set; }
        // ここまでに伝播で入った縁の合計
        public int TotalScore { get; private set; }

        // 上限までいったか。いっていたら、この救済客はもうだれにも伝えない
        public bool IsFull => Count >= MaxTargets;

        // 次の1回が成立したときに入る縁（保存した倍率で計算した、変わらない値）
        public int ScorePerPropagation =>
            EnFormula.PropagationScore(Snapshot.ChainMultiplier, Snapshot.BlessingMultiplier, PointsPerContact);

        // この救済から伝播で入る縁の上限（＝遠方客の「伝播最大 +80」。付録B B-2）
        public int MaxTotalScore => ScorePerPropagation * MaxTargets;

        // この相手にはもう伝えたか（同じ向きの組が2回起きないようにする）
        public bool HasReached(int targetId) => _reached.Contains(targetId);

        /*
            相手とすれちがったので伝播をためす。成立したときだけ Applied を返す
            成立しなかったぶんは上限の人数を減らさない（黒客とすれちがっても4人の枠は残る）

            targetId: すれちがった客のID
            targetIsRescueTarget: その客が相手になれるか（active・未救済・非黒客・R>0）。CustomerState.IsRescueTarget を渡す
            withinTimeLimit: 接触した時刻が 3:00 より前か（GameSession.AcceptsPropagationAt の答えを渡す）
        */
        public SmilePropagationResult TryPropagate(int targetId, bool targetIsRescueTarget, bool withinTimeLimit)
        {
            // 3:00 の境目（v8 7章）。画面のフレームじゃなくて、できごとの時刻で決める
            if (!withinTimeLimit) return SmilePropagationResult.AfterDeadline;

            // 自分自身・IDなし・条件を満たさない客（入場中／救済ずみ／黒客／R=0）はここで落ちる
            if (targetId <= 0 || targetId == RescuerId) return SmilePropagationResult.NotEligible;
            if (!targetIsRescueTarget) return SmilePropagationResult.NotEligible;

            // 同じ向きの組の2回目。上限の人数は減らさない
            if (_reached.Contains(targetId)) return SmilePropagationResult.AlreadyPropagated;

            if (IsFull) return SmilePropagationResult.LimitReached;

            _reached.Add(targetId);
            Count++;
            TotalScore += ScorePerPropagation;
            return SmilePropagationResult.Applied;
        }
    }
}
