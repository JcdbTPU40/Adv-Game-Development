using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Toufuku.GameInput;
using Toufuku.Hud;

namespace Toufuku.Menu
{
    /*
        （仮）メインメニュー

        ・項目: はじめる／あそびかた／設定（キャリブレーション）／おわる
        ・操作
            大幣: 左右に傾けて選ぶ（tiltStepDegrees ごとに1つとなり）、振って決める
            正面ボタン: 1秒長押しで正面を合わせる（どの画面でも。企画書 v8 3章の受理条件つき）。合わせた正面は ControllerCalibration でプレイのシーンへ引きつぐ
            キーボード: ←→（↑↓）で選ぶ、Enter / Space で決める、Esc でもどる
            マウス: 指すと選ぶ、クリックで決める
        ・Canvas はコードで組み立てる（ResultScreen と同じ）。シーンの空の GameObject に付けて、rawSourceSource に Esp32RawSource をドラッグするだけで動く
        ・見た目は仮。デザインが決まったら、同じ操作のまま組み立て（Build〜）だけ差しかえる
    */
    public class MainMenu : MonoBehaviour
    {
        enum Page { Top, HowTo, Settings, QuitConfirm }

        [Header("入力（Esp32RawSource / KeyboardMouseRawSource をドラッグ）。未設定ならシーンから探す")]
        [SerializeField] MonoBehaviour rawSourceSource;
        [Tooltip("設定画面で接続の状態を出し、再接続するのに使う。未設定ならシーンから探す")]
        [SerializeField] ConecteController controller;

        [Header("はじめる")]
        [Tooltip("「はじめる」で開くシーン名（Build Settings に入っていること）。共有のプレイシーン（_Scenes/GameScene。ResultScene の「もう一度」と同じ）")]
        [SerializeField] string gameSceneName = "GameScene";

        [Header("大幣で選ぶ")]
        [Tooltip("この角度（度）左右に傾けるごとに、1つとなりの項目へ動く")]
        [SerializeField, Range(5f, 60f)] float tiltStepDegrees = 20f;
        [Tooltip("ON なら左右を逆にする（センサーの向きが逆のとき）")]
        [SerializeField] bool invertTilt = false;
        [Tooltip("ON なら、マウスの左クリックと同じフレームの振りは数えない（Esp32RawSource は未接続のときクリックを振りとして返すため。クリックはボタンで受ける）")]
        [SerializeField] bool ignoreSwingOnMouseClick = true;

        [Header("正面合わせ（企画書 v8 3章: 正面ボタン1秒長押し・色ボタンをすべて離す・角速度30deg/s未満が0.5秒）")]
        [SerializeField, Min(0.1f)] float frontHoldSeconds = 1.0f;
        [SerializeField, Min(0f)] float stillDegreesPerSecond = 30f;
        [SerializeField, Min(0f)] float stillSeconds = 0.5f;

        [Header("見た目")]
        [Tooltip("NotoSansJP の .ttf を入れる。未設定なら組み込みフォント")]
        [SerializeField] Font font;
        [SerializeField] int sortingOrder = 50;
        [SerializeField] Color backgroundColor = new Color(0.07f, 0.05f, 0.1f, 1f);
        [SerializeField] Color itemColor = new Color(0.22f, 0.16f, 0.26f, 1f);
        [SerializeField] Color selectedColor = new Color(0.85f, 0.2f, 0.25f, 1f);

        [Header("デバッグ")]
        [SerializeField] bool logEvents = true;

        sealed class Item
        {
            public RectTransform Rect;
            public Image Image;
            public Text Label;
            public Action Action;
        }

        sealed class PageView
        {
            public GameObject Root;
            public RectTransform Rect => (RectTransform)Root.transform;
            public readonly List<Item> Items = new List<Item>();
            public int DefaultIndex;
            public Page Back = Page.Top;
        }

        const float SelectedScale = 1.08f;
        const float ToastSeconds = 2.5f;

