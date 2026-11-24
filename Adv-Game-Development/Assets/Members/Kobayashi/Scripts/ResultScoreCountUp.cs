using System.Collections;
using Toufuku.Hud;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ResultScreen の縁スコアを0から最終値までカウントアップします。
/// ResultScreen と同じ GameObject に追加すると、その結果表示へ自動適用されます。
/// </summary>
public sealed class ResultScoreCountUp : MonoBehaviour
{
    [Header("カウントアップ")]
    [SerializeField, Min(0f)] private float startDelay = 0.25f;
    [SerializeField, Min(0.05f)] private float countDuration = 1.8f;

    private ResultScreen resultScreen;
    private GameObject resultCanvasObject;
    private Text scoreValueText;
    private Coroutine countRoutine;
    private bool countedThisResult;
    private bool warnedMissingReferences;

    private void Start()
    {
        // TimeUpImageDisplay があるシーンでは、既存コンポーネント側の処理に一本化します。
        if (FindAnyObjectByType<TimeUpImageDisplay>() != null)
        {
            enabled = false;
            return;
        }

        TryFindResultUI();
    }

    // ResultScreen.Update がCanvasを表示した同じフレームの描画前に0へ戻すため、LateUpdateで監視します。
    private void LateUpdate()
    {
        if (resultScreen == null || resultCanvasObject == null || scoreValueText == null)
        {
            TryFindResultUI();
        }

        if (resultScreen == null || resultCanvasObject == null || scoreValueText == null)
        {
            return;
        }

        bool resultVisible = resultCanvasObject.activeInHierarchy;
        if (!resultVisible)
        {
            if (countRoutine != null)
            {
                StopCoroutine(countRoutine);
                countRoutine = null;
            }
            countedThisResult = false;
            return;
        }

        if (!countedThisResult)
        {
            countedThisResult = true;
            Debug.Log($"リザルトスコアのカウントアップ開始: {resultScreen.ShownEn}", this);
            countRoutine = StartCoroutine(CountUp(resultScreen.ShownEn));
        }
    }

    private void TryFindResultUI()
    {
        if (resultScreen == null)
        {
            resultScreen = GetComponent<ResultScreen>();
            if (resultScreen == null)
            {
                resultScreen = GetComponentInChildren<ResultScreen>(true);
            }
        }

        if (resultCanvasObject == null)
        {
            Transform canvasTransform = transform.Find("ResultScreenCanvas");
            if (canvasTransform == null)
            {
                Canvas[] canvases = GetComponentsInChildren<Canvas>(true);
                for (int i = 0; i < canvases.Length; i++)
                {
                    if (canvases[i].name == "ResultScreenCanvas")
                    {
                        canvasTransform = canvases[i].transform;
                        break;
                    }
                }
            }
            if (canvasTransform != null)
            {
                resultCanvasObject = canvasTransform.gameObject;
            }
        }

        if (scoreValueText == null)
        {
            Transform valueTransform = transform.Find("ResultScreenCanvas/Panel/EnValue/Text");
            if (valueTransform != null)
            {
                scoreValueText = valueTransform.GetComponent<Text>();
            }

            if (scoreValueText == null)
            {
                Text[] textComponents = GetComponentsInChildren<Text>(true);
                for (int i = 0; i < textComponents.Length; i++)
                {
                    Transform parent = textComponents[i].transform.parent;
                    if (textComponents[i].gameObject.name == "Text" && parent != null && parent.name == "EnValue")
                    {
                        scoreValueText = textComponents[i];
                        break;
                    }
                }
            }
        }

        if (!warnedMissingReferences && (resultScreen == null || resultCanvasObject == null || scoreValueText == null))
        {
            warnedMissingReferences = true;
            Debug.LogWarning("ResultScoreCountUp は ResultScreen と同じGameObjectに追加してください。結果UIの生成を待っています。", this);
        }
    }

    private IEnumerator CountUp(int finalScore)
    {
        finalScore = Mathf.Clamp(finalScore, 0, HudUi.MaxDisplayedScore);
        scoreValueText.text = HudUi.FormatScore(0);

        if (startDelay > 0f)
        {
            yield return new WaitForSecondsRealtime(startDelay);
        }

        float elapsed = 0f;
        while (elapsed < countDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / countDuration);
            int displayedScore = Mathf.RoundToInt(Mathf.Lerp(0f, finalScore, progress));
            scoreValueText.text = HudUi.FormatScore(displayedScore);
            yield return null;
        }

        scoreValueText.text = HudUi.FormatScore(finalScore);
        countRoutine = null;
        Debug.Log($"リザルトスコアをカウントアップしました: {finalScore}", this);
    }

    private void OnDisable()
    {
        if (countRoutine != null)
        {
            StopCoroutine(countRoutine);
            countRoutine = null;
        }
    }
}
