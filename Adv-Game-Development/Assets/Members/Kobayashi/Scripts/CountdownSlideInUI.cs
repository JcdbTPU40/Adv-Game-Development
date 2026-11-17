using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SessionEndCountdownTMP の開始カウントダウンに合わせ、指定Canvas内のUI要素を画面内へスライドさせます。
/// UIを持つGameObjectに追加し、対象Canvas名をInspectorで指定して別シーンでも再利用できます。
/// 非表示のCanvasは画面外に待機させ、表示された時点でスライドします。
/// </summary>
public sealed class CountdownSlideInUI : MonoBehaviour
{
    [Header("対象")]
    [SerializeField] private string targetCanvasName = "ScoreBoardHudCanvas";
    [Header("スライド")]
    [SerializeField] private Vector2 slideDirection = Vector2.left;
    [SerializeField, Min(0f)] private float slideDistance = 1000f;
    [SerializeField, Min(0.01f)] private float slideDuration = 0.8f;

    private sealed class SlideTarget
    {
        public RectTransform rect;
        public Canvas canvas;
        public Vector2 finalPosition;
        public bool isAnimating;
    }

    private readonly Dictionary<RectTransform, SlideTarget> pendingTargets = new Dictionary<RectTransform, SlideTarget>();
    private readonly List<SlideTarget> targetBuffer = new List<SlideTarget>();

    public static void PlayAll()
    {
        CountdownSlideInUI[] slideIns = FindObjectsByType<CountdownSlideInUI>(FindObjectsSortMode.None);
        if (slideIns.Length == 0)
        {
            Debug.LogWarning("開始カウントダウン用の CountdownSlideInUI がシーン内にありません。");
            return;
        }
        for (int i = 0; i < slideIns.Length; i++) slideIns[i].PrepareSlideIn();
    }

    private void Update()
    {
        if (pendingTargets.Count == 0) return;
        targetBuffer.Clear();
        foreach (SlideTarget target in pendingTargets.Values)
        {
            if (!target.isAnimating && target.rect != null && target.canvas != null &&
                target.rect.gameObject.activeInHierarchy && target.canvas.isActiveAndEnabled)
            {
                target.isAnimating = true;
                targetBuffer.Add(target);
            }
        }
        for (int i = 0; i < targetBuffer.Count; i++) StartCoroutine(AnimateSlideIn(targetBuffer[i]));
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        foreach (SlideTarget target in pendingTargets.Values)
        {
            if (target.rect != null) target.rect.anchoredPosition = target.finalPosition;
        }
        pendingTargets.Clear();
        targetBuffer.Clear();
    }

    private void PrepareSlideIn()
    {
        Canvas[] canvases = GetComponentsInChildren<Canvas>(true);
        int preparedCount = 0;
        Vector2 offset = GetOffset();
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas.name != targetCanvasName) continue;
            Transform canvasTransform = canvas.transform;
            for (int childIndex = 0; childIndex < canvasTransform.childCount; childIndex++)
            {
                if (!(canvasTransform.GetChild(childIndex) is RectTransform childRect)) continue;
                SlideTarget target = new SlideTarget
                {
                    rect = childRect,
                    canvas = canvas,
                    finalPosition = childRect.anchoredPosition
                };
                pendingTargets[childRect] = target;
                childRect.anchoredPosition = target.finalPosition + offset;
                preparedCount++;
            }
        }
        if (preparedCount == 0)
        {
            Debug.LogWarning($"スライド対象のCanvas「{targetCanvasName}」またはその直下のUIが見つかりません。", this);
            return;
        }
        Debug.Log($"「{targetCanvasName}」のUI {preparedCount}個をスライド対象にしました。", this);
    }

    private IEnumerator AnimateSlideIn(SlideTarget target)
    {
        Vector2 startPosition = target.rect.anchoredPosition;
        float elapsed = 0f;
        while (elapsed < slideDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / slideDuration);
            float eased = 1f - Mathf.Pow(1f - progress, 3f);
            target.rect.anchoredPosition = Vector2.LerpUnclamped(startPosition, target.finalPosition, eased);
            yield return null;
        }
        if (target.rect != null)
        {
            target.rect.anchoredPosition = target.finalPosition;
            pendingTargets.Remove(target.rect);
        }
    }

    private Vector2 GetOffset()
    {
        Vector2 direction = slideDirection.sqrMagnitude > 0f ? slideDirection.normalized : Vector2.left;
        return direction * slideDistance;
    }
}
