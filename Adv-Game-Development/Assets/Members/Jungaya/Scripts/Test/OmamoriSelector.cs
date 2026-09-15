using UnityEngine;
using Toufuku.Rescue;
using Toufuku.GameInput;

/*
    今投げるお守りの「今の種類」を持っておくクラス（#9 のとりあえずの実装 ＋ #10 のテスト用の足場）

    ・選ぶ入力（数字キー1〜5 みたいなもの）で種類を切りかえて、画面の左上に今の種類を表示する
    ・発射する側（Shoot_Bullet / Shoot / TestShooter など）は Current を読んで、
      作った弾の OmamoriBullet.SetType に入れる

    #20: 入力は IInputProvider を通す（入っていなければキーボードを直接読む）
    ちゃんとした選ぶUI（#9）が決まったら、この入力の部分だけ入れかえればいい
    （発射する側は Current を読むだけなので、えいきょうはない）
*/
public class OmamoriSelector : MonoBehaviour
{
    [Header("現在選択中のお守り（テスト時はInspectorからも変更可）")]
    [SerializeField] private OmamoriType current = OmamoriType.Kenkou;

    [Header("テスト表示（本番UIができたらオフに）")]
    [SerializeField] private bool showDebugLabel = true;

    [Header("入力の供給元（#20）。IInputProvider 実装をドラッグ。未設定ならキーボード直読み")]
    [SerializeField] private MonoBehaviour inputProviderSource;

    private IInputProvider _input;

    // 今選んでいるお守りの種類。発射する側はこれを読んで弾にのせる
    public OmamoriType Current => current;

    private void Awake()
    {
        _input = inputProviderSource as IInputProvider;
        if (inputProviderSource != null && _input == null)
            Debug.LogWarning("[OmamoriSelector] inputProviderSource が IInputProvider を実装していません", this);
    }

    // 外（UI のボタンなど）から種類を決めたいときに呼ぶ
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
            // #20: まとめた入力から、選んだ番号（0〜4）を受け取る
            int index = _input.OmamoriSelectTriggered;
            if (index >= 0 && index <= 4)
                Select((OmamoriType)index);
            return;
        }

        // 予備: 数字キーで切りかえる（プロバイダーが入っていないとき）。企画書 v3 §3 の enum の順番
        if (Input.GetKeyDown(KeyCode.Alpha1)) Select(OmamoriType.Kenkou);
        else if (Input.GetKeyDown(KeyCode.Alpha2)) Select(OmamoriType.Gakugyou);
        else if (Input.GetKeyDown(KeyCode.Alpha3)) Select(OmamoriType.Yakuyoke);
        else if (Input.GetKeyDown(KeyCode.Alpha4)) Select(OmamoriType.Enmusubi);
        else if (Input.GetKeyDown(KeyCode.Alpha5)) Select(OmamoriType.Kinun);
    }

    private void OnGUI()
    {
        if (!showDebugLabel) return;
        GUI.Label(
            new Rect(10, 10, 480, 24),
            $"お守り: {current}   (1:健康 2:学業成就 3:厄除け安全 4:縁結び 5:金運)");
    }
}
