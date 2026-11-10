using UnityEngine;

namespace Toufuku.Hud
{
    /*
        HUD の「神社の掲示物」の見た目（木札・和紙の帯）を、画像ファイルなしでコードで描く道具

        ・木札: 上の角を斜めに落とした板。木目と、こげ茶のふち、上のまん中に吊るす穴
        ・和紙: 生成り色に繊維のむら。上に朱の帯
        どちらも決まった大きさのテクスチャを1回だけ作って、同じ大きさならつかい回す
        乱数は種を固定しているので、毎回同じ見た目になる
    */
    public static class HudShapes
    {
        public static readonly Color WoodLight = new Color(0.86f, 0.70f, 0.47f);
        public static readonly Color WoodDark = new Color(0.72f, 0.54f, 0.33f);
        public static readonly Color WoodEdge = new Color(0.39f, 0.22f, 0.02f);
        public static readonly Color WashiBase = new Color(0.96f, 0.94f, 0.88f);
        public static readonly Color WashiFiber = new Color(0.86f, 0.82f, 0.72f);
        public static readonly Color Shu = new Color(0.66f, 0.18f, 0.16f);

        static Sprite s_plaque;
        static Vector2Int s_plaqueSize;
        static Sprite s_washi;
        static Vector2Int s_washiSize;

        // 木札。size はピクセル（1080 基準の HUD の大きさをそのまま渡す）
        public static Sprite WoodPlaque(Vector2 size)
        {
            Vector2Int px = ToPixels(size);
            if (s_plaque != null && s_plaqueSize == px) return s_plaque;

            int w = px.x, h = px.y;
            var tex = NewTexture(w, h, "WoodPlaque");
            var pixels = new Color32[w * h];
            float cut = Mathf.Min(w, h) * 0.12f;
            float border = Mathf.Max(3f, Mathf.Min(w, h) * 0.025f);
            Vector2 hole = new Vector2(w * 0.5f, h - cut * 0.75f);
            float holeR = Mathf.Max(4f, Mathf.Min(w, h) * 0.022f);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float inside = PlaqueDistance(x + 0.5f, y + 0.5f, w, h, cut);
                    if (inside <= 0f) { pixels[y * w + x] = new Color32(0, 0, 0, 0); continue; }

                    // 木目: 横にのびる筋を、ゆらぎのある sin で
                    float grain = Mathf.Sin(y * 0.22f + Mathf.PerlinNoise(x * 0.012f, y * 0.05f) * 9f) * 0.5f + 0.5f;
                    float fine = Mathf.PerlinNoise(x * 0.08f, y * 0.6f);
                    Color c = Color.Lerp(WoodLight, WoodDark, grain * 0.55f + fine * 0.25f);

                    // ふち（こげ茶）と、ふちの内側のうすい影
                    if (inside < border) c = WoodEdge;
                    else if (inside < border * 2.5f) c = Color.Lerp(c, WoodEdge, 0.35f * (1f - (inside - border) / (border * 1.5f)));

                    // 吊るす穴
                    float dh = Vector2.Distance(new Vector2(x, y), hole);
                    if (dh < holeR) c = WoodEdge * 0.7f;
                    else if (dh < holeR + 1.5f) c = Color.Lerp(c, WoodEdge, 0.6f);

                    float a = Mathf.Clamp01(inside);
                    pixels[y * w + x] = new Color(c.r, c.g, c.b, a);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            s_plaque = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            s_plaque.name = "WoodPlaque";
            s_plaqueSize = px;
            return s_plaque;
        }

        // 和紙の帯。上に朱の帯
        public static Sprite WashiBand(Vector2 size)
        {
            Vector2Int px = ToPixels(size);
            if (s_washi != null && s_washiSize == px) return s_washi;

            int w = px.x, h = px.y;
            var tex = NewTexture(w, h, "WashiBand");
            var pixels = new Color32[w * h];
            int band = Mathf.Max(6, Mathf.RoundToInt(h * 0.07f));
            var rng = new System.Random(20261110);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Color c;
                    if (y >= h - band)
                    {
                        c = Shu;
                    }
                    else
                    {
                        float cloud = Mathf.PerlinNoise(x * 0.015f + 3f, y * 0.015f + 7f);
                        c = Color.Lerp(WashiBase, WashiFiber, cloud * 0.35f);
                    }
                    pixels[y * w + x] = new Color(c.r, c.g, c.b, 0.95f);
                }
            }

            // 繊維: 短い曲がった線をちらす
            int fibers = w * h / 900;
            for (int i = 0; i < fibers; i++)
            {
                float fx = (float)rng.NextDouble() * w;
                float fy = (float)rng.NextDouble() * (h - band);
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                int len = 6 + rng.Next(14);
                for (int s = 0; s < len; s++)
                {
                    ang += ((float)rng.NextDouble() - 0.5f) * 0.5f;
                    fx += Mathf.Cos(ang);
                    fy += Mathf.Sin(ang);
                    int ix = Mathf.RoundToInt(fx), iy = Mathf.RoundToInt(fy);
                    if (ix < 0 || iy < 0 || ix >= w || iy >= h - band) break;
                    Color c = pixels[iy * w + ix];
                    pixels[iy * w + ix] = Color.Lerp(c, WashiFiber, 0.55f);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            s_washi = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            s_washi.name = "WashiBand";
            s_washiSize = px;
            return s_washi;
        }

        // 上の角を斜めに落とした板の内側までの距離（ピクセル）。外なら 0 以下
        static float PlaqueDistance(float x, float y, int w, int h, float cut)
        {
            float d = Mathf.Min(Mathf.Min(x, w - x), Mathf.Min(y, h - y));
            // 左上・右上の斜めの辺（上が y = h）
            float diagL = (x + (h - y) - cut) * 0.7071f;
            float diagR = ((w - x) + (h - y) - cut) * 0.7071f;
            return Mathf.Min(d, Mathf.Min(diagL, diagR));
        }

        static Vector2Int ToPixels(Vector2 size)
        {
            return new Vector2Int(Mathf.Clamp(Mathf.RoundToInt(size.x), 8, 2048), Mathf.Clamp(Mathf.RoundToInt(size.y), 8, 2048));
        }

        static Texture2D NewTexture(int w, int h, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.name = name;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.hideFlags = HideFlags.DontSave;
            return tex;
        }
    }
}
