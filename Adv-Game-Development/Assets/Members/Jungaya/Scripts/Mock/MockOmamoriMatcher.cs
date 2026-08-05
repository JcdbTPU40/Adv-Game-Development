using UnityEngine;

namespace Toufuku.Rescue.Mock
{
    /// <summary>
    /// 「輪郭の色」と「その客の正解お守り」を一致させる橋渡し。
    ///
    /// なぜ要るか（実測で見つかった不具合）:
    ///   <see cref="MockCrowdDirector"/> は輪郭色を 5 色で機械的に巡回させるだけで、
    ///   正解お守りには一切関与しない。一方 <see cref="CustomerProfileApplier"/> は
    ///   Start でカタログからランダムに客タイプを引く。両者は無関係なので、
    ///   輪郭が緑（健康）の客の正解が「厄除け安全」になる、という状態が普通に起きる。
    ///   実測では非黒客 12 体中 11 体が不一致だった（＝偶然一致する 1/5 だけ当たる）。
    ///   これでは「輪郭色で悩みを判別できるか」という #44 の検証そのものが成立しない。
    ///
    /// 何をするか:
    ///   スポーン直後（Start）に <see cref="MockCustomerTag"/> の色インデックスを読み、
    ///   同じ種別の <see cref="CustomerProfile"/> をカタログから引いて客に適用し直す。
    ///   <see cref="CustomerType"/> と <see cref="OmamoriType"/> は宣言順が 1:1
    ///   （健康・学業成就・厄除け安全・縁結び・金運。企画書v3 §3 の並び）なので、
    ///   インデックスをそのまま使える。
    ///
    ///   黒客（色インデックス -1）は「悩みが読めない客」なので、正解はランダムのままにする。
    ///
    /// 前提:
    ///   同じ GameObject の <see cref="CustomerProfileApplier"/> は
    ///   pickRandomOnStart を OFF にしておくこと。ON のままだと Start の実行順によっては
    ///   こちらの割り当てが上書きされる。Prefab Variant 側で OFF にしてある。
    ///
    /// ※ 検証用の使い捨て。Mock/ ごと削除できる。既存の本番系スクリプトは無改変で、
    ///   公開 API（CustomerProfileApplier.Apply / CustomerRescue.Setup）だけを使っている。
    /// </summary>
    [RequireComponent(typeof(MockCustomerTag))]
    public class MockOmamoriMatcher : MonoBehaviour
    {
        [Tooltip("客タイプ定義のカタログ。未設定なら CustomerRescue.Setup(type) だけで種別を合わせる。")]
        [SerializeField] private CustomerProfileCatalog catalog;

        [Tooltip("黒客にも種別を割り当てるか。OFF だと黒客は既定値(健康)のまま。")]
        [SerializeField] private bool randomizeBlackCustomer = true;

        private void Start()
        {
            var tag = GetComponent<MockCustomerTag>();
            var rescue = GetComponent<CustomerRescue>();
            if (tag == null || rescue == null) return;

            int index = tag.ColorIndex;

            if (index < 0)
            {
                // 黒客。輪郭からは悩みが読めないのが仕様なので、正解はランダムにしておく。
                if (!randomizeBlackCustomer) return;
                index = Random.Range(0, 5);
            }

            var type = (CustomerType)index;

            // カタログがあればプロフィールごと適用する（正解お守り・相性テーブル・見た目がまとめて揃う）。
            CustomerProfile profile = catalog != null ? catalog.Get(type) : null;
            if (profile != null)
            {
                var applier = GetComponent<CustomerProfileApplier>();
                if (applier != null)
                {
                    applier.Apply(profile, catalog);
                    return;
                }

                rescue.Setup(profile, catalog.AffinityTable);
                return;
            }

            // カタログが無い／その種別が未登録なら、種別だけ合わせる。
            // 相性テーブルはプレハブに設定済みのものがそのまま使われる。
            rescue.Setup(type);
        }
    }
}
