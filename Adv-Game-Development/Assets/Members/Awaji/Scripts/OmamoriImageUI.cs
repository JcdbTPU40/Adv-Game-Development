
using UnityEngine;
using UnityEngine.UI;
using Toufuku.Rescue;

public class OmamoriImageUI : MonoBehaviour
{
    [Header("お守りの選択元")]
    [SerializeField] private OmamoriSelector selector;

    [Header("表示するUI Image")]
    [SerializeField] private Image omamoriImage;

    [Header("OmamoriTypeの順番に対応する画像")]
    [SerializeField] private Sprite[] omamoriSprites = new Sprite[5];

    [Header("画像の設定")]
    [SerializeField] private bool preserveAspect = true;

    private OmamoriType previousType;

    private void Start()
    {
        if (selector == null)
            selector = FindAnyObjectByType<OmamoriSelector>();

        if (selector == null)
        {
            Debug.LogError(
                "[OmamoriImageUI] OmamoriSelectorが見つかりません。",
                this
            );
            enabled = false;
            return;
        }

        if (omamoriImage == null)
        {
            Debug.LogError(
                "[OmamoriImageUI] 表示先のImageが設定されていません。",
                this
            );
            enabled = false;
            return;
        }

        omamoriImage.preserveAspect = preserveAspect;

        // 初期選択のお守りを表示
        UpdateImage(selector.Current);
    }

    private void Update()
    {
        if (selector == null) return;

        // 選択中の種類が変わったときだけ更新
        if (previousType != selector.Current)
            UpdateImage(selector.Current);
    }

    private void UpdateImage(OmamoriType type)
    {
        previousType = type;

        int index = (int)type;

        if (omamoriSprites == null ||
            index < 0 ||
            index >= omamoriSprites.Length ||
            omamoriSprites[index] == null)
        {
            omamoriImage.enabled = false;

            Debug.LogWarning(
                $"[OmamoriImageUI] {type} に対応する画像が設定されていません。",
                this
            );
            return;
        }

        omamoriImage.sprite = omamoriSprites[index];
        omamoriImage.enabled = true;

        Debug.Log($"[OmamoriImageUI] 画像更新: {type}", this);
    }
}
