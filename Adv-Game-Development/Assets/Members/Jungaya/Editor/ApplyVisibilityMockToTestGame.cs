using System.IO;
using Toufuku.Rescue;
using Toufuku.Rescue.Mock;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Toufuku.Rescue.MockEditor
{
    /// <summary>
    /// 視認性モック(#44)の見え方を、本番のプレイ画面 TestGame.unity に適用する Editor 拡張。
    ///
    /// なぜ手作業ではなくコードで組むか:
    ///   何度でもやり直せて、差分がコードとして残りレビューできるため。
    ///   Apply / Revert のどちらも「何度実行しても同じ状態になる（冪等）」ように書いてある。
    ///
    /// VisibilityMockSceneBuilder との違い:
    ///   あちらは「空シーンをゼロから組む」。こちらは「既にある本番シーンに載せる」。
    ///   したがってカメラ・ライト・地面・鳥居は作らず、TestGame の既存物をそのまま使う。
    ///   客も MockCustomer.prefab（ただのカプセル）ではなく、本番 Customer.prefab の
    ///   Prefab Variant を使う ＝ 見えているのは本番の客そのもの。
    ///
    /// 追加されるもの（すべて Revert で消える）:
    ///   MockDirector                  MockCrowdDirector / MockGaugeHud / MockVisibilityDebugHud
    ///     └ Customers                 生成した客のぶら下げ先
    ///   Global Volume (Bloom)         MockPostProcess.asset（既存 Volume があればそちらに Bloom を足す）
    ///
    /// 無効化されるもの（Revert で再有効化。コンポーネントの enabled を落とすだけで、GameObject は残す）:
    ///   CustomerSpawner の Customer_Spawner   … 二重スポーン防止。鳥居 Transform としては使い続ける
    ///   ScoreManager の CustomerGaugeHud      … 頭上ゲージの二重描画防止
    ///
    /// 既存の本番系スクリプトのソースは 1 行も変更していない。
    /// </summary>
    public static class ApplyVisibilityMockToTestGame
    {
        private const string ApplyMenuPath = "Toufuku/Mock/Apply Visibility Mock to TestGame";
        private const string RevertMenuPath = "Toufuku/Mock/Revert Visibility Mock from TestGame";

        private const string TestGameScenePath = "Assets/Members/Jungaya/Scenes/TestGame.unity";

        private const string MockFolder = "Assets/Members/Jungaya/Mock";
        private const string BaseCustomerPrefabPath = "Assets/Members/Jungaya/Prefabs/Customer.prefab";
        private const string VariantPrefabPath = MockFolder + "/Customer_MockVisibility.prefab";
        // 本番 Customer.prefab の CustomerProfileApplier が参照しているのと同じカタログ。
        private const string ProfileCatalogGuid = "a016feb484070384fa9816aec415b657";

        private const string OutlineMatPath = MockFolder + "/MockOutline.mat";
        private const string BodyMatPath = MockFolder + "/MockCustomerBody.mat";
        private const string VolumeProfilePath = MockFolder + "/MockPostProcess.asset";

        // ── シーン上の目印となる名前 ────────────────────────────
        private const string DirectorName = "MockDirector";
        private const string CustomersName = "Customers";
        private const string VolumeName = "Global Volume (Bloom)";

        private const string SpawnerObjectName = "CustomerSpawner";
        private const string GaugeHudObjectName = "ScoreManager";

        // Revert でカメラ設定を元に戻すために、Apply 前の値を覚えておく。
        // シーンに専用コンポーネントを足すと「モックを消したのに残骸が残る」ことになるので、
        // Editor 側（EditorPrefs）に置く。
        private const string PrefKeyPostFx = "Toufuku.Mock.TestGame.PrevRenderPostProcessing";

        // ── 定位置バンド ────────────────────────────────────────
        // z はモック既定（近27〜33 / 中19〜26 / 遠12〜18）をそのまま使う。
        // カメラは (0,5.3,39) から -Z を向いているので、いずれもカメラ手前かつ画面内に収まる。
        //
        // x だけは TestGame に合わせて狭めてある。TestGame には Wall/Cube・Cube(1) の
        // 見えない壁が x=±9.6（内側の面が ±9.1）に立っており、モック既定の ±9 / ±11 だと
        // 客が壁に埋まる／壁の向こうへ出る。客の半径 0.5 を見て ±6 / ±7.5 / ±8.5 にした。
        // なお z の上限は Wall/Cube (2)（z=34.0〜35.0 の横壁）より手前である必要がある。
        private struct BandDef
        {
            public string Label;
            public Vector2 ZRange;
            public Vector2 XRange;
            public int SlotCount;
            public Color GizmoColor;
        }

        private static readonly BandDef[] Bands =
        {
            new BandDef { Label = "近", ZRange = new Vector2(27f, 33f), XRange = new Vector2(-6f, 6f),     SlotCount = 6, GizmoColor = new Color(0.3f, 1.0f, 0.6f, 0.35f) },
            new BandDef { Label = "中", ZRange = new Vector2(19f, 26f), XRange = new Vector2(-7.5f, 7.5f), SlotCount = 6, GizmoColor = new Color(0.3f, 0.8f, 1.0f, 0.35f) },
            new BandDef { Label = "遠", ZRange = new Vector2(12f, 18f), XRange = new Vector2(-8.5f, 8.5f), SlotCount = 6, GizmoColor = new Color(0.9f, 0.6f, 1.0f, 0.35f) },
        };

        // ── デバッグHUDのキー割り当て ──────────────────────────
        // TestGame では OmamoriSelector / MouseInputProvider が数字キー 1〜5 を
        // 「お守りの選択」に使っている。モック既定の 1/2/3（体数プリセット）と正面衝突するので、
        // 体数系だけファンクションキーへ逃がす。他のキー（F/Space/R/O/W/G/L/H）は
        // TestGame 側の使用キー（A: PlayerDirect、Escape: ESC）と衝突しないのでそのまま。
        private const KeyCode Key8 = KeyCode.F1;
        private const KeyCode Key12 = KeyCode.F2;
        private const KeyCode Key15 = KeyCode.F3;
        private const KeyCode KeyClearOverride = KeyCode.F4;

        // モック既定の (10,10) だと TestGame の ScoreHud（縁/コンボ/倍率/神社評価）と
        // GokagoTime の Reset / Force Miss ボタンに完全に重なって両方読めなくなる。
        // それらの下に逃がす。1920×1080 の Game ビューで実測した値。
        private static readonly Vector2 DebugHudOffset = new Vector2(10f, 280f);

        // ── 客プレハブ Variant の調整値 ────────────────────────
        // 自然上昇 5/秒（本番値）だと、初期ゲージをばらけさせた客が数秒で怒って消えてしまい、
        // 12〜15体を並べた状態を作れない。maxGauge / goodHitReduce（射撃バランス）は
        // 本番のまま触らず、上昇レートと余韻だけモック側（VisibilityMockSceneBuilder）に合わせる。
        private const float MoodNaturalRiseRate = 1.5f;
        private const float MoodResolveLinger = 0.4f;

        // ══════════════════════════════════════════════════════════
        //  Apply
        // ══════════════════════════════════════════════════════════

        [MenuItem(ApplyMenuPath, priority = 120)]
        public static void Apply()
        {
            if (!OpenTestGameScene(out Scene scene)) return;

            GameObject variant = EnsureCustomerVariant();
            if (variant == null) return;

            // ── MockDirector ────────────────────────────────────
            GameObject directorGo = FindRoot(scene, DirectorName) ?? new GameObject(DirectorName);
            directorGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            Transform customers = directorGo.transform.Find(CustomersName);
            if (customers == null)
            {
                var customersGo = new GameObject(CustomersName);
                customersGo.transform.SetParent(directorGo.transform, false);
                customers = customersGo.transform;
            }

            var crowd = GetOrAdd<MockCrowdDirector>(directorGo);
            var gaugeHud = GetOrAdd<MockGaugeHud>(directorGo);
            var debugHud = GetOrAdd<MockVisibilityDebugHud>(directorGo);

            // ── 鳥居は既存スポナーの Transform を流用する ──────
            GameObject spawnerGo = FindRoot(scene, SpawnerObjectName);
            if (spawnerGo == null)
            {
                Debug.LogWarning(
                    $"[ApplyMock] '{SpawnerObjectName}' が見つかりません。鳥居位置は toriiPosition の既定値を使います。");
            }

            Camera mainCamera = Camera.main;
            Material outlineMat = AssetDatabase.LoadAssetAtPath<Material>(OutlineMatPath);
            if (outlineMat == null)
                Debug.LogWarning($"[ApplyMock] {OutlineMatPath} が見つかりません。輪郭が出ない可能性があります。");

            // ── MockCrowdDirector の結線 ────────────────────────
            var crowdSo = new SerializedObject(crowd);
            SetObject(crowdSo, "customerPrefab", variant);
            SetObject(crowdSo, "customerParent", customers);
            SetObject(crowdSo, "outlineMaterial", outlineMat);
            if (spawnerGo != null) SetObject(crowdSo, "toriiTransform", spawnerGo.transform);

            // 開始時も鳥居から歩いて入場させる。
            // prefillInstant(既定 ON) だと Start の時点で全員が定位置に瞬間配置され、
            // 「開幕からもう客が立っている」画になる。本番のプレイ画面としては
            // 門をくぐって入ってくるところから始まってほしいので OFF にする。
            // OFF にすると通常の補充ループに乗るので、respawnDelay 後に
            // minSpawnInterval 間隔で 1 体ずつ湧き、それぞれ walkDuration かけて歩いてくる。
            SetBool(crowdSo, "prefillInstant", false);

            WriteBands(crowdSo);
            crowdSo.ApplyModifiedPropertiesWithoutUndo();

            // ── MockGaugeHud の結線 ─────────────────────────────
            var gaugeSo = new SerializedObject(gaugeHud);
            SetObject(gaugeSo, "director", crowd);
            SetObject(gaugeSo, "targetCamera", mainCamera);
            gaugeSo.ApplyModifiedPropertiesWithoutUndo();

            // ── MockVisibilityDebugHud の結線＋キー競合回避 ────
            var debugSo = new SerializedObject(debugHud);
            SetObject(debugSo, "director", crowd);
            SetObject(debugSo, "gaugeHud", gaugeHud);
            SetEnum(debugSo, "key8", (int)Key8);
            SetEnum(debugSo, "key12", (int)Key12);
            SetEnum(debugSo, "key15", (int)Key15);
            SetEnum(debugSo, "keyClearOverride", (int)KeyClearOverride);
            SetVector2(debugSo, "statusOffset", DebugHudOffset);
            debugSo.ApplyModifiedPropertiesWithoutUndo();

            // ── Bloom / カメラ ──────────────────────────────────
            EnsureBloom(scene);
            EnablePostProcessing(mainCamera);

            // ── 既存機能の無効化（二重スポーン／二重ゲージの防止）──
            SetComponentEnabled<Customer_Spawner>(FindRoot(scene, SpawnerObjectName), false);
            SetComponentEnabled<CustomerGaugeHud>(FindRoot(scene, GaugeHudObjectName), false);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log(
                "[ApplyMock] 視認性モックを TestGame に適用しました。\n" +
                $"体数プリセット: {Key8}/{Key12}/{Key15}（8/12/15）　固定解除: {KeyClearOverride}\n" +
                "F 祭事／H ゲージ凍結／Space 一時停止／O 輪郭方式／W 太さ／G ゲージ幅／L 配置替え／R ランク\n" +
                "元に戻すには \"" + RevertMenuPath + "\"。");
        }

        // ══════════════════════════════════════════════════════════
        //  Revert
        // ══════════════════════════════════════════════════════════

        [MenuItem(RevertMenuPath, priority = 121)]
        public static void Revert()
        {
            if (!OpenTestGameScene(out Scene scene)) return;

            GameObject directorGo = FindRoot(scene, DirectorName);
            if (directorGo != null) Object.DestroyImmediate(directorGo);

            // Apply が自分で作った Volume だけを消す。
            // 既存 Volume に Bloom を足しただけの場合は、その Volume には触らない
            // （元から在ったものを壊さないため。Bloom の override は Inspector で外せる）。
            GameObject volumeGo = FindRoot(scene, VolumeName);
            if (volumeGo != null) Object.DestroyImmediate(volumeGo);

            // カメラのポストプロセスは Apply 前の値に戻す。
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                var data = mainCamera.GetComponent<UniversalAdditionalCameraData>();
                if (data != null)
                {
                    data.renderPostProcessing = EditorPrefs.GetBool(PrefKeyPostFx, false);
                    EditorUtility.SetDirty(data);
                }
            }
            EditorPrefs.DeleteKey(PrefKeyPostFx);

            // 止めていた既存機能を戻す。
            SetComponentEnabled<Customer_Spawner>(FindRoot(scene, SpawnerObjectName), true);
            SetComponentEnabled<CustomerGaugeHud>(FindRoot(scene, GaugeHudObjectName), true);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log(
                "[ApplyMock] 視認性モックを TestGame から取り外しました。\n" +
                $"※ {VariantPrefabPath} はアセットとして残してあります（作り直しのコストを避けるため）。" +
                "不要なら手動で削除してください。");
        }

        // ══════════════════════════════════════════════════════════
        //  客プレハブ Variant
        // ══════════════════════════════════════════════════════════

        /// <summary>
        /// 本番 Customer.prefab の Prefab Variant を用意し、モック用の見た目・挙動を載せる。
        ///
        /// Variant にする理由:
        ///   本番プレハブ側の変更（当たり判定・相性テーブル・スコア連携など）が
        ///   自動的に降りてくるため。モックのために本番プレハブを汚さずに済む。
        /// </summary>
        private static GameObject EnsureCustomerVariant()
        {
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BaseCustomerPrefabPath);
            if (basePrefab == null)
            {
                EditorUtility.DisplayDialog(
                    "客プレハブが見つかりません",
                    $"{BaseCustomerPrefabPath} が見つかりませんでした。",
                    "OK");
                return null;
            }

            EnsureFolder(MockFolder);

            if (AssetDatabase.LoadAssetAtPath<GameObject>(VariantPrefabPath) == null)
            {
                // 「プレハブインスタンスを SaveAsPrefabAsset する」＝ Variant が作られる。
                var temp = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
                temp.name = Path.GetFileNameWithoutExtension(VariantPrefabPath);
                PrefabUtility.SaveAsPrefabAsset(temp, VariantPrefabPath);
                Object.DestroyImmediate(temp);
            }

            // 中身の設定は毎回やり直す（冪等：2回実行しても同じ状態になる）。
            GameObject root = PrefabUtility.LoadPrefabContents(VariantPrefabPath);
            try
            {
                ConfigureVariant(root);
                PrefabUtility.SaveAsPrefabAsset(root, VariantPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(VariantPrefabPath);
        }

        private static void ConfigureVariant(GameObject root)
        {
            var bodyRenderer = root.GetComponent<MeshRenderer>();

            // 本体マテリアルをモック用に差し替える。
            // 理由は2つ:
            //   1) 輪郭を主役にするためニュートラルな灰にしたい（緑のままだと色の手がかりが二重になる）
            //   2) green.mat は _EMISSION キーワードが無く、比較用の Emission モード（O キー）が効かない
            var bodyMat = AssetDatabase.LoadAssetAtPath<Material>(BodyMatPath);
            if (bodyRenderer != null && bodyMat != null)
                bodyRenderer.sharedMaterial = bodyMat;

            // ── モックのコンポーネント ──────────────────────
            GetOrAdd<MockCustomerTag>(root);
            GetOrAdd<MockCustomerWalker>(root);

            var outline = GetOrAdd<MockCustomerOutline>(root);
            var outlineSo = new SerializedObject(outline);
            SetObject(outlineSo, "outlineMaterial", AssetDatabase.LoadAssetAtPath<Material>(OutlineMatPath));
            SetObject(outlineSo, "bodyRenderer", bodyRenderer);
            outlineSo.ApplyModifiedPropertiesWithoutUndo();

            // ── 本番側の挙動でモックと衝突するものを止める ──
            // Customer_Move は Rigidbody の速度を毎 FixedUpdate 上書きして +Z へ押し続ける。
            // MockCustomerWalker は transform を直接補間するので、両立しない。
            var move = root.GetComponent<Customer_Move>();
            if (move != null) move.enabled = false;

            // Customer_Move を止めると Rigidbody だけが残り、重力で落ちる／弾に押される。
            // 定位置に立たせておきたいので kinematic にする。
            // （非 Kinematic の弾側から見た衝突判定は kinematic 相手でも成立するので、
            //   OmamoriBullet.OnCollisionEnter による命中判定は生きたまま。）
            var rb = root.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            // CustomerProfileApplier は Start でカタログからランダムに客タイプを引き、
            // ついでに代表カラーを本体に塗る。どちらもモックと噛み合わないので両方止める。
            //   applyColor    … 輪郭色と別系統の色が本体に乗ると
            //                    「輪郭だけで悩みを識別できるか」の検証が成立しない
            //   pickRandom... … ランダムな客タイプは輪郭色と無関係なので、
            //                    「輪郭どおりのお守りを投げても Bad」になる（実測 12体中11体）
            // 客タイプの割り当ては下の MockOmamoriMatcher が輪郭色から決め直す。
            var applier = root.GetComponent<CustomerProfileApplier>();
            if (applier != null)
            {
                var applierSo = new SerializedObject(applier);
                SetBool(applierSo, "applyColor", false);
                SetBool(applierSo, "pickRandomOnStart", false);
                applierSo.ApplyModifiedPropertiesWithoutUndo();
            }

            // 輪郭色 ＝ 正解お守り になるよう客タイプを割り当て直す。
            var matcher = GetOrAdd<MockOmamoriMatcher>(root);
            var matcherSo = new SerializedObject(matcher);
            SetObject(matcherSo, "catalog",
                AssetDatabase.LoadAssetAtPath<CustomerProfileCatalog>(
                    AssetDatabase.GUIDToAssetPath(ProfileCatalogGuid)));
            matcherSo.ApplyModifiedPropertiesWithoutUndo();

            // 自然上昇が速すぎると 12〜15 体を並べる前に退場してしまう。
            // maxGauge / startGauge（＝射撃バランス）は本番値のまま触らない。
            var mood = root.GetComponent<CustomerMood>();
            if (mood != null)
            {
                var moodSo = new SerializedObject(mood);
                SetFloat(moodSo, "naturalRiseRate", MoodNaturalRiseRate);
                SetFloat(moodSo, "resolveLingerTime", MoodResolveLinger);
                moodSo.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // ══════════════════════════════════════════════════════════
        //  Bloom / カメラ
        // ══════════════════════════════════════════════════════════

        /// <summary>
        /// Bloom を用意する。既にシーンにグローバル Volume があればそちらへ足し、
        /// 無ければ専用の GameObject を作る（Volume の二重掛けを避けるため）。
        /// </summary>
        private static void EnsureBloom(Scene scene)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                Debug.LogWarning($"[ApplyMock] {VolumeProfilePath} が見つかりません。Bloom 無しで続行します。");
                return;
            }

            Volume existing = FindExistingGlobalVolume(scene);
            if (existing != null)
            {
                AddBloomTo(existing);
                return;
            }

            GameObject volumeGo = FindRoot(scene, VolumeName) ?? new GameObject(VolumeName);
            var volume = GetOrAdd<Volume>(volumeGo);
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = profile;
            EditorUtility.SetDirty(volume);
        }

        private static Volume FindExistingGlobalVolume(Scene scene)
        {
            Volume[] volumes = Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Volume v in volumes)
            {
                if (v == null) continue;
                if (v.gameObject.scene != scene) continue;
                if (v.gameObject.name == VolumeName) continue;   // 自分で作ったものは「既存」ではない
                if (v.isGlobal) return v;
            }
            return null;
        }

        /// <summary>既存 Volume のプロファイルに Bloom を足す（既にあれば値だけ揃える）。</summary>
        private static void AddBloomTo(Volume volume)
        {
            VolumeProfile profile = volume.sharedProfile;
            if (profile == null)
            {
                Debug.LogWarning(
                    $"[ApplyMock] 既存 Volume '{volume.gameObject.name}' にプロファイルがありません。Bloom を足せませんでした。");
                return;
            }

            if (!profile.TryGet(out Bloom bloom))
            {
                bloom = profile.Add<Bloom>(true);
                bloom.name = nameof(Bloom);
                AssetDatabase.AddObjectToAsset(bloom, profile);
            }

            bloom.active = true;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 0.9f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.7f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.6f;

            EditorUtility.SetDirty(profile);
            Debug.Log($"[ApplyMock] 既存 Volume '{volume.gameObject.name}' のプロファイルに Bloom を追加しました。");
        }

        /// <summary>
        /// カメラの位置・回転・FOV・背景（Skybox）は一切触らない。
        /// ポストプロセスの有効化だけ行う（これが無いと Bloom が乗らない）。
        /// </summary>
        private static void EnablePostProcessing(Camera camera)
        {
            if (camera == null)
            {
                Debug.LogWarning("[ApplyMock] MainCamera が見つかりません。ポストプロセスを有効にできませんでした。");
                return;
            }

            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();

            if (!EditorPrefs.HasKey(PrefKeyPostFx))
                EditorPrefs.SetBool(PrefKeyPostFx, data.renderPostProcessing);

            data.renderPostProcessing = true;
            EditorUtility.SetDirty(data);
        }

        // ══════════════════════════════════════════════════════════
        //  ユーティリティ
        // ══════════════════════════════════════════════════════════

        private static bool OpenTestGameScene(out Scene scene)
        {
            scene = SceneManager.GetActiveScene();
            if (scene.path == TestGameScenePath) return true;

            if (!File.Exists(TestGameScenePath))
            {
                EditorUtility.DisplayDialog("シーンがありません", $"{TestGameScenePath} が見つかりません。", "OK");
                return false;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;

            scene = EditorSceneManager.OpenScene(TestGameScenePath, OpenSceneMode.Single);
            return scene.IsValid();
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == name) return go;
            return null;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        private static void SetComponentEnabled<T>(GameObject go, bool enabled) where T : Behaviour
        {
            if (go == null)
            {
                Debug.LogWarning($"[ApplyMock] {typeof(T).Name} を載せた GameObject が見つかりませんでした。");
                return;
            }

            T c = go.GetComponent<T>();
            if (c == null)
            {
                Debug.LogWarning($"[ApplyMock] '{go.name}' に {typeof(T).Name} がありません。");
                return;
            }

            c.enabled = enabled;
            EditorUtility.SetDirty(c);
        }

        private static void WriteBands(SerializedObject so)
        {
            SerializedProperty bands = so.FindProperty("bands");
            if (bands == null)
            {
                Debug.LogWarning("[ApplyMock] MockCrowdDirector.bands が見つかりませんでした。");
                return;
            }

            bands.arraySize = Bands.Length;
            for (int i = 0; i < Bands.Length; i++)
            {
                SerializedProperty e = bands.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("label").stringValue = Bands[i].Label;
                e.FindPropertyRelative("zRange").vector2Value = Bands[i].ZRange;
                e.FindPropertyRelative("xRange").vector2Value = Bands[i].XRange;
                e.FindPropertyRelative("slotCount").intValue = Bands[i].SlotCount;
                e.FindPropertyRelative("gizmoColor").colorValue = Bands[i].GizmoColor;
            }
        }

        private static void SetObject(SerializedObject so, string propertyName, Object value)
        {
            SerializedProperty p = so.FindProperty(propertyName);
            if (p == null)
            {
                Debug.LogWarning($"[ApplyMock] プロパティ '{propertyName}' が見つかりませんでした（名前が変わった可能性）。");
                return;
            }
            p.objectReferenceValue = value;
        }

        private static void SetFloat(SerializedObject so, string propertyName, float value)
        {
            SerializedProperty p = so.FindProperty(propertyName);
            if (p == null)
            {
                Debug.LogWarning($"[ApplyMock] プロパティ '{propertyName}' が見つかりませんでした。");
                return;
            }
            p.floatValue = value;
        }

        private static void SetBool(SerializedObject so, string propertyName, bool value)
        {
            SerializedProperty p = so.FindProperty(propertyName);
            if (p == null)
            {
                Debug.LogWarning($"[ApplyMock] プロパティ '{propertyName}' が見つかりませんでした。");
                return;
            }
            p.boolValue = value;
        }

        private static void SetVector2(SerializedObject so, string propertyName, Vector2 value)
        {
            SerializedProperty p = so.FindProperty(propertyName);
            if (p == null)
            {
                Debug.LogWarning($"[ApplyMock] プロパティ '{propertyName}' が見つかりませんでした。");
                return;
            }
            p.vector2Value = value;
        }

        private static void SetEnum(SerializedObject so, string propertyName, int value)
        {
            SerializedProperty p = so.FindProperty(propertyName);
            if (p == null)
            {
                Debug.LogWarning($"[ApplyMock] プロパティ '{propertyName}' が見つかりませんでした。");
                return;
            }
            p.intValue = value;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(parent)) return;
            parent = parent.Replace('\\', '/');

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
