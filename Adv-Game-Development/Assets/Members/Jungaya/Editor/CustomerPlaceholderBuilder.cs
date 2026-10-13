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
        参拝客の仮モデル（こけし風・手足とアニメーションつき）を作る Editor 拡張。デザイン班のモデルができるまでのつなぎ
          ・体（断面を1周まわした胴と頭）・袖つきの腕と手・すそから出る足を、ボーン15本の「1つのスキンメッシュ」にする
            輪郭（MockCustomerOutline のインバートハル）は本体のメッシュ1つをふくらませて描くので、パーツを分けると1パーツにしか付かない
          ・法線は形から計算して、背中の継ぎ目の頂点も同じ向きにする。RecalculateNormals だと継ぎ目で輪郭が割れる
          ・顔・髪・えり・帯はテクスチャで描く。体の色（_BaseColor、通常は灰・黒客は黒）はテクスチャにかけ算されるので、黒客化はそのまま効く
          ・服に輪郭の5色（緑・青・紫・ピンク・金）や赤に近い色は使わない。体の色で欲しいお守りを読みちがえないように
          ・原点は前のカプセルと同じ「体の中心」。足もとが y=-1、頭のてっぺんが y=+0.77。前は +Z。右手は +X 側
          ・すそと肩の直径は約 0.75m。カプセル（直径 1m）より細すぎると輪郭の見える面積が減るので、こけしとしては太めにしてある
          ・アニメーションの動きはデザイン班のモデル（CustomerMobBuilder）と共通の CustomerMotions を、このリグに合わせて書く
            パラメータは CustomerAnimatorBridge のコメントを参照
    */
    public static class CustomerPlaceholderBuilder
    {
        const string Dir = "Assets/Members/Jungaya/Scenery/Customer";
        const string AnimDir = Dir + "/Animations";
        const string MeshPath = Dir + "/Customer_Kokeshi.asset";
        const string TexturePath = Dir + "/Customer_Kokeshi_Albedo.png";
        const string MaterialPath = Dir + "/Customer_Kokeshi.mat";
        const string ModelPrefabPath = Dir + "/Customer_KokeshiModel.prefab";
        const string ControllerPath = AnimDir + "/Customer_Kokeshi.controller";
        const string VariantPath = Dir + "/Customer_Placeholder.prefab";

        const int Segments = 40;
        const int TextureSize = 512;
        const float Bottom = -1f;
        const float Top = 0.77f;
        const float HeadY = 0.56f;
        const float HeadRadius = 0.21f;
        const float HipsY = -0.2f;

        [MenuItem("Toufuku/Scenery/Build Customer Placeholder (Kokeshi)")]
        public static void Build()
        {
            EnsureFolder(Dir);
            EnsureFolder(AnimDir);

            Material material = BuildMaterial();
            GameObject model = BuildModelPrefab(material);
            UpdateVariant(model);

            AssetDatabase.SaveAssets();
            Debug.Log($"[CustomerPlaceholder] {ModelPrefabPath} と {ControllerPath} を作り、{VariantPath} に入れました");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ---- ボーン ----

        enum B { Hips, Spine, Chest, Neck, Head, UpperArmR, LowerArmR, HandR, UpperArmL, LowerArmL, HandL, LegR, FootR, LegL, FootL, Count }

        static readonly string[] BoneNames =
        {
            "Hips", "Spine", "Chest", "Neck", "Head",
            "UpperArm_R", "LowerArm_R", "Hand_R", "UpperArm_L", "LowerArm_L", "Hand_L",
            "Leg_R", "Foot_R", "Leg_L", "Foot_L",
        };

        static readonly int[] BoneParents = { -1, 0, 1, 2, 3, 2, 5, 6, 2, 8, 9, 0, 11, 0, 13 };

        // モデルの中での位置（回転はすべて 0。クリップの角度がそのまま使える）
        static readonly Vector3[] BonePositions =
        {
            new Vector3(0f, HipsY, 0f), new Vector3(0f, 0f, 0f), new Vector3(0f, 0.2f, 0f), new Vector3(0f, 0.34f, 0f), new Vector3(0f, 0.42f, 0f),
            new Vector3(0.31f, 0.24f, 0f), new Vector3(0.40f, 0.0f, 0.02f), new Vector3(0.42f, -0.23f, 0.05f),
            new Vector3(-0.31f, 0.24f, 0f), new Vector3(-0.40f, 0.0f, 0.02f), new Vector3(-0.42f, -0.23f, 0.05f),
            new Vector3(0.12f, -0.25f, 0f), new Vector3(0.12f, -0.94f, 0.05f),
            new Vector3(-0.12f, -0.25f, 0f), new Vector3(-0.12f, -0.94f, 0.05f),
        };

        static string BonePath(B bone)
        {
            string path = BoneNames[(int)bone];
            for (int p = BoneParents[(int)bone]; p >= 0; p = BoneParents[p]) path = BoneNames[p] + "/" + path;
            return path;
        }

        // ---- 形 ----

        class MeshBuilder
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<BoneWeight> Weights = new List<BoneWeight>();
            public readonly List<int> Triangles = new List<int>();

            public int Add(Vector3 v, Vector3 n, Vector2 uv, BoneWeight w)
            {
                Vertices.Add(v);
                Normals.Add(n.normalized);
                Uvs.Add(uv);
                Weights.Add(w);
                return Vertices.Count - 1;
            }

            // a-b が下の段、c-d が上の段（外から見て時計回りになる向き）
            public void Quad(int a, int b, int c, int d)
            {
                Triangles.Add(a); Triangles.Add(b); Triangles.Add(d);
                Triangles.Add(a); Triangles.Add(d); Triangles.Add(c);
            }
        }

        static float V(float y) => (y - Bottom) / (Top - Bottom);

        static BoneWeight Weight(params (B bone, float w)[] items)
        {
            // 大きい順に4つまで。合計を1にする
            var list = new List<(B bone, float w)>(items);
            list.RemoveAll(i => i.w <= 1e-4f);
            list.Sort((x, y) => y.w.CompareTo(x.w));
            if (list.Count > 4) list.RemoveRange(4, list.Count - 4);
            float total = 0f;
            foreach (var i in list) total += i.w;
            var bw = new BoneWeight();
            for (int k = 0; k < list.Count; k++)
            {
                float w = list[k].w / total;
                int index = (int)list[k].bone;
                switch (k)
                {
                    case 0: bw.boneIndex0 = index; bw.weight0 = w; break;
                    case 1: bw.boneIndex1 = index; bw.weight1 = w; break;
                    case 2: bw.boneIndex2 = index; bw.weight2 = w; break;
                    default: bw.boneIndex3 = index; bw.weight3 = w; break;
                }
            }
            return bw;
        }

        /*
            胴と頭の重み。背骨は高さで となりのボーンへなめらかに受けわたす
            腰より下は、すそが脚といっしょにゆれるように、左右の脚へ少しずつ重みを分ける（side: +1 が右、-1 が左）
        */
        static BoneWeight BodyWeight(float y, float side)
        {
            B[] chain = { B.Hips, B.Spine, B.Chest, B.Neck, B.Head };
            float[] centers = { HipsY, 0f, 0.2f, 0.34f, 0.45f };

            if (y < HipsY)
            {
                float t = Mathf.Clamp01((HipsY - 0.05f - y) / 0.72f) * 0.45f;
                return Weight((B.Hips, 1f - t), (B.LegR, t * (0.5f + 0.5f * side)), (B.LegL, t * (0.5f - 0.5f * side)));
            }
            for (int i = 0; i < centers.Length - 1; i++)
            {
                if (y < centers[i + 1])
                {
                    float t = Mathf.InverseLerp(centers[i], centers[i + 1], y);
                    return Weight((chain[i], 1f - t), (chain[i + 1], t));
                }
            }
            return Weight((B.Head, 1f));
        }

        // 断面（高さ y, 半径 r）。下から上へ
        static List<Vector2> Profile()
        {
            var p = new List<Vector2>
            {
                new Vector2(Bottom, 0f),
                new Vector2(Bottom, 0.30f),
                new Vector2(-0.97f, 0.38f),   // すそ
                new Vector2(-0.80f, 0.38f),
                new Vector2(-0.50f, 0.36f),
                new Vector2(-0.22f, 0.33f),   // 帯
                new Vector2(-0.05f, 0.33f),
                new Vector2(0.12f, 0.35f),    // 胸
                new Vector2(0.24f, 0.36f),    // 肩
                new Vector2(0.31f, 0.29f),
                new Vector2(0.35f, 0.16f),    // 首
            };
            const int headSteps = 12;
            for (int i = 0; i <= headSteps; i++)
            {
                float rad = Mathf.Lerp(-58f, 90f, i / (float)headSteps) * Mathf.Deg2Rad;
                p.Add(new Vector2(HeadY + HeadRadius * Mathf.Sin(rad), HeadRadius * Mathf.Cos(rad)));
            }
            p[p.Count - 1] = new Vector2(Top, 0f);
            return p;
        }

        static void AddBody(MeshBuilder mb)
        {
            List<Vector2> profile = Profile();
            int rings = profile.Count;
            int cols = Segments + 1;   // 背中の継ぎ目は頂点を2重にして UV を 0 と 1 に分ける（位置と法線は同じ）
            int start = mb.Vertices.Count;

            for (int i = 0; i < rings; i++)
            {
                Vector2 prev = profile[Mathf.Max(0, i - 1)];
                Vector2 next = profile[Mathf.Min(rings - 1, i + 1)];
                var n2 = new Vector2(next.x - prev.x, -(next.y - prev.y)).normalized;   // (法線の r 成分, y 成分)

                float y = profile[i].x;
                float r = profile[i].y;
                for (int j = 0; j < cols; j++)
                {
                    float u = j / (float)Segments;
                    float a = (u - 0.5f) * Mathf.PI * 2f;   // u=0.5 が正面（+Z）
                    float s = Mathf.Sin(a), c = Mathf.Cos(a);
                    mb.Add(new Vector3(r * s, y, r * c), new Vector3(n2.x * s, n2.y, n2.x * c), new Vector2(u, V(y)), BodyWeight(y, s));
                }
            }
            for (int i = 0; i < rings - 1; i++)
            {
                for (int j = 0; j < Segments; j++)
                {
                    int a = start + i * cols + j;
                    mb.Quad(a, a + 1, a + cols, a + cols + 1);
                }
            }
        }

        // 袖（肩から手首へ太くなる筒）と手。side: +1 が右腕
        static void AddArm(MeshBuilder mb, float side)
        {
            B upper = side > 0 ? B.UpperArmR : B.UpperArmL;
            B lower = side > 0 ? B.LowerArmR : B.LowerArmL;
            B hand = side > 0 ? B.HandR : B.HandL;
            Vector3 Mirror(Vector3 v) => new Vector3(v.x * side, v.y, v.z);

            Vector3 shoulder = Mirror(new Vector3(0.28f, 0.27f, 0f));
            Vector3 elbow = Mirror(BonePositions[(int)B.LowerArmR]);
            Vector3 wrist = Mirror(BonePositions[(int)B.HandR]);
            Vector3[] centers = { shoulder, Vector3.Lerp(shoulder, elbow, 0.5f), elbow, Vector3.Lerp(elbow, wrist, 0.5f), wrist };
            float[] radii = { 0.085f, 0.09f, 0.095f, 0.11f, 0.12f };
            float[] lowerShare = { 0f, 0f, 0.5f, 1f, 1f };
            const int sides = 10;

            int ringStart = mb.Vertices.Count;
            for (int i = 0; i < centers.Length; i++)
            {
                Vector3 axis = (centers[Mathf.Min(i + 1, centers.Length - 1)] - centers[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 u1 = Vector3.Cross(axis, Vector3.forward).normalized;
                Vector3 u2 = Vector3.Cross(axis, u1);
                BoneWeight w = Weight((upper, 1f - lowerShare[i]), (lower, lowerShare[i]));
                float v = V(Mathf.Lerp(-0.75f, -0.3f, i / (float)(centers.Length - 1)));   // ゆかたの縞のところを使う
                for (int j = 0; j <= sides; j++)
                {
                    float a = j / (float)sides * Mathf.PI * 2f;
                    Vector3 n = u1 * Mathf.Cos(a) + u2 * Mathf.Sin(a);
                    mb.Add(centers[i] + n * radii[i], n, new Vector2(j / (float)sides, v), w);
                }
            }
            for (int i = 0; i < centers.Length - 1; i++)
            {
                for (int j = 0; j < sides; j++)
                {
                    int a = ringStart + i * (sides + 1) + j;
                    mb.Quad(a, a + 1, a + sides + 1, a + sides + 2);
                }
            }
            // 両端のふた（輪郭がすき間から漏れないように、外周の頂点は筒と共有する）
            AddCap(mb, ringStart, sides, centers[0], (centers[0] - centers[1]).normalized, Weight((upper, 1f)), true);
            int lastRing = ringStart + (centers.Length - 1) * (sides + 1);
            AddCap(mb, lastRing, sides, wrist, (wrist - centers[3]).normalized, Weight((lower, 1f)), false);

            // 手（袖口から少し出た玉）
            Vector3 handCenter = wrist + (wrist - elbow).normalized * 0.07f;
            AddEllipsoid(mb, handCenter, new Vector3(0.055f, 0.065f, 0.055f), new Vector2(0.5f, V(0.38f)), Weight((hand, 1f)));
        }

        static void AddCap(MeshBuilder mb, int ringStart, int sides, Vector3 center, Vector3 normal, BoneWeight w, bool flip)
        {
            Vector2 uv = mb.Uvs[ringStart];
            int c = mb.Add(center, normal, uv, w);
            for (int j = 0; j < sides; j++)
            {
                int a = ringStart + j, b = ringStart + j + 1;
                if (flip) { mb.Triangles.Add(c); mb.Triangles.Add(b); mb.Triangles.Add(a); }
                else { mb.Triangles.Add(c); mb.Triangles.Add(a); mb.Triangles.Add(b); }
            }
        }

        // 楕円体（手と足）。UV は1色のところを指す
        static void AddEllipsoid(MeshBuilder mb, Vector3 center, Vector3 radius, Vector2 uv, BoneWeight w)
        {
            const int lon = 10, lat = 6;
            int start = mb.Vertices.Count;
            for (int i = 0; i <= lat; i++)
            {
                float phi = Mathf.Lerp(-90f, 90f, i / (float)lat) * Mathf.Deg2Rad;
                for (int j = 0; j <= lon; j++)
                {
                    float th = j / (float)lon * Mathf.PI * 2f;
                    var unit = new Vector3(Mathf.Cos(phi) * Mathf.Sin(th), Mathf.Sin(phi), Mathf.Cos(phi) * Mathf.Cos(th));
                    Vector3 p = Vector3.Scale(unit, radius);
                    var n = new Vector3(unit.x / radius.x, unit.y / radius.y, unit.z / radius.z);
                    mb.Add(center + p, n, uv, w);
                }
            }
            for (int i = 0; i < lat; i++)
            {
                for (int j = 0; j < lon; j++)
                {
                    int a = start + i * (lon + 1) + j;
                    mb.Quad(a, a + 1, a + lon + 1, a + lon + 2);
                }
            }
        }

        // mesh: 書きこむ先。前に作ったアセットがあればそれ（GUID を変えない）
        static void BuildMesh(Mesh mesh, Transform body, Transform[] bones)
        {
            var mb = new MeshBuilder();
            AddBody(mb);
            AddArm(mb, 1f);
            AddArm(mb, -1f);
            // 足（下駄）。すそから つま先が出るように前へ
            AddEllipsoid(mb, new Vector3(0.12f, -0.955f, 0.27f), new Vector3(0.09f, 0.05f, 0.16f), new Vector2(0f, V(-0.99f)), Weight((B.FootR, 1f)));
            AddEllipsoid(mb, new Vector3(-0.12f, -0.955f, 0.27f), new Vector3(0.09f, 0.05f, 0.16f), new Vector2(0f, V(-0.99f)), Weight((B.FootL, 1f)));

            mesh.Clear();
            mesh.name = "Customer_Kokeshi";
            mesh.SetVertices(mb.Vertices);
            mesh.SetNormals(mb.Normals);
            mesh.SetUVs(0, mb.Uvs);
            mesh.boneWeights = mb.Weights.ToArray();
            mesh.SetTriangles(mb.Triangles, 0);
            var bindposes = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++) bindposes[i] = bones[i].worldToLocalMatrix * body.localToWorldMatrix;
            mesh.bindposes = bindposes;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
        }

        static GameObject BuildModelPrefab(Material material)
        {
            var root = new GameObject("Customer_KokeshiModel");
            var bones = new Transform[(int)B.Count];
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i] = new GameObject(BoneNames[i]).transform;
                bones[i].SetParent(BoneParents[i] >= 0 ? bones[BoneParents[i]] : root.transform, false);
                bones[i].position = BonePositions[i];
            }

            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);

            // 前のアセットに直接書きこむ。CopySerialized で上書きすると、GPU 側の頂点が古いままでスキンメッシュが描かれなくなる
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            bool created = mesh == null;
            if (created) mesh = new Mesh();
            BuildMesh(mesh, body.transform, bones);
            if (created) AssetDatabase.CreateAsset(mesh, MeshPath);
            else EditorUtility.SetDirty(mesh);

            var smr = body.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.sharedMaterial = material;
            smr.bones = bones;
            smr.rootBone = bones[(int)B.Hips];
            smr.quality = SkinQuality.Bone2;
            smr.updateWhenOffscreen = false;
            smr.localBounds = new Bounds(new Vector3(0f, 0.05f, 0.05f), new Vector3(1.4f, 2.0f, 1.2f));

            // 骨組みができてから、その姿勢を基準にクリップとコントローラーを作る
            BuildController(root.transform);
            // 作り直した直後のオブジェクトではなく、保存したアセットを参照させる（でないとプレハブ側の参照が空になる）
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            PrefabUtility.SaveAsPrefabAsset(root, ModelPrefabPath);
            Object.DestroyImmediate(root);
            // コントローラーを作り直すと、メモリ上のプレハブが消した前のコントローラーを指したままになる。読み込み直して今のものを指させる
            AssetDatabase.ImportAsset(ModelPrefabPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabPath);
        }

        static Material BuildMaterial()
        {
            File.WriteAllBytes(TexturePath, PaintTexture().EncodeToPNG());
            AssetDatabase.ImportAsset(TexturePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.25f);
            EditorUtility.SetDirty(material);
            return material;
        }

        // ---- バリアント（客のプレハブ）へ入れる ----

        static void UpdateVariant(GameObject modelPrefab)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(VariantPath);
            try
            {
                // 前のカプセル（または静止したこけし）の見た目を外す。当たり判定のコライダーはそのまま
                var oldRenderer = contents.GetComponent<MeshRenderer>();
                if (oldRenderer != null) Object.DestroyImmediate(oldRenderer);
                var oldFilter = contents.GetComponent<MeshFilter>();
                if (oldFilter != null) Object.DestroyImmediate(oldFilter);

                Transform old = contents.transform.Find(modelPrefab.name);
                if (old != null) Object.DestroyImmediate(old.gameObject);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, contents.transform);
                // コントローラーを作り直すあいだに「空のコントローラー」が上書きとして残ることがあるので、モデル側の設定にもどす
                PrefabUtility.RevertPrefabInstance(model, InteractionMode.AutomatedAction);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;

                var body = model.GetComponentInChildren<SkinnedMeshRenderer>();
                SetReference(contents.GetComponent<MockCustomerOutline>(), "bodyRenderer", body);
                SetReference(contents.GetComponent<CustomerReluctance>(), "targetRenderer", body);
                SetReference(contents.GetComponent<CustomerProfileApplier>(), "targetRenderer", body);

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

        // ---- アニメーション ----

        // こけしのボーンを、動きの部位に対応させる
        static Dictionary<CustomerBone, string> RigPaths() => new Dictionary<CustomerBone, string>
        {
            { CustomerBone.Hips, BonePath(B.Hips) },
            { CustomerBone.Spine, BonePath(B.Spine) },
            { CustomerBone.Chest, BonePath(B.Chest) },
            { CustomerBone.Neck, BonePath(B.Neck) },
            { CustomerBone.Head, BonePath(B.Head) },
            { CustomerBone.UpperArmR, BonePath(B.UpperArmR) },
            { CustomerBone.LowerArmR, BonePath(B.LowerArmR) },
            { CustomerBone.UpperArmL, BonePath(B.UpperArmL) },
            { CustomerBone.LowerArmL, BonePath(B.LowerArmL) },
            { CustomerBone.LegR, BonePath(B.LegR) },
            { CustomerBone.LegL, BonePath(B.LegL) },
        };

        // 動きは CustomerMotions（デザイン班のモデルと共通）。こけしは全部のボーンの基準の回転が 0
        static void BuildController(Transform modelRoot)
        {
            CustomerRig rig = CustomerRig.FromCurrentPose(modelRoot, modelRoot.Find(BoneNames[(int)B.Hips]), RigPaths());
            string P(string name) => $"{AnimDir}/Customer_{name}.anim";
            CustomerAnimationKit.BuildController(ControllerPath,
                CustomerAnimationKit.WriteClip(CustomerMotions.Idle(), rig, P("Idle")),
                CustomerAnimationKit.WriteClip(CustomerMotions.Fidget(), rig, P("Fidget")),
                CustomerAnimationKit.WriteClip(CustomerMotions.Walk(), rig, P("Walk")),
                CustomerAnimationKit.WriteClip(CustomerMotions.Rescued(), rig, P("Rescued")),
                CustomerAnimationKit.WriteClip(CustomerMotions.Black(), rig, P("Black")),
                CustomerAnimationKit.WriteClip(CustomerMotions.Nod(), rig, P("Nod"), additive: true),
                CustomerAnimationKit.WriteClip(CustomerMotions.Shake(), rig, P("Shake"), additive: true));
        }

        // ---- 模様 ----

        static readonly Color Robe = new Color(0.93f, 0.90f, 0.84f);        // 生成りのゆかた
        static readonly Color RobeStripe = new Color(0.78f, 0.76f, 0.72f);  // かすりのたて縞
        static readonly Color Obi = new Color(0.20f, 0.21f, 0.25f);         // 墨色の帯
        static readonly Color ObiCord = new Color(0.85f, 0.83f, 0.78f);
        static readonly Color Collar = new Color(1f, 1f, 1f);
        static readonly Color Skin = new Color(1f, 0.88f, 0.76f);
        static readonly Color Hair = new Color(0.09f, 0.08f, 0.08f);
        static readonly Color Feet = new Color(0.30f, 0.24f, 0.20f);
        static readonly Color Line = new Color(0.35f, 0.33f, 0.32f);

        static Texture2D PaintTexture()
        {
            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGB24, false);
            var pixels = new Color[TextureSize * TextureSize];
            for (int py = 0; py < TextureSize; py++)
            {
                float y = Mathf.Lerp(Bottom, Top, (py + 0.5f) / TextureSize);
                for (int px = 0; px < TextureSize; px++)
                {
                    float deg = ((px + 0.5f) / TextureSize - 0.5f) * 360f;   // 0 が正面、+ が客の右手側（画面では左）
                    pixels[py * TextureSize + px] = ColorAt(deg, y);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        static Color ColorAt(float deg, float y)
        {
            float absDeg = Mathf.Abs(deg);

            // 頭
            if (y > 0.36f)
            {
                bool front = absDeg < 62f;
                if (!front && y > 0.44f) return Hair;                // うしろ髪
                if (y > 0.64f) return Hair;                          // 前髪とてっぺん
                if (front && y > 0.61f && absDeg > 40f) return Hair; // 横の髪
                if (Ellipse(deg, y, 17f, 0.555f, 6.5f, 0.016f) || Ellipse(deg, y, -17f, 0.555f, 6.5f, 0.016f)) return Hair;   // 目
                if (Ellipse(deg, y, 0f, 0.485f, 4f, 0.007f)) return new Color(0.55f, 0.35f, 0.32f);                           // 口
                return Skin;
            }

            // 足もと
            if (y < -0.955f) return Feet;

            // 帯
            if (y > -0.21f && y < -0.04f)
            {
                if (Mathf.Abs(y + 0.125f) < 0.012f) return ObiCord;
                return Obi;
            }

            // えり（胸の上で V 字に合わせる）
            if (y >= 0.08f && y < 0.36f)
            {
                float t = Mathf.InverseLerp(0.08f, 0.33f, y);        // 0 が合わせ目、1 が首
                float open = Mathf.Lerp(0f, 20f, t);                 // V の開き（度）
                if (absDeg < open) return Skin;                      // 胸もと
                if (absDeg < open + 6f) return Collar;               // 白いえり
            }

            // 前の合わせ。左の身ごろを上に重ねるので、合わせの線は客の右側（+ の角度）に来る
            if (y < -0.04f && Mathf.Abs(deg - 10f) < 1.2f) return Line;

            // かすりのたて縞
            float stripe = Mathf.Repeat(deg, 24f);
            return stripe < 2.5f ? RobeStripe : Robe;
        }

        static bool Ellipse(float deg, float y, float cDeg, float cY, float rDeg, float rY)
        {
            float dx = (deg - cDeg) / rDeg;
            float dy = (y - cY) / rY;
            return dx * dx + dy * dy <= 1f;
        }
    }
}
