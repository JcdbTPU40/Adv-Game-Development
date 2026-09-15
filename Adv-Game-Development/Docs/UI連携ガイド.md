# UI連携ガイド — スコア/セッション系スクリプト取扱説明書

**対象**: UI担当プログラマ（タイトル / プレイ画面HUD / リザルト画面の実装者）
**作成**: Jungaya
**対象スクリプト**: `Assets/Members/Jungaya/Scripts/` 以下（Scoring / Session）

---

## 1. まず全体像

ゲームロジック側は **「値が変わったらイベントで通知する」** 設計になっています。
UI側は **イベントを購読（Subscribe）して表示を更新するだけ** でOKです。

```
[ゲームロジック側（Jungaya担当）]                [UI側（あなたの担当）]

ScoreManager   ──縁/コンボ/倍率が変わった──▶  プレイHUD（スコア表示）
ShrineRating   ──評価/ランクが変わった────▶  評価メーター
GokagoTime     ──フィーバー開始/終了────▶  フィーバー演出
GameSession    ──月が変わった/終了した──▶  タイマー表示・リザルト
```

### 絶対ルール

1. **`Update()` で毎フレーム値を読みに行かない（ポーリング禁止）**。イベント購読で更新する。
   - 例外: `GameSession.RemainingSeconds`（残り時間）と `GokagoTime.Remaining` は毎フレーム変わる値なので、これだけは `Update()` で読んでOK。
2. **ロジック側のスクリプトは編集しない**。「このイベント/値が欲しい」と Jungaya に依頼してください。追加します。
3. UIスクリプトは自分のフォルダ（`Assets/Members/<名前>/Scripts/UI/` など）に作る。

### シングルトンについて

`ScoreManager` / `ShrineRating` / `GameSession` はシングルトンです。
シーン上のどこからでも `クラス名.Instance` でアクセスできます。

```csharp
int score = ScoreManager.Instance.En;   // どこからでも読める
```

