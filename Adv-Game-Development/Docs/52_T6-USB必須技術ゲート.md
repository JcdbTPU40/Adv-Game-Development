# T6-USB 必須技術ゲート（遅延 p95≤80ms・30 体負荷・安全チェック）— Issue #52

> 仕様書 v8 17章（T6-USB）・3章（性能目標・ヨー角ドリフト対策）・12章（安全領域と配線）・付録B（LATENCY.USB）。
> 基準接続の USB で、**意図が遅れ・欠落・ずれなく届き、最悪描画（黒客・退場者込み 30 体）でも動く**かを見る。
> USB は MVP 必須。**USB が赤なら MVP を固定しない**。BLE は別の任意ゲート（T6-BLE）。

---

## 0. 構成

```
ESP32（USB シリアル）─行─ ConecteController ─SampleReceived─┬─▶ Esp32RawSource ─振りピーク─▶ ThrowInputController（#51）
                                                            │                                   │ SwingAccepted（受信時刻・入力時刻）
                                                            │                                   ▼
                                                            │                           OnusaThrower（#60）─弾─▶ 画面
                                                            │                                   │
                                                            └─▶ UsbGateDirector ◀───────────────┘
                                                                  ├ UsbLinkMonitor   受信の途絶（切断）・受信頻度
                                                                  ├ UsbClockAligner  入力時刻（ESP32 の millis）→ Unity の時計
                                                                  ├ UsbFrameClock    弾を描いた次のフレームの始まり ＝ 画面に出た時刻
                                                                  ├ FrameTimeStats   平均 fps・1% low
                                                                  ├ UsbStillnessDetector ドリフト標本の静止確認
                                                                  └ UsbLoadCrowd ─▶ MockCrowdDirector（TestGame と同じ群衆）30 体以上
                                                                  ▼
                                               記録 CSV（1 イベント 1 行）─▶ UsbGateSummary ─▶ 判定 CSV
```

| ファイル | 役割 |
|---|---|
| `Scripts/Playtest/UsbGatePlan.cs` | 合格ライン・手順の数値・安全チェック項目（純粋関数） |
| `Scripts/Playtest/UsbGateEvent.cs` | 記録 1 行とイベント種別・指標名 |
| `Scripts/Playtest/UsbGateCsv.cs` / `UsbGateCsvFile.cs` | 記録 CSV の書き出し・読み戻し・追記、判定 CSV |
| `Scripts/Playtest/UsbLinkMonitor.cs` | 受信の間隔から切断を数える |
| `Scripts/Playtest/UsbClockAligner.cs` | コントローラの時計を Unity の時計へ合わせる |
| `Scripts/Playtest/UsbIntentMatcher.cs` | 合図と発射の突き合わせ（欠落・誤発射） |
| `Scripts/Playtest/UsbStillnessDetector.cs` | 静止判定（30deg/s 未満が 0.5 秒） |
| `Scripts/Playtest/FrameTimeStats.cs` | 平均 fps・1% low（#45 と同じ定義） |
| `Scripts/Playtest/UsbGateSummary.cs` | 完了条件の判定と不合格時の処置 |
| `Scripts/Playtest/UsbFrameClock.cs` | フレームの始まりの時刻（実行順 -1000） |
| `Scripts/Playtest/UsbLoadCrowd.cs` | 黒客・退場者込み 30 体を保ち、描画体数を数える |
| `Scripts/Playtest/UsbGateDirector.cs` | 区間の進行・計測・記録・実施者パネル |
| `Editor/UsbGateSceneBuilder.cs` | `Toufuku/Playtest/Build T6-USB Gate Scene` ほか 3 メニュー |
| `Editor/Tests/{FrameTimeStats,UsbClockAligner,UsbLinkMonitor,UsbIntentMatcher,UsbStillnessDetector,UsbGateCsv,UsbGateSummary}Tests.cs` | EditMode テスト |

