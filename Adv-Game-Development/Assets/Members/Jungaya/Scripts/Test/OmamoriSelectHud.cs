using UnityEngine;
using UnityEngine.UI;
using Toufuku.Rescue;

/// <summary>
/// いま選択中のお守りを画面右下に大きく表示するHUD。
/// OmamoriSelector.Current を毎フレーム監視し、種類ごとの色で四角(Image)を塗り、
/// 種類名を Text に出す。左上の小さいデバッグ表示(OnGUI)より視認しやすい。
///
/// 色と名前は Inspector から変更できる（OmamoriType の enum 順: 健康/学業/恋愛/金運/厄除け）。
/// 正式な選択UI（#9）ができたらこのHUDごと差し替えればよい。
/// </summary>
public class OmamoriSelectHud : MonoBehaviour
{
    [Header("監視する選択ソース")]
    [SerializeField] OmamoriSelector selector;

    [Header("表示先")]
    [Tooltip("単色で塗る四角")]
    [SerializeField] Image icon;
    [Tooltip("種類名の文字。未設定なら色だけ表示")]
    [SerializeField] Text label;

    [Header("種類ごとの見た目（OmamoriType の enum 順）")]
    [SerializeField] Color[] typeColors =
    {
        new Color(0.30f, 0.75f, 0.35f), // 健康   = 緑
        new Color(0.25f, 0.50f, 0.90f), // 学業   = 青
        new Color(0.95f, 0.55f, 0.75f), // 恋愛   = ピンク
        new Color(0.95f, 0.80f, 0.20f), // 金運   = 金
        new Color(0.60f, 0.40f, 0.85f), // 厄除け = 紫
    };
    [SerializeField] string[] typeNames = { "健康", "学業", "恋愛", "金運", "厄除け" };

    OmamoriType _last = (OmamoriType)(-1); // 初回に必ず反映されるよう enum 外の値で開始

    void Start()
    {
        // Text の font が未設定だと何も描かれないので、組み込みフォントを入れておく
        if (label != null && label.font == null)
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        Apply();
    }

    void Update()
    {
        if (selector != null && selector.Current != _last)
            Apply();
    }

    void Apply()
    {
        if (selector == null) return;
        _last = selector.Current;

        int i = (int)_last;
        if (icon != null && i >= 0 && i < typeColors.Length)
            icon.color = typeColors[i];
        if (label != null)
            label.text = (i >= 0 && i < typeNames.Length) ? $"{i + 1}\n{typeNames[i]}" : _last.ToString();
    }
}
