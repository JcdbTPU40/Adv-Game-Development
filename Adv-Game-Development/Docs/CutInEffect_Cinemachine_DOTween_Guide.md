# カットイン&一閃演出 実装解説 — Cinemachine × DOTween

`CutInPractice.unity` / `CutInController.cs` で実装した
「元素爆発風カットイン → 鬼滅の刃風の抜刀一閃」演出の実装工程まとめ。

- 対象: Unity 6 (6000.3) / URP 17.3 / Cinemachine 3.1.7 / DOTween / Input System
- 発動: **Tキー**（`CutInController.Play()` を直接呼べばスキルからも発動可能）

---

## 1. 全体像 — なにをどう組み合わせたか

この演出は3つの道具の役割分担で成立している。

| 道具 | 役割 |
|---|---|
| **Time.timeScale** | 「世界の時間」を止める/遅くする（0 → 0.1 → 0.02 → 1） |
| **Cinemachine** | カメラの切り替えとブレンド。演出の「視点」を作る |
| **DOTween** | 演出全体のタイムライン制御。カメラリグ・UI・ポスプロ・敵切断まで全部Tweenで駆動 |

### 核心の設計思想: 「世界は止めて、演出は実時間で動かす」

`timeScale` を下げると世界は止まるが、そのままでは演出も止まってしまう。
そこで**演出に関わるものはすべて unscaled（実時間）で動かす**:

| 対象 | unscaled化の方法 |
|---|---|
| DOTween の全Tween | `.SetUpdate(true)` |
| CinemachineBrain のブレンド | `IgnoreTimeScale = true` |
| ParticleSystem | `main.useUnscaledTime = true` |
| 入力 | `Keyboard.current.tKey.wasPressedThisFrame`（Input Systemは元々timeScale非依存） |

この4点を押さえれば「時間停止の中でカメラが回り、UIが走り、パーティクルが舞う」が実現できる。

### 演出タイムライン（絶対時刻方式）

```
t=0        timeScale=0、詠唱パーティクル、Volume weight 0→1
t=0.55     カットインカメラへブレンド開始（回り込み+FOVズーム+レンズ歪みパルス）
t=0.90     バースト粒子 / フラッシュ(白→金→透明) / 集中線 / 色収差スパイク
t=0.95     帯スライドイン → ホールド → スライドアウト
t=2.08     timeScale=0.1（スロー）、構えの沈み込み
t=2.53     横引きカメラへCut切替、一閃ダッシュ(InExpo)、剣閃トレイル
t=2.74     すれ違いざまに三日月斬撃パーティクル
t=2.93     timeScale=0.02（超スロー）、残心カメラへCut、Vignette強化
t=3.43     敵切断: フラッシュライン+シェイク+上半分ずり落ち→フェード
t=4.53     復帰開始: ブレンド設定復元、timeScale→1、Volume weight→0
t=5.4~     OnComplete → Finish() で全状態リセット
```

すべて `DOTween.Sequence` に **Insert(絶対時刻, tween)** で登録している（後述）。

---

## 2. Cinemachine の扱い方

### 2-1. Cinemachine 3.x の基本（2.xとの違いに注意）

| 項目 | CM 2.x | CM 3.x（本プロジェクト） |
|---|---|---|
| 名前空間 | `Cinemachine` | `Unity.Cinemachine` |
| 仮想カメラ | `CinemachineVirtualCamera` | `CinemachineCamera` |
| レンズ | `m_Lens` | `Lens`（構造体フィールド） |
| ブレンド定義 | `Style.Cut` | `CinemachineBlendDefinition.Styles.Cut` |

### 2-2. 構成: Brain 1個 + 仮想カメラ4個の Priority 切り替え

実カメラは **Main Camera 1台だけ**。そこに `CinemachineBrain` を付け、
シーン内の仮想カメラ（vcam）のうち**最も Priority が高いものへ自動でブレンド**する。

| vcam | Priority | 役割 |
|---|---|---|
| CM_Default | 10 | 通常視点（元のMain Camera位置を保持） |
| CM_CutIn | 0 → **20** | カットイン用の寄りカメラ（リグの子） |
| CM_SlashSide | 0 → **30** | 一閃を横から見せる引きカメラ |
| CM_Zanshin | 0 → **40** | 残心の斜め前からの寄り |

カメラ切り替えはスクリプトから **Priorityを書き換えるだけ**:

```csharp
cutInCamera.Priority = 20;   // カットインへ（Brainが勝手にブレンド）
// ...
slashSideCamera.Priority = 30;  // 一閃カメラが勝つ
// ...
slashSideCamera.Priority = 0;   // 全部0に戻せばCM_Defaultへ帰る
```

「切り替えの絵作り」はBrainのブレンドに任せ、演出側はPriorityの上げ下げに専念する。
これがCinemachineの一番おいしい使い方。

### 2-3. ブレンドの制御

```csharp
// 通常はゆったりEaseInOut 0.8秒（シーン側でBrainに設定済み）
// 一閃の瞬間だけ「カット（切り替え瞬時）」に変えたい:
_defaultBlend = brain.DefaultBlend;                       // 元設定を保存
brain.DefaultBlend = new CinemachineBlendDefinition(
    CinemachineBlendDefinition.Styles.Cut, 0f);           // カット切替
// ... 演出後 ...
brain.DefaultBlend = _defaultBlend;                       // 必ず復元
```

- カットイン導入（default→CM_CutIn）は **0.8秒ブレンド**そのものが「カメラが寄っていく」画になる
- 一閃・残心は **Cut** でスパッと切り替えるのがアクション映えする
- **timeScale=0中もブレンドを進めるため `brain.IgnoreTimeScale = true` が必須**

### 2-4. 回り込みカメラワークは「リグ回転」で作る

CM3のvcamに手続き的な回り込み機能を組むより、
**空のリグ（CutInRig）の子にvcamを置き、リグのY回転をDOTweenで回す**方が単純で制御しやすい。

```
CutInRig (プレイヤー位置に配置、Y回転をtween)
 └─ CM_CutIn (ローカル(2.4, 0.4, 0)に固定、常にリグ中心=プレイヤーを向く角度)
```

```csharp
cutInRig.position = player.position;                       // 発動時にプレイヤーへ
cutInRig.rotation = Quaternion.Euler(0, startYaw, 0);      // 開始角
_sequence.Insert(tCam, cutInRig
    .DORotate(new Vector3(0, endYaw, 0), cameraMoveDuration)
    .SetEase(Ease.OutCubic));                              // 側面→正面へ回り込み
```

vcamはFollow/LookAtを持たない「素の置きカメラ」なので、リグが回れば
その分だけプレイヤーの周りを弧を描いて移動する。

### 2-5. FOVズーム（Lensは構造体なので注意）

`CinemachineCamera.Lens` は構造体。**フィールドを直接書き換えられないので
一度取り出して書き戻す**。DOTweenの汎用Tween `DOTween.To` でズームさせる:

```csharp
private void SetFov(float value)
{
    var lens = cutInCamera.Lens;   // コピーを取り出し
    lens.FieldOfView = value;
    cutInCamera.Lens = lens;       // 書き戻し
}
// 50 → 28 へズームイン
_sequence.Insert(tCam, DOTween.To(GetFov, SetFov, endFov, cameraMoveDuration)
    .SetEase(Ease.OutCubic));
```

### 2-6. カメラシェイク

Brainは「vcamのTransform」を入力として実カメラを動かすので、
**vcam自体を DOShakePosition で揺らせばそのまま画面が揺れる**:

```csharp
_sequence.Insert(tCut, zanshinCamera.transform
    .DOShakePosition(0.3f, 0.12f, 20, 90f, false, true)); // 切断の瞬間に0.3秒
```

（Cinemachine Impulseを使う方法もあるが、DOTweenに統一した方が
Sequenceのタイムラインに乗せやすい）

---

## 3. DOTween の扱い方

### 3-1. Sequence + Insert(絶対時刻) でタイムラインを組む

`Append` で数珠つなぎにすると並行動作（帯とフラッシュとカメラが同時）の管理が破綻する。
**先に絶対時刻を計算し、`Insert` / `InsertCallback` で置いていく**方式が圧倒的に見通しが良い:

```csharp
// 先にタイムラインを全部計算
float tCam     = chargeDuration;
float tBurst   = tCam + 0.35f;
float tBand    = tBurst + 0.05f;
float tBandOut = tBand + bandSlideDuration + holdDuration;
float tSlow    = tBandOut + bandSlideDuration * 0.5f;
float tDash    = tSlow + crouchDuration;
// ...

_sequence = DOTween.Sequence().SetUpdate(true);  // ← unscaled必須!

_sequence.Insert(tCam, /* カメラ回り込みtween */);
_sequence.Insert(tCam, /* FOVズームtween */);       // 同時刻に複数置ける=並行
_sequence.InsertCallback(tBurst, () => burstFx.Play());  // 瞬間イベントはCallback
_sequence.OnComplete(Finish);                     // 終了処理
```