パスはすべて `Assets/Members/Jungaya/` 以下。**既存ファイルの変更は 0 件**（ConecteController・MockCrowdDirector・入力・照準・演出はそのまま呼ぶ）。

---

## 1. 固定する条件

| 条件 | 値 | どこで固定するか |
|---|---|---|
| 接続 | USB（ESP32 シリアル） | `GameManager` の `ConecteController`。記録の `env connection` |
| クールダウン | T0-CD の採用値 | `UsbGateDirector.adoptedCooldown`（区間を始めるたびに入れ直す。F1〜F4 は切ってある） |
| フィードバック | T0-A/B の採用案 | `UsbGate` の `FeedbackTimingShifter`、名前は `feedbackLabel` |
| 描画 | 垂直同期あり（展示と同じ） | `UsbGateDirector.vSyncCount = 1`。記録の `env vsync / refresh_hz / resolution / gpu` |
| 群衆 | TestGame と同じ客・反転ハル輪郭・頭上ゲージ・Bloom | `MockDirector`（`Customer_MockVisibility.prefab`・`OmamoriPalette`・`MockPostProcess`） |

- 既定値は T0-A/B・T0-CD が終わる前の暫定（案A・0.50 秒）。採用値が出たら実施前にシーンの値を合わせる。
- 1 回の T6-USB のあいだはビルドと条件を変えない。T6-BLE は**同じビルド・同じ負荷**で `connectionLabel` だけを変えて通す（17章）。

---

## 2. 検証シーンとビルド

`Assets/Members/Jungaya/Scenes/UsbGateTest.unity`

| メニュー | 何をするか |
|---|---|
| `Toufuku/Playtest/Build T6-USB Gate Scene` | シーンを作り直す（手調整は失われる）。先に `Toufuku/Mock/Apply Visibility Mock to TestGame` で客プレハブが作られている必要がある |
| `Toufuku/Playtest/Open T6-USB Gate Scene` | 開く |
| `Toufuku/Playtest/Build T6-USB Windows Player` | `Builds/T6-USB/T6-USB.exe` を吐く |

| GameObject | 内容 |
|---|---|
| `Main Camera` / `Global Volume (Bloom)` / `Directional Light` / `Ground` / `ShootPos` / `Torii` | TestGame と同じ視点（(0, 5.3, 39) から -Z）・Bloom |
| `Targets` | 子ども用の的。`NearTarget`（投げる位置から 6m）・`FarTarget`（16m）。判定半径 0.90m（T0 と同じ）。#60 の照準が届く 3〜18m に収めた |
| `MockDirector` | `MockCrowdDirector`（定位置 36・黒客 6・目標 34 体）＋`MockGaugeHud` |
| `GameManager` | `ConecteController` |
| `ThrowInput` / `OnusaAim` / `GameFeedback` | #51 / #60 / #64（本番と同じ経路。デバッグ HUD とログは OFF） |
| `UsbGate` | `UsbFrameClock` ＋ `FeedbackTimingShifter` ＋ `UsbLoadCrowd` ＋ `AudioSource`（合図音）＋ `UsbGateDirector` |

---

## 3. 進行

区間は好きな順で行えるが、**安全チェックで全項目適合を記録するまで、ほかの区間は始められない**。
同じ区間をやり直したときは、**最後まで行った最後の回**で判定する（中断した回は合否に使わない。安全事象だけは数える）。

### 3.1 操作

| キー | いつ | 何が起きるか |
|---|---|---|
| **F5〜F10** | メニュー / 結果 | 区間を選ぶ（F5 安全 / F6 意図的 100 投 / F7 静止ドリフト / F8 操作ドリフト / F9 30 体負荷 / F10 子ども） |
| **Space** | 準備 / ドリフト / 結果 | 開始 / 置き台の標本を取る / メニューへ |
| **Esc** | 区間の途中 | 中断（その回は合否に使わない） |
| **C** | 区間の途中 | 接触 +1 |
| **O** | 区間の途中 | 領域からの逸脱 → **その場で中止**（12章「子どもが領域を出たら即時停止」） |
| **X** | 意図的 100 投 | 直前の合図を無効（振らなかった・見ていなかった） |
| **Tab** | いつでも | 実施者パネル |
| **L** | メニュー / 結果 | 記録 CSV を読み直して判定しなおす |

