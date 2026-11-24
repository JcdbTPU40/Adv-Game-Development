using System.Collections;
using System.Collections.Generic;
using Toufuku.Hud;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

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

    private sealed class ShadowRestore
    {
        public Renderer renderer;
        public ShadowCastingMode originalMode;
        public int restoreFrame;
    }

    private readonly Dictionary<RectTransform, SlideTarget> pendingTargets = new Dictionary<RectTransform, SlideTarget>();
    private readonly List<SlideTarget> targetBuffer = new List<SlideTarget>();
    private readonly HashSet<Renderer> knownCustomerRenderers = new HashSet<Renderer>();
    private readonly Dictionary<Renderer, ShadowRestore> pendingShadowRestores = new Dictionary<Renderer, ShadowRestore>();
    private float customerShadowMonitorUntil;

    public static void PlayAll()
    {
        CountdownSlideInUI[] slideIns = FindObjectsByType<CountdownSlideInUI>(FindObjectsSortMode.None);
        if (slideIns.Length == 0)
        {
            // CountdownSlideInUIをシーンに手動追加していないシーンでも、
            // 既存の共通HUDがあれば自動で演出対象にします。
            ScoreBoardHud scoreBoard = FindAnyObjectByType<ScoreBoardHud>();
            if (scoreBoard == null)
            {
                Debug.LogWarning("スライド対象の CountdownSlideInUI と ScoreBoardHud がシーン内にありません。");
                return;
            }

            CountdownSlideInUI slideIn = scoreBoard.gameObject.AddComponent<CountdownSlideInUI>();
            slideIn.targetCanvasName = "ScoreBoardHudCanvas";
            slideIn.slideDirection = Vector2.up;
            slideIn.slideDistance = 700f;
            slideIn.slideDuration = 0.8f;
            slideIns = new[] { slideIn };
        }

        if (SceneManager.GetActiveScene().name == "GameScene_Awaji")
        {
            slideIns[0].ArmCustomerShadowWarmup();
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

    private void LateUpdate()
    {
        RestoreWarmedCustomerShadows();

        if (Time.unscaledTime > customerShadowMonitorUntil)
        {
            return;
        }

        GameObject[] customers = GameObject.FindGameObjectsWithTag("Customer");
        for (int i = 0; i < customers.Length; i++)
        {
            Renderer[] renderers = customers[i].GetComponentsInChildren<Renderer>(true);
            for (int j = 0; j < renderers.Length; j++)
            {
                Renderer customerRenderer = renderers[j];
                if (customerRenderer == null || !knownCustomerRenderers.Add(customerRenderer))
                {
                    continue;
                }

                ShadowCastingMode originalMode = customerRenderer.shadowCastingMode;
                if (originalMode == ShadowCastingMode.Off)
                {
                    continue;
                }

                // Instantiateされたフレームの未安定な影マップを一度だけ避け、次フレームで通常の影に戻します。
                pendingShadowRestores[customerRenderer] = new ShadowRestore
                {
                    renderer = customerRenderer,
                    originalMode = originalMode,
                    restoreFrame = Time.frameCount + 1
                };
                customerRenderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }
    }

    private void ArmCustomerShadowWarmup()
    {
        customerShadowMonitorUntil = Time.unscaledTime + 6f;
        knownCustomerRenderers.Clear();
        pendingShadowRestores.Clear();

        GameObject[] existingCustomers = GameObject.FindGameObjectsWithTag("Customer");
        for (int i = 0; i < existingCustomers.Length; i++)
        {
            Renderer[] renderers = existingCustomers[i].GetComponentsInChildren<Renderer>(true);
            for (int j = 0; j < renderers.Length; j++)
            {
                if (renderers[j] != null)
                {
                    knownCustomerRenderers.Add(renderers[j]);
                }
            }
        }

        Debug.Log("競技開始後6秒間、新しく出現する客の影を初回描画の次フレームから有効にします。", this);
    }

    private void RestoreWarmedCustomerShadows()
    {
        if (pendingShadowRestores.Count == 0)
        {
            return;
        }

        List<Renderer> restored = null;
        foreach (KeyValuePair<Renderer, ShadowRestore> entry in pendingShadowRestores)
        {
            ShadowRestore shadowRestore = entry.Value;
            if (shadowRestore.renderer == null)
            {
                (restored ??= new List<Renderer>()).Add(entry.Key);
                continue;
            }

            if (Time.frameCount >= shadowRestore.restoreFrame)
            {
                shadowRestore.renderer.shadowCastingMode = shadowRestore.originalMode;
                (restored ??= new List<Renderer>()).Add(entry.Key);
            }
        }

        if (restored != null)
        {
            for (int i = 0; i < restored.Count; i++)
            {
                pendingShadowRestores.Remove(restored[i]);
            }
        }
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        foreach (ShadowRestore shadowRestore in pendingShadowRestores.Values)
        {
            if (shadowRestore.renderer != null)
            {
                shadowRestore.renderer.shadowCastingMode = shadowRestore.originalMode;
            }
        }
        pendingShadowRestores.Clear();
        knownCustomerRenderers.Clear();
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
