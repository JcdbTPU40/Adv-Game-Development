using Unity.VectorGraphics;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ESC : MonoBehaviour
{
    [SerializeField] string sceneName;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }
    }
}
