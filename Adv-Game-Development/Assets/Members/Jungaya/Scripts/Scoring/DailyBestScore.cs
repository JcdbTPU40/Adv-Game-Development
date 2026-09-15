using System;
using UnityEngine;

// 今日のベストスコアの保存先（テストでは PlayerPrefs を使わないように差しかえる）
public interface IBestScoreStore
{
    string LoadDate();
    int LoadBest();
    void Save(string date, int best);
}

// PlayerPrefs に保存する（展示機はこれ。アプリを閉じても同じ日なら残る）
public class PlayerPrefsBestScoreStore : IBestScoreStore
{
    public const string DateKey = "Toufuku.DailyBest.Date";
    public const string ScoreKey = "Toufuku.DailyBest.Score";

    public string LoadDate() => PlayerPrefs.GetString(DateKey, "");
    public int LoadBest() => PlayerPrefs.GetInt(ScoreKey, 0);

    public void Save(string date, int best)
    {
        PlayerPrefs.SetString(DateKey, date);
        PlayerPrefs.SetInt(ScoreKey, best);
        PlayerPrefs.Save();
    }
}

/*
    今日のベストスコア（#61 / 企画書 v8 7章「HUDに出すもの」、13章「観戦体験の設計」、18章「待機中」）

    ・日付（PC のローカル日付）が変わったら 0 から数えなおす
    ・3:00 でスコアが固定されたプレイだけを登録する（とちゅうでやめたプレイは入れない）
    ・今のプレイの縁がベストより大きいときだけ更新する（同点は更新しない）
*/
public class DailyBestScore
{
    readonly IBestScoreStore _store;
    readonly Func<DateTime> _now;

    public DailyBestScore(IBestScoreStore store, Func<DateTime> now = null)
    {
        _store = store;
        _now = now ?? (() => DateTime.Now);
    }

    // 日付をキーにする文字列（例: 2027-01-23）
    public static string DateKeyOf(DateTime date) => date.ToString("yyyy-MM-dd");

    // 今日のベスト。保存されている日付が今日でなければ 0
    public int TodayBest
    {
        get
        {
            if (_store == null) return 0;
            return _store.LoadDate() == DateKeyOf(_now()) ? Mathf.Max(0, _store.LoadBest()) : 0;
        }
    }

    // プレイの結果を登録する。ベストを更新したら true
    public bool Submit(int score)
    {
        if (_store == null || score <= 0) return false;
        if (score <= TodayBest) return false;
        _store.Save(DateKeyOf(_now()), score);
        return true;
    }
}
