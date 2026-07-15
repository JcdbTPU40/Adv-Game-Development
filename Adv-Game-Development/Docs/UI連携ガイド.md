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

## 2. ScoreManager — スコア（縁）とコンボ

シーンに1つ。スコアの本体です。

### 読み取れる値（プロパティ）

| プロパティ | 型 | 意味 | UIでの用途 |
|---|---|---|---|
| `En` | `int` | 累計スコア「縁」。増える一方 | スコア表示 |
| `Combo` | `int` | 現在の連続コンボ。ミスで0 | コンボカウンター |
| `MaxCombo` | `int` | このプレイ中の最大コンボ | リザルト表示 |
| `TotalMultiplier` | `float` | 合計倍率（コンボ×ご加護×評価） | 「x2.40」等の倍率表示 |
| `LastZone` | `HitZone` | 直近の命中ゾーン（Center/Inner/Outer/Miss） | ヒット演出の出し分け |
| `LastGain` | `int` | 直近の獲得点 | 「+300」のポップアップ |

### 購読できるイベント（**C#イベント** — コードで `+=` する）

| イベント | 引数 | いつ発火するか | UIでやること |
|---|---|---|---|
| `onEnChanged` | `int`（現在の累計縁） | スコアが増えた | スコアテキスト更新 |
| `onComboChanged` | `int`（現在のコンボ） | コンボが増えた/途切れた（0も来る） | コンボ表示更新 |
| `onMultiplierChanged` | `float`（合計倍率） | 倍率が変わった | 倍率表示更新 |
| `onMiss` | なし | 外した/相性✗を当てた | コンボ途切れ演出・ミスSE |
| `onOverSell` | なし | 救済済みの客に再ヒット（押し売り） | 「押し売り！」演出・SE |
| `onReset` | なし | リトライでリセットされた | 表示を初期状態に戻す |

### 呼んでいいメソッド

| メソッド | 用途 |
|---|---|
| `ResetAll()` | スコア全リセット。**通常はUIから直接呼ばない**（リトライは `GameSession.Retry()` を呼べば内部で呼ばれる） |

`RegisterHit()` / `RegisterMiss()` / `RegisterOverSell()` はゲームプレイ側が呼ぶものなので、**UIからは呼ばないでください**。

---

## 3. ShrineRating — 神社評価（長期の通信簿）

シーンに1つ。「縁＝瞬間スコア」に対して「評価＝プレイ全体の成績」。ランクが縁の獲得倍率にも影響します。

### 読み取れる値

| プロパティ | 型 | 意味 | UIでの用途 |
|---|---|---|---|
| `RatingNormalized` | `float` | 評価値の **0〜1 正規化** | ゲージ（`Image.fillAmount` にそのまま入る） |
| `Rating` | `float` | 生の評価値（0〜100） | 数値表示したい場合 |
| `Rank` | `ShrineRank` | 現在ランク `C / B / A / S` | ランク表示・リザルト |
| `EnMultiplier` | `float` | ランクによる縁倍率 | （通常UIでは不要） |

### 購読できるイベント（**UnityEvent** — Inspectorで繋ぐ or `AddListener`）

| イベント | 引数 | いつ発火 | UIでやること |
|---|---|---|---|
| `onRatingChanged` | `float`（0〜1 正規化値） | 評価が増減した | ゲージの `fillAmount` 更新 |
| `onRankChanged` | `ShrineRank` | ランクが変わった | ランクアイコン差し替え・昇格/降格演出 |

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
| `IsPlaying` | `bool` | プレイ中か | プレイHUDの表示切り替え |
| `IsFinished` | `bool` | 終了してリザルト中か | リザルトUIの表示切り替え |
| `CurrentMonth` | `int` | 現在の月（1〜3） | 「2ヶ月目」表示 |
| `TotalMonths` | `int` | 総月数（3） | 「2 / 3ヶ月」表示 |
| `RemainingSeconds` | `float` | 終了までの残り秒数 | タイマー表示（※`Update()`で読んでOK） |

