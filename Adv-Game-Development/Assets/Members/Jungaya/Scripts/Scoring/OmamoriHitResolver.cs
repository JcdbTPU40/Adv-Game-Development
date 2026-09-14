using System;
using UnityEngine;
using Toufuku.Rescue;
using Toufuku.Rescue.Mock;

/// <summary>客への命中を判定し終えた結果（#64 の命中音・救済音・振動用）。</summary>
public readonly struct OmamoriHitInfo
{
    /// <summary>当たった客。</summary>
    public readonly GameObject Customer;
    public readonly OmamoriType Type;
    /// <summary>スコアへ渡したゾーン。相性✗なら Miss。</summary>
    public readonly HitZone Zone;
    /// <summary>黒客に当たったか。</summary>
    public readonly bool IsBlackCustomer;
    /// <summary>この命中で救済が確定したか（ゲージが 0 になった）。</summary>
    public readonly bool Rescued;
    /// <summary>計上後の福の連なり（ScoreManager.Combo）。</summary>
    public readonly int Combo;
    /// <summary>着弾の起点時刻（Time.realtimeSinceStartupAsDouble）。フィードバック予算の計測に使う。</summary>
    public readonly double ImpactTime;

    /// <summary>相性◯で命中として計上されたか。</summary>
    public bool IsGoodHit => Zone != HitZone.Miss;

    public OmamoriHitInfo(GameObject customer, OmamoriType type, HitZone zone, bool isBlackCustomer, bool rescued, int combo, double impactTime)
    {
        Customer = customer;
        Type = type;
        Zone = zone;
        IsBlackCustomer = isBlackCustomer;
        Rescued = rescued;
        Combo = combo;
        ImpactTime = impactTime;
    }
}

/// <summary>
/// お守りが参拝客に当たったときの共通処理（救済判定 → スコア）— #13 / #22 / #60
///
/// 物理衝突で当てる OmamoriBullet と、着弾点で判定する OmamoriProjectile（#60）の両方から呼ぶ。
/// #64: 判定し終えた同じ呼び出しの中で <see cref="HitResolved"/> を発火する（命中音・救済音を判定と同時刻に鳴らすため）。
/// </summary>
public static class OmamoriHitResolver
{
    /// <summary>客への命中を判定し終えた（スコア計上後）。結末確定済みの客への命中では発火しない。</summary>
    public static event Action<OmamoriHitInfo> HitResolved;

    /// <summary>
    /// 客にお守りが当たったことを処理する。
    /// </summary>
    /// <param name="customer">当たった客</param>
    /// <param name="type">お守りの種類</param>
    /// <param name="zone">命中精度から決めたゾーン</param>
    /// <param name="impactTime">着弾の起点時刻（realtimeSinceStartupAsDouble）。省略時は呼び出した時刻</param>
    /// <returns>スコアへ渡したゾーン。相性✗なら Miss。結末確定済みの客なら何も計上せず Miss。</returns>
    public static HitZone ApplyHit(GameObject customer, OmamoriType type, HitZone zone, double? impactTime = null)
    {
        double impact = impactTime ?? Time.realtimeSinceStartupAsDouble;
        bool rescued = false;

        // 救済判定(#13)：お守りの種類を客に渡し、相性◯/✗とゲージ増減を処理させる。
        CustomerRescue rescue = customer != null ? customer.GetComponent<CustomerRescue>() : null;
        if (rescue != null)
        {
            // すでに結末確定済み（解消/怒り）の客への追撃に対する防御的ガード。
            // 過剰押し売り（#33 案B）は企画書 v3 §16【B】で廃案。結末確定時に当たり判定を
            // 消す仕様（CustomerMood.DisableHitDetection）により通常ここには到達しない。
            // 到達した場合はコンポーネントの設定漏れなので、スコアもミスも一切計上しない。
            if (rescue.IsResolved)
            {
                Debug.LogWarning("[OmamoriHitResolver] 結末確定済みの客に命中しました（当たり判定の無効化漏れの疑い）", customer);
                return HitZone.Miss;
            }

            Affinity affinity = rescue.ApplyHit(type);

            // 相性が合わなければ Miss 扱いにしてコンボを切る（誤投擲フィードバックは #14）。
            if (affinity == Affinity.Bad)
                zone = HitZone.Miss;

            // 直前まで未確定だったので、ここで解消していればこの命中で救済が確定した
            rescued = rescue.Mood != null && rescue.Mood.IsResolved;
        }

        if (ScoreManager.Instance != null)
            ScoreManager.Instance.RegisterHit(zone);

        int combo = ScoreManager.Instance != null ? ScoreManager.Instance.Combo : 0;
        HitResolved?.Invoke(new OmamoriHitInfo(customer, type, zone, IsBlackCustomer(customer), rescued, combo, impact));

        return zone;
    }

    /// <summary>客以外（地面など）に落ちた＝外し。コンボが途切れる。</summary>
    public static void ApplyMiss()
    {
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.RegisterMiss();
    }

    /// <summary>
    /// 黒客か。本番の黒客データはまだ無いので、視認性モック(#44)の札（MockCustomerTag）で判定する。
    /// 本番の客データに黒客フラグが入ったら、ここだけ差し替える。
    /// </summary>
    public static bool IsBlackCustomer(GameObject customer)
    {
        if (customer == null) return false;
        MockCustomerTag tag = customer.GetComponent<MockCustomerTag>();
        return tag != null && tag.IsBlack;
    }
}