        readonly Dictionary<Page, PageView> _pages = new Dictionary<Page, PageView>();
        IControllerRawSource _raw;
        Esp32RawSource _esp32;
        RectTransform _canvas;
        Text _settingsInfo;
        Text _calibrationText;
        Text _volumeText;
        Text _toast;
        double _toastUntil;

        Page _page;
        int _selected;
        bool _loading;

        // 大幣で選ぶ: 今の項目にいるときのヨー角（ここから tiltStepDegrees 傾けたらとなりへ）
        float _tiltAnchor;
        bool _hasTiltAnchor;

        // 正面合わせ
        double _frontDownAt = double.NaN;
        bool _frontDone;
        float _prevYaw;
        bool _hasPrevYaw;
        double _stillSince;
        string _calibrationRefusal;

        Vector3 _prevMouse;

        // 確認用
        public string CurrentPage => _page.ToString();
        public int SelectedIndex => _selected;
        public string SelectedLabel => Current.Items.Count > 0 ? Current.Items[_selected].Label.text : null;

        PageView Current => _pages[_page];

#if UNITY_EDITOR
        void Reset()
        {
            font = HudUi.FindProjectJapaneseFont();
        }
#endif

        void Awake()
        {
            _raw = rawSourceSource as IControllerRawSource;
            if (rawSourceSource != null && _raw == null)
                Debug.LogWarning("[MainMenu] rawSourceSource が IControllerRawSource を実装していません", this);
            if (_raw == null) _raw = FindRawSource();
            if (_raw == null)
                Debug.LogWarning("[MainMenu] 大幣の入力がありません（キーボードとマウスだけで動きます）", this);
            _esp32 = _raw as Esp32RawSource;

            if (controller == null) controller = FindAnyObjectByType<ConecteController>();

            EnsureEventSystem();
            Build();
            Show(Page.Top);
            _prevMouse = Input.mousePosition;
        }