- 1〜5 / A / 左 Shift は ESP32 がボタンを送らない間の代用キー（#51）なので、実施者の操作には使っていない。
- 区間の外では `ThrowInputController` を止める（振っても何も出ない）。準備中はキャリブレーション（正面ボタン 1 秒）のために有効にするが、発射は記録しない。

### 3.2 区間ごとの手順

| 区間 | 手順 | 群衆 |
|---|---|---|
| **安全チェック**（F5） | 実地で 9 項目を確かめてチェック → 記録。項目は §3.3 | - |
| **意図的 100 投**（F6） | 3 秒後から 2 秒ごとに合図（「ふって！」＋ 880Hz の音）が 100 回。**合図ごとに 1 回だけ振る**。振らなかった合図は X | 出す |
| **3 分静止ドリフト**（F7） | 置き台に置き、画面中央の的を向けて正面ボタン 1 秒 → 手を離して Space（静止を確かめて標本）→ 3 分触らない → Space（標本） | 出す |
| **3 分操作ドリフト**（F8） | 置き台で正面ボタン 1 秒 → Space（標本）→ 手に持って 3 分振る・向きを変える → 置き台へ戻して Space（標本） | 出す |
| **30 体負荷**（F9） | Space → 5 秒慣らし → 60 秒計測。1 秒ごとに自動で振りピークを入れる（弾・軌跡・SE・振動の描画も含める） | 出す |
| **子ども 近・遠**（F10） | 真ん中の的を向けて正面ボタン 1 秒 → Space → 2 分練習 → 近くの的へ 10 投 → 遠くの的へ 10 投。参加者番号は自動で進む | 出さない（的だけ） |

- 「置き台」は大幣を毎回同じ姿勢で置ける台（治具）。**始めと終わりで同じ置き方**にすることがドリフト計測の前提。
- 静止の確認は 3章のキャリブレーション受理条件と同じ（角速度 30deg/s 未満が 0.5 秒）。5 秒以内に静止しなければ取り直し。
- 子どもの区間は「近 10 投」「遠 10 投」を数え終えると入力を止め、1 秒着弾を待ってから次へ進む。練習の発射は数えない。

### 3.3 安全チェック項目（`UsbGatePlan.SafetyItems`）

| id | 項目 |
|---|---|
| `area_1_5m` | 前後左右 1.5m の振り抜き安全領域をコーン／ベルトで囲った（大幣の全長＋腕の長さで、紙垂を含む先端が境界に届かない） |
| `audience_2m` | 観戦列・アテンド待機位置が後方 2m 以上、かつ安全領域の外 |
| `floor_mat` | 立ち位置の床マットが固定され、めくれ・滑りがない |
| `usb_route` | USB 4 本の経路: ボタン箱・大幣からプレイヤーの後方へ逃がしている |
| `usb_strain` | USB: 台の脚へ 2 点でストレインリリーフしている |
| `usb_cover` | USB: 床を横切る部分はケーブルカバーで固定し、振り抜き領域と通路を横断しない |
| `usb_port` | USB 端子: 抜けかけ・ぐらつきがない（PC 側・機器側とも） |
| `strap` | ストラップ: 装着できる・摩耗や裂けがない・留め具が外れない |
| `joint` | 接合部: 柄・先端・紙垂の固定にガタ・ひび・緩みがない |

---

## 4. 記録

保存先は `PlaytestLogs/`（Editor はプロジェクト直下、ビルドは `persistentDataPath`）。実際のパスは実施者パネルに出る。UTF-8 BOM 付き。

