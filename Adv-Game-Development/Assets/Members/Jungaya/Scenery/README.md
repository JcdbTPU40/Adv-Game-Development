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

## 地面のテクスチャとディテール

- 地面：`Textures/Env_Gravel_*`（玉砂利）、`Env_PathStone_*`（参道の石畳）、`Env_ForestFloor_*`（玉垣の外と鳥居の奥の林床）。どれもタイルできる絵で、`Materials/` の同名マテリアルに入っています。
- 濃淡：`Textures/Env_Bark_Detail`（幹）、`Env_Stone_Detail`（玉垣・狛犬・縁石）、`Env_Paint_Detail`（鳥居）は白に近いグレーの絵で、トゥーンマテリアルの色に掛け算されます。色を変えたいときはマテリアルの色のほうを変えてください。
- ディテール：`Prefabs/Details/` の岩・草・低木・飛び石・苔・落ち葉を、`ShrineEnvironment.prefab` の `Details` の下に並べています。位置・向き・大きさはシーンビューでそのまま動かせます。モデルを差し替えるときは `Prefabs/Details/` のプレハブの中身を替えてください。
- 「客を隠さない」ルールはディテールも同じです。`|x| < 9.5` かつ `z > 7` に置くのは高さ 0.15m 以下の物（`GrassShort`・苔・小石・飛び石）だけにしてください。背の高い草・岩・低木は玉垣ぞいから外側か、鳥居の奥です。
- 参道の距離の線（7・12・18m）が目地に紛れないよう、石畳の目地は線より明るく細くしています。

## 手水舎・おみくじ掛けの仮の色（GameScene だけ）

デザイン班のモデル（`Members/Awaji/3DObject/FBX/cyouzuya.fbx`・`omikuzi.fbx`）にテクスチャが来るまでの仮の色です。元の FBX は触っていません。

- `Models/Cyouzuya_Parts`・`Omikuzi_Parts`：元のメッシュを、木／屋根／石／縄の4つのサブメッシュに分けた複製です（高さと面の向きで分類）。GameScene 上のインスタンスで、メッシュとマテリアル（`Env_ShrineWood`・`Env_ShrineRoof`・`Env_ShrineStone`・`Env_Rope`）を差し替えています。
- `Prefabs/Details/OmikujiPapers`：縄に結んだおみくじの紙。GameScene の `omikuzi`・`omikuzi (1)` の子に置いています。
- 本番のテクスチャが来たら、GameScene の該当オブジェクトで Mesh Filter と Materials の上書きを Revert し、`OmikujiPapers` を消せば元のモデルに戻ります。

## 賽銭箱（GameScene だけ）

`Prefabs/Saisenbako.prefab` を、GameScene の `ShrineEnvironment` の子として (0, 0, 41.6) に置いています。参道の終わり、プレイヤー（カメラ）のすぐ手前です。客が立てる場所からは 2m 以上はなれていて、カメラから客への視線も遮りません。コライダーは付けていません。

## 自作メッシュをトゥーンで描くときの注意

`Toon/Toon` は法線とタンジェントが平行な面を真っ黒に描きます。タンジェントのないメッシュは既定で (1,0,0) が入るので、法線が ±X の面が黒くなります。`Models/` のメッシュには法線に直交するタンジェントを入れてあります。メッシュを作り直したときも入れてください。

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
