using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Toufuku.Hud;
using Toufuku.Rescue;

namespace Toufuku.Tutorial
{
    /*
        段階学習（#58）の画面の手がかり。説明の文章は足さない（18章「文章を増やさず、配置・同期パルス・照準反応・ゴースト表示の順に修正」）

        ・ボタンの列: ボタン箱と同じならび（左から 健康・学業成就・厄除け安全・縁結び・金運）の5色の四角を画面の下に出す
          脈動させる色を明るく大きくする。ボタン箱の LED を光らせられるようになるまでの代わり
          （LED は StagedLearningDirector.ButtonPulseChanged につなぐ）。今選んでいる色は白い枠で囲む
        ・ゴースト: うすい照準マークが今の照準から相手の客へすべり、うすい大幣が一振りする。1回だけ再生して消える
        ・短い表示: 「色が違う」（誤投擲。罰はない）と「先に救えた！」（二重円の客を救えた）。18章に書いてある2つだけ

        Canvas はコードで組み立てるので、空の GameObject に付けるだけで動く。見た目の値は T1 の結果で調整する
    */
    [DisallowMultipleComponent]
    public class LearningCueView : MonoBehaviour
    {
        public const string WrongColorText = "色が違う";
        public const string PriorityRescuedText = "先に救えた！";

        const int ColorCount = 5;

        static readonly Color[] FallbackColors =
        {
            new Color(63 / 255f, 191 / 255f, 95 / 255f),
            new Color(47 / 255f, 127 / 255f, 216 / 255f),
            new Color(139 / 255f, 95 / 255f, 208 / 255f),
            new Color(232 / 255f, 95 / 255f, 143 / 255f),
            new Color(232 / 255f, 185 / 255f, 63 / 255f),
        };

        [Header("参照")]
        [Tooltip("NotoSansJP の .ttf。未設定なら組み込みフォント。")]
        [SerializeField] Font font;
        [Tooltip("お守り5色パレット（唯一の正）。未設定なら企画書 v8 のカラーコードの予備。")]
        [SerializeField] OmamoriPalette palette;
        [SerializeField] int sortingOrder = 60;

        [Header("ボタンの列（1080 基準のピクセル）")]
        [SerializeField, Min(10f)] float buttonSize = 72f;
        [SerializeField, Min(0f)] float buttonSpacing = 20f;
        [Tooltip("画面の下から列の中心までの高さ。")]
        [SerializeField] float buttonRowY = 130f;
        [Tooltip("脈動していないボタンの濃さ。")]
        [SerializeField, Range(0f, 1f)] float idleButtonAlpha = 0.45f;
        [Tooltip("脈動がいちばん強いときの大きさ（倍）。")]
        [SerializeField, Range(1f, 2f)] float pulseScale = 1.35f;

        [Header("ゴースト")]
        [SerializeField] Color ghostColor = new Color(1f, 1f, 1f, 0.6f);
        [SerializeField, Min(10f)] float ghostReticleSize = 120f;
        [Tooltip("照準マークが相手へすべる秒数。")]
        [SerializeField, Min(0.05f)] float ghostSlideSeconds = 0.7f;
        [Tooltip("大幣が一振りする秒数。")]
        [SerializeField, Min(0.05f)] float ghostSwingSeconds = 0.45f;
        [Tooltip("最後の形のまま止めておく秒数。")]
        [SerializeField, Min(0f)] float ghostHoldSeconds = 0.5f;
        [SerializeField, Min(0.05f)] float ghostFadeSeconds = 0.25f;
        [Tooltip("大幣の太さと長さ。")]
        [SerializeField] Vector2 wandSize = new Vector2(26f, 380f);
        [Tooltip("振りはじめの傾き（度）。相手を向いた角度からこの分だけ手前に引いたところから振る。")]
        [SerializeField, Range(0f, 120f)] float wandBackswingDegrees = 60f;

