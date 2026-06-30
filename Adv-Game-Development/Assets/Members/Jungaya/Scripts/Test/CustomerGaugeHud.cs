using UnityEngine;
using Toufuku.Rescue;

/// <summary>
/// 検証用の不満ゲージ表示（OnGUIオーバーレイ）。Canvas不要、シーンに1つ置くだけ。
/// シーン上の全 CustomerMood を探し、各客の頭上にゲージバーを描く。
/// 仕様（企画書6章/7章）: 満ちる＝悪い（赤系）、0に近いほど救済に近い。0（解消）で光る演出。
/// 本番のワールドUI（Canvas/Slider）ができたら不要になるテスト専用スクリプト。
/// </summary>
public class CustomerGaugeHud : MonoBehaviour
{
    [Header("バー表示")]
    [Tooltip("客の頭上どれだけ上に出すか（ワールド単位）。")]
    [SerializeField] private float worldHeightOffset = 2.0f;
    [SerializeField] private float barWidth = 80f;
    [SerializeField] private float barHeight = 10f;

    [Header("色（赤系で統一：満ちる＝悪い）")]
    [SerializeField] private Color emptyColor = new Color(1.0f, 0.75f, 0.4f); // 0付近＝救済間近（淡い暖色）
    [SerializeField] private Color fullColor = new Color(0.95f, 0.15f, 0.15f); // 満タン＝失敗間近（濃い赤）
    [SerializeField] private Color backColor = new Color(0f, 0f, 0f, 0.6f);

    [Header("0で光る演出（解消時）")]
    [Tooltip("発光色。0になった瞬間に点滅して光る。")]
    [SerializeField] private Color glowColor = new Color(1f, 0.95f, 0.6f);
    [Tooltip("点滅の速さ（1秒あたりの周期数）。")]
    [SerializeField] private float glowPulseSpeed = 6f;

    private Texture2D _tex;

    private void Awake()
    {
        // OnGUI で塗る単色テクスチャ（GUI.color と掛け合わせて使う）。
        _tex = Texture2D.whiteTexture;
    }

    private void OnGUI()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        // 毎フレーム探索（テスト用途なので簡易優先）。
        CustomerMood[] customers =
            Object.FindObjectsByType<CustomerMood>(FindObjectsSortMode.None);

        foreach (CustomerMood c in customers)
        {
            if (c == null || c.IsAngry) continue; // 怒り（失敗）はバーを描かない

            Vector3 worldPos = c.transform.position + Vector3.up * worldHeightOffset;
            Vector3 sp = cam.WorldToScreenPoint(worldPos);
            if (sp.z <= 0f) continue; // カメラ後方は描かない

            // スクリーン座標 → GUI座標（Yを反転）
            float x = sp.x - barWidth * 0.5f;
            float y = (Screen.height - sp.y) - barHeight * 0.5f;

            // 背景
            GUI.color = backColor;
            GUI.DrawTexture(new Rect(x - 1, y - 1, barWidth + 2, barHeight + 2), _tex);

            if (c.IsResolved)
            {
                // 解消＝0で光る演出：バー全体を発光色で点滅させる。
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * glowPulseSpeed * Mathf.PI * 2f);
                Color g = glowColor;
                g.a = Mathf.Lerp(0.4f, 1f, pulse);
                GUI.color = g;
                GUI.DrawTexture(new Rect(x, y, barWidth, barHeight), _tex);
            }
            else
            {
                // 通常：満ち具合を赤系グラデで表示。
                float t = Mathf.Clamp01(c.GaugeNormalized);
                GUI.color = Color.Lerp(emptyColor, fullColor, t);
                GUI.DrawTexture(new Rect(x, y, barWidth * t, barHeight), _tex);
            }
        }

        GUI.color = Color.white;
    }
}
