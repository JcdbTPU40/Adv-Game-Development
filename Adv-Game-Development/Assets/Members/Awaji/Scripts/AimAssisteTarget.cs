
using System.Collections.Generic;
using UnityEngine;

public class AimAssistTarget : MonoBehaviour
{
    // シーン内で有効なロック対象を管理する
    public static readonly List<AimAssistTarget> ActiveTargets =
        new List<AimAssistTarget>();

    [Header("照準対象位置")]
    [SerializeField]
    private Transform aimPoint;

    // aimPoint が設定されていればその位置、
    // 未設定ならこのオブジェクトの位置を使う
    public Vector3 AimPosition
    {
        get
        {
            return aimPoint != null
                ? aimPoint.position
                : transform.position;
        }
    }

    private void OnEnable()
    {
        if (!ActiveTargets.Contains(this))
        {
            ActiveTargets.Add(this);
        }
    }

    private void OnDisable()
    {
        ActiveTargets.Remove(this);
    }
}
