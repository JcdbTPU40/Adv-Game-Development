using UnityEngine.SceneManagement;
using UnityEngine;

public class GameOver : MonoBehaviour
{

    //キャンバス用
    private static Canvas gameOverCanvas;

    private void Awake()
    {
        //Canvasコンポーネント取得
        gameOverCanvas = GetComponent<Canvas>();
    }

    //パネルを開く用の関数 static呼び出し可能
    public static void GameOverShowPanel()
    {
        
        gameOverCanvas.enabled = true;
    }

    //ゲームを再スタートする関数
    public void ReStartGame()
    {
        //止めたゲーム内の時間を戻す
        Time.timeScale = 1f;

        //シーン再読み込み
        SceneManager.LoadScene(0);
    }
}
