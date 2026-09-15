/*
    ランクC停滞タイマー（#65 / 企画書 v8 11章、7章「動的難易度」の下方向、付録B RANK.DDA）

    ・ランク C のままでいる秒を数える。B 以上に上がった瞬間に 0 にもどす（C に落ちたら 0 から数えなおす）
    ・足すのは「競技中に時計が進んだ秒」だけ。学習中（0:00〜0:30）・ポーズ・リザルト・通信の復帰中は足さない
      → GameSession.CompetitionDeltaSeconds が、その条件をもう引いた秒を渡してくる
    ・「30秒以上停滞したら満タン秒数 ×1.2」の動的難易度は、まだ採用していない（MVP 合格後の追加。TA で決める）
      今は数えて、外（ShrineRating.RankCStallSeconds / IsRankCStalled）から読めるようにするだけ
    MonoBehaviour は使っていない
*/
public sealed class RankCStallTimer
{
    // 7章「ランクCで30秒以上停滞している場合」
    public const double DefaultStallSeconds = 30.0;

    const double TimeEpsilon = 1e-9;

    // ランク C のままでいる秒
    public double Seconds { get; private set; }

    // countedSeconds: このフレームで数えてよい秒。rank: 今のランク
    public void Tick(double countedSeconds, ShrineRank rank)
    {
        if (rank != ShrineRank.C)
        {
            Seconds = 0.0;
            return;
        }
        if (countedSeconds > 0.0) Seconds += countedSeconds;
    }

    // ランクが変わった瞬間に呼ぶ。B 以上なら 0 にもどす
    public void OnRankChanged(ShrineRank rank)
    {
        if (rank != ShrineRank.C) Seconds = 0.0;
    }

    public bool IsStalled(double thresholdSeconds = DefaultStallSeconds)
    {
        return Seconds >= thresholdSeconds - TimeEpsilon;
    }

    public void Reset()
    {
        Seconds = 0.0;
    }
}
