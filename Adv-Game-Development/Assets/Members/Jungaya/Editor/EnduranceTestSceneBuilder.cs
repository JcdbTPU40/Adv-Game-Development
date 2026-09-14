using System.IO;
using Toufuku.Aim;
using Toufuku.Aim.EditorTools;
using Toufuku.Feedback;
using Toufuku.Feedback.EditorTools;
using Toufuku.GameInput;
using Toufuku.GameInput.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Toufuku.Playtest.EditorTools
{
    /// <summary>
    /// T0-3M（#53）の検証シーンと USB 用ビルドを作る Editor 拡張。
    ///
    /// 的・視点・判定は T0-A/B（#49）・T0-CD（#50）と同じ標準ターゲット。参拝客・得点・3 分セッションは置かない。
    /// クールダウンは T0-CD の採用値、フィードバックの時刻差は T0-A/B の採用案で<b>固定</b>する
    /// （F1〜F4 の手動切替は切る）。1 テスト 1 仮説 = 3 分振り続けられるか、だけを見る。
    ///
    /// 作り直したくなったらこのメニューを再実行する（手で調整したぶんは失われる）。
    /// </summary>
    public static class EnduranceTestSceneBuilder
    {
        const string BuildMenuPath = "Toufuku/Playtest/Build T0-3M Endurance Scene";
        const string OpenMenuPath = "Toufuku/Playtest/Open T0-3M Endurance Scene";
        const string PlayerMenuPath = "Toufuku/Playtest/Build T0-3M Windows Player";

        const string AssetFolder = "Assets/Members/Jungaya/Playtest";
        const string SceneFolder = "Assets/Members/Jungaya/Scenes";
        const string ScenePath = SceneFolder + "/EnduranceTest.unity";

        // 的・地面のマテリアルは T0-A/B と同じものを使い回す（同じ的で測るため）
        const string GroundMatPath = AssetFolder + "/AbGround.mat";
        const string TargetMatPath = AssetFolder + "/AbTarget.mat";
        const string RingMatPath = AssetFolder + "/AbTargetRing.mat";

        const string PlayerFolder = "Builds/T0-3M";
        const string PlayerName = "T0-3M.exe";

        // T0-A/B・T0-CD と同じ視点・同じ的（#49 ToyFeelAbSceneBuilder / #50 CooldownTestSceneBuilder と同値）
        static readonly Vector3 CameraPosition = new Vector3(0f, 5.3f, 39f);
        static readonly Vector3 CameraEuler = new Vector3(31.7f, 180f, 0f);
        const float CameraFov = 75f;
        static readonly Vector3 ShootPosition = new Vector3(0f, 1.2f, 36f);

        static readonly Vector3[] TargetPositions =
        {
            new Vector3(0f, 0f, 28f),
            new Vector3(-5f, 0f, 26f),
            new Vector3(5f, 0f, 26f)
        };

        const float TargetRadius = 0.90f;

        [MenuItem(BuildMenuPath, priority = 420)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            if (File.Exists(ScenePath))
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "T0-3M 検証シーンの再生成",
                    $"{ScenePath} は既に存在します。\n" +
                    "作り直すと、シーン上で手調整した内容（テストID・責任者・採用値を含む）は失われます。続けますか？",
                    "作り直す", "やめる");
                if (!overwrite) return;
            }

            BuildWithoutPrompt();

            Debug.Log(
                $"[T0-3M] 検証シーンを生成しました: {ScenePath}\n" +
                "Play したら Space で開始。E 終了希望 / G 持ち替え / D 腕の下がり / Tab 実施者パネル。" +
                "記録は PlaytestLogs/ へ 1 人 1 行で追記されます。");
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

        [MenuItem(OpenMenuPath, priority = 421)]
        public static void Open()
        {
            if (!File.Exists(ScenePath))
            {
                EditorUtility.DisplayDialog(
                    "シーンがありません",
                    $"{ScenePath} が見つかりません。\n先に \"{BuildMenuPath}\" を実行してください。",
                    "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        /// <summary>採用案・採用クールダウン・USB を固定したビルド（Issue #53 の 1 つ目のやること）。</summary>
        [MenuItem(PlayerMenuPath, priority = 422)]
        public static void BuildPlayer()
        {
            if (!File.Exists(ScenePath))
            {
                EditorUtility.DisplayDialog(
                    "シーンがありません",
                    $"{ScenePath} が見つかりません。\n先に \"{BuildMenuPath}\" を実行してください。",
                    "OK");
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
                Debug.Log($"[T0-3M] ビルドしました: {options.locationPathName}（{summary.totalSize / (1024 * 1024)} MB）\n" +
                          "記録は persistentDataPath/PlaytestLogs/ に出ます。実施者パネル（Tab）に実際のパスが出ます。");
                EditorUtility.RevealInFinder(options.locationPathName);
            }
            else
            {
                Debug.LogError($"[T0-3M] ビルドに失敗しました: {summary.result}");
            }
        }

        static void BuildScene(Material groundMat, Material targetMat, Material ringMat)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.34f, 0.35f, 0.40f);
            RenderSettings.fog = false;

            // ── カメラ・ライト ──
            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraGo.transform.SetPositionAndRotation(CameraPosition, Quaternion.Euler(CameraEuler));
            var camera = cameraGo.AddComponent<Camera>();
            camera.fieldOfView = CameraFov;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 1000f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.17f, 0.19f, 0.25f);
            cameraGo.AddComponent<AudioListener>();

            var lightGo = new GameObject("Directional Light");
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;

            // ── 地面 ──
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, 0f, 22f);
            ground.transform.localScale = new Vector3(5f, 1f, 6f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;
            DestroyColliders(ground);

            var shootPos = new GameObject("ShootPos");
            shootPos.transform.position = ShootPosition;

            // ── 標準ターゲット（参拝客・得点なし。T0-A/B と同じ的・同じ判定）──
            var targetsRoot = new GameObject("Targets");
            for (int i = 0; i < TargetPositions.Length; i++)
                BuildStandardTarget(targetsRoot.transform, i, TargetPositions[i], targetMat, ringMat);

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

            // ── T0-3M の進行・記録 ──
            var testGo = new GameObject("EnduranceTest");

            // T0-A/B の採用案で固定する（既定 = 案A: 投擲SE・軌跡・振動が 3 つとも同時 = 本番の現状）
            var shifter = testGo.AddComponent<FeedbackTimingShifter>();
            var director = testGo.AddComponent<EnduranceTestDirector>();

            SetObject(shifter, "director", feedback);
            SetObject(shifter, "thrower", thrower);
            SetObject(director, "input", input);

            if (input != null) SetObject(input, "throwFeedbackSource", shifter);

            // ── 参加者の気が散らないよう開発用 HUD は止める。クールダウンは採用値で固定するので手動切替も切る ──
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

        static void BuildStandardTarget(Transform parent, int index, Vector3 position, Material bodyMat, Material ringMat)
        {
            var go = new GameObject($"StandardTarget{index + 1}");
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
            ring.transform.localScale = new Vector3(TargetRadius * 2f, 0.005f, TargetRadius * 2f);
            ring.GetComponent<MeshRenderer>().sharedMaterial = ringMat;
            DestroyColliders(ring);

            var zone = go.AddComponent<HitZoneTarget>();
            SetFloat(zone, "outerRadius", TargetRadius);

            var target = go.AddComponent<StandardTarget>();
            SetObject(target, "body", bodyRenderer);
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
            SerializedProperty p = Find(target, propertyName, out SerializedObject so);
            if (p == null) return;
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetBool(Object target, string propertyName, bool value)
        {
            SerializedProperty p = Find(target, propertyName, out SerializedObject so);
            if (p == null) return;
            p.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetFloat(Object target, string propertyName, float value)
        {
            SerializedProperty p = Find(target, propertyName, out SerializedObject so);
            if (p == null) return;
            p.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static SerializedProperty Find(Object target, string propertyName, out SerializedObject so)
        {
            so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(propertyName);
            if (p == null) Debug.LogWarning($"[T0-3M] {target.GetType().Name}.{propertyName} が見つかりません", target);
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