| ファイル | 中身 |
|---|---|
| `T6-USB-1_<日付>_events.csv` | **1 イベント 1 行**。区間が終わるたびに追記（途中で落ちてもそれまでの区間は残る） |
| `T6-USB-1_<日付>_summary.csv` | 完了条件ごとの判定と不合格時の処置。区間が終わるたびに書き直す（19章のテスト記録へ貼る） |

### 4.1 列（17 列）

```
test_id,date,run,section,participant,seq,event,t,input_time,input_unity,receive_time,fire_time,visible_time,value,label,flag,detail
```

| 列 | 内容 |
|---|---|
| `run` | 区間を始めるたびに 1 つ進む通し番号 |
| `section` | `safety` / `throws100` / `drift_static` / `drift_operate` / `load30` / `children` |
| `participant` | 子どもの参加者番号（ほかは 0）。氏名は書かない |
| `t` | 区間の開始からの秒 |
| `input_time` | **入力時刻**。ESP32 の受信行の 5 項目目（`millis()`、#63 と同じ）。コントローラの時計 |
| `input_unity` | `input_time` を Unity の時計へ合わせた推定（§4.3） |
| `receive_time` | **Unity 受信**。振りピークを確定した行を `ConecteController` が読んだ時刻（`SwingAccepted.Time`） |
| `fire_time` | **発射確定**。`ThrowInputController` が `SwingAccepted` を配った時刻 |
| `visible_time` | **画面に弾が出た**。弾を初めて描いたフレームの、次のフレームの始まり（§4.3） |

時刻はすべて秒（`Time.realtimeSinceStartupAsDouble`）。空欄は欠測で 0 ではない。

### 4.2 イベント

| event | 内容 |
|---|---|
| `section_start` / `section_end` | 区間の始まり・終わり（`flag` = 1 最後まで行った / 0 中断、`detail` = 中断理由） |
| `env` | 計測条件（`label` = owner / connection / cooldown_sec / feedback / platform / unity / build / device / gpu / resolution / refresh_hz / vsync / target_frame_rate / crowd / note） |
| `safety_item` | 安全チェック 1 項目（`label` = id、`flag` = 適合） |
| `incident` | 安全事象（`label` = contact / deviation） |
| `cue` / `cue_void` | 合図（`seq` = 合図番号、`t` = 合図の秒）/ 無効にした合図 |
| `fire` | 発射確定（`seq` = 区間内の発射番号、4 つの時刻、`value` = 振りの強さ、`label` = 子どもの段落 practice / near / far） |
| `reject` | 振りピークの却下（`label` = 理由。Cooldown など） |
| `landing` | 子どもの着弾（`seq` = 発射番号、`label` = near / far、`value` = 的の中心からの距離 m、`flag` = 命中） |
| `block` | 子どもの段落の始まり（`label` = practice / near / far） |
| `disconnect` | 受信の途絶（`value` = 途絶秒） |
| `drift_sample` | ドリフトの標本（`label` = start / end / track、`value` = 照準の画面 X ÷ 画面幅、`flag` = 静止を確認、`detail` = yaw / pitch / 画面 Y） |
| `metric` | 区間の集計値（`label` = connected / samples / sample_rate_hz / max_gap_ms / device_time_samples / avg_fps / low1_fps / worst_frame_ms / frames / rendered_min / rendered_avg / black_avg / exiting_avg） |

### 4.3 入力遅延の測り方

仕様書 3章の入力遅延は「SwingAccepted → 画面で弾が出るまで」。v8 では振りを確定した瞬間（ESP32 側）を全ログ・音・弾生成の共通時刻にしているので、
**1 投の遅延 = `visible_time` − `input_unity`** とした。内訳も記録する:

```
入力（ESP32）──USB 転送・OS の受信バッファ──▶ Unity 受信 ──同じフレーム──▶ 発射確定 ──描画・present──▶ 画面
  input_unity                                  receive_time                  fire_time                  visible_time
```

