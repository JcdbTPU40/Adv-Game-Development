using UnityEngine;
using Toufuku.Rescue;

public class OmamoriBullet : MonoBehaviour
{
    /*
        このお守りのタイプ。選ぶボタン（#9）で弾を作るときに SetType で入れるつもり
        インスペクターからもテスト用に設定できるように SerializeField にしておく
    */
    [SerializeField] private OmamoriType type = OmamoriType.Kenkou;
    [SerializeField] Shoot shoot;
    // 弾を作るときにお守りのタイプを入れる用（#9 の発射する側から呼ぶ）
    public void SetType(OmamoriType t)
    {
        type = t;

    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Customer"))
        {
            // 当たった位置から命中ゾーン（中心/中/外）を判定する
            Vector3 hitPoint = collision.GetContact(0).point;
            HitZoneTarget target = collision.gameObject.GetComponent<HitZoneTarget>();
            HitZone zone = target != null ? target.EvaluateZone(hitPoint) : HitZone.Inner;

            /*
                救済の判定（#13）→ スコア。#60 の着弾点の判定（OmamoriProjectile）と同じ処理
                客が帰る（救えた/失敗）のは CustomerRescue が管理しているので、ここでは消さない
            */
            OmamoriHitResolver.ApplyHit(collision.gameObject, type, zone);
            Destroy(gameObject); // ふつうの弾は1人に当たる → 当たったら消す
        }
        else
        {
            // 客以外（地面やかべなど）に当たった＝外れ → コンボが切れる
            OmamoriHitResolver.ApplyMiss();
            Destroy(gameObject);
        }
    }
}
