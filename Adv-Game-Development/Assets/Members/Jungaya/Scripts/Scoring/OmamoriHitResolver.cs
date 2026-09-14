using UnityEngine;
using Toufuku.Rescue;

/// <summary>
/// お守りが参拝客に当たったときの共通処理（救済判定 → スコア）— #13 / #22 / #60
///
/// 物理衝突で当てる OmamoriBullet と、着弾点で判定する OmamoriProjectile（#60）の両方から呼ぶ。
/// </summary>
public static class OmamoriHitResolver
{
    /// <summary>
    /// 客にお守りが当たったことを処理する。
    /// </summary>
    /// <param name="customer">当たった客</param>
    /// <param name="type">お守りの種類</param>
    /// <param name="zone">命中精度から決めたゾーン</param>
    /// <returns>スコアへ渡したゾーン。相性✗なら Miss。結末確定済みの客なら何も計上せず Miss。</returns>
    public static HitZone ApplyHit(GameObject customer, OmamoriType type, HitZone zone)
    {
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
        }

        if (ScoreManager.Instance != null)
            ScoreManager.Instance.RegisterHit(zone);

        return zone;
    }

    /// <summary>客以外（地面など）に落ちた＝外し。コンボが途切れる。</summary>
    public static void ApplyMiss()
    {
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.RegisterMiss();
    }
}