        void Update()
        {
            if (_loading) return;

            double now = Time.realtimeSinceStartupAsDouble;
            UpdateFrontCalibration(now);

            int move = 0;
            bool decide = false;
            bool back = false;

            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.UpArrow)) move--;
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.DownArrow)) move++;
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)) decide = true;
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Backspace)) back = true;

            move += ReadTilt();
            decide |= ReadSwing();
            UpdateMouseHover();

            if (move != 0) Select(_selected + move);
            if (back && _page != Page.Top) Show(Current.Back);
            else if (decide) Activate(_selected);

            if (_page == Page.Settings) RefreshSettings(now);
            _toast.enabled = now < _toastUntil;
        }

        // ---------- 入力 ----------

        int ReadTilt()
        {
            if (_raw == null || !_raw.IsConnected) return 0;

            float yaw = _raw.Yaw;
            if (!_hasTiltAnchor)
            {
                _tiltAnchor = yaw;
                _hasTiltAnchor = true;
                return 0;
            }

            float dir = invertTilt ? -1f : 1f;
            float d = Mathf.DeltaAngle(_tiltAnchor, yaw) * dir;
            if (d >= tiltStepDegrees)
            {
                // はしまで行ったら、それ以上傾けても動かない。基準を今の向きに寄せて、もどしたらすぐとなりへ動けるようにする
                if (_selected < Current.Items.Count - 1) { _tiltAnchor += tiltStepDegrees * dir; return 1; }
                _tiltAnchor = yaw;
            }
            else if (d <= -tiltStepDegrees)
            {
                if (_selected > 0) { _tiltAnchor -= tiltStepDegrees * dir; return -1; }
                _tiltAnchor = yaw;
            }
            return 0;
        }

        bool ReadSwing()
        {
            if (_raw == null) return false;

            bool swung = false;
            bool mouseClicked = ignoreSwingOnMouseClick && Input.GetMouseButtonDown(0);
            // 貯まっている振りはぜんぶ取り出す（取り出さないと、次の画面で決定したことになる）
            while (_raw.TryConsumeSwingPeak(out _, out _))
            {
                // 正面合わせの最中の振りは数えない（InputStateMachine の FrontHeld と同じ考え）
                if (_raw.IsFrontHeld || mouseClicked) continue;
                swung = true;
            }
            return swung;
        }

        void UpdateMouseHover()
        {
            Vector3 mouse = Input.mousePosition;
            if (mouse == _prevMouse) return;
            _prevMouse = mouse;

            List<Item> items = Current.Items;
            for (int i = 0; i < items.Count; i++)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(items[i].Rect, mouse, null))
                {
                    Select(i);
                    return;
                }
            }
        }

        // 正面ボタン長押しで正面を合わせる。条件を満たさないまま長押しが終わったら、理由を出す
        void UpdateFrontCalibration(double now)
        {
            if (_raw == null) return;

            float yaw = _raw.Yaw;
            float dt = Time.unscaledDeltaTime;
            if (!_hasPrevYaw || dt <= 0f || Mathf.Abs(Mathf.DeltaAngle(_prevYaw, yaw)) / dt >= stillDegreesPerSecond)
                _stillSince = now;
            _prevYaw = yaw;
            _hasPrevYaw = true;

            if (!_raw.IsFrontHeld)
            {
                _frontDownAt = double.NaN;
                _frontDone = false;
                return;
            }

            if (double.IsNaN(_frontDownAt)) _frontDownAt = now;
            if (_frontDone || now - _frontDownAt < frontHoldSeconds) return;

            _frontDone = true;
            if (TryCalibrate(now, out string refusal))
                return;
            _calibrationRefusal = refusal;
            ShowToast($"正面を合わせられませんでした（{refusal}）", now);
            Log($"正面合わせ 不成立: {refusal}");
        }

        bool TryCalibrate(double now, out string refusal)
        {
            refusal = null;
            if (_raw == null || !_raw.IsConnected) refusal = "大幣がつながっていません";
            else if (AnyColorHeld()) refusal = "色ボタンをはなしてください";
            else if (now - _stillSince < stillSeconds) refusal = "大幣を止めてください";
            if (refusal != null) return false;

            ControllerCalibration.SetYawOffset(_raw.Yaw, now);
            _calibrationRefusal = null;
            _tiltAnchor = _raw.Yaw;
            ShowToast("正面を合わせました", now);
            Log($"正面合わせ 基準ヨー={_raw.Yaw:0.0}");
            return true;
        }

        bool AnyColorHeld()
        {
            for (int i = 0; i < InputStateMachine.ColorCount; i++)
                if (_raw.IsColorHeld(i)) return true;
            return false;
        }

        // ---------- 画面と項目 ----------

        void Show(Page page)
        {
            foreach (KeyValuePair<Page, PageView> pair in _pages)
                pair.Value.Root.SetActive(pair.Key == page);
            _page = page;
            Select(Current.DefaultIndex);
            // 画面が変わったら、今の向きを新しい基準にする（前の画面で傾けていた分で動かないように）
            _hasTiltAnchor = false;
            Log($"画面: {page}");
        }

        void Select(int index)
        {
            List<Item> items = Current.Items;
            if (items.Count == 0) return;
            _selected = Mathf.Clamp(index, 0, items.Count - 1);
            for (int i = 0; i < items.Count; i++)
            {
                bool on = i == _selected;
                items[i].Image.color = on ? selectedColor : itemColor;
                items[i].Rect.localScale = Vector3.one * (on ? SelectedScale : 1f);
            }
        }

        void Activate(int index)
        {
            List<Item> items = Current.Items;
            if (index < 0 || index >= items.Count) return;
            Select(index);
            Log($"決定: {items[index].Label.text}");
            items[index].Action?.Invoke();
        }

        void StartGame()
        {
            if (string.IsNullOrEmpty(gameSceneName) || !Application.CanStreamedLevelBeLoaded(gameSceneName))
            {
                Debug.LogWarning($"[MainMenu] 「はじめる」のシーン「{gameSceneName}」が Build Settings にありません", this);
                ShowToast($"シーン「{gameSceneName}」が Build Settings にありません", Time.realtimeSinceStartupAsDouble);
                return;
            }
            _loading = true;
            Time.timeScale = 1f;
            SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        }

        void Quit()
        {
            Log("終了");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void CalibrateFromMenu()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (TryCalibrate(now, out string refusal)) return;
            _calibrationRefusal = refusal;
            ShowToast($"正面を合わせられませんでした（{refusal}）", now);
        }

        void Reconnect()
        {
            if (controller == null)
            {
                ShowToast("ConecteController がシーンにありません", Time.realtimeSinceStartupAsDouble);
                return;
            }
            controller.BeginReconnect();
            ShowToast("COM ポートを探しなおしています…", Time.realtimeSinceStartupAsDouble);
        }

        void ShowToast(string message, double now)
        {
            _toast.text = message;
            _toastUntil = now + ToastSeconds;
        }

        void RefreshSettings(double now)
        {
            string link;
            if (controller == null) link = "ConecteController なし";
            else if (controller.isConnected) link = $"○ {controller.portName}";
            else link = controller.isScanning ? "× さがしています…" : "× つながっていません";

            string buttons = "-";
            if (_raw != null)
            {
                buttons = "";
                for (int i = 0; i < InputStateMachine.ColorCount; i++)
                    buttons += _raw.IsColorHeld(i) ? (i + 1).ToString() : "・";
                buttons += _raw.IsFrontHeld ? "  正面" : "";
            }

            string yaw = _raw != null ? $"{_raw.Yaw:0.0}°" : "-";
            string relative = _raw != null && ControllerCalibration.HasYawOffset
                ? $"（正面から {Mathf.DeltaAngle(ControllerCalibration.YawOffset, _raw.Yaw):+0.0;-0.0;0.0}°）"
                : "（まだ合わせていません）";
            string roll = _raw != null ? $"{_raw.Roll:0.0}°" : "-";
            string hardware = _esp32 != null ? (_esp32.HasHardwareButtons ? "ボタン箱から" : "キーボードで代用（1〜5・A）") : "キーボード";

            _settingsInfo.text =
                $"接続　　{link}\n" +
                $"ヨー　　{yaw}{relative}\n" +
                $"ロール　{roll}\n" +
                $"ボタン　{buttons}　（{hardware}）";

            string calibration;
            if (_raw != null && _raw.IsFrontHeld && !_frontDone)
                calibration = $"正面ボタン長押し中… {Mathf.Clamp01((float)((now - _frontDownAt) / frontHoldSeconds)) * 100f:0}%";
            else if (_calibrationRefusal != null)
                calibration = $"正面合わせ: できませんでした（{_calibrationRefusal}）";
            else if (ControllerCalibration.HasYawOffset)
                calibration = $"正面合わせ: 済み（{now - ControllerCalibration.CalibratedAt:0}秒前）";
            else
                calibration = "正面合わせ: まだ（画面の中央を向けて、正面ボタンを1秒長押し）";
            _calibrationText.text = calibration;

            _volumeText.text = $"音量　{Mathf.RoundToInt(MasterVolume.Value * 100f)}%";
        }

        // ---------- 組み立て（1920×1080 基準のピクセル） ----------

        void Build()
        {
            _canvas = HudUi.CreateCanvas(transform, "MainMenuCanvas", sortingOrder);
            HudUi.CreateImage(_canvas, "Background", backgroundColor);

            Vector2 bottom = new Vector2(0.5f, 0f);
            BuildTop();
            BuildHowTo();
            BuildSettings();
            BuildQuitConfirm();

            Label(_canvas, "Hint", bottom, new Vector2(0f, 110f), new Vector2(1800f, 50f), 36, new Color(1f, 1f, 1f, 0.85f))
                .text = "大幣を左右に傾けて選ぶ　／　振って決める　／　正面ボタン1秒長押しで正面合わせ";
            Label(_canvas, "KeyHint", bottom, new Vector2(0f, 55f), new Vector2(1800f, 40f), 26, new Color(1f, 1f, 1f, 0.5f))
                .text = "キーボード: ←→ で選ぶ・Enter / Space で決める・Esc でもどる　　マウス: クリックで決める";
            _toast = Label(_canvas, "Toast", bottom, new Vector2(0f, 190f), new Vector2(1800f, 60f), 40, new Color(1f, 0.9f, 0.45f));
            _toast.enabled = false;
        }

        void BuildTop()
        {
            PageView page = NewPage(Page.Top);
            Vector2 top = new Vector2(0.5f, 1f);
            Label(page.Rect, "Title", top, new Vector2(0f, -110f), new Vector2(1600f, 240f), 200, Color.white).text = "投福神社";
            Label(page.Rect, "Subtitle", top, new Vector2(0f, -370f), new Vector2(1600f, 60f), 40, new Color(1f, 1f, 1f, 0.6f)).text = "（仮）メインメニュー";
            Row(page, -40f, new Vector2(380f, 200f), 64,
                ("はじめる", StartGame),
                ("あそびかた", () => Show(Page.HowTo)),
                ("設定", () => Show(Page.Settings)),
                ("おわる", () => Show(Page.QuitConfirm)));
        }

        void BuildHowTo()
        {
            PageView page = NewPage(Page.HowTo);
            Heading(page, "あそびかた");
            RectTransform panel = Panel(page, new Vector2(0f, 40f), new Vector2(1500f, 420f));
            Text text = HudUi.CreateText(Inset(panel, 60f, 40f), "Body", font, 46, TextAnchor.MiddleLeft, Color.white);
            text.lineSpacing = 1.25f;
            text.text =
                "１．参拝客の願いの色を見る\n" +
                "２．同じ色のボタンを押して、お守りを選ぶ\n" +
                "３．大幣を参拝客に向けて、振ってお守りを届ける\n" +
                "４．願いをかなえて縁をむすぼう（3分間）";
            Row(page, -300f, new Vector2(380f, 140f), 56, ("もどる", () => Show(Page.Top)));
        }

        void BuildSettings()
        {
            PageView page = NewPage(Page.Settings);
            Heading(page, "設定");
            RectTransform panel = Panel(page, new Vector2(0f, 60f), new Vector2(1500f, 420f));
            RectTransform inset = Inset(panel, 60f, 30f);

            RectTransform info = HudUi.CreateRect(inset, "Info", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(1380f, 250f));
            _settingsInfo = HudUi.CreateText(info, "Text", font, 40, TextAnchor.UpperLeft, Color.white);
            _settingsInfo.lineSpacing = 1.15f;

            RectTransform calibration = HudUi.CreateRect(inset, "Calibration", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(1380f, 50f));
            _calibrationText = HudUi.CreateText(calibration, "Text", font, 38, TextAnchor.MiddleLeft, new Color(1f, 0.9f, 0.45f));

            RectTransform volume = HudUi.CreateRect(inset, "Volume", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(1380f, 50f));
            _volumeText = HudUi.CreateText(volume, "Text", font, 38, TextAnchor.MiddleLeft, Color.white);

            Row(page, -300f, new Vector2(320f, 140f), 42,
                ("正面を合わせる", CalibrateFromMenu),
                ("音量 −", () => MasterVolume.Add(-MasterVolume.Step)),
                ("音量 ＋", () => MasterVolume.Add(MasterVolume.Step)),
                ("再接続", Reconnect),
                ("もどる", () => Show(Page.Top)));
            page.DefaultIndex = 4;
        }

        void BuildQuitConfirm()
        {
            PageView page = NewPage(Page.QuitConfirm);
            Heading(page, "おわる");
            RectTransform panel = Panel(page, new Vector2(0f, 60f), new Vector2(1100f, 260f));
            HudUi.CreateText(panel, "Body", font, 64, TextAnchor.MiddleCenter, Color.white).text = "ゲームを終了しますか？";
            Row(page, -220f, new Vector2(380f, 160f), 60,
                ("おわる", Quit),
                ("もどる", () => Show(Page.Top)));
            // まちがって振っても終わらないように、はじめは「もどる」を選んでおく
            page.DefaultIndex = 1;
        }

        PageView NewPage(Page page)
        {
            var view = new PageView();
            view.Root = HudUi.Stretch(HudUi.CreateRect(_canvas, $"Page_{page}", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero)).gameObject;
            _pages[page] = view;
            return view;
        }

        // トップ以外の画面の見出し
        void Heading(PageView page, string text)
        {
            Label(page.Rect, "Heading", new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(1600f, 120f), 88, Color.white).text = text;
        }

        // 項目を横1列に、画面の中央から y だけずらして並べる
        void Row(PageView page, float y, Vector2 size, int fontSize, params (string label, Action action)[] entries)
        {
            const float spacing = 40f;
            float width = entries.Length * size.x + (entries.Length - 1) * spacing;
            var center = new Vector2(0.5f, 0.5f);
            RectTransform parent = page.Rect;

            for (int i = 0; i < entries.Length; i++)
            {
                float x = -width * 0.5f + size.x * 0.5f + i * (size.x + spacing);
                RectTransform rt = HudUi.CreateRect(parent, $"Item_{entries[i].label}", center, center, new Vector2(x, y), size);
                Image image = rt.gameObject.AddComponent<Image>();
                image.color = itemColor;

                Button button = rt.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                button.transition = Selectable.Transition.None;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                int index = page.Items.Count;
                button.onClick.AddListener(() => Activate(index));

                Text label = HudUi.CreateText(rt, "Label", font, fontSize, TextAnchor.MiddleCenter, Color.white);
                label.text = entries[i].label;

                page.Items.Add(new Item { Rect = rt, Image = image, Label = label, Action = entries[i].action });
            }
        }

        RectTransform Panel(PageView page, Vector2 position, Vector2 size)
        {
            var center = new Vector2(0.5f, 0.5f);
            RectTransform rt = HudUi.CreateRect(page.Rect, "Panel", center, center, position, size);
            HudUi.CreateImage(rt, "Backplate", new Color(0.12f, 0.09f, 0.15f, 0.92f));
            return rt;
        }

        static RectTransform Inset(RectTransform parent, float x, float y)
        {
            RectTransform rt = HudUi.Stretch(HudUi.CreateRect(parent, "Inset", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero));
            rt.offsetMin = new Vector2(x, y);
            rt.offsetMax = new Vector2(-x, -y);
            return rt;
        }

        Text Label(RectTransform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, int fontSize, Color color)
        {
            RectTransform rt = HudUi.CreateRect(parent, name, anchor, new Vector2(0.5f, anchor.y), position, size);
            return HudUi.CreateText(rt, "Text", font, fontSize, TextAnchor.MiddleCenter, color);
        }

        static IControllerRawSource FindRawSource()
        {
            foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (behaviour is IControllerRawSource raw) return raw;
            }
            return null;
        }

        static void EnsureEventSystem()
        {
            EventSystem system = EventSystem.current != null ? EventSystem.current : FindAnyObjectByType<EventSystem>();
            if (system == null)
                system = new GameObject("EventSystem (MainMenu)", typeof(EventSystem), typeof(InputSystemUIInputModule)).GetComponent<EventSystem>();
            // ←→ と Enter はこのクラスで受けるので、uGUI の移動・決定は止める（2回決定しないように）。クリックは uGUI で受ける
            system.sendNavigationEvents = false;
        }

        void Log(string message)
        {
            if (logEvents) Debug.Log($"[MainMenu] {message}", this);
        }
    }
}
