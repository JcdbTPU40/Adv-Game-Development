using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Toufuku.Hud
{
    /*
        リザルト画面の見た目（Resources/ResultScreenView プレハブのいちばん上に付ける）

        ・位置・文字・色・画像は、プレハブを開いてふだんどおり変える。ここは、数字と称号を書き入れるだけ
        ・出てくる順番と動きは AnimationClip（Animations/Result）
            Result_Show: 板が出る → 縁が数え上がる → 称号が判子のように押される → 今日のベスト → 福の連なり → ボタン
            Result_NewRecord: 今日のベストを更新したときの札が、光ってゆっくり息をする
            Result_Button: ボタンの光
        ・縁の数え上がりは、Result_Show が enProgress を 0 → 1 に動かす（数字そのものはクリップにできないので、ここで書く）
        ・小林さんの ReturnToStartAfterSession / ShrineTitleSdfFont が名前で探しているので、
          Panel/RetryButton・Panel/TitleLabel/Text・Panel/TitleValue/Text の名前と親子は変えない
    */
    public class ResultScreenView : MonoBehaviour
    {
        [Header("値を書く所")]
        [SerializeField] Text enValue;
        [SerializeField] Text titleValue;
        [Tooltip("今日のベストを更新しなかったときに出すもの")]
        [SerializeField] GameObject bestNormal;
        [SerializeField] Text bestNormalText;
        [Tooltip("今日のベストを更新したときに出すもの")]
        [SerializeField] GameObject bestNew;
        [SerializeField] Text bestNewText;
        [SerializeField] Text chainText;

        [Header("文言（{0} に数字が入る）")]
        [SerializeField] string bestNormalFormat = "今日のベスト  {0}";
        [SerializeField] string bestNewFormat = "★ 今日のベスト更新！ {0} ★";
        [SerializeField] string chainFormat = "最大の福の連なり  {0}";

        [Header("ボタン")]
        [SerializeField] Button nextButton;
        [Tooltip("ボタンが出きる（alpha がほぼ 1）までは、押しても受けない")]
        [SerializeField] CanvasGroup nextButtonGroup;

        [Header("演出（Result_Show が動かす）")]
        [Tooltip("縁の数え上がり。0 で 0、1 で本当の値")]
        [Range(0f, 1f)] public float enProgress = 1f;

        // ボタンが押された
        public event Action NextPressed;

        int _en;
        int _shownEn = -1;
        bool _filled;
        TextMeshProUGUI _titleSdfText;

        // 確認用
        public string BestText { get; private set; }

        void Awake()
        {
            if (nextButton != null) nextButton.onClick.AddListener(OnNextClicked);
        }

        public void Fill(int en, ShrineRank title, bool newRecord, int todayBest, int maxCombo)
        {
            _en = en;
            _shownEn = -1;
            _filled = true;

            if (titleValue != null)
            {
                SetTitleRank(RankLabel(title), HudUi.RankColor(title));
            }

            BestText = string.Format(newRecord ? bestNewFormat : bestNormalFormat, HudUi.FormatScore(todayBest));
            if (bestNormal != null) bestNormal.SetActive(!newRecord);
            if (bestNew != null) bestNew.SetActive(newRecord);
            Text best = newRecord ? bestNewText : bestNormalText;
            if (best != null) best.text = BestText;

            if (chainText != null) chainText.text = string.Format(chainFormat, maxCombo);
            WriteEn();
        }

        void SetTitleRank(string label, Color color)
        {
            TMP_FontAsset font = FindTitleSdfFont();
            if (font == null)
            {
                titleValue.enabled = true;
                titleValue.text = label;
                titleValue.color = color;
                return;
            }

            if (_titleSdfText == null)
            {
                RectTransform source = titleValue.rectTransform;
                GameObject textObject = new GameObject("TitleValueSDF", typeof(RectTransform), typeof(TextMeshProUGUI));
                textObject.transform.SetParent(source.parent, false);

                RectTransform target = textObject.GetComponent<RectTransform>();
                target.anchorMin = source.anchorMin;
                target.anchorMax = source.anchorMax;
                target.pivot = source.pivot;
                target.anchoredPosition3D = source.anchoredPosition3D;
                target.sizeDelta = source.sizeDelta;
                target.localRotation = source.localRotation;
                target.localScale = source.localScale;

                _titleSdfText = textObject.GetComponent<TextMeshProUGUI>();
                _titleSdfText.font = font;
                _titleSdfText.fontSize = titleValue.fontSize;
                _titleSdfText.alignment = TextAlignmentOptions.Center;
                _titleSdfText.textWrappingMode = TextWrappingModes.NoWrap;
                _titleSdfText.overflowMode = TextOverflowModes.Overflow;
                _titleSdfText.raycastTarget = false;
                titleValue.enabled = false;
            }

            _titleSdfText.text = label;
            _titleSdfText.color = color;
        }

        static TMP_FontAsset FindTitleSdfFont()
        {
            TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            for (int i = 0; i < fonts.Length; i++)
            {
                if (fonts[i] != null && fonts[i].name == "玉ねぎ楷書激無料版v7改 SDF")
                    return fonts[i];
            }
            return null;
        }

        static string RankLabel(ShrineRank rank)
        {
            switch (rank)
            {
                case ShrineRank.C: return "貧";
                case ShrineRank.B: return "凡";
                case ShrineRank.A: return "良";
                case ShrineRank.S: return "極";
                default: return rank.ToString();
            }
        }

        void LateUpdate()
        {
            if (_filled) WriteEn();
        }

        void WriteEn()
        {
            if (enValue == null) return;
            int value = Mathf.RoundToInt(_en * Mathf.Clamp01(enProgress));
            if (value == _shownEn) return;
            _shownEn = value;
            enValue.text = HudUi.FormatScore(value);
        }

        void OnNextClicked()
        {
            if (nextButtonGroup != null && nextButtonGroup.alpha < 0.99f) return;
            NextPressed?.Invoke();
        }
    }
}
