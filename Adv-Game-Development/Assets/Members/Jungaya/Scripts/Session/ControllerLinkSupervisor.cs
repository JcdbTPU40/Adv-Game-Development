using UnityEngine;
using Toufuku.GameInput;
using Toufuku.Playtest;

/*
    コントローラーの通信がとぎれたときの見張りと復帰（#65 / 企画書 v8 3章「接続」、13章 アテンド手順書「無線切断→有線に差し替え」、17章 T7）

    ・ConecteController が受け取った行の間かくを ControllerLinkWatch で見て、0.5秒あいたら「とぎれた」
    ・とぎれている間
        - GameSession の時計を止める（SessionHoldReason.LinkRecovery）。危険度 D・スポーン・福の連なりの5秒・C停滞タイマーが進まない
          プレイ中（解決中もふくむ）だけ止める。タイトルやリザルトでは止めない
        - rescanIntervalSeconds ごとに COM ポートを別スレッドで探しなおす（ゲームは止まらない）。前につながっていたポートは最後に試すので、
          無線（Bluetooth の仮想 COM ポート）が切れたあとに USB ケーブルを挿すと、USB のほうが見つかる
        - 画面に「通信が切れました」を出す（SessionHoldOverlay）
    ・0.3秒続けて受け取れたら復帰。時計を再開して、とぎれていた秒をログ（Console と #63 の計測ログ link_lost / link_recovered）に残す
    ・一度も受け取っていない間は何もしない（開発のキーボード操作のため）。展示ビルドで起動時にまだ挿していないときは scanWhileNeverConnected を ON
    ボタン箱だけの 100ms のとぎれ（全ボタン解放）は ThrowInputController / Esp32RawSource の担当で、ここでは時計を止めない
*/
[DefaultExecutionOrder(-250)]
public class ControllerLinkSupervisor : MonoBehaviour
{
    public static ControllerLinkSupervisor Instance { get; private set; }

    [Tooltip("見張る ConecteController。未設定ならシーンから探す")]
    [SerializeField] ConecteController controller;

    [Header("とぎれの判定（#65）")]
    [Tooltip("受け取りの間かくがこの秒数以上あいたら、とぎれたとみなす（#52 の切断と同じ 0.5秒）")]
    [SerializeField, Min(0.1f)] float lostGapSeconds = (float)ControllerLinkWatch.DefaultLostGapSeconds;
    [Tooltip("とぎれたあと、この秒数続けて受け取れたら復帰とみなす")]
    [SerializeField, Min(0f)] float stableSeconds = (float)ControllerLinkWatch.DefaultStableSeconds;

    [Header("再接続")]
    [Tooltip("とぎれている間、この秒数ごとに COM ポートを探しなおす")]
    [SerializeField, Min(0.5f)] float rescanIntervalSeconds = 2f;
    [Tooltip("起動してから一度も受け取っていないときも探しつづける（展示ビルド用。開発のキーボード操作では OFF）")]
    [SerializeField] bool scanWhileNeverConnected = false;

    [Header("セッション")]
    [Tooltip("とぎれている間、GameSession の時計を止める")]
    [SerializeField] bool holdSessionWhileLost = true;
    [SerializeField] bool logEvents = true;

    readonly ControllerLinkWatch _watch = new ControllerLinkWatch();
    double _lastScanRequest = double.NegativeInfinity;
    bool _subscribed;
    GameSession _heldSession;

    public ConecteController Controller => controller;
    // 今とぎれているか（復帰するまで true）
    public bool IsLost => _watch.IsLost;
    // 今のとぎれの前に最後に受け取った時刻（Time.realtimeSinceStartupAsDouble）。とぎれていなければ NaN
    public double LostSince => _watch.LostSince;
    // いちばん新しい復帰で、とぎれていた秒（T7 の復帰秒）
    public double LastDownSeconds => _watch.LastDownSeconds;
    public int LossCount => _watch.LossCount;
    public bool IsScanning => controller != null && controller.isScanning;
    public string PortName => controller != null ? controller.portName : null;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        if (controller == null) controller = FindAnyObjectByType<ConecteController>();
    }

    void OnEnable()
    {
        Subscribe();
    }

    void OnDisable()
    {
        if (_subscribed && controller != null) controller.SampleReceived -= OnSample;
        _subscribed = false;
        SetHold(null);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Subscribe()
    {
        if (_subscribed || controller == null) return;
        controller.SampleReceived += OnSample;
        _subscribed = true;
    }

    void OnSample(ControllerSample sample)
    {
        _watch.Receive(sample.Time);
    }

    void Update()
    {
        if (controller == null) return;
        Subscribe();

        _watch.LostGapSeconds = lostGapSeconds;
        _watch.StableSeconds = stableSeconds;

        double now = Time.realtimeSinceStartupAsDouble;
        switch (_watch.Poll(now))
        {
            case ControllerLinkEvent.Lost:
                HandleLost(now);
                break;
            case ControllerLinkEvent.Recovered:
                HandleRecovered();
                break;
        }

        // プレイ中（解決中もふくむ）にとぎれているときだけ時計を止める。とぎれている間にリトライで始まったプレイも止める
        GameSession session = GameSession.Instance;
        bool hold = holdSessionWhileLost && _watch.IsLost && session != null && (session.IsPlaying || session.IsResolving);
        SetHold(hold ? session : null);

        // 受け取りが止まっている間だけ探しなおす（また届き始めて、復帰を待っている間は探さない）
        bool silent = _watch.HasReceived ? now - _watch.LastReceiveTime >= lostGapSeconds : !controller.isConnected;
        bool wantScan = silent && (_watch.IsLost || (!_watch.HasReceived && scanWhileNeverConnected));
        if (wantScan && !controller.isScanning && now - _lastScanRequest >= rescanIntervalSeconds)
        {
            _lastScanRequest = now;
            controller.BeginReconnect();
        }
    }

    void HandleLost(double now)
    {
        if (logEvents)
            Debug.LogWarning($"[Link] コントローラーの受信が {now - _watch.LostSince:0.00}秒 とぎれました（{PortName ?? "ポートなし"}）→ 時計を止めて、COM ポートを探しなおします", this);
        PlaytestLog.Marker("link_lost", PortName);
    }

    void HandleRecovered()
    {
        if (logEvents)
            Debug.Log($"[Link] コントローラーの通信が復帰しました（{PortName ?? "ポートなし"}、とぎれていた {_watch.LastDownSeconds:0.00}秒）", this);
        PlaytestLog.Marker("link_recovered", PortName, _watch.LastDownSeconds);
    }

    void SetHold(GameSession session)
    {
        if (_heldSession == session) return;
        if (_heldSession != null) _heldSession.Release(SessionHoldReason.LinkRecovery);
        _heldSession = session;
        if (_heldSession != null) _heldSession.Hold(SessionHoldReason.LinkRecovery);
    }
}
