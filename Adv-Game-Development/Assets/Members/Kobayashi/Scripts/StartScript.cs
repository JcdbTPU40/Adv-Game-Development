using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class StartScript : MonoBehaviour
{
    [SerializeField] private string sceneName = "GameScene1";
    [SerializeField] private TimeUpImageDisplay fusumaTransition;

    private bool isTransitioning;

    private void Update()
    {
        if (!isTransitioning && Input.anyKeyDown)
        {
            isTransitioning = true;
            StartCoroutine(LoadGameAfterFusumaCloses());
        }
    }

    private IEnumerator LoadGameAfterFusumaCloses()
    {
        if (fusumaTransition != null)
        {
            yield return StartCoroutine(fusumaTransition.PlayFusumaTransition());
        }

        if (!string.IsNullOrEmpty(sceneName))
        {
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }
        else
        {
            Debug.LogWarning("遷移先のシーン名がInspectorで設定されていません。", this);
            isTransitioning = false;
        }
    }
}
