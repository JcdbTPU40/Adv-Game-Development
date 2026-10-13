using System.Collections.Generic;
using System.IO;
using Toufuku.Rescue;
using Toufuku.Rescue.Mock;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Toufuku.Scenery.EditorTools
{
    /*
        デザイン班の客モデル（FBX/Mob_1_01.fbx）を、ゲームの客として動かせるようにする Editor 拡張
          ・FBX の Take 001（待機）と Take 002（歩き）を、ループする Mob_Idle / Mob_Walk として取りこむ
          ・焦り・救済・黒客・うなずき・首ふりは、仮モデルと同じ動き（CustomerMotions）をこのリグに合わせて作る
            基準の姿勢は Mob_Idle の最初のフレーム（A ポーズのままだと腕が開きすぎるため）
          ・客のプレハブ Customer_Mob01 は、Customer_MockVisibility のバリアントに、このモデルを足もとが地面に来るように入れたもの
          ・輪郭はシルエットになるパーツ（体・頭・耳など）に付け、目・まつげ・口には付けない（顔の中に線が出るため）
          ・耳（Ears）はボーンに付いていないので、BoneAttachment で実行のはじめに頭のボーンの子にする
        FBX が差しかわったら、メニューからもう一度作れば同じ設定で作りなおせる
    */
    public static class CustomerMobBuilder
    {
        const string FbxPath = "Assets/Members/Jungaya/FBX/Mob_1_01.fbx";
        const string Dir = "Assets/Members/Jungaya/Scenery/Customer/Mob01";
        const string AnimDir = Dir + "/Animations";
        const string ControllerPath = Dir + "/Customer_Mob01.controller";
        const string ModelPrefabPath = Dir + "/Customer_Mob01Model.prefab";
        const string VariantPath = Dir + "/Customer_Mob01.prefab";
        const string BaseCustomerPath = "Assets/Members/Jungaya/Mock/Customer_MockVisibility.prefab";

        const string IdleClip = "Mob_Idle";
        const string WalkClip = "Mob_Walk";

        const string HipsPath = "Body_Group/Hips";
        const string Spine2 = HipsPath + "/Spine/Spine1/Spine2";

        static readonly Dictionary<CustomerBone, string> RigPaths = new Dictionary<CustomerBone, string>
        {
            { CustomerBone.Hips, HipsPath },
            { CustomerBone.Spine, HipsPath + "/Spine" },
            { CustomerBone.Chest, Spine2 },
            { CustomerBone.Neck, Spine2 + "/Neck" },
            { CustomerBone.Head, Spine2 + "/Neck/Head" },
            { CustomerBone.UpperArmR, Spine2 + "/RightShoulder/RightArm" },
            { CustomerBone.LowerArmR, Spine2 + "/RightShoulder/RightArm/RightForeArm" },
            { CustomerBone.UpperArmL, Spine2 + "/LeftShoulder/LeftArm" },
            { CustomerBone.LowerArmL, Spine2 + "/LeftShoulder/LeftArm/LeftForeArm" },
            { CustomerBone.LegR, HipsPath + "/RightUpLeg" },
            { CustomerBone.LegL, HipsPath + "/LeftUpLeg" },
        };

        // 輪郭を付けないで、体の色だけぬるパーツ
        static readonly HashSet<string> TintOnlyParts = new HashSet<string>
        {
            "Eyebrows", "Eyelashes_1", "Eyelashes_2", "Eyes_1", "Eyes_2", "Mouth1", "Mouth2",
        };
        const string MainBodyPart = "polySurface9";
        const string EarsPart = "Body_Group/Body_G/Ears";
        const string HeadBone = Spine2 + "/Neck/Head";
        const float HullDepthPush = 0.15f;

        [MenuItem("Toufuku/Scenery/Build Customer (Mob_1_01)")]
        public static void Build()
        {
            EnsureFolder(Dir);
            EnsureFolder(AnimDir);

            ImportClips();
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            AnimationClip idle = LoadFbxClip(IdleClip);
            AnimationClip walk = LoadFbxClip(WalkClip);

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            try
            {
                // 待機の最初の姿勢を基準にする
                idle.SampleAnimation(model, 0f);
                CustomerRig rig = CustomerRig.FromCurrentPose(model.transform, model.transform.Find(HipsPath), RigPaths);
                string P(string name) => $"{AnimDir}/Mob01_{name}.anim";
                CustomerAnimationKit.BuildController(ControllerPath,
                    idle,
                    CustomerAnimationKit.WriteClip(CustomerMotions.Fidget(), rig, P("Fidget")),
                    walk,
                    CustomerAnimationKit.WriteClip(CustomerMotions.Rescued(), rig, P("Rescued")),
                    CustomerAnimationKit.WriteClip(CustomerMotions.Black(), rig, P("Black")),
                    CustomerAnimationKit.WriteClip(CustomerMotions.Nod(), rig, P("Nod"), additive: true),
                    CustomerAnimationKit.WriteClip(CustomerMotions.Shake(), rig, P("Shake"), additive: true));
            }
            finally
            {
                Object.DestroyImmediate(model);
            }

            GameObject modelPrefab = BuildModelPrefab(fbx);
            BuildVariant(modelPrefab);
            AssetDatabase.SaveAssets();
            Debug.Log($"[CustomerMob] {VariantPath} を作りました");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // Take 001 → Mob_Idle、Take 002 → Mob_Walk（どちらもループ）。Take 003（1 フレームの A ポーズ）は使わない
        static void ImportClips()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
            var defaults = importer.defaultClipAnimations;
            var clips = new List<ModelImporterClipAnimation>();
            foreach (var c in defaults)
            {
                string name = c.takeName == "Take 001" ? IdleClip : c.takeName == "Take 002" ? WalkClip : null;
                if (name == null) continue;
                c.name = name;
                c.loopTime = true;
                c.loopPose = false;
                clips.Add(c);
            }
            importer.clipAnimations = clips.ToArray();
            importer.SaveAndReimport();
        }

        static AnimationClip LoadFbxClip(string name)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(FbxPath))
            {
                if (o is AnimationClip clip && clip.name == name) return clip;
            }
            throw new FileNotFoundException($"{FbxPath} に {name} がありません");
        }

        static GameObject BuildModelPrefab(GameObject fbx)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            try
            {
                model.name = "Customer_Mob01Model";
                var animator = model.GetComponent<Animator>();
                if (animator == null) animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                var attach = model.GetComponent<BoneAttachment>();
                if (attach == null) attach = model.AddComponent<BoneAttachment>();
                var so = new SerializedObject(attach);
                var list = so.FindProperty("attachments");
                list.arraySize = 1;
                list.GetArrayElementAtIndex(0).FindPropertyRelative("part").objectReferenceValue = model.transform.Find(EarsPart);
                list.GetArrayElementAtIndex(0).FindPropertyRelative("bone").objectReferenceValue = model.transform.Find(HeadBone);
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(model, ModelPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(model);
            }
            // コントローラーを作り直したので、メモリ上のプレハブを読み込み直して今のものを指させる
            AssetDatabase.ImportAsset(ModelPrefabPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabPath);
        }

        static void BuildVariant(GameObject modelPrefab)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath) == null)
            {
                var source = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BaseCustomerPath));
                source.name = "Customer_Mob01";
                PrefabUtility.SaveAsPrefabAsset(source, VariantPath);
                Object.DestroyImmediate(source);
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(VariantPath);
            try
            {
                // カプセルの見た目を外す。当たり判定のコライダーはそのまま
                var oldRenderer = contents.GetComponent<MeshRenderer>();
                if (oldRenderer != null) Object.DestroyImmediate(oldRenderer);
                var oldFilter = contents.GetComponent<MeshFilter>();
                if (oldFilter != null) Object.DestroyImmediate(oldFilter);

                Transform old = contents.transform.Find(modelPrefab.name);
                if (old != null) Object.DestroyImmediate(old.gameObject);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, contents.transform);
                PrefabUtility.RevertPrefabInstance(model, InteractionMode.AutomatedAction);
                // 客の原点は体の中心（カプセルと同じ y=+1）。モデルは足もとが原点なので 1m 下げる
                model.transform.localPosition = new Vector3(0f, -1f, 0f);
                model.transform.localRotation = Quaternion.identity;

                Renderer main = null;
                var extra = new List<Renderer>();
                var tintOnly = new List<Renderer>();
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name == MainBodyPart) main = r;
                    else if (TintOnlyParts.Contains(r.name)) tintOnly.Add(r);
                    else extra.Add(r);
                }

                var outline = contents.GetComponent<MockCustomerOutline>();
                SetReference(outline, "bodyRenderer", main);
                SetArray(outline, "extraBodyRenderers", extra);
                SetArray(outline, "tintOnlyRenderers", tintOnly);
                // 服と体が重なっているので、輪郭を奥へずらして内側へのはみ出しを隠す（足もとが地面に沈まない程度）
                SetFloat(outline, "hullDepthPush", HullDepthPush);
                SetReference(contents.GetComponent<CustomerReluctance>(), "targetRenderer", main);
                SetReference(contents.GetComponent<CustomerProfileApplier>(), "targetRenderer", main);

                var bridge = contents.GetComponent<CustomerAnimatorBridge>();
                if (bridge == null) bridge = contents.AddComponent<CustomerAnimatorBridge>();
                SetReference(bridge, "animator", model.GetComponent<Animator>());

                PrefabUtility.SaveAsPrefabAsset(contents, VariantPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        static void SetReference(Component component, string property, Object value)
        {
            if (component == null) return;
            var so = new SerializedObject(component);
            var p = so.FindProperty(property);
            if (p == null) return;
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetFloat(Component component, string property, float value)
        {
            if (component == null) return;
            var so = new SerializedObject(component);
            var p = so.FindProperty(property);
            if (p == null) return;
            p.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetArray(Component component, string property, List<Renderer> values)
        {
            if (component == null) return;
            var so = new SerializedObject(component);
            var p = so.FindProperty(property);
            if (p == null) return;
            p.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
