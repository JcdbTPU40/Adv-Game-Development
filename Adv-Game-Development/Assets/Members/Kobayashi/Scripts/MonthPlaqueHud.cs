using System.Collections;
using TMPro;
using Toufuku.Hud;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 神社ランクの木札の下に、現在の月を表示する木札を作成します。
/// ScoreBoardHud と同じ HUD Canvas 内に自動配置するため、別シーンでも再利用できます。
/// </summary>
public sealed class MonthPlaqueHud : MonoBehaviour
{
    [Header("参照（空欄ならシーン内の RankBoard を自動検索）")]
    [SerializeField] private RectTransform rankBoard;
    [SerializeField] private TMP_FontAsset font;

    [Header("表示")]
    [SerializeField, Min(1f)] private float fontSize = 48f;
    [SerializeField] private Color textColor = new Color(0.25f, 0.14f, 0.01f, 1f);
    [SerializeField, Min(1f)] private float plaqueHeight = 100f;
    [SerializeField, Min(0f)] private float gapFromRankBoard = 12f;
    [SerializeField] private int monthNumberOffset;

    [Header("月切り替え演出")]
    [SerializeField, Min(0.05f)] private float flipDuration = 0.36f;

    private TMP_Text monthText;
    private RectTransform plaqueRect;
    private RectTransform plaqueVisualRect;
    private Coroutine flipRoutine;
    private int lastDisplayedMonth = int.MinValue;

    private void Start()
    {
        if (!TryFindRankBoard())
        {
            Debug.LogWarning("月表示を置ける RankBoard が見つかりません。MonthPlaqueHud の Rank Board に割り当ててください。", this);
            return;
        }

        CreateMonthPlaque();
        RefreshMonth();
    }

    private void Update()
    {
        if (monthText == null)
        {
            return;
        }

        RefreshMonth();
    }

    private bool TryFindRankBoard()
    {
        if (rankBoard != null)
        {
            return true;
        }

        Transform found = transform.Find("ScoreBoardHudCanvas/RankBoard");
        if (found == null)
        {
            GameObject rankObject = GameObject.Find("RankBoard");
            if (rankObject != null)
            {
                found = rankObject.transform;
            }
        }

        rankBoard = found as RectTransform;
        return rankBoard != null;
    }

    private void CreateMonthPlaque()
    {
        RectTransform canvasRect = rankBoard.parent as RectTransform;
        if (canvasRect == null)
        {
            Debug.LogWarning("RankBoard の親に HUD Canvas がありません。", this);
            return;
        }

        GameObject plaqueObject = new GameObject("MonthPlaque", typeof(RectTransform));
        plaqueRect = plaqueObject.GetComponent<RectTransform>();
        plaqueRect.SetParent(canvasRect, false);
        plaqueRect.anchorMin = rankBoard.anchorMin;
        plaqueRect.anchorMax = rankBoard.anchorMax;
        plaqueRect.pivot = rankBoard.pivot;
        plaqueRect.sizeDelta = new Vector2(rankBoard.rect.width, plaqueHeight);
        plaqueRect.anchoredPosition = rankBoard.anchoredPosition + Vector2.down * (rankBoard.rect.height + gapFromRankBoard);

        GameObject visualObject = new GameObject("MonthPlaqueVisual", typeof(RectTransform), typeof(Image));
        plaqueVisualRect = visualObject.GetComponent<RectTransform>();
        plaqueVisualRect.SetParent(plaqueRect, false);
        plaqueVisualRect.anchorMin = Vector2.zero;
        plaqueVisualRect.anchorMax = Vector2.one;
        plaqueVisualRect.pivot = new Vector2(0.5f, 0.5f);
        plaqueVisualRect.offsetMin = Vector2.zero;
        plaqueVisualRect.offsetMax = Vector2.zero;

        Image plaqueImage = visualObject.GetComponent<Image>();
        plaqueImage.sprite = HudShapes.WoodPlaque(plaqueRect.sizeDelta);
        plaqueImage.color = Color.white;
        plaqueImage.raycastTarget = false;

        GameObject textObject = new GameObject("MonthText", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(plaqueVisualRect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10f, 5f);
        textRect.offsetMax = new Vector2(-10f, -5f);

        monthText = textObject.GetComponent<TextMeshProUGUI>();
        monthText.font = font != null ? font : TMP_Settings.defaultFontAsset;
        monthText.fontSize = fontSize;
        monthText.color = textColor;
        monthText.alignment = TextAlignmentOptions.Center;
        monthText.textWrappingMode = TextWrappingModes.NoWrap;
        monthText.raycastTarget = false;
    }

    private void RefreshMonth()
    {
        GameSession session = GameSession.Instance;
        int month = session != null ? session.CurrentMonth : 1;
        month += monthNumberOffset;
        if (month == lastDisplayedMonth)
        {
            return;
        }

        if (lastDisplayedMonth == int.MinValue)
        {
            monthText.text = $"{month}か月目";
        }
        else
        {
            if (flipRoutine != null)
            {
                StopCoroutine(flipRoutine);
            }

            flipRoutine = StartCoroutine(FlipAndChangeMonth(month));
        }

        lastDisplayedMonth = month;
    }

    private IEnumerator FlipAndChangeMonth(int month)
    {
        float halfDuration = flipDuration * 0.5f;
        float elapsed = 0f;

        while (elapsed < halfDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / halfDuration);
            float angle = Mathf.SmoothStep(0f, 90f, progress);
            plaqueVisualRect.localRotation = Quaternion.Euler(angle, 0f, 0f);
            yield return null;
        }

        plaqueVisualRect.localRotation = Quaternion.Euler(90f, 0f, 0f);
        monthText.text = $"{month}か月目";

        elapsed = 0f;
        while (elapsed < halfDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / halfDuration);
            float angle = Mathf.SmoothStep(90f, 0f, progress);
            plaqueVisualRect.localRotation = Quaternion.Euler(angle, 0f, 0f);
            yield return null;
        }

        plaqueVisualRect.localRotation = Quaternion.identity;
        flipRoutine = null;
    }
}
