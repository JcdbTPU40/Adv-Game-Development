using UnityEngine;
using Toufuku.Rescue;

public class OmamoriBullet : MonoBehaviour
{
    // このお守りのタイプ。選択ボタン(#9)で弾を生成するときに SetType でセットする想定。
    // インスペクタからもテスト用に設定できるように SerializeField にしておく。
    [SerializeField] private OmamoriType type = OmamoriType.Kenkou;
    [SerializeField] Shoot shoot;
    /// <summary>
    /// 弾生成時にお守りタイプを差し込む用（#9 の発射側から呼ぶ）。
    /// </summary>
    public void SetType(OmamoriType t)
    {
        type = t;

    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Customer"))
        {
            // 当たった位置から命中ゾーン（中心/中/外）を判定
            Vector3 hitPoint = collision.GetContact(0).point;
            HitZoneTarget target = collision.gameObject.GetComponent<HitZoneTarget>();
            HitZone zone = target != null ? target.EvaluateZone(hitPoint) : HitZone.Inner;

            // 救済判定(#13) → スコア。#60 の着弾点判定（OmamoriProjectile）と共通の処理。
            // 客の退場（救済成功/失敗）は CustomerRescue が管理するので、ここでは破棄しない。
            OmamoriHitResolver.ApplyHit(collision.gameObject, type, zone);
            Destroy(gameObject); // 通常弾は単体ヒット → 当たったら消す
        }
        else
        {
            // 客以外（地面・壁など）に当たった＝外し → コンボ途切れ
            OmamoriHitResolver.ApplyMiss();
            Destroy(gameObject);
        }
    }
}
