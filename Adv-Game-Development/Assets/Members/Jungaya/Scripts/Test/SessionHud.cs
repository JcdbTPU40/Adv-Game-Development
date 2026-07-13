using UnityEngine;

/// <summary>
/// セッション表示＋簡易リザルト（OnGUIオーバーレイ）— Issue #32
///
/// ・プレイ中: 画面右上に「◯ヶ月目 / 残り時間」を表示。
/// ・終了時 : 画面中央にリザルト（縁 / 神社ランク / 最大コンボ）とリトライボタン。
/// Canvas不要、シーンに1つ置くだけ。本番UIができたら不要になるテスト専用スクリプト。
/// </summary>
public class SessionHud : MonoBehaviour
{
    [SerializeField] int fontSize = 26;
    [SerializeField] int resultFontSize = 34;

    GUIStyle _style;
    GUIStyle _resultStyle;

    void OnGUI()
    {
        var session = GameSession.Instance;
        if (session == null)
        {
            GUI.Label(new Rect(20, 60, 700, 30), "GameSession が見つかりません（シーンに配置してください）");
            return;
        }

        EnsureStyles();

        if (session.IsFinished)
            DrawResult(session);
        else
            DrawPlaying(session);
    }

    void EnsureStyles()
    {
        if (_style == null || _style.fontSize != fontSize)
            _style = new GUIStyle(GUI.skin.label) { fontSize = fontSize, alignment = TextAnchor.UpperRight };
        _style.normal.textColor = Color.white;

        if (_resultStyle == null || _resultStyle.fontSize != resultFontSize)
            _resultStyle = new GUIStyle(GUI.skin.label) { fontSize = resultFontSize, alignment = TextAnchor.MiddleCenter };
        _resultStyle.normal.textColor = Color.white;
    }

    void DrawPlaying(GameSession session)
    {
        float w = 340, h = fontSize + 10;
        float x = Screen.width - w - 14, y = 12;

        GUI.color = new Color(0f, 0f, 0f, 0.45f);
        GUI.DrawTexture(new Rect(x - 10, y - 4, w + 20, h * 2 + 12), Texture2D.whiteTexture);
        GUI.color = Color.white;

        int min = Mathf.FloorToInt(session.RemainingSeconds / 60f);
        int sec = Mathf.FloorToInt(session.RemainingSeconds % 60f);
        GUI.Label(new Rect(x, y, w, h), $"{session.CurrentMonth}ヶ月目 / {session.TotalMonths}ヶ月", _style);
        GUI.Label(new Rect(x, y + h, w, h), $"残り {min}:{sec:00}", _style);
    }

    void DrawResult(GameSession session)
    {
        var sm = ScoreManager.Instance;
        var rating = ShrineRating.Instance;

        float w = 520, h = 320;
        float x = (Screen.width - w) / 2f, y = (Screen.height - h) / 2f;

        GUI.color = new Color(0f, 0f, 0f, 0.8f);
        GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float line = resultFontSize + 14;
        float ty = y + 24;
        GUI.Label(new Rect(x, ty, w, line), "―― 三ヶ月が経ちました ――", _resultStyle);
        ty += line + 10;
        GUI.Label(new Rect(x, ty, w, line), $"縁（ハイスコア） : {(sm != null ? sm.En : 0)}", _resultStyle);
        ty += line;
        GUI.Label(new Rect(x, ty, w, line), $"神社ランク : {(rating != null ? rating.Rank.ToString() : "-")}", _resultStyle);
        ty += line;
        GUI.Label(new Rect(x, ty, w, line), $"最大コンボ : {(sm != null ? sm.MaxCombo : 0)}", _resultStyle);
        ty += line + 16;

        if (GUI.Button(new Rect(x + (w - 220) / 2f, ty, 220, 48), "もう一度（リトライ）"))
            session.Retry();
    }
}
