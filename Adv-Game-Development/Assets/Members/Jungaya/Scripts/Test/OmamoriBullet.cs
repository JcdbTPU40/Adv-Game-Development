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

            // 救済判定(#13)：お守りの種類を客に渡し、相性◯/✗とゲージ増減を処理させる。
            CustomerRescue rescue = collision.gameObject.GetComponent<CustomerRescue>();
            if (rescue != null)
            {
                // すでに結末確定済み（解消/怒り）の客への追撃。
                if (rescue.IsResolved)
                {
                    // 解消済み（救済成功）への再ヒット＝過剰押し売り（#33 案B）。
                    // 縁が少し入り、神社評価(#30)が微減する。怒り退場中はノーカウントのまま。
                    if (rescue.Mood != null && rescue.Mood.IsResolved && ScoreManager.Instance != null)
                        ScoreManager.Instance.RegisterOverSell();

                    Destroy(gameObject);
                    return;
                }

                Affinity affinity = rescue.ApplyHit(type);

                // 相性が合わなければ Miss 扱いにしてコンボを切る（誤投擲フィードバックは #14）。
                if (affinity == Affinity.Bad)
                    zone = HitZone.Miss;
            }

            if (ScoreManager.Instance != null)
                ScoreManager.Instance.RegisterHit(zone);

            // 客の退場（救済成功/失敗）は CustomerRescue が管理するので、ここでは破棄しない。
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
