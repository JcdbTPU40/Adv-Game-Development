using UnityEngine;
using Toufuku.Rescue;
using Toufuku.GameInput;

/// <summary>
/// いま撃つお守りの「現在の種類」を保持する選択ソース（#9 の暫定実装 ＋ #10 のテスト足場）。
///
/// ・選択入力（数字キー 1〜5 相当）で種類を切り替え、画面左上に現在の種類を表示する。
/// ・発射側（Shoot_Bullet / Shoot / TestShooter など）は <see cref="Current"/> を読み、
///   生成した弾の <see cref="OmamoriBullet.SetType"/> に流し込む。
///
/// #20: 入力は IInputProvider 経由（未設定ならキーボード直読みにフォールバック）。
/// 正式な選択UI（#9）が固まったら、この入力部分だけ差し替えればよい
/// （発射側は Current を読むだけなので影響しない）。
/// </summary>
public class OmamoriSelector : MonoBehaviour
{
    [Header("現在選択中のお守り（テスト時はInspectorからも変更可）")]
    [SerializeField] private OmamoriType current = OmamoriType.Kenkou;

    [Header("テスト表示（本番UIができたらオフに）")]
    [SerializeField] private bool showDebugLabel = true;

    [Header("入力の供給元（#20）。IInputProvider 実装をドラッグ。未設定ならキーボード直読み")]
    [SerializeField] private MonoBehaviour inputProviderSource;

    private IInputProvider _input;

    /// <summary>いま選択中のお守りの種類。発射側はこれを読んで弾に乗せる。</summary>
    public OmamoriType Current => current;

    private void Awake()
    {
        _input = inputProviderSource as IInputProvider;
        if (inputProviderSource != null && _input == null)
            Debug.LogWarning("[OmamoriSelector] inputProviderSource が IInputProvider を実装していません", this);
    }

    /// <summary>外部（UIボタン等）から種類を指定したい場合に呼ぶ。</summary>
    public void Select(OmamoriType t)
    {
        if (current == t) return;
        current = t;
        Debug.Log($"[OmamoriSelector] 選択: {current}");
    }

    private void Update()
    {
        if (_input != null)
        {
            // #20: 抽象化された入力から選択番号（0〜4）を受け取る
            int index = _input.OmamoriSelectTriggered;
            if (index >= 0 && index <= 4)
                Select((OmamoriType)index);
            return;
        }

        // フォールバック：数字キーで切り替え（プロバイダ未設定時）。
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
