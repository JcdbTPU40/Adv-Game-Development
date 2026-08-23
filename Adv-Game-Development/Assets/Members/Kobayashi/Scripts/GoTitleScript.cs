using Unity.VectorGraphics;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GoTitleScript : MonoBehaviour
{
    int time = (int)GameSession.Instance.RemainingSeconds;
    [SerializeField] string sceneName;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.anyKeyDown&&time==0)
        {
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }
    }
}
