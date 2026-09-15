using System;
using System.Collections.Generic;

namespace Toufuku.Rescue
{
    /// <summary>1 回の伝播を試した結果。</summary>
    public enum SmilePropagationResult
    {
        /// <summary>伝播が成立した（対象の D を5減らし、縁を加算してよい）。</summary>
        Applied,
        /// <summary>同じ有向ペア（救済客→対象客）で既に成立している。二度目は起こさない。</summary>
        AlreadyPropagated,
        /// <summary>この救済からの伝播が上限人数に達している。</summary>
        LimitReached,
        /// <summary>対象が条件を満たさない（入場中・救済済み・<b>黒客</b>・R=0・自分自身・ID無し）。</summary>
        NotEligible,
        /// <summary>接触時刻が 3:00 以後だった。演出だけが残り、D 減少も得点も起こさない。</summary>
        AfterDeadline
    }

    /// <summary>
    /// 伝播用の倍率スナップショット（企画書 v8 6章・7章「倍率の保存順」）— Issue #56
    ///
    /// 救済完了の瞬間に確定させて救済客へ持たせる 2 値。<b>3 秒後に起きる伝播の得点は、
    /// 伝播が起きた時点の倍率ではなく、この保存値で計算する</b>（v8変更点6：ここが実装差になっていた）。
    ///   ・<see cref="FukuChain"/> … 福の連なり倍率。救済で +1 した<b>あと</b>の段階の値。
    ///   ・<see cref="Gokago"/>    … ご加護倍率。救済させた通常弾の <b>SwingAccepted（発射）時</b>の値。
    /// 大祓による救済客は両方 ×1.0（<see cref="Purification"/>）。
    /// </summary>
    public readonly struct SmileMultiplierSnapshot
    {
        /// <summary>福の連なり倍率（救済完了で +1 したあとの段階）。</summary>
        public readonly float FukuChain;
        /// <summary>ご加護倍率（救済させた弾の発射時の値）。</summary>
        public readonly float Gokago;

        public SmileMultiplierSnapshot(float fukuChain, float gokago)
        {
            // 倍率は 1 未満にならない（減点に転じる経路を作らない）。
            FukuChain = fukuChain > 1f ? fukuChain : 1f;
            Gokago = gokago > 1f ? gokago : 1f;
        }

        /// <summary>大祓由来の救済客が持つスナップショット（両方 ×1.0。v8 7章）。</summary>
        public static SmileMultiplierSnapshot Purification => new SmileMultiplierSnapshot(1f, 1f);

        /// <summary>掛け合わせた倍率（ログ・HUD 表示用）。</summary>
        public float Product => FukuChain * Gokago;

        /// <summary>
        /// 伝播 1 回ぶんの縁：<c>round(perContact × 福の連なり倍率 × ご加護倍率)</c>（v8 7章 縁の計算式）。
        /// 小数を掛けた最後に四捨五入する。
        /// </summary>
        public int ScoreOf(int perContact) => SmilePropagation.Round(perContact * (double)Product);

        public override string ToString() => $"C×{FukuChain:0.00} G×{Gokago:0.00} = ×{Product:0.00}";
    }

    /// <summary>
    /// 笑顔の伝播の規則 — Issue #56（企画書 v8 6章「笑顔の伝播」／7章 得点表／付録B PROPAGATE）
    ///
    /// 救済された客 1 人ぶんの伝播を数える。救済完了時に 1 つ作り、退場歩行の間そのまま持ち回る。
    ///
    /// 規則:
    ///   ・対象は <b>active かつ 未救済・非黒客・R&gt;0</b> だけ（<c>CustomerState.IsRescueTarget</c>）。
    ///     入場中・別の退場中の救済客・黒客は対象外。黒客を入れると「黒客が多いほど縁が増える」抜け道になる。
    ///   ・<b>有向ペア（救済客→対象客）ごとに 1 回だけ</b>。当たり判定が複数フレーム重なっても再発動しない。
    ///     1 人の対象が<b>別々の救済客</b>から伝播を受けるのは可（インスタンスが別なので自然に成立する）。
    ///   ・1 回の救済からの伝播は<b>最大 4 人</b>（<see cref="DefaultMaxTargets"/>）。
    ///     ＝ 1 救済あたりの伝播縁は最大 +80（遠方客の「基礎200＋伝播最大+80」の後半。付録B B-2）。
    ///   ・対象は<b>予約しない</b>。退場歩行中に実際に交差した時点で判定する（呼び出し側が接触を見つけて呼ぶ）。
    ///   ・接触時刻が <b>180.000 秒未満</b>のものだけ有効（v8 7章「3:00境界の処理順」）。
    ///     3:00 以後の退場歩行は演出だけで、D 減少も伝播得点も新しく発生させない。
    ///   ・得点は<b>救済時のスナップショット</b>（<see cref="SmileMultiplierSnapshot"/>）で計算し、伝播時点の倍率へ差し替えない。
    ///
    /// 数値（+20 / 4 人 / 180 秒）は付録B の写しである <c>ScoreBonusTable</c> から渡す。
    /// ここの定数はその表が無いときのフォールバック（付録B と同じ既定値）。
    ///
    /// MonoBehaviour 非依存。規則の全ケースはエディタテストで検証する（SmilePropagationTests）。
    /// Unity 側の接触判定・D 減少・加算は <see cref="SmileCarrier"/> が受け持つ。
    /// </summary>
    public class SmilePropagation
    {
        /// <summary>1 回の救済から伝播できる人数の上限（付録B PROPAGATE）。</summary>
        public const int DefaultMaxTargets = 4;
        /// <summary>伝播 1 回ぶんの縁（倍率を掛ける前。付録B B-2）。</summary>
        public const int DefaultScorePerContact = 20;
        /// <summary>伝播が有効な接触時刻の上限（秒）。この時刻<b>未満</b>だけ有効（v8 7章）。</summary>
        public const float DefaultDeadlineSeconds = 180f;

