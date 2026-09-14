using UnityEngine;
using Toufuku.Rescue;

/// <summary>
/// 検証用の危険度D／残り必要発数R 表示（OnGUIオーバーレイ）。Canvas不要、シーンに1つ置くだけ。
/// シーン上の全 CustomerState(#54) を探し、各客の頭上に表示する。
///   ・バー   … 危険度 D（0〜100）。満ちる＝悪い（赤系）、100で黒客化。
///   ・目盛り … 残り必要発数 R。企画書 v8 6章どおり R≧2 の客（欲張り・ボス）だけ表示する。
/// 本番の表示は足元円＝D／頭上ゲージ＝R に分離する（v8 6章）。これはそれまでのテスト専用スクリプト。
/// </summary>
public class CustomerGaugeHud : MonoBehaviour
{
    [Header("バー表示")]
    [Tooltip("客の頭上どれだけ上に出すか（ワールド単位）。")]
    [SerializeField] private float worldHeightOffset = 2.0f;
    [SerializeField] private float barWidth = 80f;
    [SerializeField] private float barHeight = 10f;

    [Header("残り必要発数R（R≧2 の客だけ表示）")]
    [Tooltip("R の目盛りをバーの上に出す高さ（ピクセル）。")]
    [SerializeField] private float remainingPipOffset = 14f;
    [Tooltip("目盛り1つの幅（ピクセル）。")]
    [SerializeField] private float remainingPipWidth = 16f;
    [Tooltip("目盛りの色。")]
    [SerializeField] private Color remainingColor = new Color(1f, 1f, 1f, 0.9f);

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
        CustomerState[] customers =
            Object.FindObjectsByType<CustomerState>(FindObjectsSortMode.None);

        foreach (CustomerState c in customers)
        {
            if (c == null || c.IsBlack) continue; // 黒客はバーを描かない（頭上ゲージは消す。v8 6章）

            Vector3 worldPos = c.transform.position + Vector3.up * worldHeightOffset;
            Vector3 sp = cam.WorldToScreenPoint(worldPos);
            if (sp.z <= 0f) continue; // カメラ後方は描かない

            // スクリーン座標 → GUI座標（Yを反転）
            float x = sp.x - barWidth * 0.5f;
            float y = (Screen.height - sp.y) - barHeight * 0.5f;

            // 背景
            GUI.color = backColor;
            GUI.DrawTexture(new Rect(x - 1, y - 1, barWidth + 2, barHeight + 2), _tex);

            if (c.IsRescued)
            {
                // 救済完了＝光る演出：バー全体を発光色で点滅させる。
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * glowPulseSpeed * Mathf.PI * 2f);
                Color g = glowColor;
                g.a = Mathf.Lerp(0.4f, 1f, pulse);
                GUI.color = g;
                GUI.DrawTexture(new Rect(x, y, barWidth, barHeight), _tex);
            }
            else
            {
                // 通常：危険度Dの満ち具合を赤系グラデで表示。
                float t = Mathf.Clamp01(c.DangerNormalized);
                GUI.color = Color.Lerp(emptyColor, fullColor, t);
                GUI.DrawTexture(new Rect(x, y, barWidth * t, barHeight), _tex);

                // 残り必要発数R：1発で救済できる客には出さない（v8 6章「表示しない：通常客・移動客・遠方客」）。
                if (c.Remaining >= 2)
                {
                    GUI.color = remainingColor;
                    float pipY = y - remainingPipOffset;
                    for (int i = 0; i < c.Remaining; i++)
                    {
                        float pipX = x + i * (remainingPipWidth + 3f);
                        GUI.DrawTexture(new Rect(pipX, pipY, remainingPipWidth, barHeight * 0.6f), _tex);
                    }
                }
            }
        }

        GUI.color = Color.white;
    }
}
