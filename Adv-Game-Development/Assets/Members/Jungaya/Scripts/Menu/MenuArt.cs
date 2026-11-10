using System;
using System.Collections.Generic;
using UnityEngine;

namespace Toufuku.Menu
{
    /*
        メインメニューの絵の部品（円・光・輪・角丸・三角・絵馬の形・紙垂・山の稜線・空のグラデーション）を、コードで作るスプライト

        ・画像の素材がまだないので、形だけをテクスチャに描いて、色は Image.color でつける（白で描いておく）
        ・ふちは 3×3 の点でならして、ぎざぎざを消す
        ・一度作ったものは使い回す（シーンを開きなおしても作りなおさない）
        ・デザインの素材ができたら、MainMenu の組み立てで Sprite を差しかえるだけで済む
    */
    public static class MenuArt
    {
        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        // 円（ふちはくっきり）
        public static Sprite Disc => Get("disc", () => Shape(128, 128, (u, v) => Sq(u - 0.5f) + Sq(v - 0.5f) <= 0.25f));

        // 中心が明るく、外へ向かって消えていく光
        public static Sprite Glow => Get("glow", () => Field(128, 128, (u, v) =>
        {
            float d = Mathf.Sqrt(Sq(u - 0.5f) + Sq(v - 0.5f)) * 2f;
            float a = Mathf.Clamp01(1f - d);
            return a * a;
        }));

        // 輪（太さは半径の 12%）
        public static Sprite Ring => Get("ring", () => Shape(128, 128, (u, v) =>
        {
            float d = Mathf.Sqrt(Sq(u - 0.5f) + Sq(v - 0.5f)) * 2f;
            return d <= 1f && d >= 0.88f;
        }));

        // 角丸の四角。Image.type = Sliced で、どの大きさにものばせる
        public static Sprite RoundRect => Get("roundrect", () =>
        {
            const int size = 64;
            const float r = 18f;
            return Shape(size, size, (u, v) =>
            {
                float x = u * size, y = v * size;
                float dx = Mathf.Max(0f, Mathf.Max(r - x, x - (size - r)));
                float dy = Mathf.Max(0f, Mathf.Max(r - y, y - (size - r)));
                return dx * dx + dy * dy <= r * r;
            }, new Vector4(22, 22, 22, 22));
        });

        // 上を向いた三角（屋根・後光の光の筋）
        public static Sprite Triangle => Get("triangle", () => Shape(64, 64, (u, v) => Mathf.Abs(u - 0.5f) <= 0.5f * (1f - v)));

        // 絵馬の形（下が四角、上が屋根）
        public static Sprite Ema => Get("ema", () => Shape(256, 210, (u, v) =>
        {
            const float eave = 0.66f;
            if (v <= eave) return true;
            return Mathf.Abs(u - 0.5f) <= 0.5f * (1f - (v - eave) / (1f - eave));
        }));

        // 紙垂（しで）のジグザグ
        public static Sprite Shide => Get("shide", () =>
        {
            // x: -4〜10、y: 0〜30（下向き）で描いた形を 0〜1 にする
            var pts = new[]
            {
                new Vector2(0, 0), new Vector2(8, 0), new Vector2(2, 14), new Vector2(10, 14),
                new Vector2(4, 30), new Vector2(-2, 30), new Vector2(2, 16), new Vector2(-4, 16)
            };
            for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2((pts[i].x + 4f) / 14f, 1f - pts[i].y / 30f);
            return Shape(56, 120, (u, v) => InsidePolygon(pts, u, v));
        });

        // 上が細い台形（参道）
        public static Sprite Path => Get("path", () => Shape(128, 128, (u, v) => Mathf.Abs(u - 0.5f) <= 0.5f * Mathf.Lerp(1f, 0.25f, v)));

        // 山の稜線。seed ごとにちがう形
        public static Sprite Ridge(int seed) => Get($"ridge{seed}", () =>
        {
            var rnd = new System.Random(seed);
            var f = new float[4];
            var p = new float[4];
            for (int i = 0; i < 4; i++)
            {
                f[i] = (i + 1) * (1.5f + (float)rnd.NextDouble() * 1.5f);
                p[i] = (float)rnd.NextDouble() * Mathf.PI * 2f;
            }
            return Shape(512, 256, (u, v) =>
            {
                float h = 0.7f;
                for (int i = 0; i < 4; i++) h += Mathf.Sin(u * f[i] * Mathf.PI + p[i]) * (0.14f / (i + 1));
                return v <= h;
            });
        });

        // 縦のグラデーション（下から上へ colors の順）
        public static Sprite VerticalGradient(string key, params Color[] colors) => Get($"grad:{key}", () =>
        {
            const int h = 256;
            var tex = NewTexture(2, h);
            var px = new Color[2 * h];
            for (int y = 0; y < h; y++)
            {
                float t = y / (h - 1f) * (colors.Length - 1);
                int i = Mathf.Min(Mathf.FloorToInt(t), colors.Length - 2);
                Color c = Color.Lerp(colors[i], colors[i + 1], t - i);
                px[y * 2] = c;
                px[y * 2 + 1] = c;
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, 2, h), new Vector2(0.5f, 0.5f), 100f);
        });

        // ---------- 作り方 ----------

        static Sprite Get(string key, Func<Sprite> create)
        {
            if (Cache.TryGetValue(key, out Sprite sprite) && sprite != null) return sprite;
            sprite = create();
            sprite.name = $"MenuArt_{key}";
            Cache[key] = sprite;
            return sprite;
        }

        // inside が true の所を白でぬる。3×3 の点の平均をアルファにする
        static Sprite Shape(int w, int h, Func<float, float, bool> inside, Vector4 border = default)
        {
            return Field(w, h, (u, v) => 0f, border, inside);
        }

        static Sprite Field(int w, int h, Func<float, float, float> alpha, Vector4 border = default, Func<float, float, bool> inside = null)
        {
            var tex = NewTexture(w, h);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float a;
                    if (inside == null)
                    {
                        a = alpha((x + 0.5f) / w, (y + 0.5f) / h);
                    }
                    else
                    {
                        int hit = 0;
                        for (int sy = 0; sy < 3; sy++)
                            for (int sx = 0; sx < 3; sx++)
                                if (inside((x + (sx + 0.5f) / 3f) / w, (y + (sy + 0.5f) / 3f) / h)) hit++;
                        a = hit / 9f;
                    }
                    px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }

        static Texture2D NewTexture(int w, int h)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
        }

        static bool InsidePolygon(Vector2[] pts, float u, float v)
        {
            bool inside = false;
            for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
            {
                if ((pts[i].y > v) != (pts[j].y > v)
                    && u < (pts[j].x - pts[i].x) * (v - pts[i].y) / (pts[j].y - pts[i].y) + pts[i].x)
                    inside = !inside;
            }
            return inside;
        }

        static float Sq(float x) => x * x;

        // 16進の色（"#c0362c"）
        public static Color Hex(string hex, float alpha = 1f)
        {
            ColorUtility.TryParseHtmlString(hex, out Color c);
            c.a = alpha;
            return c;
        }
    }
}