        [Header("短い表示")]
        [SerializeField, Range(0.02f, 0.2f)] float popupFontRatio = 0.07f;
        [SerializeField, Min(0.1f)] float popupSeconds = 0.9f;
        [SerializeField] Color wrongColorTextColor = new Color(1f, 0.82f, 0.82f);
        [SerializeField] Color priorityTextColor = new Color(1f, 0.92f, 0.45f);

        RectTransform _canvas;
        RectTransform _row;
        readonly RectTransform[] _buttonRects = new RectTransform[ColorCount];
        readonly Image[] _buttons = new Image[ColorCount];
        readonly Outline[] _selectFrames = new Outline[ColorCount];
        readonly float[] _pulse = new float[ColorCount];
        readonly float[] _ghostPulse = new float[ColorCount];
        RectTransform _ghostReticle;
        Image _ghostReticleImage;
        RectTransform _wand;
        Image _wandImage;
        Coroutine _ghost;
        bool _rowRequested;
        int _selected = -1;
        static Sprite s_ring;

        // 確認用
        public bool ButtonRowVisible => _row != null && _row.gameObject.activeSelf;
        public bool IsGhostPlaying => _ghost != null;
        public int GhostPlayCount { get; private set; }
        public string LastPopupText { get; private set; }
        public int PopupCount { get; private set; }
        public float ButtonPulseOf(int color) => color >= 0 && color < ColorCount ? Mathf.Max(_pulse[color], _ghostPulse[color]) : 0f;

#if UNITY_EDITOR
        void Reset()
        {
            font = HudUi.FindProjectJapaneseFont();
        }
#endif

        void Awake()
        {
            Build();
            ApplyRowVisibility();
        }

        void OnDisable()
        {
            StopGhost();
        }

        void LateUpdate()
        {
            for (int i = 0; i < ColorCount; i++)
            {
                if (_buttons[i] == null) continue;
                float amount = Mathf.Clamp01(Mathf.Max(_pulse[i], _ghostPulse[i]));
                Color c = ColorOf(i);
                c.a = Mathf.Lerp(idleButtonAlpha, 1f, amount);
                _buttons[i].color = Color.Lerp(c, Color.white, amount * 0.25f);
                _buttonRects[i].localScale = Vector3.one * Mathf.Lerp(1f, pulseScale, amount);
                _selectFrames[i].enabled = i == _selected;
            }
        }

        // ---------- 外から使う ----------

        public void SetPalette(OmamoriPalette next)
        {
            palette = next;
        }

        public void SetFont(Font next)
        {
            font = next;
        }

        // ボタンの列を出すか（学習中だけ出す）
        public void SetButtonRowVisible(bool visible)
        {
            _rowRequested = visible;
            ApplyRowVisibility();
        }

        // 色 color のボタンの脈動の強さ（0〜1）
        public void SetButtonPulse(int color, float amount01)
        {
            if (color < 0 || color >= ColorCount) return;
            _pulse[color] = Mathf.Clamp01(amount01);
        }

        // 今選んでいる色（白い枠）。-1 で枠なし
        public void SetSelectedColor(int color)
        {
            _selected = color;
        }

        /*
            ゴーストを1回再生する（再生中なら止めてから）
            fromScreen / toScreen: 照準マークがすべる始まりと終わり（スクリーン座標）
            slide: 照準マークをすべらせるか。swing: 最後に大幣を一振りするか。pulseColor: いっしょに脈動させるボタンの色（-1 でなし）
        */
        public void PlayGhost(Vector2 fromScreen, Vector2 toScreen, bool slide, bool swing, int pulseColor)
        {
            if (_canvas == null) Build();
            StopGhost();
            GhostPlayCount++;
            _ghost = StartCoroutine(GhostRoutine(fromScreen, toScreen, slide, swing, pulseColor));
        }

        public void StopGhost()
        {
            if (_ghost != null) StopCoroutine(_ghost);
            _ghost = null;
            for (int i = 0; i < ColorCount; i++) _ghostPulse[i] = 0f;
            if (_ghostReticle != null) _ghostReticle.gameObject.SetActive(false);
            if (_wand != null) _wand.gameObject.SetActive(false);
            ApplyRowVisibility();
        }

