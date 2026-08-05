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
    /// 視認性モック(#44)のシーンを丸ごと組み立てる Editor 拡張。
    ///
    /// シーンファイルを手で作らずプログラム的に構築するのは、
    /// 「配置やパラメータを変えたくなったら作り直せばよい」状態にしておきたいため。
    /// 実測しながらの調整はシーン上の各コンポーネントの Inspector で行い、
    /// 骨組みごとやり直したくなったらこのメニューを再実行する。
    ///
    /// 生成物:
    ///   Assets/Members/Jungaya/Mock/MockOutline.mat        輪郭マテリアル
    ///   Assets/Members/Jungaya/Mock/MockCustomerBody.mat   客本体マテリアル（Emission 比較用に _EMISSION 有効）
    ///   Assets/Members/Jungaya/Mock/MockGround.mat         地面マテリアル
    ///   Assets/Members/Jungaya/Mock/MockCustomer.prefab    仮の客（カプセル）
    ///   Assets/Members/Jungaya/Scenes/VisibilityMock.unity モックシーン
    ///
    /// 片付け方: 上記 Mock/ フォルダ・Scripts/Mock/ フォルダ・このファイル・シーンを削除すれば元に戻る。
    /// 既存の本番系スクリプトには一切手を入れていない。
    /// </summary>
    public static class VisibilityMockSceneBuilder
    {
        private const string BuildMenuPath = "Toufuku/Mock/Build Visibility Mock Scene";
        private const string OpenMenuPath = "Toufuku/Mock/Open Visibility Mock Scene";

        private const string MockFolder = "Assets/Members/Jungaya/Mock";
        private const string SceneFolder = "Assets/Members/Jungaya/Scenes";
        private const string ScenePath = SceneFolder + "/VisibilityMock.unity";

        private const string OutlineMatPath = MockFolder + "/MockOutline.mat";
        private const string BodyMatPath = MockFolder + "/MockCustomerBody.mat";
        private const string GroundMatPath = MockFolder + "/MockGround.mat";
        private const string PrefabPath = MockFolder + "/MockCustomer.prefab";
        private const string VolumeProfilePath = MockFolder + "/MockPostProcess.asset";

        private const string OutlineShaderName = "Toufuku/Mock/OutlineHull";

        // ── 客プレハブの CustomerMood 初期値 ────────────────────────
        // 既定(naturalRiseRate=5, startGauge=50)のままだと全員 10 秒で怒って消えてしまい、
        // 「15 体を並べて識別できるか」を測れない。モック用に上昇を緩めた値を焼く。
        // ※ CustomerMood 本体は無改変。private [SerializeField] を SerializedObject 経由で設定する。
        private const float MoodMaxGauge = 100f;
        private const float MoodStartGauge = 50f;
        private const float MoodNaturalRiseRate = 1.5f;
        private const float MoodResolveLinger = 0.4f;

        // ── カメラ（本番のプレイヤー視点相当。TestGame.unity の Main Camera に合わせてある）──
        private static readonly Vector3 CameraPosition = new Vector3(0f, 5.3f, 39f);
        private static readonly Vector3 CameraEuler = new Vector3(31.7f, 180f, 0f);
        private const float CameraFov = 75f;

        private static readonly Vector3 ToriiPosition = new Vector3(0f, 1f, 7f);

        [MenuItem(BuildMenuPath, priority = 100)]
        public static void Build()
        {
            // 未保存のシーンを巻き込まないよう先に確認する。
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            if (File.Exists(ScenePath))
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "視認性モックシーンの再生成",
                    $"{ScenePath} は既に存在します。\n" +
                    "作り直すと、シーン上で手調整した内容は失われます。続けますか？",
                    "作り直す", "やめる");
                if (!overwrite) return;
            }

            Shader outlineShader = Shader.Find(OutlineShaderName);
            if (outlineShader == null)
            {
                EditorUtility.DisplayDialog(
                    "シェーダが見つかりません",
                    $"'{OutlineShaderName}' が見つかりませんでした。\n" +
                    "Assets/Members/Jungaya/Scripts/Mock/MockOutlineHull.shader が\n" +
                    "インポート済みか確認してください。",
                    "OK");
                return;
            }

            EnsureFolder(MockFolder);
            EnsureFolder(SceneFolder);

            Material outlineMat = BuildOutlineMaterial(outlineShader);
            Material bodyMat = BuildBodyMaterial();
            Material groundMat = BuildGroundMaterial();
            GameObject customerPrefab = BuildCustomerPrefab(bodyMat, outlineMat);

            BuildScene(customerPrefab, outlineMat, groundMat);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"[VisibilityMock] シーンを生成しました: {ScenePath}\n" +
                "Play して 1/2/3 キーで体数 8/12/15、F で祭事、Space で一時停止。");
        }

        [MenuItem(OpenMenuPath, priority = 101)]
        public static void Open()
        {
            if (!File.Exists(ScenePath))
            {
                EditorUtility.DisplayDialog(
                    "シーンがありません",
                    $"{ScenePath} が見つかりません。\n" +
                    "先に \"Toufuku/Mock/Build Visibility Mock Scene\" を実行してください。",
                    "OK");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        // ── マテリアル ──────────────────────────────────────────

        private static Material BuildOutlineMaterial(Shader shader)
        {
            Material mat = EnsureMaterial(OutlineMatPath, shader);
            // 実際の色と太さは MaterialPropertyBlock で客ごとに上書きされる。ここは既定値。
            if (mat.HasProperty("_OutlineColor")) mat.SetColor("_OutlineColor", Color.white);
            if (mat.HasProperty("_OutlineWidth")) mat.SetFloat("_OutlineWidth", 0.09f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material BuildBodyMaterial()
        {
            Material mat = EnsureMaterial(BodyMatPath, FindLitShader());

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.72f, 0.70f, 0.66f));
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", new Color(0.72f, 0.70f, 0.66f));
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.08f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);

            // 案B（Emission 比較）用。キーワードはマテリアル単位でしか切れないので常に有効にしておき、
            // 実際の発光色は MaterialPropertyBlock で客ごとに出し入れする（既定は黒＝光らない）。
            mat.EnableKeyword("_EMISSION");
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.black);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material BuildGroundMaterial()
        {
            Material mat = EnsureMaterial(GroundMatPath, FindLitShader());
            var color = new Color(0.26f, 0.27f, 0.30f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Bloom だけを載せた VolumeProfile を用意する。
        /// 輪郭（案A）も Emission（案B）も「光って見えるか」が検証対象なので、
        /// 両案に同じ Bloom がかかる状態にそろえておく。強度は Inspector で調整可能。
        /// </summary>
        private static VolumeProfile BuildPostProcessProfile()
        {
            try
            {
                var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
                if (profile == null)
                {
                    profile = ScriptableObject.CreateInstance<VolumeProfile>();
                    AssetDatabase.CreateAsset(profile, VolumeProfilePath);
                }

                if (!profile.TryGet(out Bloom bloom))
                {
                    bloom = profile.Add<Bloom>(true);
                    // VolumeComponent はプロファイルのサブアセットとして登録しないと保存後に失われる。
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
                return profile;
            }
            catch (System.Exception e)
            {
                // URP が入っていない等で失敗しても、モックの本体は成立させたいので握りつぶす。
                Debug.LogWarning($"[VisibilityMock] Bloom の設定に失敗しました（Bloom 無しで続行します）: {e.Message}");
                return null;
            }
        }

        private static Shader FindLitShader()
        {
            Shader s = Shader.Find("Universal Render Pipeline/Lit");
            if (s == null) s = Shader.Find("Standard");
            return s;
        }

        private static Material EnsureMaterial(string path, Shader shader)
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

        // ── 客プレハブ ──────────────────────────────────────────

        private static GameObject BuildCustomerPrefab(Material bodyMat, Material outlineMat)
        {
            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            temp.name = "MockCustomer";

            // 当たり判定は使わないので落とす（15体ぶんの無駄を省く）。
            Collider collider = temp.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);

            var renderer = temp.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = bodyMat;

            // 既存の不満ゲージ(#11)をそのまま使う。値だけモック向けに緩める。
            var mood = temp.AddComponent<CustomerMood>();
            var moodSo = new SerializedObject(mood);
            SetFloat(moodSo, "maxGauge", MoodMaxGauge);
            SetFloat(moodSo, "startGauge", MoodStartGauge);
            SetFloat(moodSo, "naturalRiseRate", MoodNaturalRiseRate);
            SetFloat(moodSo, "resolveLingerTime", MoodResolveLinger);
            moodSo.ApplyModifiedPropertiesWithoutUndo();

            temp.AddComponent<MockCustomerTag>();
            temp.AddComponent<MockCustomerWalker>();

            var outline = temp.AddComponent<MockCustomerOutline>();
            var outlineSo = new SerializedObject(outline);
            SetObject(outlineSo, "outlineMaterial", outlineMat);
            SetObject(outlineSo, "bodyRenderer", renderer);
            outlineSo.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, PrefabPath);
            Object.DestroyImmediate(temp);

            return prefab;
        }

        // ── シーン ──────────────────────────────────────────────

        private static void BuildScene(GameObject customerPrefab, Material outlineMat, Material groundMat)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 空シーンは環境光が真っ暗なので、見え方の基準になるぶんだけ足しておく。
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.34f, 0.35f, 0.40f);
            RenderSettings.fog = false;

            // ── カメラ（本番のプレイヤー視点相当）──
            var cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            cameraGo.transform.SetPositionAndRotation(CameraPosition, Quaternion.Euler(CameraEuler));

            var camera = cameraGo.AddComponent<Camera>();
            camera.fieldOfView = CameraFov;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 1000f;
            // 背景は単色にしておく。黒客が背景に埋もれるかどうかは #44 の検証条件なので、
            // 空模様のような揺らぎを混ぜず、Inspector で1色だけ振れる状態にする。
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.17f, 0.19f, 0.25f);
            cameraGo.AddComponent<AudioListener>();

            // URP のカメラ追加データを明示的に付け、ポストプロセスを有効にする。
            // これが無いと案B(Emission)に Bloom が乗らず、案A との比較が成立しない。
            var urpCameraData = cameraGo.AddComponent<UniversalAdditionalCameraData>();
            urpCameraData.renderPostProcessing = true;

            // ── Bloom（輪郭発光の“光って見える”ぶんを両案そろえるため）──
            VolumeProfile profile = BuildPostProcessProfile();
            if (profile != null)
            {
                var volumeGo = new GameObject("Global Volume (Bloom)");
                var volume = volumeGo.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 0f;
                volume.sharedProfile = profile;
            }

            // ── ライト ──
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
            ground.transform.localScale = new Vector3(5f, 1f, 6f);   // 50m × 60m
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;
            Collider groundCollider = ground.GetComponent<Collider>();
            if (groundCollider != null) Object.DestroyImmediate(groundCollider);

            // ── 鳥居（スポーン位置のマーカー。動かせば補充の出発点が変わる）──
            var torii = new GameObject("Torii (Spawn Point)");
            torii.transform.position = ToriiPosition;

            // ── Director ＋ HUD ──
            var directorGo = new GameObject("MockDirector");
            var customersRoot = new GameObject("Customers");
            customersRoot.transform.SetParent(directorGo.transform, false);

            var crowd = directorGo.AddComponent<MockCrowdDirector>();
            var gaugeHud = directorGo.AddComponent<MockGaugeHud>();
            var debugHud = directorGo.AddComponent<MockVisibilityDebugHud>();

            var crowdSo = new SerializedObject(crowd);
            SetObject(crowdSo, "customerPrefab", customerPrefab);
            SetObject(crowdSo, "customerParent", customersRoot.transform);
            SetObject(crowdSo, "outlineMaterial", outlineMat);
            SetObject(crowdSo, "toriiTransform", torii.transform);
            crowdSo.ApplyModifiedPropertiesWithoutUndo();

            var gaugeSo = new SerializedObject(gaugeHud);
            SetObject(gaugeSo, "director", crowd);
            SetObject(gaugeSo, "targetCamera", camera);
            gaugeSo.ApplyModifiedPropertiesWithoutUndo();

            var debugSo = new SerializedObject(debugHud);
            SetObject(debugSo, "director", crowd);
            SetObject(debugSo, "gaugeHud", gaugeHud);
            debugSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        // ── ユーティリティ ──────────────────────────────────────

        private static void SetFloat(SerializedObject so, string propertyName, float value)
        {
            SerializedProperty p = so.FindProperty(propertyName);
            if (p == null)
            {
                Debug.LogWarning($"[VisibilityMock] プロパティ '{propertyName}' が見つかりませんでした（名前が変わった可能性）。");
                return;
            }
            p.floatValue = value;
        }

        private static void SetObject(SerializedObject so, string propertyName, Object value)
        {
            SerializedProperty p = so.FindProperty(propertyName);
            if (p == null)
            {
                Debug.LogWarning($"[VisibilityMock] プロパティ '{propertyName}' が見つかりませんでした（名前が変わった可能性）。");
                return;
            }
            p.objectReferenceValue = value;
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
