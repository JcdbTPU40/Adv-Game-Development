using UnityEngine;
using UnityEngine.Events;

/*
    ご加護タイム（フィーバー）（#29）

    企画書 v8 7章「ご加護タイム」／付録B BLESSING.G にそろえた
    ・福の連なり C とは別の、ご加護専用の進捗 G を数える（GokagoProgress）
      G はご加護中でないときの「通常弾による救済完了」だけで +1。途中の命中・笑顔の伝播では増えない
    ・G=10 になった救済得点を確定した直後に発動して、同時に G=0 にもどす。ご加護中は救済しても G は 0 のまま（再発動を予約しない）
    ・G を 0 にもどすのは、ご加護中でないときの誤投擲・黒客への通常弾・正しい色を5秒当てない
      この3つは C を切る条件と同じで、G は C より大きくならないので「C が 0 になったら G も 0」で数えられる
    ・発動中は 12秒、通常弾の救済と、その救済客からの伝播に ×1.25（福の連なり倍率と掛け算。弾は発射したときの倍率を保存する）
    ・終わっても C は切らない。G=0 から数えなおす
    ・3:00 以後の救済からは新しいご加護タイムを始めない

    onGokagoStart / onGokagoEnd を外から使えるようにしている（カットイン演出 GokagoCutIn・HUD・天気のフック用）
    ScoreManager と同じ GameObject（またはどこでもいい）に1つ置く
*/
public class GokagoTime : MonoBehaviour
{
    /*
        前の版の comboThreshold / duration / scoreMultiplier（10 / 10秒 / ×2）はシーンに焼きこまれているので、
        名前を変えて付録Bの値（10 / 12秒 / ×1.25）が入るようにした
    */
    [Header("発動条件")]
    [Tooltip("ご加護専用の進捗 G がこの数になると発動する（付録B BLESSING.G）。")]
    [SerializeField, Min(1)] int progressThreshold = GokagoProgress.DefaultThreshold;

    [Header("効果")]
    [Tooltip("発動している時間（秒）（付録B BLESSING.G）。")]
    [SerializeField, Min(0.1f)] float durationSeconds = GokagoProgress.DefaultDurationSeconds;
    [Tooltip("発動中に発射した通常弾の救済と、その救済客からの伝播にかける倍率（付録B ご加護タイム）。福の連なり倍率と掛け算。")]
    [SerializeField, Min(1f)] float blessingMultiplier = GokagoProgress.DefaultMultiplier;

    [Header("イベント（カットイン/HUD/SE/天候フック用）")]
    public UnityEvent onGokagoStart;
    public UnityEvent onGokagoEnd;
    [Tooltip("天候の強制晴朗化フック。天候システム実装後にここへ繋ぐ（現状はログのみ）。")]
    public UnityEvent onWeatherClearRequest;

    readonly GokagoProgress _progress = new GokagoProgress();

    // 発動中かどうか（HUD 用）
    public bool IsActive { get; private set; }
    // 残り時間（秒、HUD 用）。発動していないときは0
    public float Remaining { get; private set; }
    // ご加護専用の進捗 G（HUD・ログ用）
    public int Progress => _progress.Count;
    public int ProgressThreshold => progressThreshold;

    bool _subscribed;

    void Start()
    {
        TrySubscribe();
    }

    void OnDisable()
    {
        if (_subscribed && ScoreManager.Instance != null)
        {
            ScoreManager.Instance.onRescued -= OnRescued;
            ScoreManager.Instance.onComboChanged -= OnComboChanged;
            ScoreManager.Instance.onReset -= OnScoreReset;
        }
        _subscribed = false;
    }

    void Update()
    {
        // 動く順番のせいで、Start のときに ScoreManager がまだ作られていなかったときのための予備
        if (!_subscribed) TrySubscribe();

        if (!IsActive) return;

        // #65: 残り時間はセッションの時計で減らす（3:00 以後・ポーズ・通信の復帰中は止まる。7章「3:00で天候・ご加護の時計を停止」）
        Remaining -= GameSession.PlayDeltaTime;
        if (Remaining <= 0f)
            Deactivate();
    }

    void TrySubscribe()
    {
        if (_subscribed || ScoreManager.Instance == null) return;
        ScoreManager.Instance.onRescued += OnRescued;
        ScoreManager.Instance.onComboChanged += OnComboChanged;
        ScoreManager.Instance.onReset += OnScoreReset;
        _subscribed = true;
    }

    // 通常弾で救済が完了した（ScoreManager が救済得点を足した直後に呼ぶ）
    void OnRescued()
    {
        if (!_progress.AddRescue(IsActive, progressThreshold)) return;

        // #61: 3:00 以後の救済から新しいご加護タイムを始めない（7章「3:00境界の処理順」）
        if (GameSession.Instance != null && !GameSession.Instance.IsPlaying) return;

        Activate();
    }

    // 誤投擲・黒客への通常弾・5秒タイムアウトで C が 0 になったら、G も 0 にもどす
    void OnComboChanged(int combo)
    {
        if (combo == 0) _progress.Break();
    }

    void Activate()
    {
        IsActive = true;
        Remaining = durationSeconds;

        if (ScoreManager.Instance != null)
            ScoreManager.Instance.SetGokagoMultiplier(blessingMultiplier);

        Debug.Log($"[Gokago] ご加護タイム発動！ {durationSeconds}秒間 x{blessingMultiplier}");
        onGokagoStart?.Invoke();

        // 天気をむりやり晴れにする（フックを呼ぶだけ。天気の本体はまだなくてOK）
        Debug.Log("[Gokago] 天候フック: 強制晴朗化を要求");
        onWeatherClearRequest?.Invoke();
    }

    void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        Remaining = 0f;

        if (ScoreManager.Instance != null)
            ScoreManager.Instance.SetGokagoMultiplier(1f);

        Debug.Log("[Gokago] ご加護タイム終了");
        onGokagoEnd?.Invoke();
    }

    void OnScoreReset()
    {
        // リトライのとき（#32）はすぐにやめる
        Deactivate();
        _progress.Break();
    }
}

/*
    ご加護専用の進捗 G（企画書 v8 7章／付録B BLESSING.G）
    GokagoTime がこれを1つ持つ。テストでは単体で動かせる
*/
public class GokagoProgress
{
    // 付録B BLESSING.G: 発動 G=10 / 継続 12秒 / 演出 0.8秒、ご加護タイム ×1.25
    public const int DefaultThreshold = 10;
    public const float DefaultDurationSeconds = 12f;
    public const float DefaultMultiplier = 1.25f;
    public const float CutInSeconds = 0.8f;

    // 今の G
    public int Count { get; private set; }

    /*
        通常弾で救済が完了した。ご加護中なら G は 0 のまま。
        G がしきい値に届いたら G=0 にもどして true（＝発動する）
    */
    public bool AddRescue(bool blessingActive, int threshold = DefaultThreshold)
    {
        if (blessingActive)
        {
            Count = 0;
            return false;
        }

        Count++;
        if (Count < threshold) return false;

        Count = 0;
        return true;
    }

    // 誤投擲・黒客への通常弾・5秒タイムアウト・リトライで G を 0 にもどす
    public void Break()
    {
        Count = 0;
    }
}
