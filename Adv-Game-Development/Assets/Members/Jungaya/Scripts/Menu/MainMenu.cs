using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Toufuku.GameInput;

namespace Toufuku.Menu
{
    /*
        メインメニュー（タイトルの演出 → メニュー → 参道マップでステージを選ぶ）

        ・画面は、シーンの MainMenuCanvas の下に置いてある（Page_〜 が1画面、ShrineBackdrop が背景）。
          位置・文字・色・画像は、ふだんどおり Scene ビューとインスペクタで変える。
          このスクリプトは、入力を受けて画面を切りかえ、設定と参道マップの中身を書きかえるだけ
        ・動きは AnimationClip（Animations/Menu）。Page_〜 の Animator が、画面を開くたびに頭から再生する。
          星・参道の金の点は MenuTwinkle、花びらは MenuPetals（数が多いので、インスペクタの数値で動かす）
        ・流れ
            前奏（境内へ寄っていく）
              → 大幣がつながっていれば「大幣を振ってみよう」→ 振るとお守りが飛び出す → 光ってロゴへ
              → つながっていなければ、そのままロゴへ
            ロゴ「投福神社」→ 決めるとメニュー（プレイ／設定／終了）
            プレイ → 参道マップ（unlockAllStages が ON なら、どのステージもはじめから選べる）→ ステージのシーンへ
            （ゲームショウで次々に遊んでもらうので、セーブ枠を選ぶ画面は出さない。記録もファイルに残さない）
        ・2回目からは前奏を飛ばしてロゴから（リザルトからもどってきたとき。replayIntroEveryTime で毎回にもできる）
        ・操作
            大幣: 左右に傾けて選ぶ（tiltStepDegrees ごとに1つとなり）、振って決める
            正面ボタン: 1秒長押しで正面を合わせる（どの画面でも。企画書 v8 3章の受理条件つき）。合わせた正面は ControllerCalibration でプレイのシーンへ引きつぐ
            キーボード: ←→（↑↓）で選ぶ、Enter / Space で決める、Esc でもどる
            マウス: 指すと選ぶ、クリックで決める（演出の画面ではクリックで先へ）
        ・項目を足すとき: Fuda_〜 を複製 → MenuEntry の onDecide に呼ぶもの（下の Open〜 など）を入れる → その画面の MenuPage の items に足す
        ・ステージを足すとき: Node_〜 を複製 → MenuStageNode の stage を書く → 参道マップの MenuPage の items と、ここの stageNodes に足す
    */
    public class MainMenu : MonoBehaviour
    {
        // 設定画面の、押すと光るボタンの印
        [Serializable]
        public class ButtonLamp
        {
            public Image image;
            public Color idle = Color.gray;
            public Color held = Color.white;
            public float heldScale = 1f;
        }

        [Header("入力（Esp32RawSource / KeyboardMouseRawSource をドラッグ）。未設定ならシーンから探す")]
        [SerializeField] MonoBehaviour rawSourceSource;
        [Tooltip("設定画面で接続の状態を出し、再接続するのに使う。未設定ならシーンから探す")]
        [SerializeField] ConecteController controller;

        [Header("シーンの部品（MainMenuCanvas の下）")]
        [SerializeField] ShrineBackdrop backdrop;
        [Tooltip("画面ぜんぶ（Page_〜）")]
        [SerializeField] List<MenuPage> pages = new List<MenuPage>();
        [Tooltip("画面を変えるときに光る板")]
        [SerializeField] Image flash;
        [Tooltip("右下の操作の案内")]
        [SerializeField] Text hint;
        [Tooltip("下に少しのあいだ出す知らせ")]
        [SerializeField] Text toast;

        [Header("タイトル")]
        [SerializeField] Text logoPrompt;
        [SerializeField] string promptWithOnusa = "大幣を振って開始";
        [SerializeField] string promptWithoutOnusa = "ボタンを押して開始";
        [SerializeField] Text swingBadge;
        [SerializeField] string badgeConnected = "● 大幣 接続中";
        [SerializeField] string badgeDisconnected = "○ 大幣がつながっていません（Enter・クリックで進めます）";

