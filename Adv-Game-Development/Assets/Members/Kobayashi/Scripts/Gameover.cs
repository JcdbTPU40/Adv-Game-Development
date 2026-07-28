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
        //ゲーム内の時間を止める
        Time.timeScale = 0f;

        //ボタンを有効にする
        //GameOverCanvasのCanvasのチェックをデフォルトで外すと関数が呼ばれた時にtrueになる(衝突した時に表示される)
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
