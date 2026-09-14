using System.IO;
using Toufuku.Aim;
using Toufuku.Aim.EditorTools;
using Toufuku.GameInput;
using Toufuku.GameInput.EditorTools;
using Toufuku.Rescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Toufuku.Rescue.EditorTools
{
    /// <summary>
    /// 二重円と優先救済（#55）の確認シーンを組み立てる Editor 拡張。
    ///
    /// 置くのは「地面・カメラ・入力・照準・スコア・足元の円・確認進行」だけ。
    /// 客は <see cref="PriorityRescueVerifier"/> が実行時に並べるので、シーンを作り直さなくても
    /// F8 で何度でも同じ配置をやり直せる。
    ///
    /// 確認の手順は Docs/55_二重円と優先救済.md にある。
    /// </summary>
    public static class PriorityRescueSceneBuilder
    {
        const string BuildMenuPath = "Toufuku/Rescue/Build Priority Rescue Scene";
        const string OpenMenuPath = "Toufuku/Rescue/Open Priority Rescue Scene";

        const string AssetFolder = "Assets/Members/Jungaya/Materials";
        const string ScoringFolder = "Assets/Members/Jungaya/Scripts/Scoring";
        const string SceneFolder = "Assets/Members/Jungaya/Scenes";
        const string ScenePath = SceneFolder + "/PriorityRescueTest.unity";
        const string GroundMatPath = AssetFolder + "/PriorityRescueGround.mat";
        const string BonusTablePath = ScoringFolder + "/ScoreBonusTable.asset";

        // 本番（TestGame）と同じ視点。振る人はカメラの手前に立つ
        static readonly Vector3 CameraPosition = new Vector3(0f, 5.3f, 39f);
        static readonly Vector3 CameraEuler = new Vector3(31.7f, 180f, 0f);
        const float CameraFov = 75f;
        static readonly Vector3 ShootPosition = new Vector3(0f, 1.2f, 36f);

        [MenuItem(BuildMenuPath, priority = 250)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            if (File.Exists(ScenePath))
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "優先救済シーンの再生成",
                    $"{ScenePath} は既に存在します。\n作り直すと手調整は失われます。続けますか？",
                    "作り直す", "やめる");
                if (!overwrite) return;
            }

            EnsureFolder(AssetFolder);
            EnsureFolder(SceneFolder);

            Material groundMat = BuildGroundMaterial();
            ScoreBonusTable bonusTable = EnsureBonusTable();

            BuildScene(groundMat, bonusTable);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"[#55] 確認シーンを生成しました: {ScenePath}\n" +
                "Play して 1 キーで色を選び、クリックで投げる。F5=同値ケース／F6=移動ケース／F7=通常／F8=客を作り直す。");
        }

        [MenuItem(OpenMenuPath, priority = 251)]
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

        static void BuildScene(Material groundMat, ScoreBonusTable bonusTable)
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
            camera.backgroundColor = new Color(0.14f, 0.16f, 0.21f);
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
            foreach (Collider c in ground.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

            // ── 振る人の基準点 ──
            var shootPos = new GameObject("ShootPos");
            shootPos.transform.position = ShootPosition;

            // ── 実機（ESP32）。未接続ならクリック / 1〜5 キーで代用される ──
            var gameManager = new GameObject("GameManager");
            gameManager.AddComponent<ConecteController>();

            // ── #51 入力 → #60 照準・飛翔（本番と同じ経路）──
            ThrowInputSceneSetup.Setup();
            AimSceneSetup.Setup();

            var aim = Object.FindAnyObjectByType<OnusaAimController>(FindObjectsInactive.Include);
            var thrower = Object.FindAnyObjectByType<OnusaThrower>(FindObjectsInactive.Include);
            if (aim != null) SetObject(aim, "origin", shootPos.transform);
            if (thrower != null) SetObject(thrower, "spawnPoint", shootPos.transform);

            // ── 得点（優先救済 +50 の加点はここで乗る）──
            var scoreGo = new GameObject("ScoreManager");
            var score = scoreGo.AddComponent<ScoreManager>();
            if (bonusTable != null) SetObject(score, "bonusTable", bonusTable);
            scoreGo.AddComponent<ScoreHud>();

            // ── 足元の円（危険円・二重円）を全員へ配る ──
            var ringGo = new GameObject("GroundRingDirector");
            ringGo.AddComponent<GroundRingDirector>();

            // ── 頭上の D/R 表示（確認用。本番は足元円＋頭上ゲージに分ける）──
            var gaugeGo = new GameObject("CustomerGaugeHud");
            gaugeGo.AddComponent<CustomerGaugeHud>();

            // ── 確認の進行（同値ケース・移動ケース）──
            var verifyGo = new GameObject("PriorityRescueVerifier");
            var verifier = verifyGo.AddComponent<PriorityRescueVerifier>();
            SetObject(verifier, "arcCenter", shootPos.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        /// <summary>加点数値表（付録B B-2）。無ければ既定値（優先救済 +50）で作る。</summary>
        static ScoreBonusTable EnsureBonusTable()
        {
            var table = AssetDatabase.LoadAssetAtPath<ScoreBonusTable>(BonusTablePath);
            if (table != null) return table;

            table = ScriptableObject.CreateInstance<ScoreBonusTable>();
            AssetDatabase.CreateAsset(table, BonusTablePath);
            Debug.Log($"[#55] 加点数値表を作りました: {BonusTablePath}（優先救済 +50。T2 で支配的なら +30 へ）");
            return table;
        }

        static Material BuildGroundMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) return null;

            var material = AssetDatabase.LoadAssetAtPath<Material>(GroundMatPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, GroundMatPath);
            }

            material.shader = shader;
            // 足元の円（赤系・金系）が沈まないよう、地面は彩度の低い暗めの色にする。
            material.color = new Color(0.22f, 0.23f, 0.26f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void SetObject(Object target, string propertyName, Object value)
        {
            if (target == null) return;

            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogWarning($"[#55] {target.GetType().Name}.{propertyName} が見つかりません", target);
                return;
            }
            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