- **Insert**: 指定時刻からtweenを再生（重ねて並行可）
- **InsertCallback**: 指定時刻に一度だけ実行（Priority切替、timeScale変更、particle Play/Stop等）
- Sequenceの長さは「最も遅く終わるInsert」で決まる。終端保証に
  `_sequence.InsertCallback(tEnd, () => { });` のダミーを置いている

### 3-2. 今回使ったTween API一覧

| API | 用途 |
|---|---|
| `transform.DORotate(..., RotateMode.FastBeyond360)` | カメラリグ回り込み / 集中線の連続回転 |
| `transform.DOMove / DOMoveY` | 一閃ダッシュ / 構えの沈み込み |
| `transform.DOScaleY` | 溜めのスクワッシュ |
| `transform.DOLocalMove / DOLocalRotate` | 敵上半分のずり落ち |
| `transform.DOShakePosition` | カメラシェイク |
| `transform.DOScaleX` | 切断フラッシュラインの伸長 |
| `rectTransform.DOAnchorPosX` | 帯のスライドイン/アウト |
| `image.DOFade / DOColor` | フラッシュ2段（白→金→透明）/ 集中線の明滅 |
| `material.DOFade(0f, "_BaseColor", dur)` | 敵のフェード消滅（URPは`_BaseColor`指定が必要） |
| `DOTween.To(getter, setter, to, dur)` | **プロパティ汎用**: Volume.weight / FOV / ポスプロintensity / timeScale復帰 |

`DOTween.To` は「floatが1個あるものなら何でもtween化できる」万能枠。
Volumeのweight、ポスプロの `intensity.value`、果ては `Time.timeScale` 自体まで:

```csharp
// timeScaleを0.02から1へ0.4秒でなめらかに復帰（これもunscaledで動く）
_sequence.Insert(tRestore, DOTween
    .To(() => Time.timeScale, v => Time.timeScale = v, 1f, 0.4f)
    .SetEase(Ease.OutQuad));
```

### 3-3. Ease の使い分け（演出の「気持ちよさ」はここで決まる）

| Ease | 使った場所 | 狙い |
|---|---|---|
| `InExpo` | 一閃ダッシュ | 「溜め→瞬間移動級の加速」。最初ほぼ動かず最後に爆発 |
| `OutCubic` | カメラ回り込み、帯イン | 勢いよく入ってスッと止まる |
| `InCubic` | 帯アウト | ゆっくり動き出して加速して消える |
| `OutQuad` | 沈み込み、timeScale復帰 | 柔らかい減速 |
| `InQuad` | 敵上半分の落下 | 重力っぽい加速 |
| `Linear` | 集中線の回転 | 機械的な等速 |

### 3-4. ループとコールバック

```csharp
// 集中線の高速明滅: α0.85⇔0.35をYoyoで10往復
_sequence.Insert(tBurst + 0.05f, speedLines
    .DOFade(0.35f, 0.07f)
    .SetLoops(10, LoopType.Yoyo));
```

### 3-5. クリーンアップ（暴発・リーク防止）

```csharp
private void Finish()          // OnCompleteから呼ばれる
{
    Time.timeScale = 1f;
    // カメラPriority全戻し / ブレンド復元 / Volume weight 0 /
    // ポスプロ値復元 / プレイヤー・敵・トレイルをResetSlashState()で初期化
}

private void OnDestroy()       // シーン破棄時の保険
{
    _sequence?.Kill();
    if (_isPlaying) Time.timeScale = 1f;
}
```

- 多重発動は `_isPlaying` フラグでガード
- 「演出が終わったら**必ず**元に戻る」ために、復元処理はtween側とFinish側の二重にしている

---

## 4. Step by Step — 実装工程

実際に実装した順序そのまま。各Stepが独立して動作確認できる粒度になっている。

### Step 0: 環境と API の確認
1. `Packages/manifest.json` で Cinemachine 3.1.7 / Input System 1.19 を確認
2. DOTween が `Assets/Plugins/Demigiant/` にあることを確認
3. **CM3はAPIが大きく変わっているので、コードを書く前に
   `CinemachineBrain` に `IgnoreTimeScale` / `DefaultBlend` フィールドがあること、
   `CinemachineCamera.Lens` の型を確認**（リフレクションやドキュメントで）

