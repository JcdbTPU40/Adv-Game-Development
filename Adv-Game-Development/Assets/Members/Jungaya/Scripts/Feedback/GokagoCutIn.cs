using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using Toufuku.Hud;

namespace Toufuku.Feedback
{
    /*
        ご加護タイムのカットイン演出（#29 / 企画書 v8 7章「ご加護タイム」演出）

        仕様: 0.8秒。巫女の姿が一瞬透けて神の姿が重なる＋画面の縁が金色に光り、鈴が鳴る。操作は止めない（止めると3分の体験が削られる）
        ・GokagoTime.onGokagoStart を受け取って再生する
        ・Time.timeScale・カメラ（Cinemachine）・入力には触らない。画面の上に重ねる UI だけで見せる
          （GameSession の時計は実時間で進むので、世界を止めると危険度Dだけが進んでしまう）
        ・画面のまん中（照準と客）は覆わない。金色は画面の縁、神の姿は左はしに半透明で出す
        ・Tween は SetUpdate(true)（通信の復帰待ちで timeScale=0 になっても 0.8秒で終わる）
        ・神の姿は、godModelPrefab（3Dモデル）を専用カメラで RenderTexture に映して重ねる。
          モデルは画面から遠い場所（stageOffset）に置き、専用カメラは演出中だけ動かす
          モデルがなければ godSprite（絵）、それもなければ仮のシルエットを合成して出す
    */
    public class GokagoCutIn : MonoBehaviour
    {
        [Tooltip("未設定なら同じシーンの GokagoTime を探す")]
        [SerializeField] GokagoTime gokago;

        [Header("素材（未設定なら仮のものを合成）")]
        [Tooltip("神の姿の3Dモデル。GokagoGodFigure を付けておくと、演出に合わせて動く。入っていれば godSprite より優先")]
        [SerializeField] GameObject godModelPrefab;
        [Tooltip("神の姿の絵（モデルがないとき）。巫女に重なって見えるよう、透過 PNG の立ち姿を入れる")]
        [SerializeField] Sprite godSprite;
        [SerializeField] Font font;
        [SerializeField] AudioClip bellSe;
        [SerializeField, Range(0f, 1f)] float bellVolume = 0.9f;

        [Header("見た目")]
        [SerializeField] Color gold = new Color(1f, 0.82f, 0.32f);
        [Tooltip("金色に光る縁の太さ（画面の高さに対するわりあい）")]
        [SerializeField, Range(0.02f, 0.3f)] float edgeWidth = 0.14f;
        [Tooltip("神の姿のいちばん濃いときの不透明度。照準の読みやすさを優先して薄めにする")]
        [SerializeField, Range(0f, 1f)] float figureMaxAlpha = 0.65f;
        [Tooltip("神の姿の高さ（画面の高さに対するわりあい）")]
        [SerializeField, Range(0.3f, 1.2f)] float figureHeight = 0.95f;
        [Tooltip("「ご加護」の文字の大きさ（画面の高さに対するわりあい）")]
        [SerializeField, Range(0.02f, 0.15f)] float labelSize = 0.07f;
        [SerializeField] int sortingOrder = 60;

        [Header("モデルを映すカメラ")]
        [Tooltip("モデルを置く場所。境内のカメラに映らない遠く")]
        [SerializeField] Vector3 stageOffset = new Vector3(0f, -5000f, 0f);
        [Tooltip("映す RenderTexture の大きさ（縦横比 3:4）")]
        [SerializeField] Vector2Int modelTextureSize = new Vector2Int(576, 768);
        [SerializeField] Vector3 modelCameraPosition = new Vector3(0f, 1.35f, -5.3f);
        [SerializeField] Vector3 modelLookAt = new Vector3(0f, 1.35f, 0f);
        [SerializeField, Range(10f, 60f)] float modelCameraFov = 36f;

        [Header("時間（付録B BLESSING.G 演出 0.8秒）")]
        [SerializeField, Min(0.1f)] float totalSeconds = GokagoProgress.CutInSeconds;

        Canvas _canvas;
        Image _edge;
        Graphic _figure;
        Transform _stage;
        Camera _modelCamera;
        RenderTexture _modelTexture;
        GokagoGodFigure _godFigure;
        Text _label;
        AudioSource _audio;
        Sequence _sequence;

        public bool IsPlaying => _sequence != null && _sequence.IsActive() && _sequence.IsPlaying();

        void Awake()
        {
            if (gokago == null) gokago = FindAnyObjectByType<GokagoTime>();
            Build();
        }

