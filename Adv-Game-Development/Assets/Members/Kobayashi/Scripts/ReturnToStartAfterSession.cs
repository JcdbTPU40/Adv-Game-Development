using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Toufuku.GameInput;

/// <summary>
/// リザルト表示後、入力があればInspectorで指定したシーンへ戻ります。
/// </summary>
public class ReturnToStartAfterSession : MonoBehaviour
{
    [SerializeField, Tooltip("ゲーム終了後に戻るシーン名。Build Settingsに登録してください。")]
    private string returnSceneName = "Assets/Members/Kobayashi/Scenes/Start.unity";
    [SerializeField, Min(0f), Tooltip("リザルト表示後、キーとボタン操作を無効にする秒数。")]
    private float resultInputLockDuration = 3f;

    private const int ColorButtonCount = 5;
    private readonly bool[] previousColorButtonStates = new bool[ColorButtonCount];
    private GameSession subscribedSession;
    private Esp32RawSource controllerInput;
    private Button retryButton;
    private bool previousFrontButtonState;
    private bool sessionEnded;
    private bool sceneChangeStarted;
    private bool inputReadyLogged;
    private float resultShownAt;

    private void Start()
    {
        controllerInput = FindAnyObjectByType<Esp32RawSource>();
        UpdateControllerButtonStates(detectPress: false);
        TrySubscribeSession();
    }

    private void OnDisable()
    {
        if (subscribedSession != null)
        {
            subscribedSession.onSessionEnd.RemoveListener(EnableReturnOnKey);
            subscribedSession = null;
        }

        if (retryButton != null)
        {
            retryButton.interactable = true;
            retryButton = null;
        }
    }

    private void Update()
    {
        if (subscribedSession == null)
        {
            TrySubscribeSession();
        }

        bool controllerButtonDown = UpdateControllerButtonStates(detectPress: true);
        if (!sessionEnded || sceneChangeStarted)
        {
            return;
        }

        if (Time.unscaledTime - resultShownAt < resultInputLockDuration)
        {
            SetRetryButtonInteractable(false);
            return;
        }

        SetRetryButtonInteractable(true);
        if (!inputReadyLogged)
        {
            Debug.Log("[ReturnToStartAfterSession] 3秒ロック解除。入力待ちです。", this);
            inputReadyLogged = true;
        }
        if (!Input.anyKeyDown && !Input.anyKey && !IsInputSystemButtonHeld() && !controllerButtonDown && !IsControllerButtonHeld())
        {
            return;
        }

        Debug.Log("[ReturnToStartAfterSession] キー/ボタン入力を検知しました。", this);
        if (string.IsNullOrWhiteSpace(returnSceneName) || !Application.CanStreamedLevelBeLoaded(returnSceneName))
        {
            Debug.LogWarning($"戻り先のシーン「{returnSceneName}」が見つかりません。Build Settingsへの登録を確認してください。", this);
            return;
        }

        sceneChangeStarted = true;
        Time.timeScale = 1f;
        SceneManager.LoadScene(returnSceneName, LoadSceneMode.Single);
    }

    private void TrySubscribeSession()
    {
        if (subscribedSession != null || GameSession.Instance == null)
        {
            return;
        }

        subscribedSession = GameSession.Instance;
        subscribedSession.onSessionEnd.AddListener(EnableReturnOnKey);
        Debug.Log("[ReturnToStartAfterSession] GameSessionの終了通知を購読しました。", this);
        if (subscribedSession.IsFinished)
        {
            BeginResultInputLock();
        }
    }

    private bool IsInputSystemButtonHeld()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.anyKey.isPressed)
        {
            return true;
        }

        foreach (InputDevice device in InputSystem.devices)
        {
            var controls = device.allControls;
            for (int i = 0; i < controls.Count; i++)
            {
                if (controls[i] is ButtonControl button && button.isPressed)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsControllerButtonHeld()
    {
        if (controllerInput == null)
        {
            controllerInput = FindAnyObjectByType<Esp32RawSource>();
        }

        if (controllerInput == null)
        {
            return false;
        }

        for (int i = 0; i < ColorButtonCount; i++)
        {
            if (controllerInput.IsColorHeld(i))
            {
                return true;
            }
        }

        return controllerInput.IsFrontHeld;
    }
    private bool UpdateControllerButtonStates(bool detectPress)
    {
        if (controllerInput == null)
        {
            controllerInput = FindAnyObjectByType<Esp32RawSource>();
            if (controllerInput == null)
            {
                return false;
            }
        }

        bool pressed = false;
        for (int i = 0; i < ColorButtonCount; i++)
        {
            bool isHeld = controllerInput.IsColorHeld(i);
            if (detectPress && isHeld && !previousColorButtonStates[i])
            {
                pressed = true;
            }

            previousColorButtonStates[i] = isHeld;
        }

        bool frontHeld = controllerInput.IsFrontHeld;
        if (detectPress && frontHeld && !previousFrontButtonState)
        {
            pressed = true;
        }

        previousFrontButtonState = frontHeld;
        return pressed;
    }

    private void EnableReturnOnKey()
    {
        BeginResultInputLock();
    }

    private void BeginResultInputLock()
    {
        sessionEnded = true;
        sceneChangeStarted = false;
        inputReadyLogged = false;
        resultShownAt = Time.unscaledTime;
        Debug.Log("[ReturnToStartAfterSession] リザルト入力ロックを開始します。", this);
        SetRetryButtonInteractable(false);
    }

    private void SetRetryButtonInteractable(bool interactable)
    {
        if (retryButton == null)
        {
            GameObject resultScreenObject = GameObject.Find("ScoreBoardHud");
            Transform buttonTransform = resultScreenObject != null
                ? resultScreenObject.transform.Find("ResultScreenCanvas/Panel/RetryButton")
                : null;
            if (buttonTransform != null)
            {
                retryButton = buttonTransform.GetComponent<Button>();
            }
        }

        if (retryButton != null)
        {
            retryButton.interactable = interactable;
        }
    }
}