        // 誤投擲の短い表示（罰はない）
        public void ShowWrongColor(Vector2 screen) => ShowPopup(WrongColorText, screen, wrongColorTextColor);

        // 二重円の客を救えたときの短い表示
        public void ShowPriorityRescued(Vector2 screen) => ShowPopup(PriorityRescuedText, screen, priorityTextColor, 1.3f);

        public void ShowPopup(string text, Vector2 screen, Color color, float sizeScale = 1f)
        {
            if (_canvas == null) Build();
            LastPopupText = text;
            PopupCount++;
            if (isActiveAndEnabled) StartCoroutine(PopupRoutine(text, ToLocal(screen), color, sizeScale));
        }

        // ---------- 組み立て ----------

        void Build()
        {
            if (_canvas != null) return;
            _canvas = HudUi.CreateCanvas(transform, "LearningCueCanvas", sortingOrder);

            float width = ColorCount * buttonSize + (ColorCount - 1) * buttonSpacing;
            _row = HudUi.CreateRect(_canvas, "ButtonRow", new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, buttonRowY), new Vector2(width, buttonSize));

            for (int i = 0; i < ColorCount; i++)
            {
                float x = -width * 0.5f + buttonSize * 0.5f + i * (buttonSize + buttonSpacing);
                RectTransform rt = HudUi.CreateRect(_row, "Button" + i, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(x, 0f), new Vector2(buttonSize, buttonSize));
                Image image = rt.gameObject.AddComponent<Image>();
                image.raycastTarget = false;
                image.color = ColorOf(i);

                Outline frame = rt.gameObject.AddComponent<Outline>();
                frame.effectColor = Color.white;
                frame.effectDistance = new Vector2(5f, -5f);
                frame.useGraphicAlpha = false;
                frame.enabled = false;

                _buttonRects[i] = rt;
                _buttons[i] = image;
                _selectFrames[i] = frame;
            }

            _ghostReticle = HudUi.CreateRect(_canvas, "GhostReticle", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(ghostReticleSize, ghostReticleSize));
            _ghostReticleImage = _ghostReticle.gameObject.AddComponent<Image>();
            _ghostReticleImage.sprite = RingSprite();
            _ghostReticleImage.raycastTarget = false;
            _ghostReticle.gameObject.SetActive(false);