- **入力時刻 → Unity の時計**: 行ごとの「受信時刻 − millis」の、前後 5 秒での最小値を時計の差とみなす（いちばん待たされなかった行の遅れを 0 と置く）。
  水晶のずれで 3 分に数 ms 動くので全体の最小ではなく近くの最小。**USB の転送そのもの（最小でも数 ms）は 0 と置くので、実際より短めに出る**。
- **画面に出た時刻**: Unity は前のフレームを present してから（垂直同期の待ちを含めて）次のフレームに入るので、
  弾を描いたフレームの次のフレームの始まり（`UsbFrameClock`、実行順 -1000）を **present 完了の上限**として使う。表示機そのものの遅れ（数 ms〜）は含まない。
- 入力時刻が無い投擲（5 項目目を送らないファームウェア・マウス代用）は **`visible_time` − `receive_time`** で測り、判定に「受信時刻から・暫定」と出す。
- 転送と表示機の遅れの合計は、合格の境目（p95 が 70ms を超えるなど）に近いときに外部のハイスピード撮影で数投だけ確かめる（§8）。

---

## 5. 完了条件と判定

実施者パネルと判定 CSV に ○× で出る（`UsbGateSummary`）。

| 完了条件 | 判定のしかた | 不合格時の処置（17章ほか） |
|---|---|---|
| 安全チェック全項目適合 | 最後の安全チェックで 9/9 適合 | 直してからチェックし直す |
| 接触／逸脱 0 件 | その日のすべての回（中断した回も）の `incident` | 原因を除くまで再開しない |
| 遅延 p95 ≤ 80ms | 意図的 100 投の各投の遅延の p95（`PERCENTILE.INC` と同じ）。80ms ちょうどは合格 | 描画を簡略化（#45）。発射→画面が小さいのに遅いなら送信周期を見直す |
| 遅延 最大 ≤ 100ms | 同じく最大。100ms ちょうどは合格 | 同上 |
| 意図的入力欠落 < 2% | 応答の無い合図 ÷ 意図した投擲（合図 − 無効）。**2% ちょうどは不合格**。意図した投擲が 100 未満も不合格 | 3章: 採用 CD は変えずに角度閾値だけを独立実験 |
| 誤発射 ≤ 2% | 余分な発射 ÷ 意図した投擲。**2% ちょうどは合格** | 同上 |
| 切断 0 | 実機を使う全区間の最後の回で、受信が 0.5 秒以上途絶えた回数。**実機につながっていない区間が 1 つでもあれば不合格** | ケーブル・端子の交換、ストレインリリーフの取り直し |
| 3 分ドリフト ≤ 画面幅 5% | 静止・操作の**両方**で、置き台での照準の画面 X の差 ÷ 画面幅 | 照準を相対姿勢（振り始めの姿勢からの相対値）へ |
| 負荷条件 | 30 体負荷の計測中の描画体数の**最小**が 30 以上（満たさなければ計測が無効） | 体数の設定を直して測り直す |
| 60fps | 30 体負荷の平均 fps が 59.0 以上 | 描画を簡略化（#45 の軽量化） |
| 1% low ≥ 55fps | 30 体負荷の 1% low | 同上 |
| 近 7/10・遠 6/10 命中 | 子ども全員の合計（5 人で近 35/50・遠 30/50 以上）。参加者 5 人未満は不合格 | 当たり判定・照準補正 |
| 2 回連続合格（19章） | 1 回分では判定できない。§7 に 2 回並べる | - |

合図の窓 = 合図の 0.3 秒前〜1.2 秒後。発射は窓に入っている合図のうちいちばん近いものへの応答とし、同じ合図への 2 発目以降と、どの窓にも入らない発射を誤発射と数える。

---

## 6. 確認記録

### 6.1 EditMode テスト（自動）

