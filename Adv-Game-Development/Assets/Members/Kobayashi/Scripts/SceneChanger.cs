using UnityEngine;
 using UnityEngine.SceneManagement;

 public class SceneChanger : MonoBehaviour
 { 
    [SerializeField] private string _loadScene;    
    
    public void SceneChange()
    {
       SceneManager.LoadScene(_loadScene);
    }
 }
