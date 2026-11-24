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
        FBX フォルダの人間モデルを、ゲームの客として動かせるプレハブにする Editor 拡張
        CustomerMobBuilder（Mob_1_01 専用）と同じ作りを、モデルごとの設定表（Defs）で回せるようにしたもの
          ・FBX に待機・歩きのテイクがあればそれを使い、無ければ仮モデルと同じ動き（CustomerMotions）で作る
          ・焦り・救済・黒客・うなずき・首ふりは、どのモデルも CustomerMotions をそのリグに合わせて作る
          ・基準の姿勢は「待機の最初のフレーム」→ 無ければ「歩きの、両足がそろう瞬間」→ それも無ければ「腕を下ろしたバインドポーズ」
          ・できる客プレハブは Customer_MockVisibility のバリアント。足もとが地面に来るように入れる
        モデルを足すときは Defs に1つ足して、メニューからもう一度作る
    */
    public static class CustomerModelBuilder
    {
        class Def
        {
            public string Id;            // 出力名（Customer_<Id>）
            public string Fbx;           // FBX のパス
            public string IdleTake;      // 待機のテイク名（無ければ null）
            public string WalkTake;      // 歩きのテイク名（無ければ null）
            public string Hips;          // Animator から見た腰のボーンのパス
            public string Head = "Head"; // 首の下の頭のボーン名
            public string MainBody;      // 体のメインのレンダラーのパス
            public float Scale = 1.5f;   // 客として見やすい大きさにする倍率
            public float BottomY;        // モデルの足もとの高さ（原点からのずれ、m）
            public string AttachPart;    // ボーンに付いていないパーツ（耳など）。無ければ null
        }

        static readonly Def[] Defs =
        {
            new Def
            {
                Id = "Woman02", Fbx = "Assets/Members/Jungaya/FBX/Woman_Anim_02.fbx",
                IdleTake = "Woman_Idle", WalkTake = "Woman_Walk",
                Hips = "Body_Group/Hips", MainBody = "Body_Group/Body_G/polySurface9",
                Scale = 1.5f, AttachPart = "Body_Group/Body_G/Ears",
            },
            new Def
            {
                Id = "Man01", Fbx = "Assets/Members/Jungaya/FBX/Man_Motion.fbx",
                IdleTake = null, WalkTake = "Man_Walk",
                Hips = "Face:Body_Group/Hips", MainBody = "Face:Body_Group/skin/NOHM_L_Hand_mesh4",
                Scale = 1.4f,
            },
            new Def
            {
                Id = "MobC", Fbx = "Assets/Members/Jungaya/FBX/Mob_C_Characterise.fbx",
                IdleTake = null, WalkTake = null,
                Hips = "Hips", Head = "Head3", MainBody = "Main_Body/NOHM_L_Hand_mesh2",
                Scale = 1.85f,   // このモデルだけ身長 1.1m ほどで作られているので、ほかより大きく拡大する
            },
        };

        const string OutRoot = "Assets/Members/Jungaya/Scenery/Customer";
        const string BaseCustomerPath = "Assets/Members/Jungaya/Mock/Customer_MockVisibility.prefab";
        const float HullDepthPush = 0.15f;

        // 輪郭を付けないで、体の色だけぬるパーツ（名前の先頭で見分ける）
        static readonly string[] TintOnlyPrefixes =
        {
            "Eyebrows", "Eyelashes", "Eyes", "Mouth", "Teeth", "Tongue", "Face:Eyelashes", "Face:Eyes",
        };

        [MenuItem("Toufuku/Scenery/Build Customers (FBX フォルダの人間モデル)")]
        public static void BuildAll()
        {
            foreach (Def d in Defs) Build(d);
            AssetDatabase.SaveAssets();
        }

        static void Build(Def d)
        {
            string dir = $"{OutRoot}/{d.Id}";
            string animDir = dir + "/Animations";
            string controllerPath = $"{dir}/Customer_{d.Id}.controller";
            string modelPrefabPath = $"{dir}/Customer_{d.Id}Model.prefab";
            string variantPath = $"{dir}/Customer_{d.Id}.prefab";
            EnsureFolder(dir);
            EnsureFolder(animDir);

            ImportClips(d);
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(d.Fbx);
            AnimationClip idle = d.IdleTake != null ? LoadFbxClip(d, d.Id + "_Idle") : null;
            AnimationClip walk = d.WalkTake != null ? LoadFbxClip(d, d.Id + "_Walk") : null;

            Dictionary<CustomerBone, string> paths = RigPaths(d);
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            try
            {
                if (idle != null) idle.SampleAnimation(model, 0f);
                else if (walk != null) walk.SampleAnimation(model, FeetTogetherTime(walk, model, paths));
                else LowerArms(model.transform, paths);

                CustomerRig rig = CustomerRig.FromCurrentPose(model.transform, model.transform.Find(d.Hips), paths);
                string P(string name) => $"{animDir}/{d.Id}_{name}.anim";
                Motion idleMotion = idle != null ? idle : CustomerAnimationKit.WriteClip(CustomerMotions.Idle(), rig, P("Idle"));
                Motion walkMotion = walk != null ? walk : CustomerAnimationKit.WriteClip(CustomerMotions.Walk(), rig, P("Walk"));
                CustomerAnimationKit.BuildController(controllerPath,
                    idleMotion,
                    CustomerAnimationKit.WriteClip(CustomerMotions.Fidget(), rig, P("Fidget")),
                    walkMotion,
                    CustomerAnimationKit.WriteClip(CustomerMotions.Rescued(), rig, P("Rescued")),
                    CustomerAnimationKit.WriteClip(CustomerMotions.Black(), rig, P("Black")),
                    CustomerAnimationKit.WriteClip(CustomerMotions.Nod(), rig, P("Nod"), additive: true),
                    CustomerAnimationKit.WriteClip(CustomerMotions.Shake(), rig, P("Shake"), additive: true));
            }
            finally
            {
                Object.DestroyImmediate(model);
            }

            GameObject modelPrefab = BuildModelPrefab(d, fbx, controllerPath, modelPrefabPath, paths);
            BuildVariant(d, modelPrefab, variantPath);
            Debug.Log($"[CustomerModel] {variantPath} を作りました");
        }

        static Dictionary<CustomerBone, string> RigPaths(Def d)
        {
            string spine2 = d.Hips + "/Spine/Spine1/Spine2";
            return new Dictionary<CustomerBone, string>
            {
                { CustomerBone.Hips, d.Hips },
                { CustomerBone.Spine, d.Hips + "/Spine" },
                { CustomerBone.Chest, spine2 },
                { CustomerBone.Neck, spine2 + "/Neck" },
                { CustomerBone.Head, spine2 + "/Neck/" + d.Head },
                { CustomerBone.UpperArmR, spine2 + "/RightShoulder/RightArm" },
                { CustomerBone.LowerArmR, spine2 + "/RightShoulder/RightArm/RightForeArm" },
                { CustomerBone.UpperArmL, spine2 + "/LeftShoulder/LeftArm" },
                { CustomerBone.LowerArmL, spine2 + "/LeftShoulder/LeftArm/LeftForeArm" },
                { CustomerBone.LegR, d.Hips + "/RightUpLeg" },
                { CustomerBone.LegL, d.Hips + "/LeftUpLeg" },
            };
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // 使うテイクだけを <Id>_Idle / <Id>_Walk としてループで取りこむ。モデルに入っているカメラは取りこまない
        static void ImportClips(Def d)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(d.Fbx);
            var clips = new List<ModelImporterClipAnimation>();
            foreach (var c in importer.defaultClipAnimations)
            {
                string name = c.takeName == d.IdleTake ? d.Id + "_Idle" : c.takeName == d.WalkTake ? d.Id + "_Walk" : null;
                if (name == null) continue;
                c.name = name;
                c.loopTime = true;
                c.loopPose = false;
                clips.Add(c);
            }
            importer.clipAnimations = clips.ToArray();
            importer.importCameras = false;
            importer.importLights = false;
            importer.SaveAndReimport();
        }

        static AnimationClip LoadFbxClip(Def d, string name)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(d.Fbx))
            {
                if (o is AnimationClip clip && clip.name == name) return clip;
            }
            throw new FileNotFoundException($"{d.Fbx} に {name} がありません");
        }

        // 歩きの中で、両足が前後にいちばんそろう時刻（待機の基準の姿勢に使う）
        static float FeetTogetherTime(AnimationClip walk, GameObject model, Dictionary<CustomerBone, string> paths)
        {
            Transform legL = Deepest(model.transform.Find(paths[CustomerBone.LegL]));
            Transform legR = Deepest(model.transform.Find(paths[CustomerBone.LegR]));
            float best = 0f, bestGap = float.MaxValue;
            const int steps = 32;
            for (int i = 0; i < steps; i++)
            {
                float t = walk.length * i / steps;
                walk.SampleAnimation(model, t);
                Vector3 gap = model.transform.InverseTransformVector(legL.position - legR.position);
                float g = Mathf.Abs(gap.z) + Mathf.Abs(gap.y);
                if (g < bestGap) { bestGap = g; best = t; }
            }
            return best;
        }

        static Transform Deepest(Transform t)
        {
            while (t.childCount > 0) t = t.GetChild(0);
            return t;
        }

        // バインドポーズ（腕を広げた姿勢）から、腕を体の横へ下ろす
        static void LowerArms(Transform root, Dictionary<CustomerBone, string> paths)
        {
            LowerArm(root, root.Find(paths[CustomerBone.UpperArmR]), root.Find(paths[CustomerBone.LowerArmR]));
            LowerArm(root, root.Find(paths[CustomerBone.UpperArmL]), root.Find(paths[CustomerBone.LowerArmL]));
        }

        static void LowerArm(Transform root, Transform upper, Transform lower)
        {
            if (upper == null || lower == null) return;
            Vector3 dir = lower.position - upper.position;
            if (dir.sqrMagnitude < 1e-10f) return;
            float side = Mathf.Sign(Vector3.Dot(dir, root.right));
            if (side == 0f) side = 1f;
            Vector3 target = (root.right * (0.2f * side) - root.up + root.forward * 0.05f).normalized;
            upper.rotation = Quaternion.FromToRotation(dir.normalized, target) * upper.rotation;
        }

        static GameObject BuildModelPrefab(Def d, GameObject fbx, string controllerPath, string modelPrefabPath,
            Dictionary<CustomerBone, string> paths)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            try
            {
                model.name = $"Customer_{d.Id}Model";
                var animator = model.GetComponent<Animator>();
                if (animator == null) animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                if (d.AttachPart != null)
                {
                    var attach = model.GetComponent<BoneAttachment>();
                    if (attach == null) attach = model.AddComponent<BoneAttachment>();
                    var so = new SerializedObject(attach);
                    var list = so.FindProperty("attachments");
                    list.arraySize = 1;
                    list.GetArrayElementAtIndex(0).FindPropertyRelative("part").objectReferenceValue = model.transform.Find(d.AttachPart);
                    list.GetArrayElementAtIndex(0).FindPropertyRelative("bone").objectReferenceValue = model.transform.Find(paths[CustomerBone.Head]);
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                PrefabUtility.SaveAsPrefabAsset(model, modelPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(model);
            }
            AssetDatabase.ImportAsset(modelPrefabPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<GameObject>(modelPrefabPath);
        }

        static void BuildVariant(Def d, GameObject modelPrefab, string variantPath)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(variantPath) == null)
            {
                var source = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BaseCustomerPath));
                source.name = $"Customer_{d.Id}";
                PrefabUtility.SaveAsPrefabAsset(source, variantPath);
                Object.DestroyImmediate(source);
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(variantPath);
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
                // 客の原点は体の中心（カプセルと同じ y=+1）。足もとが地面に来るように下げる
                model.transform.localPosition = new Vector3(0f, -1f - d.BottomY * d.Scale, 0f);
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one * d.Scale;

                Renderer main = null;
                Transform mainT = model.transform.Find(d.MainBody);
                if (mainT != null) main = mainT.GetComponent<Renderer>();
                var extra = new List<Renderer>();
                var tintOnly = new List<Renderer>();
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == main) continue;
                    if (IsTintOnly(r.name)) tintOnly.Add(r);
                    else extra.Add(r);
                }
                if (main == null && extra.Count > 0)
                {
                    main = extra[0];
                    extra.RemoveAt(0);
                }

                var outline = contents.GetComponent<MockCustomerOutline>();
                SetReference(outline, "bodyRenderer", main);
                SetArray(outline, "extraBodyRenderers", extra);
                SetArray(outline, "tintOnlyRenderers", tintOnly);
                SetFloat(outline, "hullDepthPush", HullDepthPush);
                SetReference(contents.GetComponent<CustomerReluctance>(), "targetRenderer", main);
                SetReference(contents.GetComponent<CustomerProfileApplier>(), "targetRenderer", main);

                var bridge = contents.GetComponent<CustomerAnimatorBridge>();
                if (bridge == null) bridge = contents.AddComponent<CustomerAnimatorBridge>();
                SetReference(bridge, "animator", model.GetComponent<Animator>());

                PrefabUtility.SaveAsPrefabAsset(contents, variantPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        static bool IsTintOnly(string name)
        {
            foreach (string p in TintOnlyPrefixes)
            {
                if (name.StartsWith(p)) return true;
            }
            return false;
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