            // 大幣は画面の下のまん中を根元にして回す
            _wand = HudUi.CreateRect(_canvas, "GhostOnusa", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, -wandSize.y * 0.15f), wandSize);
            _wandImage = _wand.gameObject.AddComponent<Image>();
            _wandImage.raycastTarget = false;
            _wand.gameObject.SetActive(false);
        }

        void ApplyRowVisibility()
        {
            if (_row == null) return;
            bool ghostNeedsRow = false;
            for (int i = 0; i < ColorCount; i++) ghostNeedsRow |= _ghostPulse[i] > 0f;
            _row.gameObject.SetActive(_rowRequested || (_ghost != null && ghostNeedsRow));
        }

        Color ColorOf(int index)
        {
            if (palette != null) return palette.GetColor(index);
            return index >= 0 && index < FallbackColors.Length ? FallbackColors[index] : Color.magenta;
        }

        // スクリーン座標 → この Canvas のまん中を原点にした座標
        Vector2 ToLocal(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvas, screen, null, out Vector2 local);
            return local;
        }

        // ---------- 再生 ----------

        IEnumerator GhostRoutine(Vector2 fromScreen, Vector2 toScreen, bool slide, bool swing, int pulseColor)
        {
            Vector2 from = ToLocal(fromScreen);
            Vector2 to = ToLocal(toScreen);
            bool pulse = pulseColor >= 0 && pulseColor < ColorCount;

            _ghostReticle.gameObject.SetActive(true);
            _ghostReticle.anchoredPosition = slide ? from : to;
            if (pulse) _ghostPulse[pulseColor] = 1f;
            ApplyRowVisibility();

            // 1) 照準マークが相手へすべる（色のボタンはいっしょに脈動）
            float slideSeconds = slide ? ghostSlideSeconds : ghostSlideSeconds * 0.5f;
            for (float t = 0f; t < slideSeconds; t += Time.unscaledDeltaTime)
            {
                float p = Mathf.SmoothStep(0f, 1f, t / slideSeconds);
                if (slide) _ghostReticle.anchoredPosition = Vector2.Lerp(from, to, p);
                SetGhostAlpha(Mathf.Clamp01(t / (slideSeconds * 0.4f)));
                if (pulse) _ghostPulse[pulseColor] = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 4f);
                yield return null;
            }
            _ghostReticle.anchoredPosition = to;
            SetGhostAlpha(1f);

            // 2) 大幣が相手に向かって一振りする
            if (swing)
            {
                Vector2 root = new Vector2(0f, -_canvas.rect.height * 0.5f + _wand.anchoredPosition.y);
                Vector2 dir = to - root;
                float aimAngle = -Mathf.Atan2(dir.x, Mathf.Max(1f, dir.y)) * Mathf.Rad2Deg;
                float back = aimAngle + (dir.x >= 0f ? wandBackswingDegrees : -wandBackswingDegrees);

                _wand.gameObject.SetActive(true);
                for (float t = 0f; t < ghostSwingSeconds; t += Time.unscaledDeltaTime)
                {
                    // 振り下ろしは後半ほど速く（ease-in）
                    float p = t / ghostSwingSeconds;
                    _wand.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(back, aimAngle, p * p));
                    SetGhostAlpha(1f);
                    yield return null;
                }
                _wand.localRotation = Quaternion.Euler(0f, 0f, aimAngle);
                _ghostReticle.localScale = Vector3.one * 1.2f;
            }

            // 3) 少し止めてから消える
            for (float t = 0f; t < ghostHoldSeconds; t += Time.unscaledDeltaTime) yield return null;
            for (float t = 0f; t < ghostFadeSeconds; t += Time.unscaledDeltaTime)
            {
                SetGhostAlpha(1f - t / ghostFadeSeconds);
                yield return null;
            }

            _ghostReticle.localScale = Vector3.one;
            _ghost = null;
            StopGhost();
        }

        void SetGhostAlpha(float a)
        {
            Color c = ghostColor;
            c.a *= Mathf.Clamp01(a);
            if (_ghostReticleImage != null) _ghostReticleImage.color = c;
            if (_wandImage != null) _wandImage.color = c;
        }

        IEnumerator PopupRoutine(string text, Vector2 local, Color color, float sizeScale)
        {
            int size = HudUi.FontSizeOf(popupFontRatio * sizeScale);
            RectTransform rt = HudUi.CreateRect(_canvas, "Popup", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                local + new Vector2(0f, size * 1.2f), new Vector2(size * text.Length * 1.1f, size * 1.4f));
            Text label = HudUi.CreateText(rt, "Text", font, size, TextAnchor.MiddleCenter, color);
            label.text = text;

            Vector2 start = rt.anchoredPosition;
            for (float t = 0f; t < popupSeconds; t += Time.unscaledDeltaTime)
            {
                float p = t / popupSeconds;
                rt.anchoredPosition = start + new Vector2(0f, size * 0.6f * p);
                Color c = color;
                c.a = p < 0.7f ? 1f : 1f - (p - 0.7f) / 0.3f;
                label.color = c;
                yield return null;
            }
            Destroy(rt.gameObject);
        }

        // うすい照準マーク（輪と中心の点）の Sprite をコードで作る
        static Sprite RingSprite()
        {
            if (s_ring != null) return s_ring;

            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(r - 0.82f) / 0.09f);
                    float dot = Mathf.Clamp01(1f - r / 0.12f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Max(ring, dot)));
                }
            }
            tex.Apply();
            s_ring = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return s_ring;
        }
    }
}
