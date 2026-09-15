/*
    3:00 境界の決まり（#61 / 企画書 v8 7章「3:00境界の処理順」、付録B GAME.END）

    ・SwingAccepted の時刻が 180.000秒未満なら受理する。180.000秒以上では新しい弾を作らない
    ・3:00 でスポーン・危険度の進行・入力の受付を止める
    ・受理済みの弾だけは最大飛翔時間 0.65秒ぶん（180.650秒まで）解決し、その弾が生んだ救済得点は足す
    ・3:00 以後の接触による伝播・退場歩行では、得点を新しく作らない
    ・受理済みの弾がぜんぶ落ちたら（おそくとも 180.650秒）スコアを固定してリザルトへ移る

    どの弾も「発射時刻 + 飛翔時間」で落ちるので、180.000秒未満に受理した弾は 180.650秒までに必ず落ちる。
    なので「残っている弾が 0 になったら固定」を正にする。フレームの順番で同じフレームに弾より先に固定してしまわないように、
    締め切りの時刻（180.650秒）では固定しない。弾がこわれて落ちなかったときのために、締め切り + graceSeconds でだけ強制的に固定する
*/
public static class SessionBoundary
{
    // GAME.END: 入力の締め切りから、受理済みの弾の解決の締め切りまで（180.650 - 180.000）
    public const float ResolveWindowSeconds = 0.65f;

    // この時刻に SwingAccepted を受理してよいか（180.000秒未満）
    public static bool AcceptsSwing(double elapsedSeconds, double totalSeconds)
    {
        return elapsedSeconds < totalSeconds;
    }

    // スコアを固定してよいか
    public static bool ShouldLock(double elapsedSeconds, double totalSeconds, int pendingShots, double graceSeconds)
    {
        if (elapsedSeconds < totalSeconds) return false;
        if (pendingShots <= 0) return true;
        return elapsedSeconds >= totalSeconds + ResolveWindowSeconds + System.Math.Max(0.0, graceSeconds);
    }
}
