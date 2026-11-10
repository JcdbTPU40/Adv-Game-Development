using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Toufuku.Menu
{
    /*
        メインメニューの背景（夜明けの境内）を uGUI で組み立てて、動かす

        ・空のグラデーション、星、月、山なみ、桜の木、参道、大鳥居（しめ縄と紙垂）、石灯籠、舞う花びら
        ・形は MenuArt のスプライトに色をつけて重ねるだけ。素材ができたら、ここの Place〜 を画像に差しかえる
        ・座標は 1600×900 の下書き（企画のモック）の値をそのまま書き、Mock() で 1920×1080・左下原点に直す
        ・カメラの寄り引きは、背景ぜんたいの拡大率で出す（Zoom）。画面を暗くするのは Dim
        MonoBehaviour は使っていない。MainMenu の Update から Tick を呼ぶ
    */
    public sealed class ShrineBackdrop
    {
        const float MockScale = 1920f / 1600f;
        const int PetalCount = 34;
        const int StarCount = 110;

        readonly RectTransform _scene;
        readonly Image _dim;
        readonly List<Image> _lanternGlows = new List<Image>();
        readonly List<(Image image, float phase, float speed)> _stars = new List<(Image, float, float)>();
        readonly List<Petal> _petals = new List<Petal>();
        readonly System.Random _rnd = new System.Random(7);

        float _zoom = 1f, _zoomTarget = 1f;
        float _dimAlpha, _dimTarget;
        Vector2 _offset, _offsetTarget;

        sealed class Petal
        {
            public RectTransform Rect;
            public Vector2 Pos;
            public float Fall, Phase, Spin;
        }

        public RectTransform Root { get; }

        public ShrineBackdrop(RectTransform parent)
        {
            Root = HudUi_Stretch(new GameObject("ShrineBackdrop", typeof(RectTransform)), parent);

            // 空は画面いっぱい（横長の画面でもすき間が出ないように）
            Image sky = Root.gameObject.AddComponent<Image>();
            sky.sprite = MenuArt.VerticalGradient("sky",
                MenuArt.Hex("#f09a6a"), MenuArt.Hex("#8a3a5c"), MenuArt.Hex("#2c1846"), MenuArt.Hex("#0b0820"), MenuArt.Hex("#0b0820"));
            sky.raycastTarget = false;

            // 絵は 1920×1080 の板に描いて、中央に置く。寄り引きはこの板の拡大率
            _scene = (RectTransform)new GameObject("Scene", typeof(RectTransform)).transform;
            _scene.SetParent(Root, false);
            _scene.anchorMin = _scene.anchorMax = new Vector2(0.5f, 0.5f);
            _scene.pivot = new Vector2(0.5f, 0.5f);
            _scene.sizeDelta = new Vector2(1920f, 1080f);

            BuildSky();
            BuildHills();
            BuildTorii();
            BuildLantern(470f);
            BuildLantern(1130f);

            _dim = Place(Root, "Dim", null, Vector2.zero, Vector2.zero, MenuArt.Hex("#0b0618", 0f));
            HudUi_StretchRect(_dim.rectTransform);

            BuildPetals();
        }

        // 寄り（1 で全景、大きいほど鳥居に寄る）。offsetY は下書きの y（大きいほど下を見る）
        public void SetCamera(float zoom, float offsetMockY = 0f, bool instant = false)
        {
            _zoomTarget = zoom;
            _offsetTarget = new Vector2(0f, offsetMockY * MockScale);
            if (instant)
            {
                _zoom = _zoomTarget;
                _offset = _offsetTarget;
            }
        }

        public void SetDim(float alpha, bool instant = false)
        {
            _dimTarget = alpha;
            if (instant) _dimAlpha = alpha;
        }

        public void Tick(float time, float dt)
        {
            // 寄り引きはゆっくり（2.4秒ほどで落ちつく）
            float k = 1f - Mathf.Exp(-dt * 1.8f);
            _zoom = Mathf.Lerp(_zoom, _zoomTarget, k);
            _offset = Vector2.Lerp(_offset, _offsetTarget, k);
            _scene.localScale = Vector3.one * _zoom;
            _scene.anchoredPosition = _offset * _zoom;

            _dimAlpha = Mathf.MoveTowards(_dimAlpha, _dimTarget, dt * 1.2f);
            Color dc = _dim.color;
            dc.a = _dimAlpha;
            _dim.color = dc;

            // 灯籠のゆらぎ
            for (int i = 0; i < _lanternGlows.Count; i++)
            {
                float f = 0.82f + 0.18f * Mathf.PerlinNoise(time * 2.2f, i * 7.3f);
                _lanternGlows[i].rectTransform.localScale = Vector3.one * (0.94f + 0.12f * f);
                Color c = _lanternGlows[i].color;
                c.a = 0.75f * f;
                _lanternGlows[i].color = c;
            }

            // 星のまたたき
            foreach ((Image image, float phase, float speed) s in _stars)
            {
                Color c = s.image.color;
                c.a = 0.25f + 0.55f * (0.5f + 0.5f * Mathf.Sin(time * s.speed + s.phase));
                s.image.color = c;
            }

            // 花びら（画面の座標で落とす。寄り引きには乗せない）
            Vector2 size = Root.rect.size;
            foreach (Petal p in _petals)
            {
                p.Pos.y -= p.Fall * dt;
                p.Pos.x += (Mathf.Sin(time * 1.3f + p.Phase) * 40f + 25f) * dt;
                if (p.Pos.y < -size.y * 0.5f - 30f)
                {
                    p.Pos.y = size.y * 0.5f + 30f;
                    p.Pos.x = Rand(-size.x * 0.5f, size.x * 0.5f);
                }
                if (p.Pos.x > size.x * 0.5f + 30f) p.Pos.x = -size.x * 0.5f - 30f;
                p.Rect.anchoredPosition = p.Pos;
                p.Rect.localRotation = Quaternion.Euler(0f, 0f, time * p.Spin + p.Phase * 57f);
            }
        }

        // ---------- 組み立て ----------

        void BuildSky()
        {
            for (int i = 0; i < StarCount; i++)
            {
                float r = Rand(1.2f, 4.2f);
                Image star = PlaceMock("Star", MenuArt.Disc, new Vector2(Rand(-200f, 1800f), Rand(0f, 440f)), new Vector2(r, r) / MockScale, Color.white);
                _stars.Add((star, Rand(0f, 6.3f), Rand(0.6f, 2.2f)));
            }

            PlaceMock("MoonGlow", MenuArt.Glow, new Vector2(1260f, 170f), new Vector2(330f, 330f), MenuArt.Hex("#fff6dc", 0.55f));
            PlaceMock("Moon", MenuArt.Disc, new Vector2(1260f, 170f), new Vector2(104f, 104f), MenuArt.Hex("#fbefcf"));
            // 地平の朝焼け
            PlaceMock("Dawn", MenuArt.Glow, new Vector2(800f, 760f), new Vector2(2200f, 700f), MenuArt.Hex("#ffb080", 0.35f));
        }

        void BuildHills()
        {
            PlaceMock("FarRidge", MenuArt.Ridge(3), new Vector2(800f, 640f), new Vector2(2400f, 600f), MenuArt.Hex("#3a2048", 0.9f));
            PlaceMock("NearRidge", MenuArt.Ridge(11), new Vector2(800f, 740f), new Vector2(2400f, 440f), MenuArt.Hex("#241433"));

            // 左右の桜の木（暗い木の影に、ぼんやり光る花）
            Color tree = MenuArt.Hex("#160c20");
            Color bloom = MenuArt.Hex("#f2a7bf", 0.55f);
            foreach (float side in new[] { -1f, 1f })
            {
                float cx = side < 0 ? 140f : 1470f;
                PlaceMock("TreeTrunk", null, new Vector2(cx, 740f), new Vector2(26f, 220f), tree);
                PlaceMock("TreeCrown", MenuArt.Disc, new Vector2(cx, 640f), new Vector2(380f, 200f), tree);
                PlaceMock("TreeCrown2", MenuArt.Disc, new Vector2(cx + 70f * side, 600f), new Vector2(260f, 170f), tree);
                for (int i = 0; i < 9; i++)
                {
                    float r = Rand(50f, 100f);
                    PlaceMock("Bloom", MenuArt.Disc, new Vector2(cx + Rand(-170f, 170f), Rand(560f, 660f)), new Vector2(r, r * 0.8f), bloom);
                }
                PlaceMock("BloomGlow", MenuArt.Glow, new Vector2(cx, 610f), new Vector2(520f, 300f), MenuArt.Hex("#ffc4d6", 0.25f));
            }

            // 地面と参道
            PlaceMock("Ground", null, new Vector2(800f, 860f), new Vector2(2600f, 120f), MenuArt.Hex("#140a1a"));
            PlaceMock("Sando", MenuArt.Path, new Vector2(800f, 790f), new Vector2(360f, 300f), MenuArt.Hex("#3e2c46"));
            for (int i = 0; i < 6; i++)
            {
                float y = 670f + i * 40f;
                float w = Mathf.Lerp(110f, 330f, (y - 640f) / 300f);
                PlaceMock("Paving", null, new Vector2(800f, y), new Vector2(w, 3f), MenuArt.Hex("#1c1222", 0.8f));
            }
        }

        void BuildTorii()
        {
            Color black = MenuArt.Hex("#1d0d0d");
            Color red = MenuArt.Hex("#c0362c");
            Color redShade = MenuArt.Hex("#8e2219");

            // 柱（少し末広がり）と亀腹
            foreach (float x in new[] { 652f, 948f })
            {
                Image pillar = PlaceMock("Hashira", null, new Vector2(x, 500f), new Vector2(36f, 320f), red);
                pillar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, x < 800f ? -1.2f : 1.2f);
                PlaceMock("HashiraShade", null, new Vector2(x + 10f, 500f), new Vector2(10f, 320f), redShade)
                    .rectTransform.localRotation = Quaternion.Euler(0f, 0f, x < 800f ? -1.2f : 1.2f);
                PlaceMock("Kamebara", null, new Vector2(x, 652f), new Vector2(52f, 22f), black);
            }

            PlaceMock("Nuki", null, new Vector2(800f, 399f), new Vector2(400f, 20f), red);
            PlaceMock("Shimaki", null, new Vector2(800f, 337f), new Vector2(470f, 28f), red);
            PlaceMock("Gakuzuka", null, new Vector2(800f, 370f), new Vector2(30f, 44f), red);
            PlaceMock("Gaku", null, new Vector2(800f, 370f), new Vector2(20f, 32f), MenuArt.Hex("#e8c65a"));

            // 笠木（両はしが反りあがる）
            PlaceMock("Kasagi", null, new Vector2(800f, 312f), new Vector2(500f, 22f), black);
            PlaceMock("KasagiTop", null, new Vector2(800f, 298f), new Vector2(520f, 14f), black);
            foreach (float side in new[] { -1f, 1f })
            {
                PlaceMock("KasagiEnd", null, new Vector2(800f + side * 270f, 292f), new Vector2(60f, 16f), black)
                    .rectTransform.localRotation = Quaternion.Euler(0f, 0f, side * 9f);
            }

            // しめ縄と紙垂
            Rope(new Vector2(600f, 418f), new Vector2(800f, 470f), new Vector2(1000f, 418f), 11f);
            foreach (Vector2 p in new[] { new Vector2(700f, 456f), new Vector2(800f, 470f), new Vector2(900f, 456f) })
                PlaceMock("Shide", MenuArt.Shide, p, new Vector2(16f, 36f), MenuArt.Hex("#fffaf0"));
        }

        void BuildLantern(float x)
        {
            Color stone = MenuArt.Hex("#5b5266");
            Image glow = PlaceMock("LanternGlow", MenuArt.Glow, new Vector2(x, 600f), new Vector2(260f, 260f), MenuArt.Hex("#ffb040", 0.75f));
            _lanternGlows.Add(glow);
            PlaceMock("Kasa", MenuArt.Triangle, new Vector2(x, 540f), new Vector2(100f, 42f), stone);
            PlaceMock("Cap", null, new Vector2(x, 557f), new Vector2(76f, 12f), stone);
            PlaceMock("Frame", null, new Vector2(x, 595f), new Vector2(66f, 70f), stone);
            PlaceMock("Light", null, new Vector2(x, 595f), new Vector2(46f, 52f), MenuArt.Hex("#ffd98a"));
            PlaceMock("Sao", null, new Vector2(x, 675f), new Vector2(24f, 92f), stone);
            PlaceMock("Kiso", null, new Vector2(x, 725f), new Vector2(86f, 18f), stone);
        }

        void BuildPetals()
        {
            for (int i = 0; i < PetalCount; i++)
            {
                float s = Rand(0.7f, 1.3f);
                Image img = Place(Root, "Petal", MenuArt.Disc, Vector2.zero, new Vector2(20f, 12f) * s, MenuArt.Hex("#f7b8cc", Rand(0.65f, 0.95f)));
                img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                _petals.Add(new Petal
                {
                    Rect = img.rectTransform,
                    Pos = new Vector2(Rand(-1100f, 1100f), Rand(-600f, 600f)),
                    Fall = Rand(40f, 110f),
                    Phase = Rand(0f, 6.3f),
                    Spin = Rand(-120f, 120f)
                });
            }
        }

        // 2次ベジェに沿って短い棒をならべ、縄にする
        void Rope(Vector2 a, Vector2 ctrl, Vector2 b, float thickness)
        {
            const int segments = 24;
            Vector2 prev = a;
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector2 p = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * ctrl + t * t * b;
                Vector2 mid = (prev + p) * 0.5f;
                Vector2 d = p - prev;
                Image seg = PlaceMock("Rope", null, mid, new Vector2(d.magnitude + 2f, thickness), MenuArt.Hex(i % 2 == 0 ? "#d8c08a" : "#c4a870"));
                seg.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                prev = p;
            }
        }

        // ---------- 置き方 ----------

        // 下書き（1600×900・左上原点）の中心と大きさで置く
        Image PlaceMock(string name, Sprite sprite, Vector2 mockCenter, Vector2 mockSize, Color color)
        {
            return Place(_scene, name, sprite, Mock(mockCenter), mockSize * MockScale, color);
        }

        public static Vector2 Mock(Vector2 p) => new Vector2(p.x * MockScale, (900f - p.y) * MockScale);

        static Image Place(RectTransform parent, string name, Sprite sprite, Vector2 position, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static RectTransform HudUi_Stretch(GameObject go, RectTransform parent)
        {
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            HudUi_StretchRect(rt);
            return rt;
        }

        static void HudUi_StretchRect(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        float Rand(float min, float max) => min + (float)_rnd.NextDouble() * (max - min);
    }
}
