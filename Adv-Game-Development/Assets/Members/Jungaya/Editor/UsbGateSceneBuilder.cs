using System.IO;
using Toufuku.Aim;
using Toufuku.Aim.EditorTools;
using Toufuku.Feedback;
using Toufuku.Feedback.EditorTools;
using Toufuku.GameInput;
using Toufuku.GameInput.EditorTools;
using Toufuku.Rescue;
using Toufuku.Rescue.Mock;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Toufuku.Playtest.EditorTools
{
    /// <summary>
    /// T6-USB（#52）の検証シーンと USB 用ビルドを作る Editor 拡張。
    ///
    /// ・視点は T0 系・TestGame と同じ（カメラ (0, 5.3, 39) から -Z）。
    /// ・群衆は本番のプレイ画面（TestGame）と同じ MockCrowdDirector ＋ Customer_MockVisibility.prefab・頭上ゲージ・Bloom。
    ///   定位置を 36 にして、<see cref="UsbLoadCrowd"/> が黒客・退場者込み 30 体以上を保つ。
    /// ・入力 → 照準・飛翔 → SE・振動は #51 / #60 / #64 のセットアップをそのまま呼ぶ（本番と同じ経路で測る）。
    /// ・子ども用の的は近（6m）と遠（16m）。#60 の照準が届く 3〜18m に収める。
    ///
    /// 作り直したくなったらこのメニューを再実行する（手で調整したぶんは失われる）。既存のシーン・プレハブは変更しない。
    /// </summary>
    public static class UsbGateSceneBuilder
    {
        const string BuildMenuPath = "Toufuku/Playtest/Build T6-USB Gate Scene";
        const string OpenMenuPath = "Toufuku/Playtest/Open T6-USB Gate Scene";
        const string PlayerMenuPath = "Toufuku/Playtest/Build T6-USB Windows Player";

        const string AssetFolder = "Assets/Members/Jungaya/Playtest";
        const string SceneFolder = "Assets/Members/Jungaya/Scenes";
        const string ScenePath = SceneFolder + "/UsbGateTest.unity";

        const string GroundMatPath = AssetFolder + "/AbGround.mat";
        const string TargetMatPath = AssetFolder + "/AbTarget.mat";
        const string RingMatPath = AssetFolder + "/AbTargetRing.mat";

        // TestGame と同じ群衆の素材（#44 ApplyVisibilityMockToTestGame が作ったもの）
        const string MockFolder = "Assets/Members/Jungaya/Mock";
        const string CustomerPrefabPath = MockFolder + "/Customer_MockVisibility.prefab";
        const string OutlineMatPath = MockFolder + "/MockOutline.mat";
        const string VolumeProfilePath = MockFolder + "/MockPostProcess.asset";
        const string PalettePath = "Assets/Members/Jungaya/Scripts/Rescue/OmamoriPalette.asset";

        const string PlayerFolder = "Builds/T6-USB";
        const string PlayerName = "T6-USB.exe";

        static readonly Vector3 CameraPosition = new Vector3(0f, 5.3f, 39f);
        static readonly Vector3 CameraEuler = new Vector3(31.7f, 180f, 0f);
        const float CameraFov = 75f;
        static readonly Vector3 ShootPosition = new Vector3(0f, 1.2f, 36f);
        static readonly Vector3 ToriiPosition = new Vector3(0f, 1f, 7f);

        static readonly Vector3 NearTargetPosition = new Vector3(0f, 0f, 30f);
        static readonly Vector3 FarTargetPosition = new Vector3(0f, 0f, 20f);

        // 定位置: z は TestGame と同じ近 27〜33 / 中 19〜26 / 遠 12〜18、x も TestGame の壁に合わせた幅。数だけ 12 ずつに増やす
        struct BandDef
        {
            public string Label;
            public Vector2 ZRange;
            public Vector2 XRange;
            public int SlotCount;
        }

        static readonly BandDef[] Bands =
        {
            new BandDef { Label = "近", ZRange = new Vector2(27f, 33f), XRange = new Vector2(-6f, 6f), SlotCount = 12 },
            new BandDef { Label = "中", ZRange = new Vector2(19f, 26f), XRange = new Vector2(-7.5f, 7.5f), SlotCount = 12 },
            new BandDef { Label = "遠", ZRange = new Vector2(12f, 18f), XRange = new Vector2(-8.5f, 8.5f), SlotCount = 12 },
        };

        const int BlackCustomers = 6;

        [MenuItem(BuildMenuPath, priority = 430)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            if (AssetDatabase.LoadAssetAtPath<GameObject>(CustomerPrefabPath) == null)
            {
                EditorUtility.DisplayDialog(
                    "客プレハブがありません",
                    $"{CustomerPrefabPath} が見つかりません。\n先に \"Toufuku/Mock/Apply Visibility Mock to TestGame\" を実行してください（TestGame と同じ客を使うため）。",
                    "OK");
                return;
            }

            if (File.Exists(ScenePath))
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "T6-USB 検証シーンの再生成",
                    $"{ScenePath} は既に存在します。\n作り直すと、シーン上で手調整した内容（テストID・責任者・採用値を含む）は失われます。続けますか？",
                    "作り直す", "やめる");
                if (!overwrite) return;
            }

            BuildWithoutPrompt();

            Debug.Log(
                $"[T6-USB] 検証シーンを生成しました: {ScenePath}\n" +
                "Play したら F5 で安全チェック → F6 意図的 100 投 / F7・F8 ドリフト / F9 30 体負荷 / F10 子ども。" +
                "記録は PlaytestLogs/ に 1 イベント 1 行で追記されます。");
        }

        /// <summary>確認ダイアログなしで作り直す（自動確認用）。</summary>
        public static void BuildWithoutPrompt()
        {
            EnsureFolder(AssetFolder);
            EnsureFolder(SceneFolder);

            Material groundMat = LoadOrCreateMaterial(GroundMatPath, new Color(0.26f, 0.27f, 0.30f), 0.05f);
            Material targetMat = LoadOrCreateMaterial(TargetMatPath, new Color(0.80f, 0.80f, 0.84f), 0.2f);
            Material ringMat = LoadOrCreateMaterial(RingMatPath, new Color(0.55f, 0.42f, 0.25f), 0.1f);

            BuildScene(groundMat, targetMat, ringMat);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem(OpenMenuPath, priority = 431)]
        public static void Open()
        {
            if (!File.Exists(ScenePath))
            {
                EditorUtility.DisplayDialog("シーンがありません",
                    $"{ScenePath} が見つかりません。\n先に \"{BuildMenuPath}\" を実行してください。", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        /// <summary>採用案・採用クールダウン・USB を固定したビルド。T6-BLE も同じビルドで測る（17章）。</summary>
        [MenuItem(PlayerMenuPath, priority = 432)]
        public static void BuildPlayer()
        {
            if (!File.Exists(ScenePath))
            {
                EditorUtility.DisplayDialog("シーンがありません",
                    $"{ScenePath} が見つかりません。\n先に \"{BuildMenuPath}\" を実行してください。", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            string root = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string folder = Path.Combine(root, PlayerFolder);
            Directory.CreateDirectory(folder);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(folder, PlayerName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None
            };

            UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            if (summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.Log($"[T6-USB] ビルドしました: {options.locationPathName}（{summary.totalSize / (1024 * 1024)} MB）\n" +
                          "記録は persistentDataPath/PlaytestLogs/ に出ます。実施者パネル（Tab）に実際のパスが出ます。");
                EditorUtility.RevealInFinder(options.locationPathName);
            }
            else
            {
                Debug.LogError($"[T6-USB] ビルドに失敗しました: {summary.result}");
            }
        }

        static void BuildScene(Material groundMat, Material targetMat, Material ringMat)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.34f, 0.35f, 0.40f);
            RenderSettings.fog = false;

            // ── カメラ・ライト（TestGame と同じ視点。Bloom のためポストプロセスを有効にする）──
            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraGo.transform.SetPositionAndRotation(CameraPosition, Quaternion.Euler(CameraEuler));
            var camera = cameraGo.AddComponent<Camera>();
            camera.fieldOfView = CameraFov;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 1000f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.17f, 0.19f, 0.25f);
            cameraGo.AddComponent<AudioListener>();
            var cameraData = cameraGo.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = true;

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile != null)
            {
                var volumeGo = new GameObject("Global Volume (Bloom)");
                var volume = volumeGo.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.sharedProfile = profile;
            }
            else
            {
                Debug.LogWarning($"[T6-USB] {VolumeProfilePath} が見つかりません。Bloom 無しで作ります（TestGame より描画が軽くなる）");
            }

            var lightGo = new GameObject("Directional Light");
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, 0f, 22f);
            ground.transform.localScale = new Vector3(5f, 1f, 6f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;
            DestroyColliders(ground);

            var shootPos = new GameObject("ShootPos");
            shootPos.transform.position = ShootPosition;

            var torii = new GameObject("Torii");
            torii.transform.position = ToriiPosition;

            // ── 子ども用の的（近・遠）──
            var targetsRoot = new GameObject("Targets");
            GameObject near = BuildStandardTarget(targetsRoot.transform, "NearTarget", NearTargetPosition, targetMat, ringMat);
            GameObject far = BuildStandardTarget(targetsRoot.transform, "FarTarget", FarTargetPosition, targetMat, ringMat);

            // ── 群衆（TestGame と同じ客・輪郭・ゲージ）──
            var directorGo = new GameObject("MockDirector");
            var customers = new GameObject("Customers");
            customers.transform.SetParent(directorGo.transform, false);
            var crowd = directorGo.AddComponent<MockCrowdDirector>();
            var gaugeHud = directorGo.AddComponent<MockGaugeHud>();

            var crowdSo = new SerializedObject(crowd);
            SetObject(crowdSo, "customerPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(CustomerPrefabPath));
            SetObject(crowdSo, "customerParent", customers.transform);
            SetObject(crowdSo, "outlineMaterial", AssetDatabase.LoadAssetAtPath<Material>(OutlineMatPath));
            SetObject(crowdSo, "palette", AssetDatabase.LoadAssetAtPath<OmamoriPalette>(PalettePath));
            SetObject(crowdSo, "toriiTransform", torii.transform);
            SetBool(crowdSo, "prefillInstant", true);
            SetBool(crowdSo, "freezeGauges", false);
            SetInt(crowdSo, "blackCustomerCount", BlackCustomers);
            SetInt(crowdSo, "overrideTargetCount", UsbGatePlan.LoadBodies + 4);
            SetFloat(crowdSo, "respawnDelay", 0.2f);
            SetFloat(crowdSo, "minSpawnInterval", 0.05f);
            WriteBands(crowdSo);
            crowdSo.ApplyModifiedPropertiesWithoutUndo();

            var gaugeSo = new SerializedObject(gaugeHud);
            SetObject(gaugeSo, "director", crowd);
            SetObject(gaugeSo, "targetCamera", camera);
            gaugeSo.ApplyModifiedPropertiesWithoutUndo();

            // ── 実機（ESP32・USB）。未接続ならクリック / 1〜5 / A キーで代用される ──
            var gameManager = new GameObject("GameManager");
            gameManager.AddComponent<ConecteController>();

            // ── #51 入力 → #60 照準・飛翔 → #64 SE・振動（本番と同じ経路）──
            ThrowInputSceneSetup.Setup();
            AimSceneSetup.Setup();
            FeedbackSceneSetup.Setup();

            var input = Object.FindAnyObjectByType<ThrowInputController>(FindObjectsInactive.Include);
            var aim = Object.FindAnyObjectByType<OnusaAimController>(FindObjectsInactive.Include);
            var thrower = Object.FindAnyObjectByType<OnusaThrower>(FindObjectsInactive.Include);
            var feedback = Object.FindAnyObjectByType<GameFeedbackDirector>(FindObjectsInactive.Include);

            if (aim != null) SetObject(aim, "origin", shootPos.transform);
            if (thrower != null) SetObject(thrower, "spawnPoint", shootPos.transform);

            // ── T6-USB の進行・記録 ──
            var gateGo = new GameObject("UsbGate");
            var frameClock = gateGo.AddComponent<UsbFrameClock>();
            var shifter = gateGo.AddComponent<FeedbackTimingShifter>();
            var loadCrowd = gateGo.AddComponent<UsbLoadCrowd>();
            var se = gateGo.AddComponent<AudioSource>();
            se.playOnAwake = false;
            var director = gateGo.AddComponent<UsbGateDirector>();

            SetObject(shifter, "director", feedback);
            SetObject(shifter, "thrower", thrower);
            SetObject(loadCrowd, "director", crowd);
            SetObject(director, "input", input);
            SetObject(director, "aim", aim);
            SetObject(director, "controller", gameManager.GetComponent<ConecteController>());
            SetObject(director, "frameClock", frameClock);
            SetObject(director, "loadCrowd", loadCrowd);
            SetObject(director, "nearTarget", near.transform);
            SetObject(director, "farTarget", far.transform);
            SetObject(director, "seSource", se);

            if (input != null) SetObject(input, "throwFeedbackSource", shifter);

            // ── 開発用 HUD は止める。クールダウンは採用値で固定するので手動切替も切る ──
            if (input != null)
            {
                SetBool(input, "showDebugHud", false);
                SetBool(input, "logEvents", false);
                SetBool(input, "cooldownHotkeys", false);
            }
            if (feedback != null) SetBool(feedback, "showDebugHud", false);
            if (thrower != null) SetBool(thrower, "logThrows", false);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        static GameObject BuildStandardTarget(Transform parent, string name, Vector3 position, Material bodyMat, Material ringMat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Body";
            body.transform.SetParent(go.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            body.transform.localScale = new Vector3(1.4f, 0.8f, 1.4f);
            var bodyRenderer = body.GetComponent<MeshRenderer>();
            bodyRenderer.sharedMaterial = bodyMat;
            DestroyColliders(body);

            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Ring";
            ring.transform.SetParent(go.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.01f, 0f);
            ring.transform.localScale = new Vector3(UsbGatePlan.TargetRadius * 2f, 0.005f, UsbGatePlan.TargetRadius * 2f);
            ring.GetComponent<MeshRenderer>().sharedMaterial = ringMat;
            DestroyColliders(ring);

            var zone = go.AddComponent<HitZoneTarget>();
            SetFloat(zone, "outerRadius", UsbGatePlan.TargetRadius);

            var target = go.AddComponent<StandardTarget>();
            SetObject(target, "body", bodyRenderer);
            return go;
        }

        static void WriteBands(SerializedObject so)
        {
            SerializedProperty bands = so.FindProperty("bands");
            if (bands == null)
            {
                Debug.LogWarning("[T6-USB] MockCrowdDirector.bands が見つかりません");
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
            }
        }

        static void DestroyColliders(GameObject go)
        {
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(c);
        }

        static Material LoadOrCreateMaterial(string path, Color color, float smoothness)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(mat, path);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void SetObject(Object target, string propertyName, Object value)
        {
            var so = new SerializedObject(target);
            SetObject(so, propertyName, value);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetBool(Object target, string propertyName, bool value)
        {
            var so = new SerializedObject(target);
            SetBool(so, propertyName, value);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetFloat(Object target, string propertyName, float value)
        {
            var so = new SerializedObject(target);
            SetFloat(so, propertyName, value);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetObject(SerializedObject so, string propertyName, Object value)
        {
            SerializedProperty p = Find(so, propertyName);
            if (p != null) p.objectReferenceValue = value;
        }

        static void SetBool(SerializedObject so, string propertyName, bool value)
        {
            SerializedProperty p = Find(so, propertyName);
            if (p != null) p.boolValue = value;
        }

        static void SetInt(SerializedObject so, string propertyName, int value)
        {
            SerializedProperty p = Find(so, propertyName);
            if (p != null) p.intValue = value;
        }

        static void SetFloat(SerializedObject so, string propertyName, float value)
        {
            SerializedProperty p = Find(so, propertyName);
            if (p != null) p.floatValue = value;
        }

        static SerializedProperty Find(SerializedObject so, string propertyName)
        {
            SerializedProperty p = so.FindProperty(propertyName);
            if (p == null) Debug.LogWarning($"[T6-USB] {so.targetObject.GetType().Name}.{propertyName} が見つかりません", so.targetObject);
            return p;
        }

        static void EnsureFolder(string path)
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
