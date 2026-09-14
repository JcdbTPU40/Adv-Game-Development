using System.IO;
using Toufuku.Aim;
using Toufuku.Aim.EditorTools;
using Toufuku.Feedback;
using Toufuku.Feedback.EditorTools;
using Toufuku.GameInput;
using Toufuku.GameInput.EditorTools;
using Toufuku.Playtest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Toufuku.Playtest.EditorTools
{
    /// <summary>
    /// T0-A/B（#49）の検証シーンを丸ごと組み立てる Editor 拡張。
    ///
    /// 「参拝客・得点なしの標準ターゲット検証シーン（同じ的・重量・判定）」を作る。
    /// ・参拝客・スコア・セッション（3 分）は置かない。置かないことで得点も福の連なりも発生しない
    ///   （ScoreManager が無ければ <see cref="OmamoriHitResolver"/> は何も計上しない）。
    /// ・的は <see cref="StandardTarget"/> を 3 つ、毎回同じ位置・同じ判定半径で置く。
    /// ・入力 → 照準・飛翔 → SE・振動 は #51 / #60 / #64 のセットアップをそのまま呼ぶので、本番と同じ経路になる。
    /// ・その間に <see cref="FeedbackTimingShifter"/> を挟み、案A / 案B の時刻差を切り替える。
    ///
    /// 作り直したくなったらこのメニューを再実行する（手で調整したぶんは失われる）。
    /// </summary>
    public static class ToyFeelAbSceneBuilder
    {
        const string BuildMenuPath = "Toufuku/Playtest/Build T0-AB Toy Feel Scene";
        const string OpenMenuPath = "Toufuku/Playtest/Open T0-AB Toy Feel Scene";

        const string AssetFolder = "Assets/Members/Jungaya/Playtest";
        const string SceneFolder = "Assets/Members/Jungaya/Scenes";
        const string ScenePath = SceneFolder + "/ToyFeelAbTest.unity";
        const string GroundMatPath = AssetFolder + "/AbGround.mat";
        const string TargetMatPath = AssetFolder + "/AbTarget.mat";
        const string RingMatPath = AssetFolder + "/AbTargetRing.mat";

        // 本番（TestGame）の視点に合わせる。振る人はカメラの位置に立つ
        static readonly Vector3 CameraPosition = new Vector3(0f, 5.3f, 39f);
        static readonly Vector3 CameraEuler = new Vector3(31.7f, 180f, 0f);
        const float CameraFov = 75f;

        // 大幣を振る基準点（照準の距離と左右はここから測る）
        static readonly Vector3 ShootPosition = new Vector3(0f, 1.2f, 36f);

        // 標準ターゲット（同じ的・同じ判定を毎回再現するため座標を焼く）
        static readonly Vector3[] TargetPositions =
        {
            new Vector3(0f, 0f, 28f),
            new Vector3(-5f, 0f, 26f),
            new Vector3(5f, 0f, 26f)
        };

        /// <summary>判定半径（本番の HitZoneTarget の既定と同じ）。</summary>
        const float TargetRadius = 0.90f;

        [MenuItem(BuildMenuPath, priority = 400)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            if (File.Exists(ScenePath))
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "T0-A/B 検証シーンの再生成",
                    $"{ScenePath} は既に存在します。\n" +
                    "作り直すと、シーン上で手調整した内容（案A / 案B の値を含む）は失われます。続けますか？",
                    "作り直す", "やめる");
                if (!overwrite) return;
            }

            EnsureFolder(AssetFolder);
            EnsureFolder(SceneFolder);

            Material groundMat = BuildMaterial(GroundMatPath, new Color(0.26f, 0.27f, 0.30f), 0.05f);
            Material targetMat = BuildMaterial(TargetMatPath, new Color(0.80f, 0.80f, 0.84f), 0.2f);
            Material ringMat = BuildMaterial(RingMatPath, new Color(0.55f, 0.42f, 0.25f), 0.1f);

            BuildScene(groundMat, targetMat, ringMat);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"[T0-A/B] 検証シーンを生成しました: {ScenePath}\n" +
                "Play したら Space で開始。Tab で実施者パネル。記録は PlaytestLogs/ へ 1 人 1 行で追記されます。");
        }

        [MenuItem(OpenMenuPath, priority = 401)]
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

            // ── 振る人の基準点 ──
            var shootPos = new GameObject("ShootPos");
            shootPos.transform.position = ShootPosition;

            // ── 標準ターゲット（参拝客ではない。救済も得点も無い）──
            var targetsRoot = new GameObject("Targets");
            for (int i = 0; i < TargetPositions.Length; i++)
                BuildTarget(targetsRoot.transform, i, TargetPositions[i], targetMat, ringMat);

            // ── 実機（ESP32）。未接続ならクリック / 1〜5 / A キーで代用される ──
            var gameManager = new GameObject("GameManager");
            gameManager.AddComponent<ConecteController>();

            // ── #51 入力 → #60 照準・飛翔 → #64 SE・振動（本番と同じ経路を組む）──
            ThrowInputSceneSetup.Setup();
            AimSceneSetup.Setup();
            FeedbackSceneSetup.Setup();

            var input = Object.FindAnyObjectByType<ThrowInputController>(FindObjectsInactive.Include);
            var aim = Object.FindAnyObjectByType<OnusaAimController>(FindObjectsInactive.Include);
            var thrower = Object.FindAnyObjectByType<OnusaThrower>(FindObjectsInactive.Include);
            var director = Object.FindAnyObjectByType<GameFeedbackDirector>(FindObjectsInactive.Include);

            if (aim != null) SetObject(aim, "origin", shootPos.transform);
            if (thrower != null) SetObject(thrower, "spawnPoint", shootPos.transform);

            // ── A/B の進行と時刻差 ──
            var abGo = new GameObject("AbTest");
            var shifter = abGo.AddComponent<FeedbackTimingShifter>();
            var abDirector = abGo.AddComponent<ToyFeelAbDirector>();

            SetObject(shifter, "director", director);
            SetObject(shifter, "thrower", thrower);
            SetObject(abDirector, "shifter", shifter);
            SetObject(abDirector, "input", input);

            // 投擲SE と発射振動は Shifter 経由で出す（FeedbackSceneSetup が入れた直結を上書きする）
            if (input != null) SetObject(input, "throwFeedbackSource", shifter);

            // ── 参加者の気が散らないよう、開発用 HUD とログを止める ──
            if (input != null)
            {
                SetBool(input, "showDebugHud", false);
                SetBool(input, "logEvents", false);
                SetBool(input, "cooldownHotkeys", false); // F1〜F4 のクールダウン切替は T0-CD(#50) のもの
            }
            if (director != null) SetBool(director, "showDebugHud", false);
            if (thrower != null) SetBool(thrower, "logThrows", false);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        static void BuildTarget(Transform parent, int index, Vector3 position, Material bodyMat, Material ringMat)
        {
            var go = new GameObject($"StandardTarget{index + 1}");
            go.transform.SetParent(parent, false);
            go.transform.position = position;

            // 本体（判定の中心は足元 = 着弾点と同じ地面の高さ）
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Body";
            body.transform.SetParent(go.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            body.transform.localScale = new Vector3(1.4f, 0.8f, 1.4f);
            var bodyRenderer = body.GetComponent<MeshRenderer>();
            bodyRenderer.sharedMaterial = bodyMat;
            DestroyColliders(body);

            // 判定半径の目印（地面の輪）
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

        static Material BuildMaterial(string path, Color color, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (shader != null && mat.shader != shader)
            {
                mat.shader = shader;
            }

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void SetObject(Object target, string propertyName, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(propertyName);
            if (p == null)
            {
                Debug.LogWarning($"[T0-A/B] {target.GetType().Name}.{propertyName} が見つかりません", target);
                return;
            }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetBool(Object target, string propertyName, bool value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(propertyName);
            if (p == null)
            {
                Debug.LogWarning($"[T0-A/B] {target.GetType().Name}.{propertyName} が見つかりません", target);
                return;
            }
            p.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetFloat(Object target, string propertyName, float value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(propertyName);
            if (p == null)
            {
                Debug.LogWarning($"[T0-A/B] {target.GetType().Name}.{propertyName} が見つかりません", target);
                return;
            }
            p.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
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