        [Header("参道マップ")]
        [Tooltip("マスを、進む順に（前をクリアすると次が解放）")]
        [SerializeField] List<MenuStageNode> stageNodes = new List<MenuStageNode>();
        [Tooltip("ON なら、はじめからどのステージも選べる（ゲームショウ用）。OFF なら、前をクリアすると次が解放")]
        [SerializeField] bool unlockAllStages = true;
        [SerializeField] Text infoTitle;
        [SerializeField] Text infoBody;
        [SerializeField] Text infoStats;
        [SerializeField] Text infoButton;
        [SerializeField] Image infoButtonImage;

        [Header("設定")]
        [SerializeField] Text linkValue;
        [SerializeField] Text frontValue;
        [SerializeField] Text volumeValue;
        [SerializeField] Image[] volumeBars = new Image[0];
        [SerializeField] Text settingsHelp;
        [SerializeField] Image statusDot;
        [SerializeField] Text statusLink;
        [SerializeField] RectTransform aimMarker;
        [Tooltip("向きの印が、左右 60° で動く幅（ピクセル）")]
        [SerializeField] float aimRange = 220f;
        [SerializeField] Text aimText;
        [Tooltip("色ボタン1〜5 と正面ボタンの印（この順）")]
        [SerializeField] ButtonLamp[] buttonLamps = new ButtonLamp[0];
        [SerializeField] Text statusSource;
        [SerializeField] Image frontGaugeFill;
        [Tooltip("正面ボタン長押しのゲージが、いっぱいのときの幅（ピクセル）")]
        [SerializeField] float frontGaugeWidth = 440f;
        [SerializeField] Text calibrationText;

        [Header("演出")]
        [Tooltip("前奏の長さ（秒）。Menu_Intro の長さに合わせる。決めるボタン・振りで飛ばせる")]
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

        [Header("デバッグ")]
        [SerializeField] bool logEvents = true;

        // ---------- 色（コードから書きかえる所だけ） ----------
        static readonly Color Cream = Hex("#fff3d6");
        static readonly Color Gold = Hex("#f0c25a");
        static readonly Color Vermilion = Hex("#b8302a");

        const float FadeSeconds = 0.55f;
        const float FlashSeconds = 0.8f;
        const float MinPhaseSeconds = 0.45f;
        const float ToastSeconds = 2.5f;
        // 前奏のはじめの背景（ここから Page_Intro の寄りへ、ゆっくり引いていく）
        const float IntroStartZoom = 2.1f;
        const float IntroStartOffsetY = -48f;

        static bool s_introSeen;

        readonly Dictionary<MenuPageId, MenuPage> _pages = new Dictionary<MenuPageId, MenuPage>();
        IControllerRawSource _raw;
        Esp32RawSource _esp32;
        AudioSource _sfx, _bgm;
        bool _volumeAdjusting;
        double _toastUntil;

        MenuPageId _page;
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
        public string SelectedLabel => Current.items.Count > 0 ? Current.items[_selected].name : null;

        MenuPage Current => _pages[_page];
        bool ControllerReady => _raw != null && _raw.IsConnected;

        void Awake()
        {
            // 動き（Animator・星・花びら）はふつうの時間で進むので、止まったシーンからもどってきても動くようにする
            Time.timeScale = 1f;

            _raw = rawSourceSource as IControllerRawSource;
            if (rawSourceSource != null && _raw == null)
                Debug.LogWarning("[MainMenu] rawSourceSource が IControllerRawSource を実装していません", this);
            if (_raw == null) _raw = FindRawSource();
            if (_raw == null)
                Debug.LogWarning("[MainMenu] 大幣の入力がありません（キーボードとマウスだけで動きます）", this);
            _esp32 = _raw as Esp32RawSource;

            if (controller == null) controller = FindAnyObjectByType<ConecteController>();

            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
            _bgm = gameObject.AddComponent<AudioSource>();
            _bgm.playOnAwake = false;
            _bgm.loop = true;

            EnsureEventSystem();
            CollectPages();
            _prevMouse = Input.mousePosition;
            if (toast != null) toast.enabled = false;
            if (flash != null) flash.enabled = false;

            if (s_introSeen && !replayIntroEveryTime)
            {
                backdrop.SetCamera(1f, 0f, true);
                backdrop.SetDim(1f, true);
                Show(MenuPageId.Logo, true);
            }
            else
            {
                backdrop.SetCamera(IntroStartZoom, IntroStartOffsetY, true);
                backdrop.SetDim(1f, true);
                Show(MenuPageId.Intro, true);
            }
            s_introSeen = true;
        }

