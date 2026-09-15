using UnityEngine;
using UnityEngine.UI;

namespace Toufuku.Hud
{
    /*
        時計を止めているときの表示（#65）

        ・通信の復帰中（ControllerLinkSupervisor がとぎれを見つけた）: 「コントローラーの通信が切れました」とアテンド向けの手順
        ・アテンドの一時停止（GameSession のポーズキー）: 「一時停止中」
        ・ボタン箱だけの 100ms のとぎれ（全ボタン解放）は時計を止めないので、ここでは出さない（ThrowInputController のデバッグ表示に出る）
        Canvas はコードで組み立てるので、シーンの空の GameObject に付けるだけで動く。リザルトより手前（sortingOrder 80）に出す
    */
    public class SessionHoldOverlay : MonoBehaviour
    {
        [Header("フォント（NotoSansJP の .ttf を入れる。未設定なら組み込みフォント）")]
        [SerializeField] Font font;
        [SerializeField] int sortingOrder = 80;

        [Header("色")]
        [SerializeField] Color dimColor = new Color(0f, 0f, 0f, 0.6f);
        [SerializeField] Color titleColor = new Color(1f, 0.85f, 0.35f);
        [SerializeField] Color bodyColor = Color.white;

        [Header("文字の大きさ（画面の高さのわりあい）")]
        [SerializeField] float titleFontRatio = 0.08f;
        [SerializeField] float bodyFontRatio = 0.042f;

        RectTransform _canvas;
        Text _title;
        Text _body;

        public bool IsShowing { get; private set; }
        public string TitleText => _title != null ? _title.text : null;
        public string BodyText => _body != null ? _body.text : null;

#if UNITY_EDITOR
        void Reset()
        {
            font = HudUi.FindProjectJapaneseFont();
        }
#endif

        void Awake()
        {
            Build();
            SetShowing(false);
        }

        void Update()
        {
            GameSession session = GameSession.Instance;
            ControllerLinkSupervisor link = ControllerLinkSupervisor.Instance;
            bool linkLost = link != null && link.IsLost;
            bool paused = session != null && (session.HoldReasons & SessionHoldReason.Paused) != 0;

            if (linkLost)
            {
                double down = Time.realtimeSinceStartupAsDouble - link.LostSince;
                bool clockStopped = session != null && session.IsHeld;
                _title.text = "コントローラーの通信が切れました";
                _body.text =
                    "USB ケーブルを挿しなおしてください（自動でさがしています" + (link.IsScanning ? "…" : "") + "）\n" +
                    $"とぎれてから {down:0} 秒" + (clockStopped ? "　ゲームの時間は止まっています" : "");
            }
            else if (paused)
            {
                _title.text = "一時停止中";
                _body.text = session.PauseKey != KeyCode.None ? $"{session.PauseKey} で再開" : "";
            }

            SetShowing(linkLost || paused);
        }

        void SetShowing(bool show)
        {
            if (IsShowing == show && _canvas.gameObject.activeSelf == show) return;
            IsShowing = show;
            _canvas.gameObject.SetActive(show);
        }

        void Build()
        {
            _canvas = HudUi.CreateCanvas(transform, "SessionHoldOverlayCanvas", sortingOrder);
            HudUi.CreateImage(_canvas, "Dim", dimColor);

            RectTransform titleRect = HudUi.CreateRect(_canvas, "Title", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f),
                new Vector2(0f, 20f), new Vector2(HudUi.ReferenceWidth, HudUi.FontSizeOf(titleFontRatio) * 1.4f));
            _title = HudUi.CreateText(titleRect, "Text", font, HudUi.FontSizeOf(titleFontRatio), TextAnchor.LowerCenter, titleColor);

            RectTransform bodyRect = HudUi.CreateRect(_canvas, "Body", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f),
                new Vector2(0f, -20f), new Vector2(HudUi.ReferenceWidth, HudUi.FontSizeOf(bodyFontRatio) * 3f));
            _body = HudUi.CreateText(bodyRect, "Text", font, HudUi.FontSizeOf(bodyFontRatio), TextAnchor.UpperCenter, bodyColor);
        }
    }
}