        void OnEnable()
        {
            if (gokago != null) gokago.onGokagoStart.AddListener(Play);
        }

        void OnDisable()
        {
            if (gokago != null) gokago.onGokagoStart.RemoveListener(Play);
            Stop();
        }

        void OnDestroy()
        {
            if (_stage != null) Destroy(_stage.gameObject);
            if (_modelTexture != null)
            {
                _modelTexture.Release();
                Destroy(_modelTexture);
            }
        }

        // ご加護の発動で呼ばれる。再生中にまた呼ばれたら最初からやりなおす
        [ContextMenu("Play")]
        public void Play()
        {
            if (_canvas == null) Build();
            Stop();

            _canvas.enabled = true;
            if (_modelCamera != null) _modelCamera.enabled = true;
            if (_godFigure != null) _godFigure.Play(totalSeconds);
            SetAlpha(_edge, 0f);
            SetAlpha(_figure, 0f);
            SetAlpha(_label, 0f);
            _figure.rectTransform.localScale = Vector3.one * 1.06f;
            _label.rectTransform.localScale = Vector3.one * 1.3f;

            PlayBell();

            // 時刻は 0.8秒を 1 としたわりあいで置く（totalSeconds を変えても形が同じになる）
            float s = totalSeconds;
            _sequence = DOTween.Sequence().SetUpdate(true);

            // 立ち上がり: 縁が金色に光り、神の姿が重なる
            _sequence.Insert(0f, _edge.DOFade(1f, 0.15f * s).SetEase(Ease.OutQuad));
            _sequence.Insert(0f, _figure.DOFade(figureMaxAlpha, 0.2f * s).SetEase(Ease.OutQuad));
            _sequence.Insert(0f, _figure.rectTransform.DOScale(1f, s).SetEase(Ease.OutCubic));
            _sequence.Insert(0.05f * s, _label.DOFade(1f, 0.15f * s));
            _sequence.Insert(0.05f * s, _label.rectTransform.DOScale(1f, 0.3f * s).SetEase(Ease.OutBack));

            // 引き: 0.55〜1.0 で消える
            _sequence.Insert(0.55f * s, _edge.DOFade(0f, 0.45f * s).SetEase(Ease.InQuad));
            _sequence.Insert(0.5f * s, _figure.DOFade(0f, 0.5f * s).SetEase(Ease.InQuad));
            _sequence.Insert(0.6f * s, _label.DOFade(0f, 0.4f * s));

            _sequence.OnComplete(Hide);
        }

        void Stop()
        {
            if (_sequence != null && _sequence.IsActive()) _sequence.Kill();
            _sequence = null;
            Hide();
        }

        void Hide()
        {
            if (_canvas != null) _canvas.enabled = false;
            if (_modelCamera != null) _modelCamera.enabled = false;
        }

        void PlayBell()
        {
            if (_audio == null) return;
            if (bellSe == null) bellSe = ProceduralSe.Bell();
            _audio.PlayOneShot(bellSe, bellVolume);
        }

        void Build()
        {
            RectTransform root = HudUi.CreateCanvas(transform, "GokagoCutInCanvas", sortingOrder);
            _canvas = root.GetComponent<Canvas>();
            // 入力を受けない（クリックやポインタを奪わない）
            Destroy(root.GetComponent<GraphicRaycaster>());

            _edge = HudUi.CreateImage(root, "GoldEdge", gold);
            _edge.sprite = CreateEdgeSprite(edgeWidth);

            float h = figureHeight * HudUi.ReferenceHeight;
            if (godModelPrefab != null)
            {
                BuildModelStage();
                float w = h * modelTextureSize.x / modelTextureSize.y;
                RawImage raw = CreateFigureRect(root, w, h).gameObject.AddComponent<RawImage>();
                raw.texture = _modelTexture;
                raw.color = Color.white;
                _figure = raw;
            }
            else
            {
                Sprite figureSprite = godSprite != null ? godSprite : CreateSilhouetteSprite();
                float w = h * figureSprite.rect.width / figureSprite.rect.height;
                Image image = CreateFigureRect(root, w, h).gameObject.AddComponent<Image>();
                image.sprite = figureSprite;
                image.preserveAspect = true;
                image.color = godSprite != null ? Color.white : gold;
                _figure = image;
            }
            _figure.raycastTarget = false;

            RectTransform labelRect = HudUi.CreateRect(root, "GokagoLabel", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -HudUi.ReferenceHeight * 0.06f), new Vector2(800f, HudUi.ReferenceHeight * labelSize * 1.4f));
            _label = HudUi.CreateText(labelRect, "Text", font, HudUi.FontSizeOf(labelSize), TextAnchor.MiddleCenter, gold);
            _label.text = "ご加護";

