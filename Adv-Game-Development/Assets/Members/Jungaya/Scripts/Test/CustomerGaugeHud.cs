using UnityEngine;
using Toufuku.Rescue;

/// <summary>
/// 検証用の不満ゲージ表示（OnGUIオーバーレイ）。Canvas不要、シーンに1つ置くだけ。
/// シーン上の全 CustomerRescue を探し、各客の頭上にゲージバーを描く。
/// 仕様: 満ちる＝悪い（赤系）、0に近いほど救済に近い。
/// 本番のワールドUI（Canvas/Slider）ができたら不要になるテスト専用スクリプト。
/// </summary>
public class CustomerGaugeHud : MonoBehaviour
{
    [Header("バー表示")]
    [Tooltip("客の頭上どれだけ上に出すか（ワールド単位）。")]
    [SerializeField] private float worldHeightOffset = 2.0f;
    [SerializeField] private float barWidth = 80f;
    [SerializeField] private float barHeight = 10f;

    [Header("色")]
    [SerializeField] private Color emptyColor = new Color(0.2f, 0.9f, 0.3f); // 0付近＝救済間近
    [SerializeField] private Color fullColor = new Color(0.95f, 0.2f, 0.2f); // 満タン＝失敗間近
    [SerializeField] private Color backColor = new Color(0f, 0f, 0f, 0.6f);

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
        CustomerRescue[] customers =
            Object.FindObjectsByType<CustomerRescue>(FindObjectsSortMode.None);

        foreach (CustomerRescue c in customers)
        {
            if (c == null || c.IsResolved) continue;

            Vector3 worldPos = c.transform.position + Vector3.up * worldHeightOffset;
            Vector3 sp = cam.WorldToScreenPoint(worldPos);
            if (sp.z <= 0f) continue; // カメラ後方は描かない

            // スクリーン座標 → GUI座標（Yを反転）
            float x = sp.x - barWidth * 0.5f;
            float y = (Screen.height - sp.y) - barHeight * 0.5f;

            float t = Mathf.Clamp01(c.GaugeNormalized);

            // 背景
            GUI.color = backColor;
            GUI.DrawTexture(new Rect(x - 1, y - 1, barWidth + 2, barHeight + 2), _tex);

            // 満ち具合
            GUI.color = Color.Lerp(emptyColor, fullColor, t);
            GUI.DrawTexture(new Rect(x, y, barWidth * t, barHeight), _tex);
        }

        GUI.color = Color.white;
    }
}
