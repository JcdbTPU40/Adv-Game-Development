using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Toufuku.Aim
{
    /*
        照準の見た目を担当するクラス（#60 / 企画書 v8 4章）

        ・照準マーク: 紙垂（しで）っぽい輪を、弾が落ちる予定の場所の画面上にずっと出しておく
        ・着弾予測点: 地面にうすく光る輪を置く。輪の大きさは客の当たり判定と同じにして、中心40%の目安もうすく描く
        ・クールダウン中に振ったとき: FlashRejected で 80ms だけ両方を灰色にする
        ちゃんとした素材が来るまでは、Canvas もテクスチャもマテリアルも実行中に作っている
    */
    public class AimReticleView : MonoBehaviour
    {
        [Tooltip("未設定なら同じ GameObject → シーン内の順に探す")]
        [SerializeField] OnusaAimController aim;

        [Header("照準マーク（紙垂モチーフの輪）")]
        [Tooltip("画面の高さ 1080px のときの大きさ（px）")]
        [SerializeField, Min(8f)] float reticleSize = 96f;
        [SerializeField] Color reticleColor = new Color(1f, 0.96f, 0.86f, 0.95f);
        [SerializeField] int canvasSortingOrder = 50;

        [Header("着弾予測点（地面の薄い光の輪）")]
        [Tooltip("参拝客の判定半径（HitZoneTarget の既定 0.9）に合わせると、輪に入れば当たる目安になる")]
        [SerializeField, Min(0.05f)] float groundRingRadius = 0.9f;
        [SerializeField, Min(0.005f)] float groundRingWidth = 0.06f;
        [SerializeField] Color groundRingColor = new Color(1f, 0.92f, 0.6f, 0.45f);
        [Tooltip("中心 40%（+50）の目安を薄く描く")]
        [SerializeField] bool showCenterRing = true;
        [Tooltip("地面とのちらつきを避けるための浮かせ量")]
        [SerializeField, Min(0f)] float groundLift = 0.03f;
        [Tooltip("未設定なら Sprites/Default から生成する")]
        [SerializeField] Material ringMaterial;

        [Header("クールダウン中の返し（#60: 80ms 灰色）")]
        [SerializeField] Color rejectColor = new Color(0.55f, 0.55f, 0.55f, 0.9f);
        [SerializeField, Min(0f)] float rejectFlashSeconds = 0.08f;

        const int RingSegments = 64;
        const int TextureSize = 128;

        Canvas _canvas;
        RectTransform _reticleRect;
        RawImage _reticleImage;
        Texture2D _reticleTexture;
        Transform _ringRoot;
        LineRenderer _ring;
        LineRenderer _centerRing;
        Material _ownedMaterial;
        double _rejectUntil = double.NegativeInfinity;
        bool _visible = true;

        // 今、灰色になっている最中かどうか
        public bool IsRejectFlashing => Time.realtimeSinceStartupAsDouble < _rejectUntil;
        // 照準マークの今の色（確認用）
        public Color CurrentReticleColor => _reticleImage != null ? _reticleImage.color : reticleColor;
        // 灰色にした回数（確認用）
        public int RejectFlashCount { get; private set; }

        // クールダウン中に振られたときに呼ぶ。照準と地面の輪をちょっとだけ灰色にする
        public void FlashRejected()
        {
            _rejectUntil = Time.realtimeSinceStartupAsDouble + rejectFlashSeconds;
            RejectFlashCount++;
            ApplyColors();
        }

        void Awake()
        {
            if (aim == null) aim = GetComponent<OnusaAimController>();
            if (aim == null) aim = FindAnyObjectByType<OnusaAimController>();

            BuildReticle();
            BuildGroundRings();
            SetVisible(false);
        }

        void OnEnable()
        {
            if (_canvas != null && aim != null && aim.HasAim) SetVisible(true);
        }

        void OnDisable()
        {
            SetVisible(false);
        }

        void OnDestroy()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
            if (_ringRoot != null) Destroy(_ringRoot.gameObject);
            if (_reticleTexture != null) Destroy(_reticleTexture);
            if (_ownedMaterial != null) Destroy(_ownedMaterial);
        }

        void LateUpdate()
        {
            if (aim == null || !aim.isActiveAndEnabled || !aim.HasAim)
            {
                SetVisible(false);
                return;
            }
            SetVisible(true);

            Vector3 screen = aim.ScreenPosition;
            float scale = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            _reticleRect.gameObject.SetActive(screen.z > 0f);
            _reticleRect.anchoredPosition = new Vector2(screen.x, screen.y) / scale;
            float size = reticleSize * Screen.height / 1080f / scale;
            _reticleRect.sizeDelta = new Vector2(size, size);

            Vector3 p = aim.TargetPoint;
            p.y = aim.GroundY + groundLift;
            _ringRoot.SetPositionAndRotation(p, Quaternion.identity);

            ApplyColors();
        }

        void ApplyColors()
        {
            bool gray = IsRejectFlashing;
            if (_reticleImage != null) _reticleImage.color = gray ? rejectColor : reticleColor;

            Color ring = gray ? new Color(rejectColor.r, rejectColor.g, rejectColor.b, groundRingColor.a) : groundRingColor;
            SetLineColor(_ring, ring);
            SetLineColor(_centerRing, new Color(ring.r, ring.g, ring.b, ring.a * 0.5f));
        }

        static void SetLineColor(LineRenderer line, Color color)
        {
            if (line == null) return;
            line.startColor = color;
            line.endColor = color;
        }

        void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;
            if (_canvas != null) _canvas.gameObject.SetActive(visible);
            if (_ringRoot != null) _ringRoot.gameObject.SetActive(visible);
        }

        // ---- ここから作る処理 ----

        void BuildReticle()
        {
            var canvasGo = new GameObject("AimReticleCanvas", typeof(RectTransform), typeof(Canvas));
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = canvasSortingOrder;

            var imageGo = new GameObject("ShideReticle", typeof(RectTransform), typeof(RawImage));
            imageGo.transform.SetParent(canvasGo.transform, false);
            _reticleRect = (RectTransform)imageGo.transform;
            _reticleRect.anchorMin = Vector2.zero;
            _reticleRect.anchorMax = Vector2.zero;
            _reticleRect.pivot = new Vector2(0.5f, 0.5f);

            _reticleTexture = CreateShideRingTexture(TextureSize);
            _reticleImage = imageGo.GetComponent<RawImage>();
            _reticleImage.texture = _reticleTexture;
            _reticleImage.raycastTarget = false; // クリックをじゃましないようにする
            _reticleImage.color = reticleColor;
        }

        void BuildGroundRings()
        {
            _ringRoot = new GameObject("AimGroundRing").transform;

            Material mat = ringMaterial;
            if (mat == null)
            {
                _ownedMaterial = AimRenderUtil.CreateTransparentMaterial("AimGroundRing");
                mat = _ownedMaterial;
            }

            _ring = CreateRing("Ring", groundRingRadius, groundRingWidth, mat);
            if (showCenterRing)
                _centerRing = CreateRing("CenterRing", groundRingRadius * HitAccuracy.CenterRatio, groundRingWidth * 0.6f, mat);
        }

        LineRenderer CreateRing(string name, float radius, float width, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_ringRoot, false);
            // 線は XY 平面で作って、X軸まわりに90度たおして地面（XZ平面）に寝かせている
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.alignment = LineAlignment.TransformZ;
            line.positionCount = RingSegments;
            line.widthMultiplier = width;
            line.sharedMaterial = mat;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;

            for (int i = 0; i < RingSegments; i++)
            {
                float a = i * Mathf.PI * 2f / RingSegments;
                line.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
            }
            return line;
        }

        /*
            紙垂っぽい照準のテクスチャを作る。真ん中の点と輪と、ななめ4方向のギザギザの紙垂
            白い本体に暗いふちを付けて、明るい地面の上でも見えるようにしている（色は RawImage.color でかける）
        */
        static Texture2D CreateShideRingTexture(int size)
        {
            const float ringRadius = 0.46f;
            const float ringHalfWidth = 0.045f;
            const float dotRadius = 0.05f;
            const float shideHalfWidth = 0.03f;
            const float outline = 0.035f;

            Vector2[] zigzag =
            {
                new Vector2(0.58f, 0f), new Vector2(0.67f, 0.08f), new Vector2(0.76f, -0.08f),
                new Vector2(0.85f, 0.08f), new Vector2(0.94f, 0f)
            };

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "ShideReticle",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            float aa = 2f / size * 1.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);
                    float r = p.magnitude;

                    float dRing = Mathf.Abs(r - ringRadius);
                    float dShide = float.MaxValue;
                    for (int k = 0; k < 4; k++)
                    {
                        float angle = (45f + 90f * k) * Mathf.Deg2Rad;
                        float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
                        var local = new Vector2(p.x * c + p.y * s, -p.x * s + p.y * c);
                        for (int i = 0; i < zigzag.Length - 1; i++)
                            dShide = Mathf.Min(dShide, DistanceToSegment(local, zigzag[i], zigzag[i + 1]));
                    }

                    float core = Mathf.Max(Coverage(dRing, ringHalfWidth, aa),
                        Mathf.Max(Coverage(r, dotRadius, aa), Coverage(dShide, shideHalfWidth, aa)));
                    float edge = Mathf.Max(Coverage(dRing, ringHalfWidth + outline, aa),
                        Mathf.Max(Coverage(r, dotRadius + outline, aa), Coverage(dShide, shideHalfWidth + outline, aa)));

                    float alpha = Mathf.Max(core, edge * 0.55f);
                    float bright = alpha > 0f ? Mathf.Clamp01(core / alpha) : 0f;
                    byte v = (byte)Mathf.RoundToInt(Mathf.Lerp(0.15f, 1f, bright) * 255f);
                    pixels[y * size + x] = new Color32(v, v, v, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        static float Coverage(float distance, float halfWidth, float aa)
        {
            return Mathf.Clamp01((halfWidth - distance) / aa + 0.5f);
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
