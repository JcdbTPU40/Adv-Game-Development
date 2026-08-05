using System.IO;
using Toufuku.Rescue.Outline;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Toufuku.Rescue.OutlineEditor
{
    /// <summary>
    /// #45 アウトライン負荷検証シーンを組み立てる Editor 拡張。
    /// VisibilityMockSceneBuilder の作りに倣い、配置は作り直し前提でプログラム生成する。
    /// </summary>
    public static class OutlinePerfSceneBuilder
    {
        const string BuildMenuPath = "Toufuku/Perf/Build Outline Perf Scene";
        const string OpenMenuPath = "Toufuku/Perf/Open Outline Perf Scene";

        const string PerfFolder = "Assets/Members/Jungaya/Perf";
        const string SceneFolder = "Assets/Members/Jungaya/Scenes";
        const string ScenePath = SceneFolder + "/OutlinePerf.unity";

        const string BodyMatPath = PerfFolder + "/OutlinePerfBody.mat";
        const string GroundMatPath = PerfFolder + "/OutlinePerfGround.mat";
        const string OccluderMatPath = PerfFolder + "/OutlinePerfOccluder.mat";
        const string PrefabPath = PerfFolder + "/OutlinePerfCustomer.prefab";
        const string RendererPath = "Assets/Settings/PC_Renderer.asset";

        static readonly Vector3 CameraPosition = new Vector3(0f, 5.3f, 39f);
        static readonly Vector3 CameraEuler = new Vector3(31.7f, 180f, 0f);
        const float CameraFov = 75f;

        [MenuItem(BuildMenuPath, priority = 200)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            if (File.Exists(ScenePath))
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "Outline Perf シーンの再生成",
                    $"{ScenePath} は既に存在します。\n作り直すと手調整は失われます。続けますか？",
                    "作り直す", "やめる");
                if (!overwrite) return;
            }

            EnsureFolder(PerfFolder);
            EnsureFolder(SceneFolder);

            Material bodyMat = BuildBodyMaterial();
            Material groundMat = BuildGroundMaterial();
            Material occluderMat = BuildOccluderMaterial();
            GameObject customerPrefab = BuildCustomerPrefab(bodyMat);

            EnsureOutlineFeatureOnRenderer();

            BuildScene(customerPrefab, groundMat, occluderMat);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"[OutlinePerf] シーンを生成しました: {ScenePath}\n" +
                "Play して 1/2/3=体数、O=アウトライン、F=雨天、P=計測、C=CSV。");
        }

        [MenuItem(OpenMenuPath, priority = 201)]
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

        /// <summary>
        /// PC_Renderer に OutlineRendererFeature が無ければ追加する。
        /// Native RenderPass はステンシル Compose と干渉しうるため OFF を維持する。
        /// </summary>
        static void EnsureOutlineFeatureOnRenderer()
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null)
            {
                Debug.LogWarning($"[OutlinePerf] {RendererPath} が見つかりません。");
                return;
            }

            // Forward+ を維持（URP 17: ForwardPlus=2）。Deferred だとステンシル下位ビットが衝突する。
            if (rendererData.renderingMode != RenderingMode.ForwardPlus)
            {
                rendererData.renderingMode = RenderingMode.ForwardPlus;
                Debug.Log("[OutlinePerf] PC_Renderer を Forward+ に変更しました（ステンシルアウトラインのため）。");
            }

            // Native RenderPass はフルスクリーン depth-stencil バインドと干渉する場合があるため OFF。
            if (rendererData.useNativeRenderPass)
            {
                rendererData.useNativeRenderPass = false;
                Debug.Log("[OutlinePerf] Native RenderPass を OFF にしました（ステンシル Compose のため）。");
            }

            bool hasOutline = false;
            foreach (var feature in rendererData.rendererFeatures)
            {
                if (feature is OutlineRendererFeature)
                {
                    hasOutline = true;
                    feature.SetActive(true);
                    break;
                }
            }

            if (!hasOutline)
            {
                var feature = ScriptableObject.CreateInstance<OutlineRendererFeature>();
                feature.name = "OutlineRendererFeature";
                AssetDatabase.AddObjectToAsset(feature, rendererData);
                rendererData.rendererFeatures.Add(feature);
                rendererData.SetDirty();
                Debug.Log("[OutlinePerf] OutlineRendererFeature を PC_Renderer に追加しました。");
            }

            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();
        }

        static Material BuildBodyMaterial()
        {
            Material mat = EnsureMaterial(BodyMatPath, FindLitShader());
            var color = new Color(0.72f, 0.70f, 0.66f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.08f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material BuildGroundMaterial()
        {
            Material mat = EnsureMaterial(GroundMatPath, FindLitShader());
            var color = new Color(0.26f, 0.27f, 0.30f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material BuildOccluderMaterial()
        {
            Material mat = EnsureMaterial(OccluderMatPath, FindLitShader());
            var color = new Color(0.45f, 0.32f, 0.22f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static GameObject BuildCustomerPrefab(Material bodyMat)
        {
            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            temp.name = "OutlinePerfCustomer";

            var collider = temp.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);

            var renderer = temp.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = bodyMat;

            var outline = temp.AddComponent<OutlineTarget>();
            var so = new SerializedObject(outline);
            var rendProp = so.FindProperty("targetRenderer");
            if (rendProp != null) rendProp.objectReferenceValue = renderer;
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, PrefabPath);
            Object.DestroyImmediate(temp);
            return prefab;
        }

        static void BuildScene(GameObject customerPrefab, Material groundMat, Material occluderMat)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.34f, 0.35f, 0.40f);
            RenderSettings.fog = false;

            // カメラ
            var cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            cameraGo.transform.SetPositionAndRotation(CameraPosition, Quaternion.Euler(CameraEuler));
            var camera = cameraGo.AddComponent<Camera>();
            camera.fieldOfView = CameraFov;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 1000f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.17f, 0.19f, 0.25f);
            cameraGo.AddComponent<AudioListener>();
            cameraGo.AddComponent<UniversalAdditionalCameraData>();

            // ライト
            var lightGo = new GameObject("Directional Light");
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;

            // 地面
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, 0f, 22f);
            ground.transform.localScale = new Vector3(5f, 1f, 6f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;
            var groundCol = ground.GetComponent<Collider>();
            if (groundCol != null) Object.DestroyImmediate(groundCol);

            // 遮蔽物（鳥居・提灯を模した Cube/Cylinder）
            // 客の一部が裏に回ると輪郭が消えることを確認するための配置。
            BuildOccluder("ToriiPillar_L", PrimitiveType.Cube, new Vector3(-2.2f, 2.5f, 24f), new Vector3(0.6f, 5f, 0.6f), occluderMat);
            BuildOccluder("ToriiPillar_R", PrimitiveType.Cube, new Vector3(2.2f, 2.5f, 24f), new Vector3(0.6f, 5f, 0.6f), occluderMat);
            BuildOccluder("ToriiBeam", PrimitiveType.Cube, new Vector3(0f, 4.8f, 24f), new Vector3(5.5f, 0.4f, 0.5f), occluderMat);
            BuildOccluder("Lantern_L", PrimitiveType.Cylinder, new Vector3(-5f, 2.2f, 20f), new Vector3(0.8f, 1.1f, 0.8f), occluderMat);
            BuildOccluder("Lantern_R", PrimitiveType.Cylinder, new Vector3(5.5f, 2.2f, 18f), new Vector3(0.8f, 1.1f, 0.8f), occluderMat);
            BuildOccluder("Lantern_Far", PrimitiveType.Cylinder, new Vector3(-3f, 2.0f, 15f), new Vector3(0.7f, 1.0f, 0.7f), occluderMat);

            // Director + HUD
            var root = new GameObject("OutlinePerfRoot");
            var customers = new GameObject("Customers");
            customers.transform.SetParent(root.transform, false);

            var director = root.AddComponent<OutlinePerfDirector>();
            var hud = root.AddComponent<OutlinePerfHud>();

            var dirSo = new SerializedObject(director);
            SetObject(dirSo, "customerPrefab", customerPrefab);
            SetObject(dirSo, "customerParent", customers.transform);
            var countProp = dirSo.FindProperty("targetCount");
            if (countProp != null) countProp.intValue = 16;
            dirSo.ApplyModifiedPropertiesWithoutUndo();

            var hudSo = new SerializedObject(hud);
            SetObject(hudSo, "director", director);
            hudSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        static void BuildOccluder(string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
        }

        static Shader FindLitShader()
        {
            Shader s = Shader.Find("Universal Render Pipeline/Lit");
            if (s == null) s = Shader.Find("Standard");
            return s;
        }

        static Material EnsureMaterial(string path, Shader shader)
        {
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
            return mat;
        }

        static void SetObject(SerializedObject so, string propertyName, Object value)
        {
            SerializedProperty p = so.FindProperty(propertyName);
            if (p == null)
            {
                Debug.LogWarning($"[OutlinePerf] プロパティ '{propertyName}' が見つかりません。");
                return;
            }
            p.objectReferenceValue = value;
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
