using System;
using UnityEngine;

/*
    ランクのしきい値（#61 / 企画書 v8 7章「ランクの閾値」、付録B RANK.UP・RANK.DOWN）

    昇格と降格で別の値にする（ヒステリシス）。同じ値だと境界でランク表示が上下にちらついて読めなくなる
          C→B  B→A  A→S
      昇格  60   150  250   （この値以上になったら上がる）
      降格  40   120  210   （この値より下がったら下がる）
*/
[Serializable]
public struct RankThresholds
{
    [Tooltip("C→B へ上がる評価値（以上）。付録B RANK.UP")]
    public float promoteToB;
    [Tooltip("B→A へ上がる評価値（以上）。付録B RANK.UP")]
    public float promoteToA;
    [Tooltip("A→S へ上がる評価値（以上）。付録B RANK.UP")]
    public float promoteToS;
    [Tooltip("B→C へ下がる評価値（未満）。付録B RANK.DOWN")]
    public float demoteToC;
    [Tooltip("A→B へ下がる評価値（未満）。付録B RANK.DOWN")]
    public float demoteToB;
    [Tooltip("S→A へ下がる評価値（未満）。付録B RANK.DOWN")]
    public float demoteToA;

    public RankThresholds(float promoteToB, float promoteToA, float promoteToS, float demoteToC, float demoteToB, float demoteToA)
    {
        this.promoteToB = promoteToB;
        this.promoteToA = promoteToA;
        this.promoteToS = promoteToS;
        this.demoteToC = demoteToC;
        this.demoteToB = demoteToB;
        this.demoteToA = demoteToA;
    }

    // 付録B の値
    public static RankThresholds Default => new RankThresholds(60f, 150f, 250f, 40f, 120f, 210f);
}

/*
    評価値からランクを出すクラス（MonoBehaviour なし）（#61）
    今のランクによって、上がる値と下がる値がちがう。ShrineRating がこれを使う
*/
public static class RankLadder
{
    // 今のランクと新しい評価値から、次のランクを出す。大きく動いたときは何段でも動く
    public static ShrineRank Next(ShrineRank current, float rating, RankThresholds t)
    {
        // 何段も動くときのために、動かなくなるまでくり返す（しきい値が正しければ最大3回ずつ）
        for (int guard = 0; guard < 8; guard++)
        {
            if (current < ShrineRank.S && rating >= PromoteThreshold(current + 1, t))
            {
                current++;
                continue;
            }
            if (current > ShrineRank.C && rating < DemoteThreshold(current, t))
            {
                current--;
                continue;
            }
            break;
        }
        return current;
    }

    // そのランクへ上がる評価値
    public static float PromoteThreshold(ShrineRank target, RankThresholds t)
    {
        switch (target)
        {
            case ShrineRank.B: return t.promoteToB;
            case ShrineRank.A: return t.promoteToA;
            case ShrineRank.S: return t.promoteToS;
            default: return float.NegativeInfinity;
        }
    }

    // そのランクから1つ下がる評価値
    public static float DemoteThreshold(ShrineRank from, RankThresholds t)
    {
        switch (from)
        {
            case ShrineRank.B: return t.demoteToC;
            case ShrineRank.A: return t.demoteToB;
            case ShrineRank.S: return t.demoteToA;
            default: return float.NegativeInfinity;
        }
    }

    // 高いほうのランク
    public static ShrineRank Higher(ShrineRank a, ShrineRank b) => a >= b ? a : b;

    /*
        しきい値の組み合わせがおかしくないか（上がるたびに1段下がって、ちらつきが止まらない組み合わせを見つける）
        ・降格の値は同じ境界の昇格の値より小さい（ヒステリシスがある）
        ・昇格の値は C→B < B→A < A→S の順（降格の値もこれで上がる順になる）
        ・A→S の昇格の値が評価値の上限以下（届かないランクを作らない）
    */
    public static bool IsValid(RankThresholds t, float ratingMax, out string error)
    {
        if (!(t.demoteToC < t.promoteToB && t.demoteToB < t.promoteToA && t.demoteToA < t.promoteToS))
        {
            error = "降格の値が同じ境界の昇格の値以上になっています（ヒステリシスがありません）";
            return false;
        }
        if (!(t.promoteToB < t.promoteToA && t.promoteToA < t.promoteToS))
        {
            error = "昇格の値が C→B < B→A < A→S の順になっていません";
            return false;
        }
        if (t.promoteToS > ratingMax)
        {
            error = $"A→S の昇格の値 {t.promoteToS} が評価値の上限 {ratingMax} をこえています";
            return false;
        }
        error = null;
        return true;
    }
}
