using UnityEngine;
using UnityEngine.UI;

namespace Toufuku.Hud
{
    /*
        HUD とリザルトを、プレハブなしでコードから組み立てるための小さな道具（#61）

        ・Canvas は 1920×1080 を基準にして、画面の高さに合わせて拡大縮小する。
          なので「フォントサイズ ÷ 1080」がそのまま「画面の高さに対するわりあい」になる（HudLegibility で読める距離を出せる）
        ・文字は uGUI の Text（ダイナミックフォント）を使う。TextMeshPro の NotoSansJP SDF は静的アトラスで、
          入っていない漢字が□になるおそれがあるため
    */
    public static class HudUi
    {
        public const float ReferenceWidth = 1920f;
        public const float ReferenceHeight = 1080f;

        // HUD の縁は5桁（7章）。これより大きい値は 99999 で止めて表示する
        public const int MaxDisplayedScore = 99999;

        // Noto Sans JP のディセンダー（ベースラインより下）の大きさ ÷ フォントサイズ。大きさのちがう文字の下（ベースライン）をそろえるのに使う
        public const float DescenderRatio = 0.29f;

        // フォントが入っていなければ組み込みのフォント（日本語は OS のフォントで代わりに出る）
        public static Font ResolveFont(Font font)
        {
            return font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        // 画面の高さのわりあいから、基準の 1080 でのピクセル数
        public static int FontSizeOf(float screenHeightRatio)
        {
            return Mathf.Max(1, Mathf.RoundToInt(screenHeightRatio * ReferenceHeight));
        }

        public static RectTransform CreateCanvas(Transform parent, string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(parent, false);

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f; // 高さに合わせる

            return (RectTransform)go.transform;
        }

        // アンカーを1点にした四角
        public static RectTransform CreateRect(RectTransform parent, string name, Vector2 anchor, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = size;
            return rt;
        }

        // 親いっぱいに広げる
        public static RectTransform Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        // 親いっぱいの単色の板
        public static Image CreateImage(RectTransform parent, string name, Color color)
        {
            RectTransform rt = Stretch(CreateRect(parent, name, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero));
            Image image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        // 親いっぱいの文字。はみ出しても切らない。outline で縁取りして、明るい背景の上でも読めるようにする
        public static Text CreateText(RectTransform parent, string name, Font font, int fontSize, TextAnchor alignment,
            Color color, bool outline = true)
        {
            RectTransform rt = Stretch(CreateRect(parent, name, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero));
            Text text = rt.gameObject.AddComponent<Text>();
            text.font = ResolveFont(font);
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.raycastTarget = false;

            if (outline)
            {
                Outline o = rt.gameObject.AddComponent<Outline>();
                float d = Mathf.Max(2f, fontSize * 0.025f);
                o.effectColor = new Color(0f, 0f, 0f, 0.85f);
                o.effectDistance = new Vector2(d, -d);
            }
            return text;
        }

        // 縁の表示（5桁で止める）
        public static string FormatScore(int value)
        {
            return Mathf.Clamp(value, 0, MaxDisplayedScore).ToString();
        }

        // ランクの文字の色。上のランクほど明るく、S は金
        public static Color RankColor(ShrineRank rank)
        {
            switch (rank)
            {
                case ShrineRank.S: return new Color(1f, 0.85f, 0.25f);
                case ShrineRank.A: return new Color(1f, 0.55f, 0.35f);
                case ShrineRank.B: return new Color(0.55f, 0.9f, 0.6f);
                default:           return new Color(0.78f, 0.84f, 0.95f);
            }
        }

#if UNITY_EDITOR
        // エディタで付けたときに、プロジェクトの日本語フォントを探して入れる
        public static Font FindProjectJapaneseFont()
        {
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/NotoSansJP-Black.ttf")
                ?? UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/NotoSansJP-Bold.ttf");
        }
#endif
    }
}