| テスト | 確認していること |
|---|---|
| `FrameTimeStatsTests` | 一定 60fps、100 フレームに 1 回の遅いフレーム、1% の数（切り上げ・最低 1）、欠測 |
| `UsbClockAlignerTests` | 最小の待ちで時計を合わせる、時計のずれは前後の窓だけで合わせる、欠測、逆行 |
| `UsbLinkMonitorTests` | 途絶なし、行が届いて気づく／途絶中に気づく（1 回だけ数える）、0.5 秒ちょうど、最初から来ない、未接続で始めた区間 |
| `UsbIntentMatcherTests` | 全応答、欠落 2%（不合格）、同じ合図への 2 発目と窓の外（誤発射 2% は合格）、窓の境目、無効にした合図 |
| `UsbStillnessDetectorTests` | 0.5 秒で静止、動くと数え直す、ゆっくりの漂いと 360 度の折り返し |
| `UsbGateCsvTests` | 列数、書いて読み戻して同じ、欠測、所見のカンマ・引用符（改行は空白にして 1 行に収める）、壊れた行、区間名の往復 |
| `UsbGateSummaryTests` | 典型的な 1 回分で合格、未実施は不合格、安全チェック 1 項目不適合、中断した回の安全事象、遅延の境目（80 / 100ms ちょうど）、入力時刻なしは暫定、欠落 < 2%・誤発射 ≤ 2% の境目、やり直しは最後まで行った最後の回、切断と未接続、ドリフト両方、描画の境目、子ども 35/50・30/50 と人数不足、不合格時の処置 |

2026-09-15: Unity 6000.3.13f1 の EditMode 全 **318 件成功**（上の 43 件を含む。#49 / #50 / #51 / #53 / #60 / #63 / #64 の既存テストも回帰なし）。
コンパイルはエラー 0（警告は既存の `Shoot.cs` / `Shoot2.cs` の 2 件のみ）。新規スクリプトはすべてコンパイル対象に入っていた（NULL インポートなし）。
最初の実行では `UsbGateCsvTests` の 1 件が失敗した。共通の `AbTestCsv.Escape`（#49）は所見の改行を空白にして 1 行に収める仕様で、テストの期待値のほうが誤っていたので直した。

### 6.2 PlayMode ロジック確認（2026-09-15）

`Toufuku/Playtest/Build T6-USB Gate Scene` で生成した `UsbGateTest.unity` を Editor で Play し、実施者のキー操作の代わりに `UsbGateDirector` の公開メソッドを直接呼んで確認した。
確認時間を短くするため、Play 中だけ合図数・間隔・ドリフト秒・負荷秒・練習秒・近遠の投数を書き換えた（シーンには保存していない）。
**実機（ESP32）は未接続**。振りは (a) `Esp32RawSource` へ合成の受信行（5 項目・millis つき）を流して本番の検出経路から、(b) 状態機械の入口 `InputStateMachine.SwingPeak` から入れた。
**音・画面は目視で未確認**（記録の行と判定の値で確認）。Editor の fps・遅延の数値は合成入力と Editor 描画を含むので、合否の参考にならない。

