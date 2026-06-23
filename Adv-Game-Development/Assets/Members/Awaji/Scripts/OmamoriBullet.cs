using UnityEngine;

public class OmamoriBullet : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    // TODO: このお守りのタイプ。相性判定をここに足す（合わなければ Miss 扱い）
    // public OmamoriType type;

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Customer"))
        {
            // 当たった位置から命中ゾーン（中心/中/外）を判定
            Vector3 hitPoint = collision.GetContact(0).point;
            HitZoneTarget target = collision.gameObject.GetComponent<HitZoneTarget>();
            HitZone zone = target != null ? target.EvaluateZone(hitPoint) : HitZone.Inner;

            // TODO: お守りの相性が合わなければ zone = HitZone.Miss にしてコンボを切る
            //       例) if (!IsCompatible(target, this.type)) zone = HitZone.Miss;

            if (ScoreManager.Instance != null)
                ScoreManager.Instance.RegisterHit(zone);

            Destroy(collision.gameObject);
            Destroy(gameObject); // 通常弾は単体ヒット → 当たったら消す
        }
        else
        {
            // 客以外（地面・壁など）に当たった＝外し → コンボ途切れ
            if (ScoreManager.Instance != null)
                ScoreManager.Instance.RegisterMiss();

            Destroy(gameObject);
        }
    }
}