### Step 1: カメラ基盤
1. Main Camera に `CinemachineBrain` を追加、`IgnoreTimeScale = true`
2. デフォルトブレンドを EaseInOut 0.8秒 に設定
3. `CM_Default`（元カメラ位置に静置、Priority 10）を作成
   — **Brain導入後は「元の視点」もvcamとして持たないと帰る場所がなくなる**
4. `CutInRig`（空GO）→ 子に `CM_CutIn`（ローカルオフセット+プレイヤー向き、Priority 0）

### Step 2: カットインUI（uGUI）
1. `CutInCanvas`（Screen Space Overlay / sortingOrder 100 / 1920×1080スケール）
2. `CutInBand`: 半透明黒の帯 2400×300、Z回転-8°、anchoredPosition X=-2600（画面外）
3. 帯の上下に縁（後でグラデーション化）
4. `FlashImage`: 全画面白Image α0

### Step 3: CutInController 第1版（最小構成で一連を通す）
1. Update()でTキー検知 → `Play()`
2. `Play()`: `timeScale=0` → リグをプレイヤーへ配置 → `cutInCamera.Priority=20`
3. `DOTween.Sequence().SetUpdate(true)` に
   リグ回転 / FOVズーム / 帯イン・アウト / フラッシュ を登録
4. 終了で Priority 0 → ブレンド戻り待ち → `timeScale=1`
5. **ここで一度再生して通しの動作を確認してから次へ進む**

### Step 4: ポストプロセス（URP Volume）
1. カメラの `renderPostProcessing = true`、HDR有効化
2. `VolumeProfile` アセットを作成し、Bloom / ChromaticAberration / Vignette /
   LensDistortion を**サブアセットとして**追加（`profile.Add<T>()` + `AddObjectToAsset`）
3. Global Volume `CutInVolume`（weight 0, priority 10）に割り当て
4. スクリプトからは:
   - `volume.weight` を 0→1→0 で「カットイン中だけ適用」
   - `volume.profile.TryGet(out _chromatic)` 等で**ランタイム複製**を取得して
     intensityをtween（アセット本体を汚さない）

### Step 5: パーティクル
1. `ChargeRise`（円周から上昇する光粒）/ `ChargeRing`（orbital回転リング）/
   `BurstFx`（放射バースト90発）を作成
2. 全て `main.useUnscaledTime = true` / `playOnAwake = false`
3. マテリアルは **URP Particles/Unlit を加算ブレンド化**した自作アセット
   （`_Surface=Transparent`, `SrcAlpha/One`, ZWrite off）
   — ビルトインのDefault-ParticleマテリアルはURPだとマゼンタになる
4. 発火はSequenceの `InsertCallback` から `Play()` / `Stop()`

### Step 6: UIの追い込み
1. 集中線: `CutInProceduralTexture.cs` が Awake で放射状テクスチャを
   コード生成（セクター毎にランダムな長さ/太さ）→ RawImage へ。
   バースト以降、明滅(SetLoops Yoyo)+連続回転(DORotate FastBeyond360)
2. 帯の縁: 同スクリプトのEdgeGradientモードで「中央が白熱する」横グラデーションを生成
3. フラッシュを `DOFade`→`DOColor`→`DOFade` の白→アクセント→透明の2段に

### Step 7: 敵と切断ギミック
1. `Enemy` プレハブ: 空ルート + **上下半分のカプセル2個**（スケールY 0.5で潰して積む）
   + `CutFlashLine`（加算発光の細長Cube、通常scaleX=0で非表示）
2. マテリアルは **URP Lit Transparent**（後でαフェードするため）
3. 「切断」は物理不要のフェイク:
   上半分を `DOLocalMove/DOLocalRotate` で斜め上へずらす→落下→`material.DOFade` で消す

