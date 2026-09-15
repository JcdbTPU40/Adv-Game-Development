using UnityEngine;

/*
    福の連なり C の決まり（#61 / 企画書 v8 7章「福の連なりの判定」、付録B B-2・CHAIN.TIMEOUT）

    ・通常弾で救済が完了したときだけ C を +1 する。+1 したあとの C で倍率を決めて、その救済に使う
        0〜2 = ×1.0 ／ 3〜5 = ×1.10 ／ 6〜9 = ×1.20 ／ 10 以上 = ×1.30（上限）
    ・欲張り客の1発目などのとちゅうの当たりは、C を増やさないで保つ。5秒タイマーだけ 0 にもどす
    ・誤投擲（色ちがい）と黒客への通常弾で C を 0 にもどす
    ・5秒間、正しい色をどの客にも当てないと 0 にもどる
    ・地面に落ちた外れ、別の客が黒客になったことでは切らない（狙いどおりの投擲と画面全体の失敗をまぜない）

    数値は付録B が正本。ScoreBonusTable に写してあり、ここの定数は表が入っていないときの予備の値
*/
public static class FukuChain
{
    public const int Step1Count = 3;
    public const float Step1Multiplier = 1.10f;
    public const int Step2Count = 6;
    public const float Step2Multiplier = 1.20f;
    public const int Step3Count = 10;
    public const float Step3Multiplier = 1.30f;

    // CHAIN.TIMEOUT: 正しい色を当てないまま、この秒数がたつと C が 0 にもどる
    public const float DefaultTimeoutSeconds = 5f;

    // C から倍率を出す（付録B の値）
    public static float MultiplierOf(int chain)
    {
        return MultiplierOf(chain, Step1Count, Step1Multiplier, Step2Count, Step2Multiplier, Step3Count, Step3Multiplier);
    }

    // C から倍率を出す（段の数と倍率を渡すバージョン。ScoreBonusTable から使う）
    public static float MultiplierOf(int chain, int count1, float multiplier1, int count2, float multiplier2, int count3, float multiplier3)
    {
        if (chain >= count3) return multiplier3;
        if (chain >= count2) return multiplier2;
        if (chain >= count1) return multiplier1;
        return 1f;
    }
}

/*
    福の連なり C の数と 5秒タイマーを持つだけのクラス（MonoBehaviour なし。時刻は呼ぶ側が渡す）
    ScoreManager がこれを1つ持つ。テストでは時刻を好きに進められる
*/
public class FukuChainCounter
{
    float _lastCorrectHitTime;

    // 今の C
    public int Count { get; private set; }
    // このプレイでいちばん大きかった C（リザルト用）
    public int Max { get; private set; }

    // 通常弾で救済が完了した。C を +1 して、+1 したあとの C を返す
    public int AddRescue(float now)
    {
        Count++;
        if (Count > Max) Max = Count;
        _lastCorrectHitTime = now;
        return Count;
    }

    // 正しい色で当たったけど、まだ救済が完了していない（欲張り客の1発目など）。C は保って、タイマーだけ 0 にもどす
    public void KeepAlive(float now)
    {
        _lastCorrectHitTime = now;
    }

    // 誤投擲・黒客への通常弾で C を 0 にもどす。もともと 0 なら false
    public bool Break()
    {
        if (Count == 0) return false;
        Count = 0;
        return true;
    }

    // 最後に正しい色を当ててから timeoutSeconds 以上たっていたら C を 0 にもどす。もどしたら true
    public bool TickTimeout(float now, float timeoutSeconds)
    {
        if (Count == 0) return false;
        if (now - _lastCorrectHitTime < timeoutSeconds) return false;
        Count = 0;
        return true;
    }

    // リトライ用。いちばん大きかった C も消す
    public void Reset()
    {
        Count = 0;
        Max = 0;
        _lastCorrectHitTime = 0f;
    }
}
