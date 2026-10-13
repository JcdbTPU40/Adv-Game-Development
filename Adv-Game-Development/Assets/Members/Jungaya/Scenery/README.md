# 境内の環境（仮モデル）とライティングの土台

GameScene の背景・ライティング・日周表現の土台です。モデルはすべて Unity のプリミティブで作った**仮モデル**です。デザイン班のモデルができたら、プレハブ単位で差し替えてください。

- 確認用シーン: `Assets/Members/Jungaya/Scenes/ShrineEnvironment.unity`（Play すると 60 秒で1日が回ります）
- 本体: `Prefabs/ShrineEnvironment.prefab`（地面・鳥居・灯籠・篝火・玉垣・森・遠景・ライティング一式）

## 座標（GameScene に合わせています）

| もの | 位置 |
|---|---|
| カメラ | (0, 5.3, 39)、下向き 31.7°、FOV 75 |
| 照準の基準点（ShootPos） | z = 38.73 |
| 近／中／遠の帯 | 基準点から 3–7m／7–12m／12–18m（参道の黒い線が 7・12・18m） |
| 客がいる範囲 | x = ±8.5、z ≒ 20.7〜36 |
| 客の出発点 | `MockDirector.toriiTransform` = CustomerSpawner (0, 1, 0)。鳥居は z = 5 なので、客は鳥居をくぐって入ってくる |

## 差し替えのルール

1. **客を隠さない**: 企画書 13章では、鳥居・提灯などの遮蔽物の向こうにいる客の輪郭は表示しません。なので、カメラから客の範囲までの視線上には物を置きません。
   - 背の高い物は `|x| ≥ 9.5` か、鳥居より奥（z < 7）に置く
   - 客の範囲の中の地面には、高さ 0.15m を超える物を置かない
   - 今の配置は、定位置と歩く経路（出発点から鳥居をくぐる区間を含む）の視線 約4000 本が、どの物にも当たらないことを確認済み
2. **ピボットは足もとの中心**、Y が上、1 単位 = 1m です。差し替えたあとも、プレハブの原点・向き・大きさを変えないでください。
3. **コライダーを付けない**: 地面の当たり判定は GameScene の Plane が受け持ちます。
4. **篝火**は `Flame`（灯ったときだけ表示）と `FireLight` の2つの子を残してください。`BonfireProgress` がこの2つを参照しています。
5. **灯籠の火袋**（`Hibukuro`）は夜に光ります。`DayCycleController.nightGlowRenderers` に入っているレンダラーの `_EmissionColor` を上書きする仕組みです。
6. **狐火**（`Prefabs/Kitsunebi.prefab`）は飾りです。子の `Wisp_xx`（ParticleSystem）1つが狐火1つで、`KitsunebiWisps` が置いた位置のまわりを漂わせます。動く幅は 0.35m なので、置き場所は `|x| ≥ 10` か鳥居より奥（z < 7）にしてください。色は輪郭5色（特に学業の青）とまぎれないよう、彩度を落とした青白（`Materials/Env_Kitsunebi.mat`）です。

## ライティング

- `DayCycleController`：1ヶ月（GameSession の 60 秒）を1日として、`Profiles/TimeOfDay_June_*` の朝(0)→昼(0.2)→夕(0.42)→夜(0.68)→翌月の朝 をつなぎます。
- 時刻で変えるのは**ライト・環境光・空・フォグだけ**です。輪郭は `AfterRenderingOpaques` で描かれ、ポストエフェクトがその上にかかります。ポストで暗くすると、夜に輪郭の明度まで落ちてしまいます（9章「必須情報の明度を変えない」）。
- ポストはプレハブに入れていません。シーン既存の `Global Volume (Bloom)`（`Mock/MockPostProcess`、視認性テストで調整済み）をそのまま使います。色や明るさを変えるエフェクト（Tonemapping・Color Adjustments・Vignette など）は、輪郭の5色まで変わるので足さないでください。
- `BonfireProgress`：篝火は月が変わるごとに1つずつ灯ります（8章「進行の可視化」）。セッションが始まる前は消えています。
- 天候（雨・風・雪）は別プロファイルを作らず、あとからオーバーレイで重ねる方針です（9章）。

## シーンへの入れ方

`Scenes/TestGame.unity` には組み込み済みです。GameScene へ入れるときも同じ手順です（`.unity` の同時編集になるので、チームと相談してから）。

1. `ShrineEnvironment.prefab` を置く。
2. 既存の `Directional Light` と `Stage_01` を消す（どこからも参照されていない）。
3. `Plane` は Mesh Renderer だけ無効にして、コライダーは残す。
4. `MockDirector.toriiTransform` と `Global Volume (Bloom)` はそのまま使う。