        // シーンの画面を名前で引けるようにし、項目のクリックをつなぐ
        void CollectPages()
        {
            foreach (MenuPage page in pages)
            {
                if (page == null) continue;
                if (_pages.ContainsKey(page.id))
                {
                    Debug.LogWarning($"[MainMenu] 画面 {page.id} が2つあります（{page.name}）。あとのほうは使いません", page);
                    continue;
                }
                _pages[page.id] = page;

                for (int i = 0; i < page.items.Count; i++)
                {
                    MenuEntry entry = page.items[i];
                    if (entry == null)
                    {
                        Debug.LogWarning($"[MainMenu] {page.name} の items[{i}] が空です", page);
                        continue;
                    }
                    Button button = entry.GetComponent<Button>();
                    if (button == null) continue;
                    MenuPageId id = page.id;
                    int index = i;
                    button.onClick.AddListener(() =>
                    {
                        if (_loading || _flashStart >= 0f || _page != id) return;
                        Activate(index);
                    });
                }
            }
            foreach (MenuPageId id in Enum.GetValues(typeof(MenuPageId)))
                if (!_pages.ContainsKey(id))
                    Debug.LogError($"[MainMenu] 画面 {id} が pages に入っていません", this);
        }

        void Update()
        {
            // ConecteController が COM ポートを探す間などでフレームが数秒止まっても、演出が飛ばないように 1 フレームは 0.1 秒まで
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _time += dt;
            double now = Time.realtimeSinceStartupAsDouble;

            UpdateFades(dt);
            UpdateFlash();
            if (toast != null) toast.enabled = now < _toastUntil;

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
            // 押した瞬間を取りこぼさないよう、毎フレーム見ておく
            _colorPressed = ColorPressedThisFrame();

            if (_flashStart >= 0f) return;

            if (Current.cinematic)
            {
                decide |= Input.GetMouseButtonDown(0);
                UpdateCinematic(decide);
                return;
            }

            if (_page == MenuPageId.Settings) RefreshSettings(now);

            // 音量の調整中は、傾け・←→で音量を変え、決める・もどるで調整を終える
            if (_volumeAdjusting)
            {
                if (move != 0) MasterVolume.Add(move * MasterVolume.Step);
                if (decide || back || Input.GetMouseButtonDown(0)) SetVolumeAdjusting(false);
                return;
            }

            UpdateMouseHover();
            if (move != 0) Select(_selected + move);
            if (back && Current.canGoBack) Show(Current.back);
            else if (decide) Activate(_selected);
        }

        // ---------- 項目から呼ぶもの（MenuEntry の onDecide に入れる） ----------

        public void OpenTop() => Show(MenuPageId.Top);
        public void OpenStageMap() => Show(MenuPageId.StageMap);
        public void OpenSettings() => Show(MenuPageId.Settings);
        public void OpenQuitConfirm() => Show(MenuPageId.QuitConfirm);

        // 今の画面の「もどる」先へ
        public void Back()
        {
            if (Current.canGoBack) Show(Current.back);
        }

        public void ChooseStage(MenuStageNode node)
        {
            if (node == null) return;
            if (!IsUnlocked(node))
            {
                ShowToast("前のお参りをすませると、参拝できます", Time.realtimeSinceStartupAsDouble);
                return;
            }
            StartStage(node.stage);
        }

        public void BeginVolumeAdjust() => SetVolumeAdjusting(true);

        public void CalibrateFromMenu()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (TryCalibrate(now, out string refusal)) return;
            _calibrationRefusal = refusal;
            ShowToast($"正面を合わせられませんでした（{refusal}）", now);
        }

        public void Reconnect()
        {
            if (controller == null)
            {
                ShowToast("ConecteController がシーンにありません", Time.realtimeSinceStartupAsDouble);
                return;
            }
            controller.BeginReconnect();
            ShowToast("COM ポートを探しなおしています…", Time.realtimeSinceStartupAsDouble);
        }

