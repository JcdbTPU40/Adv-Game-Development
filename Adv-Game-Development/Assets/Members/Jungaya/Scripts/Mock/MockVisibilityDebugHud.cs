using UnityEngine;

namespace Toufuku.Rescue.Mock
{
    /*
        視認性モック（#44）のテストを進めやすくするためのクラス。プレイ中にキーで条件を切りかえて、今の状態を画面のすみに出す

        目的は「見てもらっている人に見せながら、その場で条件を変えられる」こと
        人数・お祭り・輪郭の表し方・ゲージの距離スケールをプレイ中に切りかえて、
        どの条件ならぱっと見て見分けられるかをくらべる。静止画で見せたいときは一時停止する

        キーの割り当ては全部 Inspector から変えられる（ほかのテスト用スクリプトとキーがかぶったとき用）

        ※ 検証用の使い捨て。Mock/ フォルダごと消せる
    */
    public class MockVisibilityDebugHud : MonoBehaviour
    {
        [Header("参照（未設定ならシーンから自動取得）")]
        [SerializeField] private MockCrowdDirector director;
        [SerializeField] private MockGaugeHud gaugeHud;

        [Header("キー割り当て：体数")]
        [Tooltip("目標体数を8体に固定。")]
        [SerializeField] private KeyCode key8 = KeyCode.Alpha1;
        [Tooltip("目標体数を12体に固定。")]
        [SerializeField] private KeyCode key12 = KeyCode.Alpha2;
        [Tooltip("目標体数を15体に固定。")]
        [SerializeField] private KeyCode key15 = KeyCode.Alpha3;
        [Tooltip("固定を解除し、ランク＋祭事の計算に戻す。")]
        [SerializeField] private KeyCode keyClearOverride = KeyCode.Alpha0;

        [Header("キー割り当て：条件切替")]
        [Tooltip("祭事 ON/OFF。")]
        [SerializeField] private KeyCode keyFestival = KeyCode.F;
        [Tooltip("一時停止（静止画で識別テストする用）。")]
        [SerializeField] private KeyCode keyPause = KeyCode.Space;
        [Tooltip("神社ランクを C→B→A→S と巡回。")]
        [SerializeField] private KeyCode keyRank = KeyCode.R;
        [Tooltip("輪郭の表現方式（インバートハル ⇄ Emission）。")]
        [SerializeField] private KeyCode keyOutlineMode = KeyCode.O;
        [Tooltip("輪郭の太さモード（ワールド固定 ⇄ 画面上一定）。")]
        [SerializeField] private KeyCode keyWidthMode = KeyCode.W;
        [Tooltip("輪郭を細くする。実測しながら適正値を探す用。")]
        [SerializeField] private KeyCode keyWidthDown = KeyCode.LeftBracket;
        [Tooltip("輪郭を太くする。実測しながら適正値を探す用。")]
        [SerializeField] private KeyCode keyWidthUp = KeyCode.RightBracket;
        [Tooltip("ゲージ幅の距離スケール ON/OFF。")]
        [SerializeField] private KeyCode keyGaugeScale = KeyCode.G;
        [Tooltip("定位置を抽選し直す。")]
        [SerializeField] private KeyCode keyReshuffle = KeyCode.L;
        [Tooltip("ゲージ凍結 ON/OFF。ON の間は誰も退場せず、体数が目標どおりに揃う。")]
        [SerializeField] private KeyCode keyFreezeGauges = KeyCode.H;

        [Header("体数プリセット")]
        [SerializeField] private int preset8 = 8;
        [SerializeField] private int preset12 = 12;
        [SerializeField] private int preset15 = 15;

        [Header("表示")]
        [Tooltip("画面隅の状態表示を出すか。")]
        [SerializeField] private bool showStatus = true;
        [Tooltip("操作ヘルプも一緒に出すか。被験者に見せるときは OFF 推奨。")]
        [SerializeField] private bool showHelp = true;
        [Tooltip("表示の基準位置（画面左上からのオフセット）。")]
        [SerializeField] private Vector2 statusOffset = new Vector2(10f, 10f);
        [SerializeField] private int fontSize = 13;
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private Color panelColor = new Color(0f, 0f, 0f, 0.55f);

        private bool _paused;
        private float _timeScaleBeforePause = 1f;
        private GUIStyle _style;
        private Texture2D _tex;

        private void Awake()
        {
            _tex = Texture2D.whiteTexture;
            if (director == null) director = FindFirstObjectByType<MockCrowdDirector>();
            if (gaugeHud == null) gaugeHud = FindFirstObjectByType<MockGaugeHud>();
        }

        private void OnDisable()
        {
            // 一時停止したままシーンをぬけると、次に再生したときに止まって見えるので、必ずもどす
            if (_paused)
            {
                Time.timeScale = _timeScaleBeforePause;
                _paused = false;
            }
        }

