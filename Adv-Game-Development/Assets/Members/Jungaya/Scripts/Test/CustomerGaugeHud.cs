using UnityEngine;
using Toufuku.Rescue;

/*
    検証用に、危険度D と残りの必要な発数R を表示するクラス（OnGUI で重ねて描く）。Canvas はいらなくて、シーンに1つ置くだけ
    シーンにあるぜんぶの CustomerState（#54）をさがして、それぞれの客の頭の上に表示する
      ・バー: 危険度 D（0〜100）。たまる＝悪い（赤っぽい色）、100 で黒客になる
      ・目もり: 残りの必要な発数 R。企画書 v8 6章のとおり、R が2以上の客（欲張り・ボス）だけ表示する
    本番の表示は、足元の円＝D、頭の上のゲージ＝R に分ける（v8 6章）。これはそれまでのテスト専用のスクリプト
*/
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
    [SerializeField] private Color emptyColor = new Color(1.0f, 0.75f, 0.4f); // 0 の近く＝もうすぐ救える（うすい暖かい色）
    [SerializeField] private Color fullColor = new Color(0.95f, 0.15f, 0.15f); // 満タン＝もうすぐ失敗（こい赤）
    [SerializeField] private Color backColor = new Color(0f, 0f, 0f, 0.6f);

    [Header("0で光る演出（解消時）")]
    [Tooltip("発光色。0になった瞬間に点滅して光る。")]
    [SerializeField] private Color glowColor = new Color(1f, 0.95f, 0.6f);
    [Tooltip("点滅の速さ（1秒あたりの周期数）。")]
    [SerializeField] private float glowPulseSpeed = 6f;

    private Texture2D _tex;

    private void Awake()
    {
        // OnGUI でぬる1色のテクスチャ（GUI.color とかけ合わせて使う）
        _tex = Texture2D.whiteTexture;
    }

    private void OnGUI()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        // 毎フレームさがす（テスト用なのでかんたんさを優先）
        CustomerState[] customers =
            Object.FindObjectsByType<CustomerState>(FindObjectsSortMode.None);

        foreach (CustomerState c in customers)
        {
            if (c == null || c.IsBlack) continue; // 黒客はバーを描かない（頭の上のゲージは消す。v8 6章）

            Vector3 worldPos = c.transform.position + Vector3.up * worldHeightOffset;
            Vector3 sp = cam.WorldToScreenPoint(worldPos);
            if (sp.z <= 0f) continue; // カメラのうしろは描かない

            // スクリーン座標を GUI の座標に直す（Y を反対にする）
            float x = sp.x - barWidth * 0.5f;
            float y = (Screen.height - sp.y) - barHeight * 0.5f;

            // 背景
            GUI.color = backColor;
            GUI.DrawTexture(new Rect(x - 1, y - 1, barWidth + 2, barHeight + 2), _tex);

            if (c.IsRescued)
            {
                // 救えた＝光る演出: バー全体を光る色で点滅させる
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * glowPulseSpeed * Mathf.PI * 2f);
                Color g = glowColor;
                g.a = Mathf.Lerp(0.4f, 1f, pulse);
                GUI.color = g;
                GUI.DrawTexture(new Rect(x, y, barWidth, barHeight), _tex);
            }
            else
            {
                // ふつう: 危険度D のたまり具合を赤っぽいグラデーションで表示する
                float t = Mathf.Clamp01(c.DangerNormalized);
                GUI.color = Color.Lerp(emptyColor, fullColor, t);
                GUI.DrawTexture(new Rect(x, y, barWidth * t, barHeight), _tex);

                // 残りの必要な発数R: 1発で救える客には出さない（v8 6章「表示しない：通常客・移動客・遠方客」）
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