        public void Quit()
        {
            Log("終了");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------- 演出の画面 ----------

        void UpdateCinematic(bool decide)
        {
            float elapsed = _time - _enteredAt;
            bool accept = decide && elapsed >= MinPhaseSeconds;

            switch (_page)
            {
                case MenuPageId.Intro:
                    if (accept || elapsed >= introSeconds) AfterIntro();
                    break;
                case MenuPageId.SwingPrompt:
                    // 振っている間にコントローラーが抜けたら、キーとクリックでも進める案内に変える
                    if (swingBadge != null) swingBadge.text = ControllerReady ? badgeConnected : badgeDisconnected;
                    if (accept)
                    {
                        Play(omamoriClip);
                        Show(MenuPageId.Omamori);
                    }
                    break;
                case MenuPageId.Omamori:
                    if (elapsed >= omamoriSeconds) StartFlash(() => Show(MenuPageId.Logo, true));
                    break;
                case MenuPageId.Logo:
                    // 「ボタンを押して開始」なので、色ボタンを押しても始める
                    if (accept || (elapsed >= MinPhaseSeconds && _colorPressed))
                    {
                        Play(decideClip);
                        Show(MenuPageId.Top);
                    }
                    break;
            }
        }

        void AfterIntro()
        {
            if (introClip != null && _sfx.clip == introClip) _sfx.Stop();
            Show(ControllerReady ? MenuPageId.SwingPrompt : MenuPageId.Logo);
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
                if (flash != null) flash.enabled = false;
                return;
            }
            float t = (_time - _flashStart) / FlashSeconds;
            if (t >= 0.5f && _flashPeak != null)
            {
                Action a = _flashPeak;
                _flashPeak = null;
                a();
            }
            if (flash != null)
            {
                flash.enabled = true;
                Color c = flash.color;
                c.a = t < 0.5f ? t * 2f : Mathf.Clamp01((1f - t) * 2f);
                flash.color = c;
            }
            if (t >= 1f) _flashStart = -1f;
        }

