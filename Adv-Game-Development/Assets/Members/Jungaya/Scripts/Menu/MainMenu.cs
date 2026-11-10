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
        メインメニュー（タイトルの演出 → メニュー → 絵馬（セーブ）を選ぶ → 参道マップでステージを選ぶ）

        ・流れ
            前奏（境内へ寄っていく）
              → 大幣がつながっていれば「大幣を振ってみよう」→ 振るとお守りが飛び出す → 光ってロゴへ
              → つながっていなければ、そのままロゴへ
            ロゴ「投福神社」→ 決めるとメニュー（プレイ／設定／終了）
            プレイ → 絵馬を選ぶ（セーブ枠。白紙なら新しくはじめる）→ 参道マップ（はじめは 1-1 だけ解放）→ ステージのシーンへ
        ・2回目からは前奏を飛ばしてロゴから（リザルトからもどってきたとき。replayIntroEveryTime で毎回にもできる）
        ・操作
            大幣: 左右に傾けて選ぶ（tiltStepDegrees ごとに1つとなり）、振って決める
            正面ボタン: 1秒長押しで正面を合わせる（どの画面でも。企画書 v8 3章の受理条件つき）。合わせた正面は ControllerCalibration でプレイのシーンへ引きつぐ
            キーボード: ←→（↑↓）で選ぶ、Enter / Space で決める、Esc でもどる
            マウス: 指すと選ぶ、クリックで決める（演出の画面ではクリックで先へ）
        ・絵はすべてコードで組み立てる（背景は ShrineBackdrop、形は MenuArt）。シーンの空の GameObject に付けて、
          rawSourceSource に Esp32RawSource をドラッグするだけで動く。画像の素材ができたら Build〜 の Sprite を差しかえる
    */
    public class MainMenu : MonoBehaviour
    {
        enum Page { Intro, SwingPrompt, Omamori, Logo, Top, SaveSelect, StageMap, Settings, QuitConfirm }

        [Header("入力（Esp32RawSource / KeyboardMouseRawSource をドラッグ）。未設定ならシーンから探す")]
        [SerializeField] MonoBehaviour rawSourceSource;
        [Tooltip("設定画面で接続の状態を出し、再接続するのに使う。未設定ならシーンから探す")]
        [SerializeField] ConecteController controller;

        [Header("ステージ（参道マップに左から順にならぶ。前をクリアすると次が解放）")]
        [SerializeField] List<StageInfo> stages = DefaultStages();
        [Tooltip("マップの見出し")]
        [SerializeField] string mapTitle = "一の鳥居";
        [SerializeField] string mapSubtitle = "参道マップ　第一章";

        [Header("演出")]
        [Tooltip("前奏の長さ（秒）。決めるボタン・振りで飛ばせる")]
        [SerializeField, Min(0.5f)] float introSeconds = 4.5f;
        [Tooltip("お守りが出てから、光ってロゴに変わるまで（秒）")]
        [SerializeField, Min(0.5f)] float omamoriSeconds = 1.9f;
        [Tooltip("ON なら、シーンを開くたびに前奏から。OFF なら2回目からはロゴから")]
        [SerializeField] bool replayIntroEveryTime = false;
        [Tooltip("前奏の音（なくてもよい）")]
        [SerializeField] AudioClip introClip;
        [Tooltip("ロゴ以降に流す BGM（ループ。なくてもよい）")]
        [SerializeField] AudioClip menuBgm;
        [Tooltip("お守りが出たときの音（なくてもよい）")]
        [SerializeField] AudioClip omamoriClip;
        [Tooltip("項目を決めたときの音（なくてもよい）")]
        [SerializeField] AudioClip decideClip;

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

        [Header("素材（デザイン担当。Textures/Menu。未設定ならコードで描いた仮の絵）")]
        [Tooltip("ロゴ（鳥居・紋・「投福神社」）")]
        [SerializeField] Sprite logoSprite;
        [Tooltip("紋。形だけ白で抜いたもの（Menu_CrestMask）。色はコードでつける")]
        [SerializeField] Sprite crestSprite;
        [Tooltip("お守り5種。OmamoriType の順（縁結び・金運・厄除け・健康・学業）＝ボタン箱の左から右")]
        [SerializeField] Sprite[] omamoriSprites = new Sprite[5];

        [Header("見た目")]
        [Tooltip("本文の日本語フォント（NotoSansJP の .ttf）。未設定なら組み込みフォント")]
        [SerializeField] Font font;
        [Tooltip("ロゴ・見出しのフォント（明朝などを入れる）。未設定なら font と同じ")]
        [SerializeField] Font titleFont;
        [SerializeField] int sortingOrder = 50;

        [Header("デバッグ")]
        [SerializeField] bool logEvents = true;

        // ---------- 色 ----------
        static readonly Color Cream = MenuArt.Hex("#fff3d6");
        static readonly Color Gold = MenuArt.Hex("#f0c25a");
        static readonly Color Vermilion = MenuArt.Hex("#b8302a");
        static readonly Color Ink = MenuArt.Hex("#3a1c10");
        static readonly Color Wood = MenuArt.Hex("#7a4a22");
        static readonly Color Paper = MenuArt.Hex("#e9d7ad");
        // 板の地（下の金のふちが透けて茶色くならないよう、不透明にする）
        static readonly Color Night = MenuArt.Hex("#1c1228");

        sealed class Item
        {
            public string Name;
            public RectTransform Rect;
            public Action Action;
            public Action<bool> SetSelected;
        }

        sealed class PageView
        {
            public GameObject Root;
            public CanvasGroup Group;
            public RectTransform Rect => (RectTransform)Root.transform;
            public readonly List<Item> Items = new List<Item>();
            public int DefaultIndex;
            public Page? Back;
            public bool Cinematic;
            public float Zoom = 1f, OffsetY, Dim;
            public Action<float> Tick;   // 画面に入ってからの秒
            public Action OnShow;
        }

        // 絵馬1枚の、あとから書きかえる所
        sealed class EmaView
        {
            public Image Border, Body, Glow;
            public Text Title, Sub, Status;
            public RectTransform Rect;
        }

        // 参道マップの1マス
        sealed class NodeView
        {
            public StageInfo Stage;
            public RectTransform Rect;
            public Image Glow, Ring, Stamp;
            public Image[] Wood;
            public Text Label;
        }

        const float FadeSeconds = 0.55f;
        const float FlashSeconds = 0.8f;
        const float MinPhaseSeconds = 0.45f;
        const float ToastSeconds = 2.5f;

        static bool s_introSeen;

        readonly Dictionary<Page, PageView> _pages = new Dictionary<Page, PageView>();
        readonly List<EmaView> _emas = new List<EmaView>();
        readonly List<NodeView> _nodes = new List<NodeView>();
        IControllerRawSource _raw;
        Esp32RawSource _esp32;
        RectTransform _canvas;
        ShrineBackdrop _backdrop;
        AudioSource _sfx, _bgm;
        Image _flash;
        Text _hint, _toast, _logoPrompt, _swingBadge, _mapSlot;
        // 設定
        Text _linkValue, _frontValue, _volumeValue, _settingsHelp;
        Image[] _volumeBars;
        Image _statusDot, _frontGaugeFill;
        Text _statusLink, _aimText, _statusSource, _calibrationText;
        RectTransform _aimMarker;
        Image[] _buttonDots;
        bool _volumeAdjusting;
        readonly List<string> _settingsHelps = new List<string>();
        Text _infoTitle, _infoBody, _infoStats, _infoButton;
        Image _infoButtonImage;
        double _toastUntil;

        Page _page;
        int _selected;
        bool _loading;
        float _time;
        float _enteredAt;

        // 光ってから画面を変える
        float _flashStart = -1f;
        Action _flashPeak;

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
        public string SelectedLabel => Current.Items.Count > 0 ? Current.Items[_selected].Name : null;

        PageView Current => _pages[_page];
        bool ControllerReady => _raw != null && _raw.IsConnected;

#if UNITY_EDITOR
        void Reset()
        {
            font = HudUi.FindProjectJapaneseFont();
            FillArtFromProject();
        }

        // 素材が空なら、Textures/Menu から入れる
        [ContextMenu("素材を Textures/Menu から入れる")]
        void FillArtFromProject()
        {
            const string dir = "Assets/Members/Jungaya/Textures/Menu/";
            if (logoSprite == null) logoSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(dir + "Menu_Logo.png");
            if (crestSprite == null) crestSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(dir + "Menu_CrestMask.png");
            string[] names = { "Enmusubi", "Kinun", "Yakuyoke", "Kenkou", "Gakugyou" };
            if (omamoriSprites == null || omamoriSprites.Length != names.Length) omamoriSprites = new Sprite[names.Length];
            for (int i = 0; i < names.Length; i++)
                if (omamoriSprites[i] == null)
                    omamoriSprites[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"{dir}Omamori_{names[i]}.png");
        }
#endif

        bool HasOmamoriArt
        {
            get
            {
                if (omamoriSprites == null || omamoriSprites.Length < 5) return false;
                for (int i = 0; i < 5; i++) if (omamoriSprites[i] == null) return false;
                return true;
            }
        }

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
            if (stages == null || stages.Count == 0) stages = DefaultStages();

            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
            _bgm = gameObject.AddComponent<AudioSource>();
            _bgm.playOnAwake = false;
            _bgm.loop = true;

            EnsureEventSystem();
            Build();
            _prevMouse = Input.mousePosition;

            if (s_introSeen && !replayIntroEveryTime)
            {
                _backdrop.SetCamera(1f, 0f, true);
                _backdrop.SetDim(1f, true);
                Show(Page.Logo, true);
            }
            else
            {
                _backdrop.SetCamera(2.1f, -40f, true);
                _backdrop.SetDim(1f, true);
                Show(Page.Intro, true);
            }
            s_introSeen = true;
        }

        void Update()
        {
            // ConecteController が COM ポートを探す間などでフレームが数秒止まっても、演出が飛ばないように 1 フレームは 0.1 秒まで
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _time += dt;
            double now = Time.realtimeSinceStartupAsDouble;

            _backdrop.Tick(_time, dt);
            UpdateFades(dt);
            UpdateFlash();
            Current.Tick?.Invoke(_time - _enteredAt);
            _toast.enabled = now < _toastUntil;

            if (_loading) return;

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

            if (_flashStart >= 0f) return;

            if (Current.Cinematic)
            {
                decide |= Input.GetMouseButtonDown(0);
                UpdateCinematic(decide);
                return;
            }

            if (_page == Page.Settings) RefreshSettings(now);

            // 音量の調整中は、傾け・←→で音量を変え、決める・もどるで調整を終える
            if (_volumeAdjusting)
            {
                if (move != 0) MasterVolume.Add(move * MasterVolume.Step);
                if (decide || back || Input.GetMouseButtonDown(0)) SetVolumeAdjusting(false);
                return;
            }

            UpdateMouseHover();
            if (move != 0) Select(_selected + move);
            if (back && Current.Back.HasValue) Show(Current.Back.Value);
            else if (decide) Activate(_selected);
        }

        // ---------- 演出の画面 ----------

        void UpdateCinematic(bool decide)
        {
            float elapsed = _time - _enteredAt;
            bool accept = decide && elapsed >= MinPhaseSeconds;

            switch (_page)
            {
                case Page.Intro:
                    if (accept || elapsed >= introSeconds) AfterIntro();
                    break;
                case Page.SwingPrompt:
                    // 振っている間にコントローラーが抜けたら、キーとクリックでも進める案内に変える
                    _swingBadge.text = ControllerReady ? "● 大幣 接続中" : "○ 大幣がつながっていません（Enter・クリックで進めます）";
                    if (accept)
                    {
                        Play(omamoriClip);
                        Show(Page.Omamori);
                    }
                    break;
                case Page.Omamori:
                    if (elapsed >= omamoriSeconds) StartFlash(() => Show(Page.Logo, true));
                    break;
                case Page.Logo:
                    if (accept)
                    {
                        Play(decideClip);
                        Show(Page.Top);
                    }
                    break;
            }
        }

        void AfterIntro()
        {
            if (introClip != null && _sfx.clip == introClip) _sfx.Stop();
            Show(ControllerReady ? Page.SwingPrompt : Page.Logo);
        }

        void StartFlash(Action atPeak)
        {
            _flashStart = _time;
            _flashPeak = atPeak;
        }

        void UpdateFlash()
        {
            if (_flashStart < 0f)
            {
                _flash.enabled = false;
                return;
            }
            float t = (_time - _flashStart) / FlashSeconds;
            if (t >= 0.5f && _flashPeak != null)
            {
                Action a = _flashPeak;
                _flashPeak = null;
                a();
            }
            _flash.enabled = true;
            Color c = _flash.color;
            c.a = t < 0.5f ? t * 2f : Mathf.Clamp01((1f - t) * 2f);
            _flash.color = c;
            if (t >= 1f) _flashStart = -1f;
        }

        void UpdateFades(float dt)
        {
            foreach (KeyValuePair<Page, PageView> pair in _pages)
            {
                bool on = pair.Key == _page;
                CanvasGroup g = pair.Value.Group;
                g.alpha = Mathf.MoveTowards(g.alpha, on ? 1f : 0f, dt / FadeSeconds);
                bool visible = on || g.alpha > 0f;
                if (pair.Value.Root.activeSelf != visible) pair.Value.Root.SetActive(visible);
            }
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

            int count = Current.Items.Count;
            float dir = invertTilt ? -1f : 1f;
            float d = Mathf.DeltaAngle(_tiltAnchor, yaw) * dir;
            if (d >= tiltStepDegrees)
            {
                // はしまで行ったら、それ以上傾けても動かない。基準を今の向きに寄せて、もどしたらすぐとなりへ動けるようにする
                if (_selected < count - 1) { _tiltAnchor += tiltStepDegrees * dir; return 1; }
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

        void Show(Page page, bool instant = false)
        {
            _page = page;
            _enteredAt = _time;
            PageView view = Current;
            view.OnShow?.Invoke();
            _backdrop.SetCamera(view.Zoom, view.OffsetY, instant && page != Page.Intro);
            _backdrop.SetDim(view.Dim, false);
            if (instant)
            {
                foreach (KeyValuePair<Page, PageView> pair in _pages)
                {
                    pair.Value.Group.alpha = pair.Key == page ? 1f : 0f;
                    pair.Value.Root.SetActive(pair.Key == page);
                }
            }
            else
            {
                view.Root.SetActive(true);
            }
            foreach (KeyValuePair<Page, PageView> pair in _pages)
            {
                bool on = pair.Key == page;
                pair.Value.Group.interactable = on;
                pair.Value.Group.blocksRaycasts = on;
            }
            _hint.enabled = !view.Cinematic;

            if (page == Page.Intro && introClip != null) { _sfx.clip = introClip; _sfx.Play(); }
            if (page == Page.Logo && menuBgm != null && !_bgm.isPlaying) { _bgm.clip = menuBgm; _bgm.Play(); }

            Select(view.DefaultIndex);
            // 画面が変わったら、今の向きを新しい基準にする（前の画面で傾けていた分で動かないように）
            _hasTiltAnchor = false;
            _volumeAdjusting = false;
            Log($"画面: {page}");
        }

        void Select(int index)
        {
            List<Item> items = Current.Items;
            if (items.Count == 0) return;
            _selected = Mathf.Clamp(index, 0, items.Count - 1);
            for (int i = 0; i < items.Count; i++) items[i].SetSelected(i == _selected);
        }

        void Activate(int index)
        {
            List<Item> items = Current.Items;
            if (index < 0 || index >= items.Count) return;
            Select(index);
            Log($"決定: {items[index].Name}");
            Play(decideClip);
            items[index].Action?.Invoke();
        }

        void ChooseSlot(int slot)
        {
            SaveData data = SaveSlots.Select(slot);
            Log($"絵馬 {slot + 1} を選んだ（{(data.stages.Count == 0 ? "白紙" : $"{data.ClearedCount} 参拝済み")}）");
            Show(Page.StageMap);
        }

        void ChooseStage(NodeView node)
        {
            if (!IsUnlocked(node.Stage))
            {
                ShowToast("前のお参りをすませると、参拝できます", Time.realtimeSinceStartupAsDouble);
                return;
            }
            StartStage(node.Stage);
        }

        void StartStage(StageInfo stage)
        {
            string scene = stage.sceneName;
            if (string.IsNullOrEmpty(scene) || !Application.CanStreamedLevelBeLoaded(scene))
            {
                Debug.LogWarning($"[MainMenu] ステージ {stage.id} のシーン「{scene}」が Build Settings にありません", this);
                ShowToast($"シーン「{scene}」が Build Settings にありません", Time.realtimeSinceStartupAsDouble);
                return;
            }
            SaveSlots.SelectedStage = stage;
            SaveSlots.MarkPlayed(stage.id);
            Log($"ステージ {stage.id} を始める → {scene}");
            _loading = true;
            Time.timeScale = 1f;
            StartFlash(() => SceneManager.LoadScene(scene, LoadSceneMode.Single));
        }

        bool IsUnlocked(StageInfo stage)
        {
            int i = stages.IndexOf(stage);
            if (i <= 0) return true;
            return SaveSlots.Current != null && SaveSlots.Current.IsCleared(stages[i - 1].id);
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

        void Play(AudioClip clip)
        {
            if (clip != null) _sfx.PlayOneShot(clip);
        }

        void RefreshSettings(double now)
        {
            // 大幣の接続
            bool linked = controller != null ? controller.isConnected : ControllerReady;
            string link;
            if (controller == null) link = ControllerReady ? "つながっています" : "キーボードで操作中";
            else if (controller.isConnected) link = $"つながっています（{controller.portName}）";
            else link = controller.isScanning ? "さがしています…" : "つながっていません";
            _linkValue.text = linked ? "○ つながっている" : (controller != null && controller.isScanning ? "… さがし中" : "× つながっていない");
            _linkValue.color = linked ? MenuArt.Hex("#9ff0c8") : MenuArt.Hex("#ffb4a0");
            _statusLink.text = link;
            _statusDot.color = linked ? MenuArt.Hex("#5fe0a0") : MenuArt.Hex("#e05a4a");

            // 正面合わせ
            _frontValue.text = ControllerCalibration.HasYawOffset ? "済み" : "まだ";
            _frontValue.color = ControllerCalibration.HasYawOffset ? MenuArt.Hex("#9ff0c8") : MenuArt.Hex("#ffe08a");

            // 向き（正面から何度）。左右 60° までをゲージに出す
            if (_raw == null)
            {
                _aimText.text = "大幣がありません";
                _aimMarker.gameObject.SetActive(false);
            }
            else if (!ControllerCalibration.HasYawOffset)
            {
                _aimText.text = "正面を合わせると出ます";
                _aimMarker.gameObject.SetActive(false);
            }
            else
            {
                float rel = Mathf.DeltaAngle(ControllerCalibration.YawOffset, _raw.Yaw);
                _aimMarker.gameObject.SetActive(true);
                _aimMarker.anchoredPosition = new Vector2(Mathf.Clamp(rel / 60f, -1f, 1f) * 220f, _aimMarker.anchoredPosition.y);
                _aimText.text = Mathf.Abs(rel) < 3f ? "まっすぐ正面" : (rel > 0f ? $"右へ {rel:0}°" : $"左へ {-rel:0}°");
            }

            // ボタン（1〜5 と正面）
            for (int i = 0; i < _buttonDots.Length; i++)
            {
                bool held = _raw != null && (i < InputStateMachine.ColorCount ? _raw.IsColorHeld(i) : _raw.IsFrontHeld);
                bool art = i < InputStateMachine.ColorCount && HasOmamoriArt;
                // お守りの絵は、押していないときは暗く、押すと元の色で少し大きく
                _buttonDots[i].color = art
                    ? (held ? Color.white : new Color(0.38f, 0.34f, 0.42f, 1f))
                    : (held ? MenuArt.Hex("#ffd86a") : MenuArt.Hex("#3a3046"));
                _buttonDots[i].rectTransform.localScale = Vector3.one * (art && held ? 1.2f : 1f);
            }
            _statusSource.text = _esp32 != null
                ? (_esp32.HasHardwareButtons ? "ボタンはボタン箱から" : "ボタンはキーボードで代用（1〜5・A）")
                : "キーボード・マウスで操作中";

            // 正面ボタン長押しの進み具合
            float progress = 0f;
            string calibration;
            if (_raw != null && _raw.IsFrontHeld && !_frontDone)
            {
                progress = Mathf.Clamp01((float)((now - _frontDownAt) / frontHoldSeconds));
                calibration = "正面ボタン長押し中…";
            }
            else if (_calibrationRefusal != null)
                calibration = $"合わせられませんでした：{_calibrationRefusal}";
            else if (ControllerCalibration.HasYawOffset)
                calibration = $"正面は {now - ControllerCalibration.CalibratedAt:0} 秒前に合わせました";
            else
                calibration = "正面はまだ合わせていません";
            _calibrationText.text = calibration;
            _frontGaugeFill.rectTransform.sizeDelta = new Vector2(440f * progress, _frontGaugeFill.rectTransform.sizeDelta.y);

            // 音量
            int volume = Mathf.RoundToInt(MasterVolume.Value * 10f);
            for (int i = 0; i < _volumeBars.Length; i++)
                _volumeBars[i].color = i < volume ? (_volumeAdjusting ? Gold : Cream) : MenuArt.Hex("#000000", 0.35f);
            _volumeValue.text = _volumeAdjusting ? $"◀ {volume * 10}% ▶" : $"{volume * 10}%";
            _volumeValue.color = _volumeAdjusting ? Gold : Cream;

            _settingsHelp.text = _volumeAdjusting
                ? "大幣を左右に傾けると（←→キーでも）音量が変わります。\n決めると確定です。"
                : (_selected >= 0 && _selected < _settingsHelps.Count ? _settingsHelps[_selected] : "");
        }

        void SetVolumeAdjusting(bool on)
        {
            _volumeAdjusting = on;
            _hasTiltAnchor = false;
            Log(on ? "音量の調整を始める" : $"音量を {Mathf.RoundToInt(MasterVolume.Value * 100f)}% に決めた");
        }

        // ---------- 組み立て（1920×1080 基準のピクセル。位置は画面の中央から） ----------

        void Build()
        {
            _canvas = HudUi.CreateCanvas(transform, "MainMenuCanvas", sortingOrder);
            _backdrop = new ShrineBackdrop(_canvas);

            BuildIntro();
            BuildSwingPrompt();
            BuildOmamori();
            BuildLogo();
            BuildTop();
            BuildSaveSelect();
            BuildStageMap();
            BuildSettings();
            BuildQuitConfirm();

            _hint = Txt(_canvas, "Hint", new Vector2(560f, -505f), new Vector2(780f, 40f), 24, new Color(1f, 0.96f, 0.86f, 0.8f), align: TextAnchor.MiddleRight);
            _hint.text = "大幣　傾けて選ぶ・振って決める　　キー　←→・Enter・Esc";
            _toast = Txt(_canvas, "Toast", new Vector2(0f, -440f), new Vector2(1800f, 60f), 38, MenuArt.Hex("#ffe08a"));
            _toast.enabled = false;

            _flash = HudUi.CreateImage(_canvas, "Flash", new Color(1f, 0.97f, 0.88f, 0f));
            _flash.enabled = false;
        }

        void BuildIntro()
        {
            PageView page = NewPage(Page.Intro);
            page.Cinematic = true;
            page.Zoom = 1.25f;
            page.OffsetY = -20f;
            page.Dim = 0.05f;

            if (crestSprite == null) return;
            // 前奏のあいだ、境内の上に紋がうかんで消える
            Image halo = Img(page.Rect, "CrestGlow", MenuArt.Glow, new Vector2(0f, 60f), new Vector2(760f, 760f), MenuArt.Hex("#ffd27a", 0f));
            Image crest = Img(page.Rect, "Crest", crestSprite, new Vector2(0f, 60f), new Vector2(380f, 380f), MenuArt.Hex("#fff0cc", 0f));
            page.Tick = e =>
            {
                float d = introSeconds;
                float a = Mathf.Clamp01((e - 0.6f) / 1.0f) * Mathf.Clamp01((d - 0.4f - e) / 1.0f);
                crest.color = MenuArt.Hex("#fff0cc", a * 0.95f);
                halo.color = MenuArt.Hex("#ffd27a", a * 0.45f);
                crest.rectTransform.localScale = Vector3.one * (0.94f + 0.06f * Mathf.Clamp01(e / d));
            };
        }

        void BuildSwingPrompt()
        {
            PageView page = NewPage(Page.SwingPrompt);
            page.Cinematic = true;
            page.Zoom = 1.12f;
            page.Dim = 0.4f;

            var rings = new Image[2];
            for (int i = 0; i < rings.Length; i++)
                rings[i] = Img(page.Rect, "Ring", MenuArt.Ring, new Vector2(0f, 150f), new Vector2(420f, 420f), Gold);
            Img(page.Rect, "Halo", MenuArt.Glow, new Vector2(0f, 150f), new Vector2(680f, 680f), MenuArt.Hex("#ffd27a", 0.35f));

            RectTransform onusa = BuildOnusa(page.Rect, new Vector2(0f, -100f), 1.3f);

            Text title = Txt(page.Rect, "Title", new Vector2(0f, -230f), new Vector2(1400f, 130f), 96, Cream, true);
            TitleStyle(title, 3f);

            Img(page.Rect, "BadgeBack", MenuArt.RoundRect, new Vector2(0f, -335f), new Vector2(860f, 56f), MenuArt.Hex("#1f6a52", 0.7f), true);
            _swingBadge = Txt(page.Rect, "Badge", new Vector2(0f, -335f), new Vector2(860f, 56f), 28, MenuArt.Hex("#c8f2df"), outline: false);
            title.text = "大幣を振ってみよう";

            page.Tick = e =>
            {
                onusa.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(e * 5.6f) * 18f);
                for (int i = 0; i < rings.Length; i++)
                {
                    float t = Mathf.Repeat(e / 1.6f + i * 0.5f, 1f);
                    rings[i].rectTransform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.8f, t);
                    Color c = rings[i].color;
                    c.a = 0.9f * (1f - t);
                    rings[i].color = c;
                }
            };
        }

        // 大幣（柄と紙垂）。pivot は柄の下のほう（そこを中心に振れる）
        RectTransform BuildOnusa(RectTransform parent, Vector2 pos, float scale)
        {
            RectTransform root = Rect(parent, "Onusa", pos, new Vector2(300f, 420f));
            root.pivot = new Vector2(0.5f, 0.08f);
            root.anchoredPosition = pos;
            root.localScale = Vector3.one * scale;

            // 柄
            Img(root, "Stick", MenuArt.RoundRect, new Vector2(0f, -40f), new Vector2(22f, 330f), MenuArt.Hex("#c79a5a"), true);
            Img(root, "StickShade", null, new Vector2(5f, -40f), new Vector2(5f, 300f), MenuArt.Hex("#a77a40"));
            // 柄の先から、細長い紙垂を左右に5本ずつ、ふさのように垂らす（外ほど長く、少しひらく）
            const float top = 128f;
            for (int k = 4; k >= 0; k--)
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    Image s = Img(root, "Shide", MenuArt.Shide, Vector2.zero, new Vector2(30f, 200f + k * 22f),
                        MenuArt.Hex(k % 2 == 0 ? "#fffaf0" : "#e9dfc8"));
                    s.rectTransform.pivot = new Vector2(0.5f, 1f);
                    s.rectTransform.anchoredPosition = new Vector2(side * (4f + k * 6f), top);
                    s.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -side * (3f + k * 6f));
                    s.rectTransform.localScale = new Vector3(side, 1f, 1f);
                }
            }
            Img(root, "Cap", MenuArt.RoundRect, new Vector2(0f, top + 6f), new Vector2(34f, 26f), MenuArt.Hex("#8a5a2a"), true);
            Img(root, "CapGold", null, new Vector2(0f, top - 4f), new Vector2(34f, 5f), Gold);
            return root;
        }

        void BuildOmamori()
        {
            PageView page = NewPage(Page.Omamori);
            page.Cinematic = true;
            page.Zoom = 1.12f;
            page.Dim = 0.55f;

            RectTransform rays = Rect(page.Rect, "Rays", new Vector2(0f, 30f), Vector2.zero);
            for (int i = 0; i < 16; i++)
            {
                Image ray = Img(rays, "Ray", MenuArt.Triangle, Vector2.zero, new Vector2(i % 2 == 0 ? 70f : 40f, i % 2 == 0 ? 760f : 560f), MenuArt.Hex("#ffe2a0", HasOmamoriArt ? 0.09f : 0.16f));
                ray.rectTransform.pivot = new Vector2(0.5f, 1f);
                ray.rectTransform.anchoredPosition = Vector2.zero;
                ray.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i * 22.5f);
            }
            Image glow = Img(page.Rect, "Glow", MenuArt.Glow, new Vector2(0f, 30f), new Vector2(760f, 760f), MenuArt.Hex("#ffcf70", 0.85f));

            if (HasOmamoriArt)
            {
                // お守り5種が、まん中から扇のように飛び出す（まん中から外へ順に）
                var charms = new RectTransform[5];
                var targets = new Vector2[5];
                var angles = new float[5];
                // まん中に縁結び（いちばん「福」らしいもの）が来るよう、ならびを決める
                int[] order = { 2, 1, 0, 3, 4 };   // 左から 厄除け・金運・縁結び・健康・学業
                for (int slot = 0; slot < 5; slot++)
                {
                    float k = slot - 2f;
                    targets[slot] = new Vector2(k * 300f, 40f - Mathf.Abs(k) * 46f);
                    angles[slot] = -k * 9f;
                }
                // 外側から先に作って、まん中を手前にする
                foreach (int slot in new[] { 0, 4, 1, 3, 2 })
                {
                    float size = slot == 2 ? 380f : 320f;
                    Image img = Img(page.Rect, "Omamori", omamoriSprites[order[slot]], Vector2.zero, new Vector2(size, size * 1123f / 988f), Color.white);
                    img.preserveAspect = true;
                    charms[slot] = img.rectTransform;
                }

                page.Tick = e =>
                {
                    for (int slot = 0; slot < 5; slot++)
                    {
                        float delay = Mathf.Abs(slot - 2) * 0.13f;
                        float t = Mathf.Clamp01((e - delay) / 0.75f);
                        float back = EaseOutBack(t);
                        RectTransform c = charms[slot];
                        c.anchoredPosition = Vector2.LerpUnclamped(new Vector2(0f, -260f), targets[slot], back)
                            + new Vector2(0f, Mathf.Sin(e * 2.2f + slot) * 6f * t);
                        c.localScale = Vector3.one * Mathf.LerpUnclamped(0.2f, 1f, back);
                        c.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpUnclamped(0f, angles[slot], back) + Mathf.Sin(e * 2.6f + slot) * 2f * t);
                    }
                    rays.localRotation = Quaternion.Euler(0f, 0f, -e * 22f);
                    rays.localScale = Vector3.one * Mathf.SmoothStep(0f, 1.25f, e / 0.8f);
                    glow.rectTransform.localScale = Vector3.one * (1.3f + 0.12f * Mathf.Sin(e * 6f)) * Mathf.SmoothStep(0.2f, 1f, e / 0.9f);
                };
                return;
            }

            RectTransform charm = Rect(page.Rect, "Omamori", new Vector2(0f, 30f), new Vector2(250f, 340f));
            Img(charm, "Cord", MenuArt.Ring, new Vector2(0f, 200f), new Vector2(76f, 76f), Gold);
            Img(charm, "Border", MenuArt.RoundRect, Vector2.zero, new Vector2(250f, 340f), Gold, true);
            Img(charm, "Body", MenuArt.RoundRect, Vector2.zero, new Vector2(234f, 324f), MenuArt.Hex("#c0362c"), true);
            Img(charm, "Band", null, new Vector2(0f, 118f), new Vector2(234f, 6f), Gold);
            Img(charm, "Knot", MenuArt.Disc, new Vector2(0f, 150f), new Vector2(26f, 26f), Gold);
            Img(charm, "Inner", MenuArt.RoundRect, new Vector2(0f, -30f), new Vector2(190f, 230f), MenuArt.Hex("#a82a22"), true);
            Text c1 = Txt(charm, "Kanji1", new Vector2(0f, 20f), new Vector2(200f, 110f), 96, Cream, true, false);
            c1.text = "投";
            Text c2 = Txt(charm, "Kanji2", new Vector2(0f, -88f), new Vector2(200f, 110f), 96, Cream, true, false);
            c2.text = "福";

            page.Tick = e =>
            {
                float t = Mathf.Clamp01(e / 0.9f);
                float back = EaseOutBack(t);
                charm.anchoredPosition = new Vector2(0f, Mathf.LerpUnclamped(-180f, 30f, back));
                charm.localScale = Vector3.one * Mathf.LerpUnclamped(0.35f, 1f, back);
                charm.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(e * 2.4f) * 3f * t);
                rays.localRotation = Quaternion.Euler(0f, 0f, -e * 22f);
                rays.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, e / 0.8f);
                glow.rectTransform.localScale = Vector3.one * (0.9f + 0.12f * Mathf.Sin(e * 6f)) * Mathf.SmoothStep(0.2f, 1f, t);
            };
        }

        void BuildLogo()
        {
            PageView page = NewPage(Page.Logo);
            page.Cinematic = true;
            page.Zoom = 1f;
            // ロゴの絵にも鳥居があるので、背景の鳥居とけんかしないよう暗めに
            page.Dim = logoSprite != null ? 0.55f : 0.25f;

            RectTransform logo = BuildLogoMark(page.Rect, new Vector2(0f, 110f), 1f, true);

            _logoPrompt = Txt(page.Rect, "Prompt", new Vector2(0f, -330f), new Vector2(1200f, 60f), 40, Cream);
            page.OnShow = () => _logoPrompt.text = ControllerReady ? "大幣を振ってはじめる" : "Enter・クリックではじめる";
            page.Tick = e =>
            {
                // 光のあとに、少し大きいところから落ちついてくる
                float t = Mathf.Clamp01(e / 0.7f);
                logo.localScale = Vector3.one * Mathf.Lerp(1.12f, 1f, 1f - (1f - t) * (1f - t));
                Color c = _logoPrompt.color;
                c.a = e < 1f ? 0f : 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin((e - 1f) * 3.4f));
                _logoPrompt.color = c;
            };
        }

        // ロゴ。素材があればその画像、なければ しめ縄・紙垂・「投福神社」・「とうふくじんじゃ」を描く
        RectTransform BuildLogoMark(RectTransform parent, Vector2 pos, float scale, bool rope)
        {
            RectTransform root = Rect(parent, "Logo", pos, new Vector2(1400f, 600f));
            root.localScale = Vector3.one * scale;

            Img(root, "Glow", MenuArt.Glow, Vector2.zero, new Vector2(1600f, 760f), MenuArt.Hex("#ffbe5a", 0.3f));
            if (logoSprite != null)
            {
                // 2000×1007 の絵。幅 1340 で高さ 675
                Image art = Img(root, "LogoArt", logoSprite, Vector2.zero, new Vector2(1340f, 1340f * 1007f / 2000f), Color.white);
                art.preserveAspect = true;
                return root;
            }
            if (rope)
            {
                Rope(root, new Vector2(-460f, 230f), new Vector2(0f, 120f), new Vector2(460f, 230f), 18f);
                // 縄の t = 0.25, 0.5, 0.75 の点から垂らす
                foreach (Vector2 p in new[] { new Vector2(-230f, 181f), new Vector2(0f, 167f), new Vector2(230f, 181f) })
                {
                    Image s = Img(root, "Shide", MenuArt.Shide, Vector2.zero, new Vector2(30f, 76f), MenuArt.Hex("#fffaf0"));
                    s.rectTransform.pivot = new Vector2(0.5f, 1f);
                    s.rectTransform.anchoredPosition = p;
                }
            }

            Text title = Txt(root, "Title", new Vector2(0f, -30f), new Vector2(1400f, 260f), 220, Cream, true);
            title.text = "投福神社";
            TitleStyle(title, 4f);

            Img(root, "LineL", null, new Vector2(-330f, -190f), new Vector2(170f, 3f), Gold);
            Img(root, "LineR", null, new Vector2(330f, -190f), new Vector2(170f, 3f), Gold);
            Text kana = Txt(root, "Kana", new Vector2(0f, -190f), new Vector2(500f, 60f), 38, Gold, true);
            kana.text = "と う ふ く じ ん じ ゃ";
            return root;
        }

        void BuildTop()
        {
            PageView page = NewPage(Page.Top);
            page.Back = Page.Logo;
            page.Dim = logoSprite != null ? 0.55f : 0.38f;

            BuildLogoMark(page.Rect, new Vector2(0f, logoSprite != null ? 320f : 300f), logoSprite != null ? 0.56f : 0.62f, false);
            Fuda(page, "プレイ", new Vector2(0f, 40f), new Vector2(460f, 104f), 52, () => Show(Page.SaveSelect));
            Fuda(page, "設定", new Vector2(0f, -100f), new Vector2(460f, 104f), 52, () => Show(Page.Settings));
            Fuda(page, "終了", new Vector2(0f, -240f), new Vector2(460f, 104f), 52, () => Show(Page.QuitConfirm));
        }

        void BuildSaveSelect()
        {
            PageView page = NewPage(Page.SaveSelect);
            page.Back = Page.Top;
            page.Dim = 0.52f;

            Heading(page.Rect, "― 絵馬を選ぶ ―", new Vector2(0f, 400f));

            string[] numerals = { "壱", "弐", "参" };
            for (int i = 0; i < SaveSlots.SlotCount; i++)
            {
                int slot = i;
                EmaView ema = BuildEma(page.Rect, new Vector2((i - 1) * 580f, 20f), numerals[Mathf.Min(i, numerals.Length - 1)]);
                _emas.Add(ema);
                page.Items.Add(new Item
                {
                    Name = $"絵馬{numerals[Mathf.Min(i, numerals.Length - 1)]}",
                    Rect = ema.Rect,
                    Action = () => ChooseSlot(slot),
                    SetSelected = on =>
                    {
                        ema.Border.color = on ? Gold : Wood;
                        ema.Body.color = on ? MenuArt.Hex("#f2d79c") : MenuArt.Hex("#d2b27a");
                        ema.Glow.enabled = on;
                        ema.Rect.localScale = Vector3.one * (on ? 1.06f : 0.94f);
                    }
                });
                AddButton(ema.Rect, page.Items.Count - 1);
            }
            BackFuda(page);

            page.OnShow = RefreshEmas;
            page.Tick = e =>
            {
                for (int i = 0; i < _emas.Count; i++)
                    _emas[i].Rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(e * 1.5f + i * 1.3f) * 1.6f);
            };
        }

        EmaView BuildEma(RectTransform parent, Vector2 pos, string numeral)
        {
            var v = new EmaView();
            const float w = 440f, h = 361f;
            v.Rect = Rect(parent, $"Ema{numeral}", pos, new Vector2(w, h));
            v.Rect.pivot = new Vector2(0.5f, 1.15f);
            v.Rect.anchoredPosition = pos + new Vector2(0f, h * 0.65f);

            v.Glow = Img(v.Rect, "Glow", MenuArt.Glow, Vector2.zero, new Vector2(w * 1.7f, h * 1.7f), MenuArt.Hex("#ffc060", 0.45f));
            Img(v.Rect, "String", null, new Vector2(0f, h * 0.5f + 40f), new Vector2(4f, 90f), Gold);
            Img(v.Rect, "Shadow", MenuArt.Ema, new Vector2(6f, -10f), new Vector2(w, h), new Color(0f, 0f, 0f, 0.35f));
            v.Border = Img(v.Rect, "Border", MenuArt.Ema, Vector2.zero, new Vector2(w, h), Wood);
            v.Body = Img(v.Rect, "Body", MenuArt.Ema, new Vector2(0f, -2f), new Vector2(w - 22f, h - 22f), MenuArt.Hex("#d2b27a"));

            // 屋根のふち（2本）
            float eave = -h * 0.5f + h * 0.66f, apex = h * 0.5f;
            Vector2 edge = new Vector2(w * 0.5f, apex - eave);
            float angle = Mathf.Atan2(edge.y, edge.x) * Mathf.Rad2Deg;
            foreach (float side in new[] { -1f, 1f })
            {
                Image roof = Img(v.Rect, "Roof", null, new Vector2(side * w * 0.25f, (eave + apex) * 0.5f), new Vector2(edge.magnitude + 6f, 20f), Wood);
                roof.rectTransform.localRotation = Quaternion.Euler(0f, 0f, side < 0 ? angle : -angle);
            }
            Img(v.Rect, "Hole", MenuArt.Disc, new Vector2(0f, apex - 52f), new Vector2(16f, 16f), Ink);
            if (crestSprite != null)
                Img(v.Rect, "CrestMark", crestSprite, new Vector2(0f, -70f), new Vector2(230f, 230f), MenuArt.Hex("#7a4a22", 0.16f));
            Text num = Txt(v.Rect, "Numeral", new Vector2(0f, eave + 26f), new Vector2(200f, 50f), 34, Wood, true, false);
            num.text = numeral;

            v.Title = Txt(v.Rect, "Title", new Vector2(0f, -20f), new Vector2(w - 40f, 70f), 46, Ink, true, false);
            v.Sub = Txt(v.Rect, "Sub", new Vector2(0f, -80f), new Vector2(w - 40f, 40f), 26, Ink, false, false);
            v.Status = Txt(v.Rect, "Status", new Vector2(0f, -125f), new Vector2(w - 40f, 40f), 24, Vermilion, false, false);
            return v;
        }

        void RefreshEmas()
        {
            string[] numerals = { "壱", "弐", "参" };
            int defaultIndex = 0;
            string latest = null;
            for (int i = 0; i < _emas.Count; i++)
            {
                SaveData data = SaveSlots.Load(i);
                EmaView v = _emas[i];
                if (data == null)
                {
                    v.Title.text = "新しくはじめる";
                    v.Sub.text = $"{mapTitle}　{(stages.Count > 0 ? stages[0].label : "")} から";
                    v.Status.text = "白紙の絵馬";
                }
                else
                {
                    v.Title.text = $"記録　{numerals[Mathf.Min(i, numerals.Length - 1)]}";
                    v.Sub.text = $"{data.ClearedCount} / {stages.Count} 参拝済み　・　縁 {data.TotalScore:N0}";
                    v.Status.text = $"最後のお参り {data.lastPlayedAt}";
                    // いちばん最近遊んだ枠をはじめに選んでおく
                    if (latest == null || string.CompareOrdinal(data.lastPlayedAt, latest) > 0)
                    {
                        latest = data.lastPlayedAt;
                        defaultIndex = i;
                    }
                }
            }
            if (latest == null)
            {
                // ぜんぶ白紙なら、いちばん左
                defaultIndex = 0;
            }
            _pages[Page.SaveSelect].DefaultIndex = defaultIndex;
        }

        void BuildStageMap()
        {
            PageView page = NewPage(Page.StageMap);
            page.Back = Page.SaveSelect;
            page.Zoom = 1.05f;
            page.Dim = 0.78f;

            Text head = Txt(page.Rect, "MapTitle", new Vector2(-560f, 440f), new Vector2(720f, 100f), 76, Cream, true, align: TextAnchor.MiddleLeft);
            head.text = mapTitle;
            TitleStyle(head, 3f);
            _mapSlot = Txt(page.Rect, "MapSub", new Vector2(-560f, 370f), new Vector2(720f, 40f), 28, MenuArt.Hex("#e8d7b8"), align: TextAnchor.MiddleLeft);

            // 参道（マスとマスを S 字でつなぐ）
            Vector2 center = new Vector2(960f, 540f);
            RectTransform pathRoot = Rect(page.Rect, "Path", Vector2.zero, Vector2.zero);
            var goldDots = new List<(Image dot, int segment)>();
            for (int s = 0; s + 1 < stages.Count; s++)
            {
                Vector2 a = stages[s].mapPosition - center, b = stages[s + 1].mapPosition - center;
                Vector2 c1 = new Vector2(Mathf.Lerp(a.x, b.x, 0.5f), a.y), c2 = new Vector2(Mathf.Lerp(a.x, b.x, 0.5f), b.y);
                float length = Vector2.Distance(a, b) * 1.2f;
                // 敷石（とびとびの楕円）と、解放された道だけ光る金の点
                int n = Mathf.Max(6, Mathf.RoundToInt(length / 34f));
                for (int k = 1; k < n; k++)
                {
                    Vector2 p = Bezier(a, c1, c2, b, k / (float)n);
                    Img(pathRoot, "Stone", MenuArt.Disc, p, new Vector2(30f, 20f), MenuArt.Hex("#e9d7ad", 0.22f));
                    goldDots.Add((Img(pathRoot, "Dot", MenuArt.Disc, p, new Vector2(10f, 10f), Gold), s));
                }
            }

            for (int i = 0; i < stages.Count; i++)
            {
                NodeView node = BuildNode(page.Rect, stages[i], stages[i].mapPosition - center, i == stages.Count - 1);
                _nodes.Add(node);
                page.Items.Add(new Item
                {
                    Name = $"ステージ {stages[i].id}",
                    Rect = node.Rect,
                    Action = () => ChooseStage(node),
                    SetSelected = on =>
                    {
                        node.Ring.enabled = on;
                        node.Rect.localScale = Vector3.one * (on ? 1.15f : 1f);
                        if (on) RefreshStageInfo(node);
                    }
                });
                AddButton(node.Rect, page.Items.Count - 1);
            }

            // 説明の板
            RectTransform panel = Rect(page.Rect, "InfoPanel", new Vector2(610f, -290f), new Vector2(580f, 320f));
            Img(panel, "Border", MenuArt.RoundRect, Vector2.zero, new Vector2(580f, 320f), Gold, true);
            Img(panel, "Body", MenuArt.RoundRect, Vector2.zero, new Vector2(572f, 312f), Night, true);
            _infoTitle = Txt(panel, "Title", new Vector2(0f, 110f), new Vector2(520f, 60f), 44, Cream, true, align: TextAnchor.MiddleLeft);
            _infoBody = Txt(panel, "Body", new Vector2(0f, 25f), new Vector2(520f, 100f), 26, MenuArt.Hex("#e8dcc4"), outline: false, align: TextAnchor.UpperLeft);
            _infoBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            _infoBody.lineSpacing = 1.15f;
            _infoStats = Txt(panel, "Stats", new Vector2(0f, -50f), new Vector2(520f, 40f), 26, Gold, outline: false, align: TextAnchor.MiddleLeft);
            _infoButtonImage = Img(panel, "Button", MenuArt.RoundRect, new Vector2(0f, -112f), new Vector2(520f, 66f), Vermilion, true);
            _infoButton = Txt(panel, "ButtonLabel", new Vector2(0f, -112f), new Vector2(520f, 66f), 34, Cream, true, false);

            BackFuda(page);

            page.OnShow = () =>
            {
                int slot = SaveSlots.CurrentSlot;
                string[] numerals = { "壱", "弐", "参" };
                _mapSlot.text = slot >= 0 ? $"{mapSubtitle}　・　記録 {numerals[Mathf.Clamp(slot, 0, numerals.Length - 1)]}" : mapSubtitle;

                // はじめに選ぶのは、解放されていて、まだクリアしていない最初のマス（ぜんぶクリアなら、解放されている最後のマス）
                int first = -1, lastUnlocked = 0;
                for (int i = 0; i < _nodes.Count; i++)
                {
                    bool unlocked = IsUnlocked(_nodes[i].Stage);
                    bool cleared = SaveSlots.Current != null && SaveSlots.Current.IsCleared(_nodes[i].Stage.id);
                    SetNodeState(_nodes[i], unlocked, cleared);
                    if (!unlocked) continue;
                    lastUnlocked = i;
                    if (first < 0 && !cleared) first = i;
                }
                page.DefaultIndex = first >= 0 ? first : lastUnlocked;

                foreach ((Image dot, int segment) d in goldDots)
                    d.dot.enabled = IsUnlocked(stages[d.segment + 1]);
            };

            page.Tick = e =>
            {
                for (int i = 0; i < _nodes.Count; i++)
                {
                    NodeView n = _nodes[i];
                    if (n.Ring.enabled)
                    {
                        float t = Mathf.Repeat(e / 1.4f, 1f);
                        n.Ring.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.9f, 1.35f, t);
                        Color c = n.Ring.color;
                        c.a = 0.9f * (1f - t);
                        n.Ring.color = c;
                    }
                    if (n.Glow.enabled)
                        n.Glow.rectTransform.localScale = Vector3.one * (0.92f + 0.1f * Mathf.Sin(e * 2.6f + i));
                }
                for (int i = 0; i < goldDots.Count; i++)
                {
                    Color c = goldDots[i].dot.color;
                    c.a = 0.45f + 0.55f * (0.5f + 0.5f * Mathf.Sin(e * 3f - i * 0.45f));
                    goldDots[i].dot.color = c;
                }
            };
        }

        // 参道マップの1マス（小さな鳥居。最後は本殿として少し大きく）
        NodeView BuildNode(RectTransform parent, StageInfo stage, Vector2 pos, bool shrine)
        {
            var n = new NodeView { Stage = stage };
            float s = shrine ? 1.25f : 1f;
            n.Rect = Rect(parent, $"Node_{stage.id}", pos, new Vector2(150f, 170f) * s);
            n.Glow = Img(n.Rect, "Glow", MenuArt.Glow, new Vector2(0f, 20f), new Vector2(300f, 300f) * s, MenuArt.Hex("#ffb040", 0.7f));
            n.Ring = Img(n.Rect, "Ring", MenuArt.Ring, new Vector2(0f, 20f), new Vector2(190f, 190f) * s, Gold);
            n.Ring.enabled = false;
            n.Wood = new[]
            {
                Img(n.Rect, "Kasagi", null, new Vector2(0f, 72f) * s, new Vector2(124f, 14f) * s, Color.white),
                Img(n.Rect, "Nuki", null, new Vector2(0f, 46f) * s, new Vector2(100f, 10f) * s, Color.white),
                Img(n.Rect, "PillarL", null, new Vector2(-32f, 14f) * s, new Vector2(14f, 96f) * s, Color.white),
                Img(n.Rect, "PillarR", null, new Vector2(32f, 14f) * s, new Vector2(14f, 96f) * s, Color.white),
            };
            n.Label = Txt(n.Rect, "Label", new Vector2(0f, -62f) * s, new Vector2(200f, 50f), 40, Cream, true);
            n.Label.text = stage.label;
            n.Stamp = Img(n.Rect, "Stamp", MenuArt.Ring, new Vector2(52f, 80f) * s, new Vector2(46f, 46f), Vermilion);
            Text stampText = Txt(n.Stamp.rectTransform, "Text", Vector2.zero, new Vector2(46f, 46f), 24, Vermilion, true, false);
            stampText.text = "済";
            return n;
        }

        void SetNodeState(NodeView n, bool unlocked, bool cleared)
        {
            Color wood = unlocked ? MenuArt.Hex("#c0362c") : MenuArt.Hex("#4a3e52");
            for (int i = 0; i < n.Wood.Length; i++) n.Wood[i].color = i == 0 ? (unlocked ? MenuArt.Hex("#1d0d0d") : MenuArt.Hex("#2a2230")) : wood;
            n.Glow.enabled = unlocked;
            n.Label.color = unlocked ? Cream : MenuArt.Hex("#8a7e94");
            n.Stamp.gameObject.SetActive(cleared);
        }

        void RefreshStageInfo(NodeView node)
        {
            StageInfo s = node.Stage;
            bool unlocked = IsUnlocked(s);
            StageRecord record = SaveSlots.Current?.Find(s.id);
            _infoTitle.text = $"{s.label}　{s.title}";
            _infoBody.text = unlocked ? s.description : "前のお参りをすませると、参拝できます。";
            _infoStats.text = record != null && record.bestScore > 0 ? $"最高の縁　{record.bestScore:N0}" : "最高の縁　―";
            _infoButton.text = unlocked ? (ControllerReady ? "振って参拝" : "決めて参拝") : "まだ参拝できません";
            _infoButtonImage.color = unlocked ? Vermilion : MenuArt.Hex("#3a3040");
        }

        /*
            設定（左に項目のリスト、右に大幣の状態、下に選んでいる項目の説明）
            ・大幣をつなぐ: 決めると USB をさがしなおす
            ・正面を合わせる: 決めると今の向きを正面にする（正面ボタン1秒長押しでも）
            ・音量: 決めると調整に入り、傾け・←→で変えて、もう一度決めて確定
            ・もどる
        */
        void BuildSettings()
        {
            PageView page = NewPage(Page.Settings);
            page.Back = Page.Top;
            page.Dim = 0.62f;
            Heading(page.Rect, "設定", new Vector2(0f, 420f));

            const float rowX = -300f;
            Vector2 rowSize = new Vector2(860f, 112f);

            Text value;
            SettingRow(page, "大幣をつなぐ", new Vector2(rowX, 250f), rowSize, Reconnect, out value,
                "大幣が反応しないときに決めると、USB の接続をさがしなおします。");
            _linkValue = value;

            SettingRow(page, "正面を合わせる", new Vector2(rowX, 115f), rowSize, CalibrateFromMenu, out value,
                "画面の真ん中に大幣を向けて止め、決めると今の向きが「正面」になります。\n正面ボタンを1秒長押ししても合わせられます。");
            _frontValue = value;

            RectTransform volumeRow = SettingRow(page, "音量", new Vector2(rowX, -20f), rowSize, () => SetVolumeAdjusting(true), out value,
                "決めると音量を調整できます。");
            _volumeValue = value;
            _volumeBars = new Image[10];
            for (int i = 0; i < _volumeBars.Length; i++)
                _volumeBars[i] = Img(volumeRow, "VolumeBar", MenuArt.RoundRect, new Vector2(-40f + i * 24f, 0f), new Vector2(16f, 22f + i * 3.4f), Cream, true);

            SettingRow(page, "もどる", new Vector2(rowX, -155f), rowSize, () => Show(Page.Top), out value,
                "メニューにもどります。");
            value.text = "";
            page.DefaultIndex = 3;

            // 説明
            RectTransform help = Panel(page.Rect, new Vector2(rowX, -330f), new Vector2(860f, 150f));
            _settingsHelp = Txt(help, "Help", Vector2.zero, new Vector2(800f, 120f), 30, MenuArt.Hex("#f2e6cc"), outline: false, align: TextAnchor.MiddleLeft);
            _settingsHelp.horizontalOverflow = HorizontalWrapMode.Wrap;
            _settingsHelp.lineSpacing = 1.2f;

            // 大幣の状態
            Color label = MenuArt.Hex("#b8a890");
            RectTransform status = Panel(page.Rect, new Vector2(560f, 45f), new Vector2(560f, 620f));
            Txt(status, "Title", new Vector2(0f, 255f), new Vector2(500f, 60f), 40, Cream, true, align: TextAnchor.MiddleLeft).text = "大幣の状態";

            Txt(status, "LinkLabel", new Vector2(0f, 190f), new Vector2(500f, 36f), 24, label, outline: false, align: TextAnchor.MiddleLeft).text = "つながり";
            _statusDot = Img(status, "LinkDot", MenuArt.Disc, new Vector2(-235f, 148f), new Vector2(26f, 26f), Color.white);
            _statusLink = Txt(status, "Link", new Vector2(20f, 148f), new Vector2(460f, 40f), 30, Cream, outline: false, align: TextAnchor.MiddleLeft);

            Txt(status, "AimLabel", new Vector2(0f, 90f), new Vector2(500f, 36f), 24, label, outline: false, align: TextAnchor.MiddleLeft).text = "向き（正面から）";
            Img(status, "AimTrack", MenuArt.RoundRect, new Vector2(0f, 45f), new Vector2(460f, 20f), MenuArt.Hex("#000000", 0.45f), true);
            Img(status, "AimCenter", null, new Vector2(0f, 45f), new Vector2(3f, 36f), Gold);
            _aimMarker = Img(status, "AimMarker", MenuArt.Disc, new Vector2(0f, 45f), new Vector2(30f, 30f), Vermilion).rectTransform;
            Img(_aimMarker, "Core", MenuArt.Disc, Vector2.zero, new Vector2(12f, 12f), Cream);
            _aimText = Txt(status, "Aim", new Vector2(0f, 5f), new Vector2(500f, 40f), 28, Cream, outline: false);

            Txt(status, "ButtonLabel", new Vector2(0f, -50f), new Vector2(500f, 36f), 24, label, outline: false, align: TextAnchor.MiddleLeft).text = HasOmamoriArt ? "お守りボタン（押すと光る）" : "ボタン（押すと光る）";
            string[] buttonNames = { "1", "2", "3", "4", "5", "正面" };
            _buttonDots = new Image[buttonNames.Length];
            for (int i = 0; i < buttonNames.Length; i++)
            {
                Vector2 p = new Vector2(-200f + i * 80f, -100f);
                if (i < 5 && HasOmamoriArt)
                {
                    // 色ボタンは、そのボタンで選ぶお守りの絵で出す（番号は右下に小さく）
                    _buttonDots[i] = Img(status, "ButtonOmamori", omamoriSprites[i], p + new Vector2(0f, 4f), new Vector2(60f, 68f), Color.white);
                    _buttonDots[i].preserveAspect = true;
                    Txt(_buttonDots[i].rectTransform, "Name", new Vector2(22f, -24f), new Vector2(30f, 30f), 22, Cream, false, true).text = buttonNames[i];
                }
                else
                {
                    _buttonDots[i] = Img(status, "ButtonDot", MenuArt.Disc, p, new Vector2(56f, 56f), Color.white);
                    Txt(_buttonDots[i].rectTransform, "Name", Vector2.zero, new Vector2(56f, 56f), i < 5 ? 28 : 20, Cream, false, false).text = buttonNames[i];
                }
            }
            _statusSource = Txt(status, "Source", new Vector2(0f, -158f), new Vector2(500f, 36f), 22, label, outline: false);

            Txt(status, "FrontLabel", new Vector2(0f, -200f), new Vector2(500f, 36f), 24, label, outline: false, align: TextAnchor.MiddleLeft).text = "正面合わせ（正面ボタン1秒長押し）";
            Img(status, "FrontTrack", MenuArt.RoundRect, new Vector2(0f, -238f), new Vector2(460f, 16f), MenuArt.Hex("#000000", 0.45f), true);
            _frontGaugeFill = Img(status, "FrontFill", null, new Vector2(-220f, -238f), new Vector2(0f, 10f), Gold);
            _frontGaugeFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            _calibrationText = Txt(status, "Calibration", new Vector2(0f, -272f), new Vector2(520f, 36f), 22, MenuArt.Hex("#ffe08a"), outline: false);
        }

        // 設定の1行（左に名前、右に今の値）。選ぶと朱色に金のふち
        RectTransform SettingRow(PageView page, string label, Vector2 pos, Vector2 size, Action action, out Text value, string help)
        {
            RectTransform root = Rect(page.Rect, $"Row_{label}", pos, size);
            Img(root, "Shadow", MenuArt.RoundRect, new Vector2(0f, -8f), size, MenuArt.Hex("#2a140a", 0.85f), true);
            Image border = Img(root, "Border", MenuArt.RoundRect, Vector2.zero, size, Wood, true);
            Image body = Img(root, "Body", MenuArt.RoundRect, Vector2.zero, size - new Vector2(10f, 10f), MenuArt.Hex("#2a1c30"), true);
            Image cursor = Img(root, "Cursor", MenuArt.Glow, new Vector2(-size.x * 0.5f - 40f, 0f), new Vector2(64f, 64f), MenuArt.Hex("#ffe7a0"));
            if (crestSprite != null) Img(cursor.rectTransform, "Crest", crestSprite, Vector2.zero, new Vector2(54f, 54f), Gold);
            Text name =Txt(root, "Label", new Vector2(-size.x * 0.5f + 230f, 0f), new Vector2(400f, size.y), 44, Cream, true, false, TextAnchor.MiddleLeft);
            name.text = label;
            value = Txt(root, "Value", new Vector2(size.x * 0.5f - 200f, 0f), new Vector2(360f, size.y), 34, Cream, false, false, TextAnchor.MiddleRight);
            _settingsHelps.Add(help);

            page.Items.Add(new Item
            {
                Name = label,
                Rect = root,
                Action = action,
                SetSelected = on =>
                {
                    // 子の紋ごと消す（Image を切るだけだと子が残る）
                    cursor.gameObject.SetActive(on);
                    border.color = on ? Gold : Wood;
                    body.color = on ? Vermilion : MenuArt.Hex("#2a1c30");
                    root.localScale = Vector3.one * (on ? 1.03f : 1f);
                }
            });
            AddButton(root, page.Items.Count - 1);
            return root;
        }

        void BuildQuitConfirm()
        {
            PageView page = NewPage(Page.QuitConfirm);
            page.Back = Page.Top;
            page.Dim = 0.62f;
            Heading(page.Rect, "終了", new Vector2(0f, 400f));

            RectTransform panel = Panel(page.Rect, new Vector2(0f, 60f), new Vector2(1100f, 260f));
            Txt(panel, "Body", Vector2.zero, new Vector2(1000f, 120f), 60, Cream, true).text = "ゲームを終了しますか？";
            Fuda(page, "おわる", new Vector2(-220f, -200f), new Vector2(360f, 104f), 50, Quit);
            Fuda(page, "もどる", new Vector2(220f, -200f), new Vector2(360f, 104f), 50, () => Show(Page.Top));
            // まちがって振っても終わらないように、はじめは「もどる」を選んでおく
            page.DefaultIndex = 1;
        }

        // ---------- 部品 ----------

        PageView NewPage(Page page)
        {
            var view = new PageView();
            RectTransform rt = HudUi.Stretch(HudUi.CreateRect(_canvas, $"Page_{page}", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero));
            view.Root = rt.gameObject;
            view.Group = view.Root.AddComponent<CanvasGroup>();
            view.Group.alpha = 0f;
            view.Root.SetActive(false);
            _pages[page] = view;
            return view;
        }

        // 木の札の項目（選ぶと朱色に金のふちで光る）
        Item Fuda(PageView page, string label, Vector2 pos, Vector2 size, int fontSize, Action action)
        {
            RectTransform root = Rect(page.Rect, $"Fuda_{label}", pos, size);
            Image glow = Img(root, "Glow", MenuArt.Glow, Vector2.zero, size + new Vector2(260f, 200f), MenuArt.Hex("#ffb050", 0.55f));
            Img(root, "Shadow", MenuArt.RoundRect, new Vector2(0f, -9f), size, MenuArt.Hex("#2a140a", 0.85f), true);
            Image border = Img(root, "Border", MenuArt.RoundRect, Vector2.zero, size, Wood, true);
            Image body = Img(root, "Body", MenuArt.RoundRect, Vector2.zero, size - new Vector2(10f, 10f), Paper, true);
            Image cursor = Img(root, "Cursor", MenuArt.Glow, new Vector2(-size.x * 0.5f - 46f, 0f), new Vector2(64f, 64f), MenuArt.Hex("#ffe7a0"));
            Image cursorCore = crestSprite != null
                ? Img(cursor.rectTransform, "Crest", crestSprite, Vector2.zero, new Vector2(54f, 54f), Gold)
                : Img(cursor.rectTransform, "Core", MenuArt.Disc, Vector2.zero, new Vector2(16f, 16f), MenuArt.Hex("#fff6d8"));
            Text text = Txt(root, "Label", Vector2.zero, size, fontSize, Ink, true, false);
            text.text = label;

            var item = new Item
            {
                Name = label,
                Rect = root,
                Action = action,
                SetSelected = on =>
                {
                    glow.enabled = on;
                    cursor.enabled = on;
                    cursorCore.enabled = on;
                    border.color = on ? Gold : Wood;
                    body.color = on ? Vermilion : Paper;
                    text.color = on ? Cream : Ink;
                    root.localScale = Vector3.one * (on ? 1.06f : 1f);
                }
            };
            page.Items.Add(item);
            AddButton(root, page.Items.Count - 1);
            return item;
        }

        // 右上の「もどる」の札
        void BackFuda(PageView page)
        {
            Page? back = page.Back;
            Fuda(page, "もどる", new Vector2(770f, 450f), new Vector2(250f, 80f), 36, () => { if (back.HasValue) Show(back.Value); });
        }

        // クリックで決める（見えない板を当たり判定にする）
        void AddButton(RectTransform rt, int index)
        {
            Image hit = rt.gameObject.GetComponent<Image>();
            if (hit == null) hit = rt.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;
            Button button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() =>
            {
                if (_loading || _flashStart >= 0f) return;
                Activate(index);
            });
        }

        void Heading(RectTransform parent, string text, Vector2 pos)
        {
            Text t = Txt(parent, "Heading", pos, new Vector2(1600f, 110f), 72, Cream, true);
            t.text = text;
            TitleStyle(t, 3f);
        }

        RectTransform Panel(RectTransform parent, Vector2 pos, Vector2 size)
        {
            RectTransform rt = Rect(parent, "Panel", pos, size);
            Img(rt, "Border", MenuArt.RoundRect, Vector2.zero, size, Gold, true);
            Img(rt, "Body", MenuArt.RoundRect, Vector2.zero, size - new Vector2(8f, 8f), Night, true);
            return rt;
        }

        // 金のふちと、こげ茶の影
        static void TitleStyle(Text text, float outline)
        {
            Outline o = text.GetComponent<Outline>();
            if (o == null) o = text.gameObject.AddComponent<Outline>();
            o.effectColor = MenuArt.Hex("#c8902c");
            o.effectDistance = new Vector2(outline, -outline);
            Shadow s = text.gameObject.AddComponent<Shadow>();
            s.effectColor = MenuArt.Hex("#2a0c06", 0.85f);
            s.effectDistance = new Vector2(0f, -outline * 2.5f);
        }

        void Rope(RectTransform parent, Vector2 a, Vector2 ctrl, Vector2 b, float thickness)
        {
            const int segments = 30;
            Vector2 prev = a;
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector2 p = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * ctrl + t * t * b;
                Vector2 d = p - prev;
                Image seg = Img(parent, "Rope", null, (prev + p) * 0.5f, new Vector2(d.magnitude + 2f, thickness), MenuArt.Hex(i % 2 == 0 ? "#d8c08a" : "#c4a870"));
                seg.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                prev = p;
            }
        }

        // 親の中央からの位置で置く四角
        static RectTransform Rect(RectTransform parent, string name, Vector2 pos, Vector2 size)
        {
            var center = new Vector2(0.5f, 0.5f);
            return HudUi.CreateRect(parent, name, center, center, pos, size);
        }

        static Image Img(RectTransform parent, string name, Sprite sprite, Vector2 pos, Vector2 size, Color color, bool sliced = false)
        {
            RectTransform rt = Rect(parent, name, pos, size);
            Image image = rt.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            if (sliced && sprite != null) image.type = Image.Type.Sliced;
            return image;
        }

        Text Txt(RectTransform parent, string name, Vector2 pos, Vector2 size, int fontSize, Color color,
            bool title = false, bool outline = true, TextAnchor align = TextAnchor.MiddleCenter)
        {
            RectTransform rt = Rect(parent, name, pos, size);
            Font f = title && titleFont != null ? titleFont : font;
            Text text = HudUi.CreateText(rt, "Text", f, fontSize, align, color, outline);
            if (outline)
            {
                Outline o = text.GetComponent<Outline>();
                if (o != null) o.effectColor = MenuArt.Hex("#1a0a10", 0.85f);
            }
            return text;
        }

        static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
        {
            float u = 1f - t;
            return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        static List<StageInfo> DefaultStages()
        {
            return new List<StageInfo>
            {
                new StageInfo { id = "1-1", label = "1-1", title = "朝の参道", sceneName = "GameScene", mapPosition = new Vector2(230f, 150f),
                    description = "はじめてのお参り。大幣の振り方と、お守りの投げ方を覚えよう。" },
                new StageInfo { id = "1-2", label = "1-2", title = "昼の境内", sceneName = "GameScene", mapPosition = new Vector2(760f, 370f),
                    description = "参拝客がふえてくる。願いの色を見分けて、すばやく届けよう。" },
                new StageInfo { id = "1-3", label = "1-3", title = "夕暮れの縁日", sceneName = "GameScene", mapPosition = new Vector2(1210f, 590f),
                    description = "遠くの客や歩く客もやってくる。照準をしっかり合わせよう。" },
                new StageInfo { id = "1-honden", label = "本殿", title = "本殿の大祭", sceneName = "GameScene", mapPosition = new Vector2(1560f, 830f),
                    description = "一の鳥居のしめくくり。境内いっぱいの願いをかなえよう。" },
            };
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
