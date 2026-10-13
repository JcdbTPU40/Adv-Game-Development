using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Toufuku.Scenery.EditorTools
{
    /*
        参拝客の仮モデル（こけし風）を作る Editor 拡張。デザイン班のモデルができるまでのつなぎ
          ・断面（足もと→すそ→帯→肩→首→頭）を1周まわした「1枚のメッシュ」にする
            輪郭（MockCustomerOutline のインバートハル）は子の中の最初のメッシュ1つをふくらませて描くので、
            パーツを分けると1パーツにしか輪郭が付かないし、重なり目に余計な線が出る
          ・法線は断面から計算して、継ぎ目（背中）の頂点も同じ向きにする。RecalculateNormals だと継ぎ目で輪郭が割れる
          ・顔・髪・えり・帯はテクスチャで描く。体の色（_BaseColor、通常は灰・黒客は黒）はテクスチャにかけ算されるので、黒客化はそのまま効く
          ・服に輪郭の5色（緑・青・紫・ピンク・金）や赤に近い色は使わない。体の色で欲しいお守りを読みちがえないように
          ・原点は今のカプセルと同じ「体の中心」。足もとが y=-1、頭のてっぺんが y=+0.77。前は +Z（カメラの方）
          ・すそと肩の直径は約 0.75m。カプセル（直径 1m）より細すぎると輪郭の見える面積が減るので、こけしとしては太めにしてある
    */
    public static class CustomerPlaceholderBuilder
    {
        const string Dir = "Assets/Members/Jungaya/Scenery/Customer";
        const string MeshPath = Dir + "/Customer_Kokeshi.asset";
        const string TexturePath = Dir + "/Customer_Kokeshi_Albedo.png";
        const string MaterialPath = Dir + "/Customer_Kokeshi.mat";

        const int Segments = 40;
        const int TextureSize = 512;
        const float Bottom = -1f;
        const float Top = 0.77f;
        const float HeadY = 0.56f;
        const float HeadRadius = 0.21f;

        [MenuItem("Toufuku/Scenery/Build Customer Placeholder (Kokeshi)")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Members/Jungaya/Scenery", "Customer");

            Mesh mesh = BuildMesh();
            var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (existingMesh != null)
            {
                EditorUtility.CopySerialized(mesh, existingMesh);
                Object.DestroyImmediate(mesh);
            }
            else
            {
                AssetDatabase.CreateAsset(mesh, MeshPath);
            }

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
            AssetDatabase.SaveAssets();
            Debug.Log($"[CustomerPlaceholder] {MeshPath} / {MaterialPath} を作りました");
        }

        // ---- 形 ----

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
            // 頭（球）。首とつながる角度からてっぺんまで
            const int headSteps = 12;
            float start = -58f;
            for (int i = 0; i <= headSteps; i++)
            {
                float deg = Mathf.Lerp(start, 90f, i / (float)headSteps);
                float rad = deg * Mathf.Deg2Rad;
                p.Add(new Vector2(HeadY + HeadRadius * Mathf.Sin(rad), HeadRadius * Mathf.Cos(rad)));
            }
            p[p.Count - 1] = new Vector2(Top, 0f);
            return p;
        }

        static Mesh BuildMesh()
        {
            List<Vector2> profile = Profile();
            int rings = profile.Count;
            int cols = Segments + 1;   // 背中の継ぎ目は頂点を2重にして UV を 0 と 1 に分ける

            var vertices = new Vector3[rings * cols];
            var normals = new Vector3[rings * cols];
            var uvs = new Vector2[rings * cols];

            for (int i = 0; i < rings; i++)
            {
                // 断面の接線から外向きの法線（r, y の平面）を出す
                Vector2 prev = profile[Mathf.Max(0, i - 1)];
                Vector2 next = profile[Mathf.Min(rings - 1, i + 1)];
                float dr = next.y - prev.y;
                float dy = next.x - prev.x;
                var n2 = new Vector2(dy, -dr).normalized;   // (法線の r 成分, 法線の y 成分)

                float y = profile[i].x;
                float r = profile[i].y;
                for (int j = 0; j < cols; j++)
                {
                    float u = j / (float)Segments;
                    float a = (u - 0.5f) * Mathf.PI * 2f;   // u=0.5 が正面（+Z）
                    float s = Mathf.Sin(a), c = Mathf.Cos(a);
                    int k = i * cols + j;
                    vertices[k] = new Vector3(r * s, y, r * c);
                    normals[k] = new Vector3(n2.x * s, n2.y, n2.x * c).normalized;
                    uvs[k] = new Vector2(u, (y - Bottom) / (Top - Bottom));
                }
            }

            var triangles = new List<int>((rings - 1) * Segments * 6);
            for (int i = 0; i < rings - 1; i++)
            {
                for (int j = 0; j < Segments; j++)
                {
                    int a = i * cols + j;
                    int b = a + 1;
                    int c = a + cols;
                    int d = c + 1;
                    triangles.Add(a); triangles.Add(b); triangles.Add(d);
                    triangles.Add(a); triangles.Add(d); triangles.Add(c);
                }
            }

            var mesh = new Mesh { name = "Customer_Kokeshi" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
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
                    float deg = ((px + 0.5f) / TextureSize - 0.5f) * 360f;   // 0 が正面、+ が客から見て右（画面では左）
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
                if (!front && y > 0.44f) return Hair;              // うしろ髪
                if (y > 0.64f) return Hair;                        // 前髪とてっぺん
                if (front && y > 0.61f && absDeg > 40f) return Hair; // 横の髪
                // 目（細い弧の代わりに小さな楕円）
                if (Ellipse(deg, y, 17f, 0.555f, 6.5f, 0.016f) || Ellipse(deg, y, -17f, 0.555f, 6.5f, 0.016f)) return Hair;
                // 口
                if (Ellipse(deg, y, 0f, 0.485f, 4f, 0.007f)) return new Color(0.55f, 0.35f, 0.32f);
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