            _audio = gameObject.GetComponent<AudioSource>();
            if (_audio == null) _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;

            Hide();
        }

        // 神の姿の枠: 画面の左下に立たせる
        static RectTransform CreateFigureRect(RectTransform root, float w, float h)
        {
            return HudUi.CreateRect(root, "GodFigure", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(HudUi.ReferenceHeight * 0.04f, 0f), new Vector2(w, h));
        }

        // 遠くにモデルと専用カメラを置いて、透明な背景の RenderTexture に映す
        void BuildModelStage()
        {
            _stage = new GameObject("GokagoCutInStage").transform;
            _stage.position = stageOffset;

            GameObject model = Instantiate(godModelPrefab, _stage, false);
            model.transform.localPosition = Vector3.zero;
            _godFigure = model.GetComponentInChildren<GokagoGodFigure>();

            _modelTexture = new RenderTexture(modelTextureSize.x, modelTextureSize.y, 24, RenderTextureFormat.ARGB32)
            {
                name = "GokagoGodFigureRT", antiAliasing = 4
            };
            _modelTexture.Create();

            var camGo = new GameObject("GokagoModelCamera");
            camGo.transform.SetParent(_stage, false);
            camGo.transform.localPosition = modelCameraPosition;
            camGo.transform.LookAt(_stage.TransformPoint(modelLookAt));

            _modelCamera = camGo.AddComponent<Camera>();
            _modelCamera.targetTexture = _modelTexture;
            _modelCamera.clearFlags = CameraClearFlags.SolidColor;
            _modelCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _modelCamera.fieldOfView = modelCameraFov;
            _modelCamera.nearClipPlane = 0.1f;
            // 境内まで映らないよう、モデルのまわりだけを映す
            _modelCamera.farClipPlane = 15f;
            _modelCamera.depth = -10f;
            _modelCamera.enabled = false;

            // 後処理は透明（アルファ）を消すので切る
            UniversalAdditionalCameraData data = _modelCamera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
        }

        static void SetAlpha(Graphic g, float a)
        {
            Color c = g.color;
            c.a = a;
            g.color = c;
        }

        // 画面の縁ほど濃く、まん中は透明な板（縦横比 16:9 のまま、縁からの距離は高さ基準でそろえる）
        static Sprite CreateEdgeSprite(float width)
        {
            const int texH = 144;
            const int texW = 256;
            var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false)
            {
                name = "GokagoEdge", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[texW * texH];
            for (int y = 0; y < texH; y++)
            {
                for (int x = 0; x < texW; x++)
                {
                    // 縁までの距離を「画面の高さ」で割る
                    float dx = Mathf.Min(x + 0.5f, texW - x - 0.5f) / texH;
                    float dy = Mathf.Min(y + 0.5f, texH - y - 0.5f) / texH;
                    float t = Mathf.Clamp01(1f - Mathf.Min(dx, dy) / width);
                    byte a = (byte)Mathf.RoundToInt(t * t * 255f);
                    pixels[y * texW + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, texW, texH), new Vector2(0.5f, 0.5f));
        }

        // 仮の神の姿: 頭と、すそが広がった衣のシルエット（縁はぼかす）
        static Sprite CreateSilhouetteSprite()
        {
            const int texW = 128;
            const int texH = 256;
            var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false)
            {
                name = "GokagoSilhouette", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[texW * texH];
            var head = new Vector2(64f, 214f);
            const float headR = 22f;
            for (int y = 0; y < texH; y++)
            {
                for (int x = 0; x < texW; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), head) - headR;

                    // 衣: 肩（y=184 で半幅 30）から足もと（y=0 で半幅 60）へ広がる台形
                    if (y < 186)
                    {
                        float halfWidth = Mathf.Lerp(60f, 30f, y / 184f);
                        float robe = Mathf.Max(Mathf.Abs(x + 0.5f - 64f) - halfWidth, y + 0.5f - 184f);
                        d = Mathf.Min(d, robe);
                    }

                    // 上へ行くほど少し濃く、外側 6px でぼかす
                    float a = Mathf.Clamp01(0.5f - d / 6f) * Mathf.Lerp(0.55f, 1f, y / (float)texH);
                    pixels[y * texW + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, texW, texH), new Vector2(0.5f, 0f));
        }

#if UNITY_EDITOR
        void Reset()
        {
            if (font == null) font = HudUi.FindProjectJapaneseFont();
        }
#endif
    }
}