### Step 8: 一閃シーケンスの統合
1. 横引き `CM_SlashSide` / 残心 `CM_Zanshin` を静置カメラで追加
2. 既存タイムラインの帯アウト以降に追記:
   - `InsertCallback(tSlow)`: `timeScale=0.1`、沈み込みtween
   - `InsertCallback(tDash)`: ブレンドを**Cutへ一時変更**、SlashSide Priority 30、
     トレイル `Clear()`+`emitting=true`、`DOMove(敵の3m後方, InExpo)`
   - `InsertCallback(tPass)`: 三日月パーティクル `Play()`
   - `InsertCallback(tZanshin)`: `timeScale=0.02`、Zanshin Priority 40、
     `emitting=false`、Vignette強化tween
   - `tCut`: フラッシュラインscaleX 0→4、シェイク、色収差スパイク、上半分ずり落ち
   - `tRestore`: ブレンド復元、Priority全戻し、`timeScale`→1 tween、Volume→0
3. `Finish()` / `ResetSlashState()` で全状態（位置・スケール・α・トレイル）を初期化
   — **開始時にもリセットを呼び、連続発動しても壊れないようにする**

### Step 9: 検証
1. コンパイルエラー確認 → 再生してTキー発動
2. 終了後の確認ポイント: `timeScale==1` / アクティブカメラがCM_Default /
   Volume weight==0 / プレイヤーと敵が初期状態 / コンソールにエラーなし
3. シーン保存

---

## 5. ハマりどころ（実際に踏んだ罠）

| 罠 | 症状 | 対策 |
|---|---|---|
| `volume.profile` はランタイム複製を返す | エディタツール経由で足したエフェクトがドメインリロードで消え `MissingReferenceException` | アセットには `profile.Add<T>` + `AddObjectToAsset` で永続化。ランタイムの値いじりは複製側でOK（むしろアセットが汚れず好都合） |
| ParticleSystemの速度カーブのモード混在 | `Particle Velocity curves must all be in the same mode` エラー | velocityOverLifetime の x/y/z を同じモード（全部2定数など）に揃える |
| URPでビルトインパーティクルマテリアルがマゼンタ | 粒が紫に | `Universal Render Pipeline/Particles/Unlit` で加算マテリアルを自作 |
| Screen Space Overlay の UI にはポスプロが乗らない | 帯をBloomで光らせられない | 「発光して見える色」をテクスチャ側で作る（白熱グラデーション） |
| エディタ非フォーカス時はフレームが止まり、unscaled時間が一気にジャンプ | 演出が一瞬で終わったように見える | 確認はエディタをフォーカスして行う。実機ビルドでは起きない |
| `Lens` は構造体 | `cam.Lens.FieldOfView = x` が反映されない | 取り出し→書き換え→書き戻し |
| ブレンド設定の変え忘れ | 一閃後もずっとCut切替のまま | 元の `DefaultBlend` を保存し、復帰時とFinish()の両方で復元 |

---

## 6. Inspector調整パラメータ（CutInSystem → CutInController）

| 項目 | 初期値 | 意味 |
|---|---|---|
| startYaw / endYaw | 20 / 130 | 回り込みの開始/終了角 |
| startFov / endFov | 50 / 28 | ズーム範囲 |
| cameraMoveDuration | 1.0 | 回り込み+ズームの時間 |
| chargeDuration | 0.55 | 詠唱の長さ |
| bandSlideDuration / holdDuration | 0.35 / 0.6 | 帯の出入り/停止時間 |
| slowTimeScale / zanshinTimeScale | 0.1 / 0.02 | スロー/超スローの倍率 |
| crouchDuration / dashDuration | 0.45 / 0.35 | 溜め/一閃の時間 |
| zanshinHold | 0.5 | 残心の静止時間 |
| dashOverrun | 3.0 | 敵の何m後ろまで突き抜けるか |
| accentColor | 金 (1, 0.78, 0.2) | 演出全体のアクセントカラー |

---

## 7. 関連ファイル

```
Assets/Members/Jungaya/
├─ Scripts/
│  ├─ CutInController.cs          # 演出全体の制御（タイムライン本体）
│  └─ CutInProceduralTexture.cs   # 集中線/縁グラデーションの動的生成
├─ Scenes/CutInPractice.unity     # 練習シーン
├─ Prefabs/Enemy.prefab           # 2分割カプセルの敵
├─ Materials/
│  ├─ CutInParticleAdditive.mat   # 加算パーティクル/トレイル用
│  ├─ CutInSlashArc.mat           # 三日月斬撃用
│  └─ EnemyRed.mat                # 敵用(Transparent, フェード可)
├─ Textures/CrescentSlash.asset   # コード生成の三日月テクスチャ
└─ PostProcess/CutInPostProfile.asset  # カットイン用Volumeプロファイル
```