        void UpdateFades(float dt)
        {
            foreach (KeyValuePair<MenuPageId, MenuPage> pair in _pages)
            {
                bool on = pair.Key == _page;
                CanvasGroup g = pair.Value.Group;
                g.alpha = Mathf.MoveTowards(g.alpha, on ? 1f : 0f, dt / FadeSeconds);
                bool visible = on || g.alpha > 0f;
                if (pair.Value.gameObject.activeSelf != visible) pair.Value.gameObject.SetActive(visible);
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

            int count = Current.items.Count;
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

            List<MenuEntry> items = Current.items;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null && RectTransformUtility.RectangleContainsScreenPoint(items[i].Rect, mouse, null))
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

        // 色ボタンのどれかが、このフレームで押されたか（押しっぱなしは数えない）
        readonly bool[] _prevColorHeld = new bool[InputStateMachine.ColorCount];
        bool _colorPressed;
        bool ColorPressedThisFrame()
        {
            if (_raw == null) return false;
            bool pressed = false;
            for (int i = 0; i < _prevColorHeld.Length; i++)
            {
                bool held = _raw.IsColorHeld(i);
                if (held && !_prevColorHeld[i]) pressed = true;
                _prevColorHeld[i] = held;
            }
            return pressed;
        }

        bool AnyColorHeld()
        {
            for (int i = 0; i < InputStateMachine.ColorCount; i++)
                if (_raw.IsColorHeld(i)) return true;
            return false;
        }

        // ---------- 画面と項目 ----------

        void Show(MenuPageId page, bool instant = false)
        {
            _page = page;
            _enteredAt = _time;
            MenuPage view = Current;

            if (page == MenuPageId.Logo && logoPrompt != null)
                logoPrompt.text = ControllerReady ? promptWithOnusa : promptWithoutOnusa;
            if (page == MenuPageId.StageMap) RefreshStageMap();

            backdrop.SetCamera(view.zoom, view.offsetY, instant && page != MenuPageId.Intro);
            backdrop.SetDim(view.dim, false);
            foreach (KeyValuePair<MenuPageId, MenuPage> pair in _pages)
            {
                bool on = pair.Key == page;
                if (instant)
                {
                    pair.Value.Group.alpha = on ? 1f : 0f;
                    pair.Value.gameObject.SetActive(on);
                }
                pair.Value.Group.interactable = on;
                pair.Value.Group.blocksRaycasts = on;
            }
            view.gameObject.SetActive(true);
            view.Restart();
            if (hint != null) hint.enabled = !view.cinematic;

            if (page == MenuPageId.Intro && introClip != null) { _sfx.clip = introClip; _sfx.Play(); }
            if (page == MenuPageId.Logo && menuBgm != null && !_bgm.isPlaying) { _bgm.clip = menuBgm; _bgm.Play(); }

            Select(view.defaultIndex);
            // 画面が変わったら、今の向きを新しい基準にする（前の画面で傾けていた分で動かないように）
            _hasTiltAnchor = false;
            _volumeAdjusting = false;
            Log($"画面: {page}");
        }

        void Select(int index)
        {
            List<MenuEntry> items = Current.items;
            if (items.Count == 0) return;
            _selected = Mathf.Clamp(index, 0, items.Count - 1);
            for (int i = 0; i < items.Count; i++)
                if (items[i] != null) items[i].SetSelected(i == _selected);

            // 参道マップでは、選んでいるマスの説明を板に出す（「もどる」を選んでいるあいだは、前のマスのまま）
            if (_page == MenuPageId.StageMap && items[_selected] != null)
            {
                MenuStageNode node = items[_selected].GetComponent<MenuStageNode>();
                if (node != null) RefreshStageInfo(node);
            }
        }

        void Activate(int index)
        {
            List<MenuEntry> items = Current.items;
            if (index < 0 || index >= items.Count || items[index] == null) return;
            Select(index);
            Log($"決定: {items[index].name}");
            Play(decideClip);
            items[index].Decide();
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

        bool IsUnlocked(MenuStageNode node)
        {
            if (unlockAllStages) return true;
            int i = stageNodes.IndexOf(node);
            if (i <= 0) return true;
            return SaveSlots.Current != null && SaveSlots.Current.IsCleared(stageNodes[i - 1].stage.id);
        }

        // 参道マップを開くたびに、マスの色と、はじめに選ぶマスを決める
        void RefreshStageMap()
        {
            MenuPage page = Current;
            // はじめに選ぶのは、解放されていて、まだクリアしていない最初のマス（ぜんぶクリアなら、解放されている最後のマス）
            MenuStageNode first = null, lastUnlocked = null;
            foreach (MenuStageNode node in stageNodes)
            {
                if (node == null) continue;
                bool unlocked = IsUnlocked(node);
                bool cleared = SaveSlots.Current != null && SaveSlots.Current.IsCleared(node.stage.id);
                node.SetState(unlocked, cleared);
                if (!unlocked) continue;
                lastUnlocked = node;
                if (first == null && !cleared) first = node;
            }
            MenuStageNode start = first != null ? first : lastUnlocked;
            if (start != null)
            {
                int index = page.items.IndexOf(start.GetComponent<MenuEntry>());
                if (index >= 0) page.defaultIndex = index;
            }
        }

        void RefreshStageInfo(MenuStageNode node)
        {
            StageInfo s = node.stage;
            bool unlocked = IsUnlocked(node);
            StageRecord record = SaveSlots.Current?.Find(s.id);
            if (infoTitle != null) infoTitle.text = $"{s.label}　{s.title}";
            if (infoBody != null) infoBody.text = unlocked ? s.description : "前のお参りをすませると、参拝できます。";
            if (infoStats != null) infoStats.text = record != null && record.bestScore > 0 ? $"最高の縁　{record.bestScore:N0}" : "最高の縁　―";
            if (infoButton != null) infoButton.text = unlocked ? (ControllerReady ? "振って参拝" : "決めて参拝") : "まだ参拝できません";
            if (infoButtonImage != null) infoButtonImage.color = unlocked ? Vermilion : Hex("#3a3040");
        }

        void ShowToast(string message, double now)
        {
            if (toast != null) toast.text = message;
            _toastUntil = now + ToastSeconds;
        }

        void Play(AudioClip clip)
        {
            if (clip != null) _sfx.PlayOneShot(clip);
        }

        // ---------- 設定 ----------

        void RefreshSettings(double now)
        {
            // 大幣の接続
            bool linked = controller != null ? controller.isConnected : ControllerReady;
            string link;
            if (controller == null) link = ControllerReady ? "つながっています" : "キーボードで操作中";
            else if (controller.isConnected) link = $"つながっています（{controller.portName}）";
            else link = controller.isScanning ? "さがしています…" : "つながっていません";
            linkValue.text = linked ? "○ つながっている" : (controller != null && controller.isScanning ? "… さがし中" : "× つながっていない");
            linkValue.color = linked ? Hex("#9ff0c8") : Hex("#ffb4a0");
            statusLink.text = link;
            statusDot.color = linked ? Hex("#5fe0a0") : Hex("#e05a4a");

            // 正面合わせ
            frontValue.text = ControllerCalibration.HasYawOffset ? "済み" : "まだ";
            frontValue.color = ControllerCalibration.HasYawOffset ? Hex("#9ff0c8") : Hex("#ffe08a");

            // 向き（正面から何度）。左右 60° までをゲージに出す
            if (_raw == null)
            {
                aimText.text = "大幣がありません";
                aimMarker.gameObject.SetActive(false);
            }
            else if (!ControllerCalibration.HasYawOffset)
            {
                aimText.text = "正面を合わせると出ます";
                aimMarker.gameObject.SetActive(false);
            }
            else
            {
                float rel = Mathf.DeltaAngle(ControllerCalibration.YawOffset, _raw.Yaw);
                aimMarker.gameObject.SetActive(true);
                aimMarker.anchoredPosition = new Vector2(Mathf.Clamp(rel / 60f, -1f, 1f) * aimRange, aimMarker.anchoredPosition.y);
                aimText.text = Mathf.Abs(rel) < 3f ? "まっすぐ正面" : (rel > 0f ? $"右へ {rel:0}°" : $"左へ {-rel:0}°");
            }

            // ボタン（1〜5 と正面）
            for (int i = 0; i < buttonLamps.Length; i++)
            {
                ButtonLamp lamp = buttonLamps[i];
                if (lamp == null || lamp.image == null) continue;
                bool held = _raw != null && (i < InputStateMachine.ColorCount ? _raw.IsColorHeld(i) : _raw.IsFrontHeld);
                lamp.image.color = held ? lamp.held : lamp.idle;
                lamp.image.rectTransform.localScale = Vector3.one * (held ? lamp.heldScale : 1f);
            }
            statusSource.text = _esp32 != null
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
            calibrationText.text = calibration;
            frontGaugeFill.rectTransform.sizeDelta = new Vector2(frontGaugeWidth * progress, frontGaugeFill.rectTransform.sizeDelta.y);

            // 音量
            int volume = Mathf.RoundToInt(MasterVolume.Value * 10f);
            for (int i = 0; i < volumeBars.Length; i++)
                volumeBars[i].color = i < volume ? (_volumeAdjusting ? Gold : Cream) : Hex("#000000", 0.35f);
            volumeValue.text = _volumeAdjusting ? $"◀ {volume * 10}% ▶" : $"{volume * 10}%";
            volumeValue.color = _volumeAdjusting ? Gold : Cream;

            List<MenuEntry> items = Current.items;
            settingsHelp.text = _volumeAdjusting
                ? "大幣を左右に傾けると（←→キーでも）音量が変わります。\n決めると確定です。"
                : (_selected >= 0 && _selected < items.Count && items[_selected] != null ? items[_selected].help : "");
        }

        void SetVolumeAdjusting(bool on)
        {
            _volumeAdjusting = on;
            _hasTiltAnchor = false;
            Log(on ? "音量の調整を始める" : $"音量を {Mathf.RoundToInt(MasterVolume.Value * 100f)}% に決めた");
        }

        // ---------- 部品 ----------

        // 16進の色（"#c0362c"）
        static Color Hex(string hex, float alpha = 1f)
        {
            ColorUtility.TryParseHtmlString(hex, out Color c);
            c.a = alpha;
            return c;
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