        readonly HashSet<int> _reached = new HashSet<int>();

        /// <param name="rescuerId">救済された客の生成ID（<c>CustomerSpawnId</c>）。有向ペアの始点。</param>
        /// <param name="snapshot">救済完了時に確定させた倍率スナップショット。</param>
        /// <param name="scorePerContact">伝播 1 回ぶんの縁（付録B B-2。既定 +20）。</param>
        /// <param name="maxTargets">1 救済あたりの上限人数（付録B PROPAGATE。既定 4 人）。</param>
        /// <param name="deadlineSeconds">この秒数未満の接触だけ有効（既定 180 秒）。境界なしにするなら <c>float.PositiveInfinity</c>。</param>
        public SmilePropagation(
            int rescuerId,
            SmileMultiplierSnapshot snapshot,
            int scorePerContact = DefaultScorePerContact,
            int maxTargets = DefaultMaxTargets,
            float deadlineSeconds = DefaultDeadlineSeconds)
        {
            RescuerId = rescuerId;
            Snapshot = snapshot;
            ScorePerContact = scorePerContact < 0 ? 0 : scorePerContact;
            MaxTargets = maxTargets < 0 ? 0 : maxTargets;
            DeadlineSeconds = deadlineSeconds;
        }

        /// <summary>救済された客の生成ID（有向ペアの始点）。</summary>
        public int RescuerId { get; }
        /// <summary>救済完了時に確定した倍率。以後変えない。</summary>
        public SmileMultiplierSnapshot Snapshot { get; }
        /// <summary>伝播 1 回ぶんの縁（倍率を掛ける前）。</summary>
        public int ScorePerContact { get; }
        /// <summary>1 救済あたりの上限人数。</summary>
        public int MaxTargets { get; }
        /// <summary>伝播が有効な接触時刻の上限（秒）。</summary>
        public float DeadlineSeconds { get; }

        /// <summary>これまでに成立した伝播の人数。</summary>
        public int Count { get; private set; }
        /// <summary>これまでに成立した伝播で入った縁の合計。</summary>
        public int TotalScore { get; private set; }

        /// <summary>上限に達したか。達していれば以後この救済客は誰にも伝播しない。</summary>
        public bool IsFull => Count >= MaxTargets;

        /// <summary>次の 1 回が成立したときに入る縁（スナップショットで計算した固定値）。</summary>
        public int ScorePerPropagation => Snapshot.ScoreOf(ScorePerContact);

        /// <summary>この救済から伝播できる縁の上限（＝遠方客の「伝播最大 +80」。付録B B-2）。</summary>
        public int MaxTotalScore => ScorePerPropagation * MaxTargets;

        /// <summary>この対象へ既に伝播したか（有向ペアの重複判定）。</summary>
        public bool HasReached(int targetId) => _reached.Contains(targetId);

        /// <summary>
        /// 対象客と交差したので伝播を試す。成立したときだけ <see cref="SmilePropagationResult.Applied"/> を返す。
        /// 成立しなかった試行は上限人数を消費しない（黒客とすれ違っても 4 人枠は減らない）。
        /// </summary>
        /// <param name="targetId">交差した客の生成ID。</param>
        /// <param name="targetIsRescueTarget">
        /// その客が伝播の対象になれるか（active・未救済・非黒客・R&gt;0）。<c>CustomerState.IsRescueTarget</c> を渡す。
        /// </param>
        /// <param name="contactSeconds">接触時刻（セッション開始からの秒）。180.000 秒以上なら成立しない。</param>
        public SmilePropagationResult TryPropagate(int targetId, bool targetIsRescueTarget, float contactSeconds)
        {
            // 3:00 境界（v8 7章）。端末のフレームレートではなくイベント時刻で判定する。
            if (contactSeconds >= DeadlineSeconds) return SmilePropagationResult.AfterDeadline;

            // 自分自身・ID 未設定・対象条件を満たさない客（入場中／救済済み／黒客／R=0）はここで落ちる。
            if (targetId <= 0 || targetId == RescuerId) return SmilePropagationResult.NotEligible;
            if (!targetIsRescueTarget) return SmilePropagationResult.NotEligible;

            // 同じ有向ペアの二度目。上限人数は消費しない。
            if (_reached.Contains(targetId)) return SmilePropagationResult.AlreadyPropagated;

            if (IsFull) return SmilePropagationResult.LimitReached;

            _reached.Add(targetId);
            Count++;
            TotalScore += ScorePerPropagation;
            return SmilePropagationResult.Applied;
        }

        /// <summary>四捨五入（0.5 は絶対値の大きい方へ）。v8 7章「小数を掛けた最後に四捨五入して整数化する」。</summary>
        public static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }
}
