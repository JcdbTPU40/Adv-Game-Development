using UnityEngine;
using Toufuku.Rescue;

/// <summary>
/// 客1体分の「救済成功/黒客化 → 神社評価(#30)」結線コンポーネント。
///
/// CustomerState(#54) の onRescued / onBlack を購読し、ShrineRating へ報告する。
/// 増減量は客種ごとの値（付録B B-1。CustomerKindTable）を渡す。
/// 客プレハブに1つ付けるだけでよい（RescueCustomerSpawner 経由の生成なら
/// 実行時に自動で AddComponent されるので、付け忘れても動く）。
/// </summary>
[RequireComponent(typeof(CustomerState))]
public class ShrineRatingHook : MonoBehaviour
{
    CustomerState _state;

    void Awake()
    {
        _state = GetComponent<CustomerState>();
    }

    void OnEnable()
    {
        _state.onRescued.AddListener(ReportRescued);
        _state.onBlack.AddListener(ReportBlack);
    }

    void OnDisable()
    {
        _state.onRescued.RemoveListener(ReportRescued);
        _state.onBlack.RemoveListener(ReportBlack);
    }

    void ReportRescued()
    {
        if (ShrineRating.Instance != null)
            ShrineRating.Instance.RegisterResolved(_state.RatingGainOnRescue);
    }

    void ReportBlack()
    {
        if (ShrineRating.Instance != null)
            ShrineRating.Instance.RegisterAngry(_state.RatingLossOnBlack);
    }
}