        private void Update()
        {
            if (director == null) return;

            // 人数のプリセット（Time.timeScale=0 でも Update は動くので、一時停止中でも効く）
            if (Input.GetKeyDown(key8)) director.SetOverrideTarget(preset8);
            if (Input.GetKeyDown(key12)) director.SetOverrideTarget(preset12);
            if (Input.GetKeyDown(key15)) director.SetOverrideTarget(preset15);
            if (Input.GetKeyDown(keyClearOverride)) director.ClearOverrideTarget();

            // 条件の切りかえ
            if (Input.GetKeyDown(keyFestival)) director.ToggleFestival();
            if (Input.GetKeyDown(keyRank)) director.CycleRank();
            if (Input.GetKeyDown(keyOutlineMode)) director.ToggleOutlineMode();
            if (Input.GetKeyDown(keyWidthMode)) director.ToggleOutlineWidthMode();
            if (Input.GetKeyDown(keyWidthDown)) director.StepOutlineWidth(-1);
            if (Input.GetKeyDown(keyWidthUp)) director.StepOutlineWidth(+1);
            if (Input.GetKeyDown(keyReshuffle)) director.ReshuffleSlots();
            if (Input.GetKeyDown(keyFreezeGauges)) director.ToggleFreezeGauges();

            if (Input.GetKeyDown(keyGaugeScale) && gaugeHud != null)
                gaugeHud.ToggleScaleWithDistance();

            if (Input.GetKeyDown(keyPause)) TogglePause();
        }

        private void TogglePause()
        {
            if (_paused)
            {
                Time.timeScale = _timeScaleBeforePause;
                _paused = false;
            }
            else
            {
                _timeScaleBeforePause = Time.timeScale > 0f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
                _paused = true;
            }
        }

        private void OnGUI()
        {
            if (!showStatus || director == null) return;
            if (Event.current.type != EventType.Repaint) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = fontSize,
                    alignment = TextAnchor.UpperLeft,
                    richText = false,
                };
            }
            _style.fontSize = fontSize;
            _style.normal.textColor = textColor;

            string status = BuildStatusText();
            string help = showHelp ? BuildHelpText() : string.Empty;
            string body = showHelp ? status + "\n\n" + help : status;

            var content = new GUIContent(body);
            Vector2 size = _style.CalcSize(content);
            var panel = new Rect(statusOffset.x, statusOffset.y, size.x + 16f, size.y + 12f);

            GUI.color = panelColor;
            GUI.DrawTexture(panel, _tex);

            GUI.color = Color.white;
            GUI.Label(new Rect(panel.x + 8f, panel.y + 6f, size.x, size.y), content, _style);
        }

        private string BuildStatusText()
        {
            string targetSource = director.OverrideTargetCount >= 0
                ? "手動固定"
                : $"ランク{director.Rank}＋{(director.FestivalMode ? "祭事" : "通常")}";

            string outline = director.OutlineMode == MockCustomerOutline.OutlineMode.InvertedHull
                ? "インバートハル"
                : "Emission";

            string width = director.OutlineWidthMode == MockCustomerOutline.WidthMode.ScreenConstant
                ? "画面一定"
                : "ワールド固定";

            string gauge = gaugeHud != null
                ? (gaugeHud.ScaleWithDistance ? "距離スケール" : "固定幅")
                : "—";

            // 「定位置に立っている数」は、見分けるテストで実際にならんでいる人数。補充している間は目標より少なくなる
            int settled = 0;
            for (int i = 0; i < director.Members.Count; i++)
            {
                MockCrowdDirector.Member m = director.Members[i];
                if (m == null || m.Go == null) continue;
                if (m.Walker == null || !m.Walker.IsWalking) settled++;
            }

            return
                $"体数 {director.AliveCount}/{director.TargetCount}　定位置 {settled}（歩行中 {director.AliveCount - settled}／補充待ち {director.PendingCount}）\n" +
                $"内訳 {targetSource}　祭事 {(director.FestivalMode ? "ON" : "OFF")}　ランク {director.Rank}\n" +
                $"輪郭 {outline}／太さ {width} {director.OutlineWidth:0.000}　ゲージ {gauge}\n" +
                $"補充テンポ スポーン遅延 {director.RespawnDelay:0.0}s ／ 歩行 {director.WalkDuration:0.0}s" +
                (director.FreezeGauges ? "\n― ゲージ凍結中（退場なし）―" : string.Empty) +
                (_paused ? "\n― 一時停止中 ―" : string.Empty);
        }

        private string BuildHelpText()
        {
            return
                $"[{key8}/{key12}/{key15}] 体数 {preset8}/{preset12}/{preset15}　[{keyClearOverride}] 固定解除\n" +
                $"[{keyFestival}] 祭事　[{keyRank}] ランク　[{keyFreezeGauges}] ゲージ凍結　[{keyPause}] 一時停止\n" +
                $"[{keyOutlineMode}] 輪郭方式　[{keyWidthMode}] 太さモード　[{keyWidthDown}/{keyWidthUp}] 太さ −/＋\n" +
                $"[{keyGaugeScale}] ゲージ幅　[{keyReshuffle}] 配置替え";
        }
    }
}
