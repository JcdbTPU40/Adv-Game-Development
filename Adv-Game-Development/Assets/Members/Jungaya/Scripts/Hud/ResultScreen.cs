using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Toufuku.Hud
{
    /*
        リザルト画面（#61 / 企画書 v8 7章「称号は最高ランクで出す」、13章「観戦体験の設計」）

        ・GameSession がスコアを固定した（IsFinished）ら出す。3:00 のあと受理済みの弾を待っている間はまだ出さない
        ・出すもの
            縁: 固定した値（3:00 をまたいだ弾の救済得点まで入った値）
            神社の称号: プレイ中の最高ランク（最終ランクではない。終盤の負荷ウェーブで評価が下がっても達成の記録を消さない）
            今日のベスト: 今回で更新したら大きく祝う
            最大の福の連なり
        ・見た目は ResultScreenView プレハブ（Canvas とオブジェクト）と AnimationClip。位置・文字・色・動きは、プレハブを開いて変える。
          このスクリプトは、出す・消すと、値をわたすだけ
            画面は、子に ResultScreenView が置いてあればそれを使う。なければ viewPrefab、それも空なら Resources の ResultScreenView を出す
            （だから、シーンの空の GameObject にこのスクリプトを付けるだけで動く）
            出した画面の名前は ResultScreenCanvas にする（小林さんのスクリプトが、この名前で中の部品を探している）
        ・ボタン「タイトルへ」でタイトルのシーンへもどる（ゲームショウで次の人に交代するため）
            もどる先は、シーンに ExhibitAutoReturn があればそちらの titleSceneName（自動復帰と同じ所）。なければ titleSceneName
            どちらも開けないとき（テスト用のシーンなど）は、前と同じ「もう一度」になる
        ・showLastResult を ON にすると、リザルト専用のシーン（ResultScene）用になる
            開いたらすぐ LastSessionResult（GameScene で ResultSceneTransition が写した値）を出す
    */
    public class ResultScreen : MonoBehaviour
    {
        // 今リザルトを出しているか（テスト用の SessionHud が、かんたんなリザルトを重ねないために見る）
        public static bool IsShowing { get; private set; }

        const string ViewName = "ResultScreenCanvas";
        const string ViewResource = "ResultScreenView";

        [Header("画面（未設定なら Resources の ResultScreenView。子に置いてあれば、それを使う）")]
        [SerializeField] ResultScreenView viewPrefab;
        [Tooltip("入れてあれば、画面の文字をぜんぶこのフォントにする（未設定なら、プレハブのフォントのまま）")]
        [SerializeField] Font font;

        [Header("リザルト専用のシーン（ResultScene）で使うとき")]
        [Tooltip("ON なら開いたらすぐ LastSessionResult を出す（GameSession を見ない）")]
        [SerializeField] bool showLastResult;
        [Tooltip("タイトルへもどれないときに「もう一度」で開くシーン名（showLastResult が ON のとき）")]
        [SerializeField] string retrySceneName = "GameScene";

        [Header("ボタン「タイトルへ」")]
        [Tooltip("もどる先のシーン名。シーンに ExhibitAutoReturn があれば、そちらの titleSceneName を使う")]
        [SerializeField] string titleSceneName = "Start";

        ResultScreenView _view;
        bool _shown;

        // 確認用: 表示した値
        public int ShownEn { get; private set; }
        public ShrineRank ShownTitle { get; private set; }
        public bool ShownNewRecord { get; private set; }
        public string BestText => _view != null ? _view.BestText : null;

        void Awake()
        {
            DailyBestRecorder.Ensure();
            CreateView();
        }

        void CreateView()
        {
            _view = GetComponentInChildren<ResultScreenView>(true);
            if (_view == null)
            {
                ResultScreenView prefab = viewPrefab != null ? viewPrefab : Resources.Load<ResultScreenView>(ViewResource);
                if (prefab == null)
                {
                    Debug.LogError($"[ResultScreen] 画面のプレハブがありません（Resources/{ViewResource}）", this);
                    return;
                }
                _view = Instantiate(prefab, transform);
            }
            _view.name = ViewName;
            if (font != null)
                foreach (Text text in _view.GetComponentsInChildren<Text>(true)) text.font = font;
            _view.NextPressed += OnNext;
            _view.gameObject.SetActive(false);
        }

        void OnDisable()
        {
            Hide();
        }

        void Update()
        {
            if (_view == null) return;

            if (showLastResult)
            {
                if (!_shown) Show();
                return;
            }

            GameSession session = GameSession.Instance;
            bool show = session != null && session.IsFinished;
            if (show && !_shown) Show();
            else if (!show && _shown) Hide();
        }

        void Show()
        {
            EnsureEventSystem();
            // 先に出してから値を入れる（出したときに Animator が頭から始まる）
            _view.gameObject.SetActive(true);
            Populate();
            _shown = true;
            IsShowing = true;
        }

        void Hide()
        {
            if (_view != null) _view.gameObject.SetActive(false);
            _shown = false;
            IsShowing = false;
        }

        void Populate()
        {
            if (showLastResult)
            {
                Fill(LastSessionResult.En, LastSessionResult.MaxRank, LastSessionResult.NewRecord,
                    LastSessionResult.TodayBest, LastSessionResult.MaxCombo);
                return;
            }

            ScoreManager sm = ScoreManager.Instance;
            ShrineRating rating = ShrineRating.Instance;
            DailyBestRecorder best = DailyBestRecorder.Instance;

            Fill(sm != null ? sm.En : 0, rating != null ? rating.MaxRank : ShrineRank.C, best != null && best.LastPlayWasNewRecord,
                best != null ? best.TodayBest : 0, sm != null ? sm.MaxCombo : 0);
        }

        void Fill(int en, ShrineRank title, bool newRecord, int todayBest, int maxCombo)
        {
            ShownEn = en;
            ShownTitle = title;
            ShownNewRecord = newRecord;
            _view.Fill(en, title, newRecord, todayBest, maxCombo);
        }

        // ボタン: タイトルへ。もどれなければ、もう一度
        void OnNext()
        {
            ExhibitAutoReturn autoReturn = FindAnyObjectByType<ExhibitAutoReturn>();
            if (autoReturn != null && autoReturn.ReturnToTitle("リザルトのボタン")) return;

            if (!string.IsNullOrEmpty(titleSceneName) && Application.CanStreamedLevelBeLoaded(titleSceneName))
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene(titleSceneName, LoadSceneMode.Single);
                return;
            }

            Debug.LogWarning($"[ResultScreen] タイトルのシーン「{titleSceneName}」が Build Settings にないので、もう一度にします", this);
            Retry();
        }

        void Retry()
        {
            if (showLastResult)
            {
                if (string.IsNullOrEmpty(retrySceneName) || !Application.CanStreamedLevelBeLoaded(retrySceneName))
                {
                    Debug.LogWarning($"[ResultScreen] 「もう一度」のシーン「{retrySceneName}」が Build Settings にありません", this);
                    return;
                }
                SceneManager.LoadScene(retrySceneName, LoadSceneMode.Single);
                return;
            }

            if (GameSession.Instance != null) GameSession.Instance.Retry();
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            if (FindAnyObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem (ResultScreen)", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
    }
}
