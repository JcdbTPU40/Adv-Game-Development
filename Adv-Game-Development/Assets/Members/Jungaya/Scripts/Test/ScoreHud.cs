using UnityEngine;

/// <summary>
/// 検証用の簡易HUD（OnGUIオーバーレイ）。Canvas不要、シーンに1つ置くだけ。
/// 縁・コンボ・倍率・最大コンボ・直近の命中ゾーンと獲得点を画面左上に表示する。
/// 本番UIができたら不要になるテスト専用スクリプト。
/// </summary>
public class ScoreHud : MonoBehaviour
{
    [SerializeField] int fontSize = 26;
    [SerializeField] Color color = Color.white;

    GUIStyle style;

    void OnGUI()
    {
        var sm = ScoreManager.Instance;
        if (sm == null)
        {
            GUI.Label(new Rect(20, 20, 600, 30), "ScoreManager が見つかりません（シーンに配置してください）");
            return;
        }

        if (style == null || style.fontSize != fontSize)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = fontSize };
        }
        style.normal.textColor = color;

        // 背景パネル
        GUI.color = new Color(0f, 0f, 0f, 0.45f);
        GUI.DrawTexture(new Rect(10, 10, 360, 210), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float x = 24, y = 18, h = fontSize + 8;
        GUI.Label(new Rect(x, y + h * 0, 360, h), $"縁(En) : {sm.En}", style);
        GUI.Label(new Rect(x, y + h * 1, 360, h), $"コンボ : {sm.Combo}  (Max {sm.MaxCombo})", style);
        GUI.Label(new Rect(x, y + h * 2, 360, h), $"倍率   : x{sm.Multiplier:0.0}", style);
        GUI.Label(new Rect(x, y + h * 3, 360, h), $"直近   : {sm.LastZone}  +{sm.LastGain}", style);

        // ボタン：テスト操作
        if (GUI.Button(new Rect(x, y + h * 4 + 6, 110, 34), "Reset"))
            sm.ResetAll();
        if (GUI.Button(new Rect(x + 120, y + h * 4 + 6, 130, 34), "Force Miss"))
            sm.RegisterMiss();
    }
}