| 確認 | 結果 |
|---|---|
| 起動時 | メニュー・入力無効。群衆 34 体（黒 7 = 常時 6 ＋怒り 1、退場中 1）。エラー・警告なし |
| 安全チェックの関所 | 安全チェック前は F6 相当が「先に安全チェック」で開始できない。8/9 適合で記録 → まだ開始できない。9/9 で開始できた |
| 意図的 100 投（無応答） | 合図 5 回に発射なし → 欠落 5・誤発射 0 |
| 意図的 100 投（本番の検出経路） | 合成の振り 2 回（0.12 秒差）→ 1 回目が発射、2 回目は Cooldown で却下。発射行に `input_time`・`receive_time`・`fire_time`・`visible_time` が入り、区間の終わりに `input_unity` が「受信 − 3.000ms」（合成した最小の待ち 1ms と、その行の待ち 4ms の差）で埋まった |
| 合図の突き合わせ | 合図 3・無効 1（X 相当）→ 意図 2・応答 1・欠落 1・誤発射 0 |
| 切断 | 合成の受信が止まってから 0.48 秒で `disconnect` 1 行。区間の終わりまで戻らなかったので値は途絶の全秒。`connected = 0` の区間として判定「切断」× |
| 接触 | C 相当で `incident contact`。判定「接触／逸脱」× |
| 静止ドリフト | 始め・終わりの標本（未接続なので静止確認 `flag = 0`）→ ドリフト 0 |
| 逸脱で即中止 | O 相当 → その場で `section_end flag = 0`（理由つき）。安全事象は 1 → 2 件に増え、操作ドリフトは未実施のまま（中断した回を使わない） |
| 30 体負荷 | 1 秒慣らし＋5 秒計測。描画体数 最小 32・平均 33.7（黒 平均 6.5・退場中 平均 0.56）→ 負荷条件 ○。自動投擲 6 発すべてに `visible_time`。fps・1% low・フレーム数が `metric` に残った |
| 子ども 近・遠 | 群衆を隠して的だけ表示。練習 → 近（遠の的を隠す）→ 遠（近の的を隠す）の順に `block` 行。近 2 投・遠 2 投で入力が止まり、着弾を待って自動で終了（`section_end flag = 1`）。発射行の `label` が near / far、着弾ごとに的の中心からの距離が入り、0.14m の 1 発だけ命中。判定「近 0/2・遠 1/2・1/5 人で不足」× |
| 記録 | `PlaytestLogs/T6-USB-1_<日付>_events.csv`（BOM 付き・17 列）に区間ごとに追記、`_summary.csv` に完了条件 13 行＋総合を書き直し。`PlaytestLogs/` は .gitignore 済み |
| 判定の表示 | 不合格の条件に 17章の処置が付いて出た（例: ドリフト → 相対姿勢へ） |

合成入力のため、確認中は #64 の予算警告「Swing: 166 ms > 80 ms」が 1 回出た（受信時刻を 0.26 秒さかのぼって入れたため。実機では出ない想定）。
確認後、確認用に書き出した CSV は削除した。

### 6.3 実機・対象者での確認（未実施）

| # | 手順 | 期待結果 | 結果 |
|---|---|---|---|
| B1 | ファームウェアを USB 送信・5 項目（`yaw,pitch,roll,buttons,millis`）にして接続 | 実施者パネルの「入力時刻つき」が増える。記録の `device_time_samples` > 0 | 未（§8-1） |
| B2 | 意図的 100 投を開発メンバーで 1 回 | `fire` 行の 4 つの時刻が埋まり、遅延 p95・欠落・誤発射がパネルと判定 CSV に出る | 未 |
| B3 | 30 体負荷を展示用ノート PC の Windows ビルドで | 平均 fps・1% low・描画体数の最小が記録される | 未 |
| B4 | 置き台での静止・操作ドリフト | 始め・終わりの標本が静止確認つき（`flag` = 1）で残る | 未（置き台の製作） |
| B5 | USB を抜いて 1 秒後に挿す（わざと） | `disconnect` 1 行・判定「切断」× | 未 |
| B6 | 外部のハイスピード撮影で 10 投 | 動画の「振りの確定 → 画面に弾」とログの遅延の差（転送＋表示機の遅れ）を §7 に書く | 未 |
| B7 | 子ども 5 人 | §5 の完了条件 | 未（T0-A/B・T0-CD・T0-3M の後） |
| B8 | 別日に 2 回目 | 2 回連続合格 | 未 |

---

## 7. 実施記録（19章のテスト記録へ転記する）

| 回 | テストID | 日付 | 責任者 | 機材（PC・GPU・表示機 Hz） | 採用案 / CD | 遅延 p95 / 最大 | 欠落 / 誤発射 | 切断 | ドリフト 静止 / 操作 | fps 平均 / 1% low（体数最小） | 近 / 遠 | 安全 | 合否 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 回目 | | | | | | | | | | | /50 ・ /50 | | |
| 2 回目（別日） | | | | | | | | | | | /50 ・ /50 | | |

