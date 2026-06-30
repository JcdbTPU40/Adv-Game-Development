using UnityEngine;
using Toufuku.Rescue;

/// <summary>
/// いま撃つお守りの「現在の種類」を保持する選択ソース（#9 の暫定実装 ＋ #10 のテスト足場）。
///
/// ・数字キー 1〜5 で種類を切り替え、画面左上に現在の種類を表示する。
/// ・発射側（Shoot_Bullet / Shoot / TestShooter など）は <see cref="Current"/> を読み、
///   生成した弾の <see cref="OmamoriBullet.SetType"/> に流し込む。
///
/// 正式な選択UI（#9）が固まったら、この入力部分だけ差し替えればよい
/// （発射側は Current を読むだけなので影響しない）。
/// </summary>
public class OmamoriSelector : MonoBehaviour
{
    [Header("現在選択中のお守り（テスト時はInspectorからも変更可）")]
    [SerializeField] private OmamoriType current = OmamoriType.Kenkou;

    [Header("テスト表示（本番UIができたらオフに）")]
    [SerializeField] private bool showDebugLabel = true;

    /// <summary>いま選択中のお守りの種類。発射側はこれを読んで弾に乗せる。</summary>
    public OmamoriType Current => current;

    /// <summary>外部（UIボタン等）から種類を指定したい場合に呼ぶ。</summary>
    public void Select(OmamoriType t)
    {
        if (current == t) return;
        current = t;
        Debug.Log($"[OmamoriSelector] 選択: {current}");
    }

    private void Update()
    {
        // テスト用：数字キーで切り替え（本番のコントローラ/UI入力ができたら不要）。
        if (Input.GetKeyDown(KeyCode.Alpha1)) Select(OmamoriType.Kenkou);
        else if (Input.GetKeyDown(KeyCode.Alpha2)) Select(OmamoriType.Gakugyou);
        else if (Input.GetKeyDown(KeyCode.Alpha3)) Select(OmamoriType.Renai);
        else if (Input.GetKeyDown(KeyCode.Alpha4)) Select(OmamoriType.Kinun);
        else if (Input.GetKeyDown(KeyCode.Alpha5)) Select(OmamoriType.Yakuyoke);
    }

    private void OnGUI()
    {
        if (!showDebugLabel) return;
        GUI.Label(
            new Rect(10, 10, 480, 24),
            $"お守り: {current}   (1:健康 2:学業 3:恋愛 4:金運 5:厄除け)");
    }
}
