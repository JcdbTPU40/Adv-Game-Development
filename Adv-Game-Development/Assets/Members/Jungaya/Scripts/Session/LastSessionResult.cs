/*
    さっき終わったプレイのリザルトの値（GameScene → ResultScene の受けわたし）

    ・GameScene で GameSession がスコアを固定した（IsFinished）ときに ResultSceneTransition が Capture() する
    ・ResultScene の ResultScreen がここから読んで出す。シーンが変わると ScoreManager・ShrineRating は消えるので、値だけ static に残す
    ・今日のベストは DailyBestRecorder が PlayerPrefs に持っているが、「今回で更新したか」はプレイごとの値なのでここに写す
*/
public static class LastSessionResult
{
    // 1回でも Capture したか（ResultScene をいきなり開いたときは false）
    public static bool HasValue { get; private set; }
    // 固定した縁
    public static int En { get; private set; }
    // 神社の称号（プレイ中の最高ランク）
    public static ShrineRank MaxRank { get; private set; } = ShrineRank.C;
    // 最大の福の連なり
    public static int MaxCombo { get; private set; }
    // 今日のベスト（今回を登録したあと）
    public static int TodayBest { get; private set; }
    // 今回で今日のベストを更新したか
    public static bool NewRecord { get; private set; }

    // 今のシーンの ScoreManager・ShrineRating・DailyBestRecorder から写す
    public static void Capture()
    {
        ScoreManager sm = ScoreManager.Instance;
        ShrineRating rating = ShrineRating.Instance;
        DailyBestRecorder best = DailyBestRecorder.Instance;

        En = sm != null ? sm.En : 0;
        MaxCombo = sm != null ? sm.MaxCombo : 0;
        MaxRank = rating != null ? rating.MaxRank : ShrineRank.C;
        TodayBest = best != null ? best.TodayBest : 0;
        NewRecord = best != null && best.LastPlayWasNewRecord;
        HasValue = true;
    }
}
