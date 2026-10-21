using UnityEngine;
using Toufuku.Rescue;

public class OmamoriBullet : MonoBehaviour
{
    /*
        このお守りのタイプ。選ぶボタン（#9）で弾を作るときに SetType で入れるつもり
        インスペクターからもテスト用に設定できるように SerializeField にしておく
    */
    [Header("お守りの種類")]
    [SerializeField] private OmamoriType type = OmamoriType.Kenkou;

    [SerializeField] private Shoot shoot;

    [Header("お守りPrefab（OmamoriTypeの順番）")]
    [SerializeField] private GameObject[] omamoriPrefab = new GameObject[5];

    [Header("子オブジェクトのTransform")]
    [SerializeField] private Vector3 localPosition = Vector3.zero;
    [SerializeField] private Vector3 localRotation = Vector3.zero;
    [SerializeField] private Vector3 localScale = Vector3.one;

    private GameObject currentOmamori;
    private bool typeAssigned;

    public void SetType(OmamoriType t)
    {
        type = t;
        typeAssigned = true;

        CreateOmamoriModel();
    }

    private void CreateOmamoriModel()
    {
        int index = (int)type;

        if (omamoriPrefab == null ||
            index < 0 ||
            index >= omamoriPrefab.Length ||
            omamoriPrefab[index] == null)
        {
            Debug.LogWarning(
                $"お守りPrefabが設定されていません。Type: {type}",
                this
            );
            return;
        }

        currentOmamori = Instantiate(
            omamoriPrefab[index],
            transform
        );

        currentOmamori.transform.localPosition = localPosition;
        currentOmamori.transform.localRotation =
            Quaternion.Euler(localRotation);
        currentOmamori.transform.localScale = localScale;
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
