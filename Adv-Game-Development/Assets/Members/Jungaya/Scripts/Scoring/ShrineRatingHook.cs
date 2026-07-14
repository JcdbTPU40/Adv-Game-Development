using UnityEngine;
using Toufuku.Rescue;

/// <summary>
/// 客1体分の「解消/怒り → 神社評価(#30)」結線コンポーネント。
///
/// CustomerMood の onResolved / onAngry を購読し、ShrineRating へ報告する。
/// 客プレハブに1つ付けるだけでよい（RescueCustomerSpawner 経由の生成なら
/// 実行時に自動で AddComponent されるので、付け忘れても動く）。
/// </summary>
[RequireComponent(typeof(CustomerMood))]
public class ShrineRatingHook : MonoBehaviour
{
    CustomerMood _mood;

    void Awake()
    {
        _mood = GetComponent<CustomerMood>();
    }

    void OnEnable()
    {
        _mood.onResolved.AddListener(ReportResolved);
        _mood.onAngry.AddListener(ReportAngry);
    }

    void OnDisable()
    {
        _mood.onResolved.RemoveListener(ReportResolved);
        _mood.onAngry.RemoveListener(ReportAngry);
    }

    void ReportResolved()
    {
        if (ShrineRating.Instance != null)
            ShrineRating.Instance.RegisterResolved();
    }

    void ReportAngry()
    {
        if (ShrineRating.Instance != null)
            ShrineRating.Instance.RegisterAngry();
    }
}