### 購読できるイベント（**UnityEvent**）

| イベント | 引数 | いつ発火 | UIでやること |
|---|---|---|---|
| `onSessionStart` | なし | ゲーム開始/リトライ時 | プレイHUD表示・リザルト非表示 |
| `onSessionEnd` | なし | 3分経過で終了 | **リザルト画面を出す**（ここが一番大事） |
| `onMonthChanged` | `int`（新しい月） | 月が変わった | 「◯ヶ月目」更新・月替わり演出 |

### 呼んでいいメソッド

| メソッド | 用途 |
|---|---|
| `Retry()` | **リトライボタンから呼ぶ**。スコア/評価リセット→客一掃→再スタートまで全部やってくれる |
| `StartSession()` | セッション開始（`autoStart=ON` なら不要。タイトル→プレイの開始制御に使える） |

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
| スコア表示 | `ScoreManager.onEnChanged` |
| コンボ数 | `ScoreManager.onComboChanged`（0が来たら非表示にする等） |
| 倍率表示 | `ScoreManager.onMultiplierChanged` |
| 獲得点ポップ「+300」 | `onEnChanged` 受信時に `ScoreManager.Instance.LastGain` を読む |
| ミス演出 | `ScoreManager.onMiss` |
| 押し売り演出 | `ScoreManager.onOverSell` |
| 評価ゲージ | `ShrineRating.onRatingChanged`（0〜1がそのまま来る） |
| ランク表示 | `ShrineRating.onRankChanged` |
| フィーバー演出 | `GokagoTime.onGokagoStart` / `onGokagoEnd`、残り時間は `Remaining` |
| 残り時間タイマー | `GameSession.Instance.RemainingSeconds` を `Update()` で（分:秒に整形） |
| 「◯ヶ月目」 | `GameSession.onMonthChanged` |

### リザルト画面

| UI要素 | 参照する値 |
|---|---|
| 表示タイミング | `GameSession.onSessionEnd` で表示 |
| 最終スコア | `ScoreManager.Instance.En` |
| 神社ランク | `ShrineRating.Instance.Rank`（C/B/A/S） |
| 最大コンボ | `ScoreManager.Instance.MaxCombo` |
| リトライボタン | `GameSession.Instance.Retry()` を呼ぶ |

### タイトル画面

ロジック側との連携は不要。スタートボタン→プレイシーンのロードだけでOK。

---

## 9. テスト用HUDの置き換え

現在シーンに置いてある以下は **本番UIができたら削除するテスト用** です（OnGUI製）:

- `ScoreHud.cs`（左上のスコア表示） — あなたが作るプレイHUDで置き換え
- `SessionHud.cs`（右上タイマー＋リザルト） — あなたが作るタイマー/リザルトで置き換え

**表示すべき値・購読すべきイベントのお手本がこの2ファイルに全部書いてある**ので、まずこれを読むのが一番早いです。置き換えが済んだらシーンから外してください（ファイル削除はJungayaがやります）。

---

## 10. 動作確認のしかた

1. `Assets/Members/Jungaya/Scenes/TestGame.unity` を開いて再生
2. 客が湧く → お守りを当てるとスコアイベントが飛ぶ
3. Consoleに `[Score]` `[Rating]` `[Session]` `[Gokago]` のログが出るので、イベントの発火タイミングが確認できる
4. 自分のUIを試すときは、このシーンに自作Canvasを置いて購読すればロジックはそのまま動きます

## 11. 困ったら / 欲しい値がないとき

- 「この値がイベントで欲しい」「このタイミングの通知が欲しい」→ **Jungayaに言えば追加します**。ロジック側を直接改造しないでください。
- イベントが飛んでこない → シーンに `ScoreManager` / `GameSession` / `ShrineRating` が配置されているか確認（無いとConsoleに警告が出ます）。