- 外部撮影で確かめた「転送＋表示機の遅れ」: ____ ms（B6）。ログの p95 にこれを足しても 80ms 以内かを併記する。
- 不合格なら §5 の処置を 1 つずつ行い、同じテストIDの回を増やして測り直す。テストIDのない数値変更は正本（付録B）へ反映しない。

---

## 8. 前提・未決事項

1. **ファームウェア（プログラム A と相談）**: 現行の `Assets/Members/Awaji/Scripts/arduino/RodController/RodController.ino` は
   **Bluetooth（`SerialBT`）にしか送っておらず、USB シリアルには何も出していない**。また 3 項目（`yaw,pitch,roll`）だけで、`millis` を送らない。
   T6-USB を実機で行う前に、USB（`Serial`）へ `yaw,pitch,roll,buttons,millis` を送る改修が必要（#51・#63 と同じ形式。Unity 側は対応済み）。
   送信周期も現行 20ms（50Hz）で、3章の IMU 100Hz より粗い。受信頻度は記録の `sample_rate_hz` に出る。
2. **有効スイングの閾値**: 3章は「200deg/s 超＋下向き 25°以上」だが、現行の `Esp32RawSource` は 180deg/s のピーク検出（#51）。
   仕様書どおり T0-CD の採用値で固定して測り、欠落・誤発射が未達のときだけ角度閾値を独立実験する。
3. **遅延の測り方（§4.3）**: USB 転送と表示機の遅れはログに入らない（短めに出る）。外部撮影（B6）で差を確かめて §7 に残す。
   入力時刻が取れない間は受信時刻からの「暫定」値しか出ない。
4. **仕様書 v8 17章と Issue #52 から次のように解釈した**（企画と照合してほしい）:
   - 「意図的 100 投」は、**画面と音の合図に合わせて 1 回ずつ振る**形にした（意図の正本を合図にするため）。合図を見ていなかった回は実施者が無効にして分母から外す。
   - 「3 分静止・操作」は、**置き台に置いたままの 3 分**と、**手に持って振る 3 分**を別々に測り、**両方**が 5% 以内で合格とした。
   - 「近 7/10・遠 6/10」は**子ども 5 人の合計**（近 35/50・遠 30/50）で見た。1 人ずつ全員が満たす解釈なら `UsbGateSummary` の判定を変える。
   - 「60fps」は垂直同期（59.94Hz の表示機を含む）の丸めを見込み、**平均 59.0fps 以上**とした。
   - 「黒客・退場者込み 30 体」の内訳は仕様書に無いので、**黒客 6 体（常時）＋毎秒 1 体を怒らせて退場（余韻の間は描画に残る）＋補充の歩行**とし、
     計測中の描画体数の最小が 30 以上であることを負荷条件にした（目標 34 体・定位置 36）。
   - 子どもの区間は照準と命中を見るため**群衆を出さない**（的だけ）。最悪描画のまま測るなら `crowdInChildren` を ON にする。
   - 切断は `ConecteController` の接続フラグではなく**受信の間隔（0.5 秒以上）**で数える。現行の `ConecteController` は読み込みの例外を握りつぶし、接続フラグを戻さないため。
5. **ゲート順**: 仕様書の順は T0-A/B → T0-CD → T0-3M → **T6-USB** → T1。この Issue では計測の道具（シーン・ビルド・記録・判定）までを用意した。
   対象者（子ども 5 人）での実施は、上流 3 ゲートの採用値を §1 に入れてから行う。
6. **T6-BLE** は同じシーン・同じビルドで `connectionLabel` を変えて通す想定。30 分連続操作・再接続秒・USB 比の差は T6-BLE の Issue で足す。
