using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// リザルト画面の神社称号の値だけを、指定されたTMP SDFフォントで表示します。
/// </summary>
public sealed class ShrineTitleSdfFont : MonoBehaviour
{
    [SerializeField] private TMP_FontAsset titleFont;

    private GameSession session;
    private Text originalText;
    private TextMeshProUGUI replacementText;
    private GameObject replacementObject;
    private Coroutine applyRoutine;

    private void OnEnable()
    {
        TrySubscribe();
    }

    private void Update()
    {
        // GameSessionの生成順に備えて購読だけ再試行します。ゲーム値のポーリングはしません。
        if (session == null)
        {
            TrySubscribe();
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
        if (applyRoutine != null)
        {
            StopCoroutine(applyRoutine);
            applyRoutine = null;
        }
        RestoreOriginalText();
    }

    private void TrySubscribe()
    {
        if (session != null || GameSession.Instance == null)
        {
            return;
        }

        session = GameSession.Instance;
        session.onSessionEnd.AddListener(OnSessionEnd);
        session.onSessionStart.AddListener(OnSessionStart);

        if (session.IsFinished)
        {
            OnSessionEnd();
        }
    }

    private void Unsubscribe()
    {
        if (session == null)
        {
            return;
        }

        session.onSessionEnd.RemoveListener(OnSessionEnd);
        session.onSessionStart.RemoveListener(OnSessionStart);
        session = null;
    }

    private void OnSessionEnd()
    {
        if (replacementText == null && applyRoutine == null)
        {
            applyRoutine = StartCoroutine(ApplyAfterResultIsBuilt());
        }
    }

    private IEnumerator ApplyAfterResultIsBuilt()
    {
        // ResultScreenが称号と色を設定してから、その表示を引き継ぎます。
        yield return null;
        applyRoutine = null;

        if (titleFont == null)
        {
            Debug.LogWarning("神社称号用のTMP SDFフォントがInspectorで設定されていません。", this);
            yield break;
        }

        Transform titleTextTransform = transform.Find("ResultScreenCanvas/Panel/TitleValue/Text");
        if (titleTextTransform == null)
        {
            yield break;
        }

        originalText = titleTextTransform.GetComponent<Text>();
        if (originalText == null)
        {
            yield break;
        }

        bool wasEnabled = originalText.enabled;
        RectTransform sourceRect = originalText.rectTransform;
        replacementObject = new GameObject("TitleValueSdfText", typeof(RectTransform), typeof(TextMeshProUGUI));
        replacementObject.transform.SetParent(originalText.transform.parent, false);

        RectTransform replacementRect = replacementObject.GetComponent<RectTransform>();
        replacementRect.anchorMin = sourceRect.anchorMin;
        replacementRect.anchorMax = sourceRect.anchorMax;
        replacementRect.pivot = sourceRect.pivot;
        replacementRect.anchoredPosition3D = sourceRect.anchoredPosition3D;
        replacementRect.sizeDelta = sourceRect.sizeDelta;
        replacementRect.localRotation = sourceRect.localRotation;
        replacementRect.localScale = sourceRect.localScale;

        replacementText = replacementObject.GetComponent<TextMeshProUGUI>();
        replacementText.font = titleFont;
        replacementText.fontSize = originalText.fontSize;
        replacementText.color = originalText.color;
        replacementText.alignment = TextAlignmentOptions.Center;
        replacementText.textWrappingMode = TextWrappingModes.NoWrap;
        replacementText.overflowMode = TextOverflowModes.Overflow;
        replacementText.raycastTarget = false;
        replacementText.text = originalText.text;
        replacementText.enabled = wasEnabled;
        originalText.enabled = false;
    }

    private void OnSessionStart()
    {
        if (applyRoutine != null)
        {
            StopCoroutine(applyRoutine);
            applyRoutine = null;
        }
        RestoreOriginalText();
    }

    private void RestoreOriginalText()
    {
        if (originalText != null)
        {
            originalText.enabled = true;
        }

        if (replacementObject != null)
        {
            if (Application.isPlaying)
            {
                Destroy(replacementObject);
            }
            else
            {
                DestroyImmediate(replacementObject);
            }
        }

        replacementText = null;
        replacementObject = null;
        originalText = null;
    }
}
