using UnityEngine;
using Toufuku.Rescue;

/*
    客1人ぶんの「救えた・黒客になった → 神社の評価（#30）」をつなぐコンポーネント

    CustomerState（#54）の onRescued / onBlack を受け取って、ShrineRating に知らせる
    増やす量・減らす量は客の種類ごとの値（付録B B-1。CustomerKindTable）を渡す
    客のプレハブに1つ付けるだけでいい（RescueCustomerSpawner で作ったなら
    プレイ中に自動で AddComponent されるので、付けわすれても動く）
*/
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