⚠️ `DontDestroyOnLoad` では**ありません**。シーンに配置されたものが `Awake` で `Instance` になります。
シーンをまたぐと消えます（→ [7. シーン遷移の注意](#7-シーン遷移とリザルトの注意) 参照）。

---

## 2. ScoreManager — スコア（縁）と福の連なり

シーンに1つ。スコアの本体です。計算式は企画書 v8 7章「縁の計算式（通常弾）」のとおり（#61）。

```
救済得点 = round((基礎点 + 最終弾の命中精度加点 + 優先救済加点) × 救済時の福の連なり倍率 × 発射時のご加護倍率)
伝播得点 = round(20 × 救済時に保存した福の連なり倍率 × 救済時に保存したご加護倍率)
```

- 福の連なり C は**救済完了でだけ +1**（欲張り客の1発目などの途中命中では増えない）。3/6/10 で ×1.10/×1.20/×1.30。
- C が 0 に戻るのは「誤投擲（色違い）」「黒客への通常弾」「正しい色を5秒当てない」の3つだけ。地面への外れでは切れない。
- ランクは縁の倍率に**入りません**（付録B「ランク すべて×1.0」）。

### 読み取れる値（プロパティ）

| プロパティ | 型 | 意味 | UIでの用途 |
|---|---|---|---|
| `En` | `int` | 累計スコア「縁」。増える一方 | スコア表示（HUDは5桁） |
| `Combo` | `int` | 今の福の連なり C | 連なりカウンター |
| `MaxCombo` | `int` | このプレイ中の最大の福の連なり | リザルト表示 |
| `Multiplier` | `float` | 今の福の連なり倍率（×1.0/1.10/1.20/1.30） | 倍率表示 |
| `TotalMultiplier` | `float` | 合計倍率（福の連なり×ご加護） | 「x1.30」等の倍率表示 |
| `LastZone` | `HitZone` | 直近の命中ゾーン（Center/Inner/Outer/Miss） | ヒット演出の出し分け |
| `LastGain` | `int` | 直近の獲得点（救済得点か伝播得点） | 「+300」のポップアップ |
| `IsLocked` | `bool` | スコアを固定したか（3:00 の解決が終わった） | リザルト前の最終値かどうか |

### 購読できるイベント（**C#イベント** — コードで `+=` する）

| イベント | 引数 | いつ発火するか | UIでやること |
|---|---|---|---|
| `onEnChanged` | `int`（現在の累計縁） | スコアが増えた | スコアテキスト更新 |
| `onComboChanged` | `int`（今の C） | 連なりが増えた/途切れた（0も来る） | 連なり表示更新 |
| `onMultiplierChanged` | `float`（合計倍率） | 倍率が変わった | 倍率表示更新 |
| `onMiss` | なし | 誤投擲・黒客への通常弾で連なりが切れた | 連なり途切れ演出・ミスSE |
| `onPropagationScored` | `int`（入った点） | 笑顔の伝播で縁が入った | 「+22」の小さいポップアップ |
| `onScoreLocked` | `int`（固定した縁） | 3:00 のあと、受理済みの弾がぜんぶ落ちてスコアを固定した | 最終スコアの確定演出 |
| `onReset` | なし | リトライでリセットされた | 表示を初期状態に戻す |

### 呼んでいいメソッド

| メソッド | 用途 |
|---|---|
| `ResetAll()` | スコア全リセット。**通常はUIから直接呼ばない**（リトライは `GameSession.Retry()` を呼べば内部で呼ばれる） |

`RegisterCorrectHit()` / `RegisterMiss()` / `RegisterGroundMiss()` / `RegisterPropagation()` / `LockScore()` はゲームプレイ側が呼ぶものなので、**UIからは呼ばないでください**。

---

## 3. ShrineRating — 神社評価（長期の通信簿）

シーンに1つ。「縁＝瞬間スコア」に対して「評価＝プレイ全体の成績」。**ランクは表示と称号だけで、縁の倍率には影響しません**（#61）。

- 評価値は 0〜300。毎プレイ 0・ランク C から始まる。
- ランクは昇格（C→B 60 / B→A 150 / A→S 250）と降格（B→C 40 / A→B 120 / S→A 210）で値が違う（境界でちらつかない）。
- **HUD は今のランク（`Rank`）、リザルトの称号はプレイ中の最高ランク（`MaxRank`）**を出す（企画書 v8 7章）。

### 読み取れる値

| プロパティ | 型 | 意味 | UIでの用途 |
|---|---|---|---|
| `RatingNormalized` | `float` | 評価値の **0〜1 正規化** | ゲージ（`Image.fillAmount` にそのまま入る） |
| `Rating` | `float` | 生の評価値（0〜300） | 数値表示したい場合 |
| `RatingMax` | `float` | 評価値の上限（300） | 数値表示したい場合 |
| `Rank` | `ShrineRank` | 今のランク `C / B / A / S` | HUD のランク表示 |
| `MaxRank` | `ShrineRank` | プレイ中の最高ランク | **リザルトの称号** |

### 購読できるイベント（**UnityEvent** — Inspectorで繋ぐ or `AddListener`）

| イベント | 引数 | いつ発火 | UIでやること |
|---|---|---|---|
| `onRatingChanged` | `float`（0〜1 正規化値） | 評価が増減した | ゲージの `fillAmount` 更新 |
| `onRankChanged` | `ShrineRank` | 今のランクが変わった | ランクアイコン差し替え・昇格/降格演出 |
| `onMaxRankChanged` | `ShrineRank` | 最高ランクが上がった | 「称号更新」演出（任意） |

※ `Start()` で初期値が一度発火されるので、シーン開始時の初期表示もこれで入ります。

---

## 4. GokagoTime — ご加護タイム（フィーバー）

シーンに1つ。コンボが閾値（デフォルト10）に達すると一定時間（デフォルト10秒）発動し、スコア倍率が上がります。

### 読み取れる値

| プロパティ | 型 | 意味 | UIでの用途 |
|---|---|---|---|
| `IsActive` | `bool` | 発動中か | フィーバー中の画面演出ON/OFF |
| `Remaining` | `float` | 残り秒数（非発動中は0） | 残り時間バー（※これは`Update()`で読んでOK） |

### 購読できるイベント（**UnityEvent**）

| イベント | いつ発火 | UIでやること |
|---|---|---|
| `onGokagoStart` | 発動した瞬間 | フィーバー演出開始・BGM変化・「ご加護タイム！」表示 |
| `onGokagoEnd` | 時間切れ/リトライで終了 | 演出を戻す |
| `onWeatherClearRequest` | 発動時（天候用） | UIでは使わない（天候システム用フック） |

⚠️ `GokagoTime` はシングルトンでは**ない**ので、`[SerializeField] GokagoTime gokago;` でInspector参照するか、`FindObjectOfType` で取得してください。

---

## 5. GameSession — 3分1ゲームのセッション管理

シーンに1つ。「1分＝ゲーム内1ヶ月、3ヶ月＝3分で終了」の進行管理。**タイトル→プレイ→リザルトの流れの背骨**です。

### 読み取れる値

| プロパティ | 型 | 意味 | UIでの用途 |
|---|---|---|---|
| `IsPlaying` | `bool` | プレイ中か（3:00 で false） | 入力・スポーンの可否 |
| `IsResolving` | `bool` | 3:00 をすぎて、受理済みの弾が落ちるのを待っている（最長 3:00.65） | プレイHUDは出したまま |
| `IsFinished` | `bool` | スコアを固定してリザルト中か | リザルトUIの表示切り替え |
| `CurrentMonth` | `int` | 現在の月（1〜3） | 「2ヶ月目」表示 |
| `TotalMonths` | `int` | 総月数（3） | 「2 / 3ヶ月」表示 |
| `RemainingSeconds` | `float` | 終了までの残り秒数 | タイマー表示（※`Update()`で読んでOK） |
| `IsHeld` / `HoldReasons` | `bool` / `SessionHoldReason` | 時計を止めているか・その理由（`Paused` アテンドの一時停止、`LinkRecovery` 通信の復帰中）（#65） | 「一時停止中」表示。止めている間は残り時間も減らない |
| `IsLearning` | `bool` | 0:00〜0:30 の学習中か（#65。段階学習そのものは #58） | 学習中の表示 |

> #65: 時計は単調増加時計です。ポーズ・通信の復帰中は `RemainingSeconds` も止まり、`Time.timeScale` が 0 になります。
> UI のアニメは `Time.unscaledDeltaTime` で動かしてください（止めている間も表示を動かすため）。
> 「通信が切れました」「一時停止中」の表示は `SessionHoldOverlay`（Jungaya）が出します。

### 購読できるイベント（**UnityEvent**）

| イベント | 引数 | いつ発火 | UIでやること |
|---|---|---|---|
| `onSessionStart` | なし | ゲーム開始/リトライ時 | プレイHUD表示・リザルト非表示 |
| `onTimeUp` | なし | 3:00 になった（入力とスポーンが止まる。スコアはまだ動く） | 終了の合図の演出 |
| `onSessionEnd` | なし | 3:00 のあと受理済みの弾がぜんぶ落ちて、スコアと評価を固定した | **リザルト画面を出す**（ここが一番大事） |
| `onMonthChanged` | `int`（新しい月） | 月が変わった | 「◯ヶ月目」更新・月替わり演出 |

### 呼んでいいメソッド

| メソッド | 用途 |
|---|---|
| `Retry()` | **リトライボタンから呼ぶ**。スコア/評価リセット→客一掃→再スタートまで全部やってくれる |
| `StartSession()` | セッション開始（`autoStart=ON` なら不要。タイトル→プレイの開始制御に使える） |
| `TogglePause()` | アテンドの一時停止の切りかえ（#65。F9 キーと同じ）。`Hold` / `Release` はゲームプレイ側（通信の見張り）が使うので、UI からは呼ばない |

---

## 6. 購読の書き方 — C#イベントとUnityEventの違い

うちのスクリプトには2種類のイベントが混在しています。書き方が違うので注意。

### A. C#イベント（`ScoreManager` の `onEnChanged` など）

Inspectorには**出ません**。コードで `+=` / `-=` します。
**必ず `OnDisable` で解除**してください（しないとシーン遷移時にエラーの元）。

```csharp
using UnityEngine;
using TMPro;

public class ScoreText : MonoBehaviour
{
    [SerializeField] TMP_Text label;
    bool _subscribed;

    void Start() => TrySubscribe();

    // 実行順の都合で Start 時に ScoreManager 未生成の場合の保険
    void Update() { if (!_subscribed) TrySubscribe(); }

    void TrySubscribe()
    {
        if (_subscribed || ScoreManager.Instance == null) return;
        ScoreManager.Instance.onEnChanged += OnEnChanged;
        OnEnChanged(ScoreManager.Instance.En); // 初期値を反映
        _subscribed = true;
    }

    void OnDisable()
    {
        if (_subscribed && ScoreManager.Instance != null)
            ScoreManager.Instance.onEnChanged -= OnEnChanged;
        _subscribed = false;
    }

    void OnEnChanged(int en) => label.text = $"{en:N0}";
}
```

この **「TrySubscribe＋Update保険＋OnDisable解除」** のパターンをテンプレとして使ってください。
実物のお手本: [ScoreHud.cs](../Assets/Members/Jungaya/Scripts/Test/ScoreHud.cs)

### B. UnityEvent（`GameSession` / `ShrineRating` / `GokagoTime`）

**Inspectorで繋げます**。コード不要で、シーン上のオブジェクトの `SetActive` やアニメ再生を直接呼べます。

> 例: `GameSession` の `onSessionEnd` に、リザルトパネルの `GameObject.SetActive(true)` をInspectorで登録

コードで繋ぐ場合は `AddListener` / `RemoveListener`:

```csharp
ShrineRating.Instance.onRatingChanged.AddListener(OnRatingChanged);   // 購読
ShrineRating.Instance.onRatingChanged.RemoveListener(OnRatingChanged); // 解除（OnDisableで）

void OnRatingChanged(float normalized) => gaugeImage.fillAmount = normalized;
```

**使い分けの目安**: 演出のON/OFFなど「繋ぐだけ」ならInspector、テキスト整形など値の加工が要るならコード。

---

## 7. シーン遷移とリザルトの注意

⚠️ **重要な設計上の前提**: シングルトンは `DontDestroyOnLoad` ではないため、**シーンをまたぐとスコア等は消えます**。

現状の設計は **「プレイシーン内でリザルトをオーバーレイ表示する」** 前提です:

- `onSessionEnd` でリザルトパネル（Canvas）を同シーン内で表示
- この方式なら `ScoreManager.Instance.En` などがそのまま読める
- `Retry()` で同シーンのままもう一度遊べる（展示会の回転率的にもこれが速い）

もし **`ResultScene` に遷移してリザルトを出したい** 場合は、スコアをシーン間で持ち運ぶ仕組み（static変数 or 引き継ぎ用オブジェクト）が必要になります。**その場合は実装するので、先に相談してください。**

タイトル→プレイの遷移は自由に作ってOKです（プレイシーンがロードされると `autoStart` でセッションが始まります）。

---

## 8. 画面別チェックリスト — 何をどこに繋ぐか

### プレイ画面HUD

| UI要素 | 参照する値/イベント |
|---|---|
| スコア表示 | `ScoreManager.onEnChanged`（5桁。画面外から読める大きさ） |
| 今日のベスト | `DailyBestRecorder.onBestChanged` → `BestBeforeThisPlay`（今の縁がこれをこえたら「更新中」） |
| 福の連なり数 | `ScoreManager.onComboChanged`（0が来たら非表示にする等） |
| 倍率表示 | `ScoreManager.onMultiplierChanged` |
| 獲得点ポップ「+300」 | `onEnChanged` 受信時に `ScoreManager.Instance.LastGain` を読む |
| ミス演出 | `ScoreManager.onMiss` |
| 伝播の点ポップ | `ScoreManager.onPropagationScored` |
| 評価ゲージ | `ShrineRating.onRatingChanged`（0〜1がそのまま来る） |
| ランク表示 | `ShrineRating.onRankChanged`（今のランク） |
| フィーバー演出 | `GokagoTime.onGokagoStart` / `onGokagoEnd`、残り時間は `Remaining` |
| 残り時間タイマー | `GameSession.Instance.RemainingSeconds` を `Update()` で（分:秒に整形） |
| 「◯ヶ月目」 | `GameSession.onMonthChanged` |

### リザルト画面

| UI要素 | 参照する値 |
|---|---|
| 表示タイミング | `GameSession.onSessionEnd` で表示（3:00 ちょうどではなく、受理済みの弾が落ちてスコアを固定したあと） |
| 最終スコア | `ScoreManager.Instance.En` |
| 神社の称号 | `ShrineRating.Instance.MaxRank`（**プレイ中の最高ランク**。最終ランク `Rank` ではない） |
| 今日のベスト | `DailyBestRecorder.Instance.TodayBest`、更新したかは `LastPlayWasNewRecord` |
| 最大の福の連なり | `ScoreManager.Instance.MaxCombo` |
| リトライボタン | `GameSession.Instance.Retry()` を呼ぶ |

### タイトル画面

ロジック側との連携は不要。スタートボタン→プレイシーンのロードだけでOK。

---

## 9. テスト用HUDの置き換え

現在シーンに置いてある以下は **本番UIができたら削除するテスト用** です（OnGUI製）:

- `ScoreHud.cs`（左上のスコア表示） — あなたが作るプレイHUDで置き換え
- `SessionHud.cs`（右上タイマー＋リザルト） — あなたが作るタイマー/リザルトで置き換え

**表示すべき値・購読すべきイベントのお手本がこの2ファイルに全部書いてある**ので、まずこれを読むのが一番早いです。

#61 で、観戦向けの大きさにした仮の本番表示を追加しました（コードで Canvas を組み立てるので、空の GameObject に付けるだけで動きます）:

- `Scripts/Hud/ScoreBoardHud.cs` — 右上に縁（5桁）と今日のベスト、左上に今のランクと評価ゲージ。文字の大きさは画面の高さのわりあいで、50インチで 7m 先から読める既定値
- `Scripts/Hud/ResultScreen.cs` — 縁・神社の称号（最高ランク）・今日のベスト（更新で大きく祝う）・最大の福の連なり・「もう一度」ボタン

見た目（背景・飾り・アニメ）を作りこむときは、この2つを置き換えるか、同じイベントを購読してください。**文字の大きさ（画面外から読めること）は企画書 13章の条件**なので、小さくする場合は `HudLegibility` で読める距離を確認してください。置き換えが済んだらシーンから外してください（ファイル削除はJungayaがやります）。

---

## 10. 動作確認のしかた

1. `Assets/Members/Jungaya/Scenes/TestGame.unity` を開いて再生
2. 客が湧く → お守りを当てるとスコアイベントが飛ぶ
3. Consoleに `[Score]` `[Rating]` `[Session]` `[Gokago]` のログが出るので、イベントの発火タイミングが確認できる
4. 自分のUIを試すときは、このシーンに自作Canvasを置いて購読すればロジックはそのまま動きます

## 11. 困ったら / 欲しい値がないとき

- 「この値がイベントで欲しい」「このタイミングの通知が欲しい」→ **Jungayaに言えば追加します**。ロジック側を直接改造しないでください。
- イベントが飛んでこない → シーンに `ScoreManager` / `GameSession` / `ShrineRating` が配置されているか確認（無いとConsoleに警告が出ます）。
