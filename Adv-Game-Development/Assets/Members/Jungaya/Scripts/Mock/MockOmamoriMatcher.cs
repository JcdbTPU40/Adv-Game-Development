using UnityEngine;

namespace Toufuku.Rescue.Mock
{
    /*
        「輪郭の色」と「その客の正解のお守り」を同じにするためのクラス

        なんで必要か（実際に測って見つかったバグ）:
          MockCrowdDirector は輪郭の色を5色で順番に回すだけで、
          正解のお守りには何もしていない。いっぽう CustomerProfileApplier は
          Start でカタログからランダムに客のタイプを選ぶ。この2つは関係ないので、
          輪郭が緑（健康）の客の正解が「厄除け安全」になる、みたいなことがふつうに起きる
          実際に測ったら、黒客じゃない12人のうち11人がちがっていた（たまたま合う 1/5 だけ当たる）
          これだと「輪郭の色でなやみを見分けられるか」という #44 の検証そのものができない

        何をするか:
          出てきた直後（Start）に MockCustomerTag の色の番号を読んで、
          同じ種類の CustomerProfile をカタログから取ってきて客に入れなおす
          CustomerType と OmamoriType は書いてある順番が1対1で同じ
          （健康・学業成就・厄除け安全・縁結び・金運。企画書 v3 §3 の順番）なので、
          番号をそのまま使える

          黒客（色の番号が -1）は「なやみが読めない客」なので、正解はランダムのままにする

        気をつけること:
          同じ GameObject の CustomerProfileApplier は
          pickRandomOnStart を OFF にしておくこと。ON のままだと、Start が動く順番によっては
          こっちで決めたのが上書きされる。Prefab Variant のほうで OFF にしてある

        ※ 検証用の使い捨て。Mock/ フォルダごと消せる。もとからある本番のスクリプトはさわっていなくて、
          公開されてるメソッド（CustomerProfileApplier.Apply / CustomerRescue.Setup）だけを使っている
    */
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
                // 黒客。輪郭からはなやみが読めないのが仕様なので、正解はランダムにしておく
                if (!randomizeBlackCustomer) return;
                // #63: 計測プレイ中は、客IDごとに決まったシードの乱数でくじを引く
                index = Toufuku.Playtest.PlaytestRandom.Range(
                    Toufuku.Playtest.PlaytestRandom.TryForCustomer(gameObject, Toufuku.Playtest.PlaytestStreams.Profile), 0, 5);
            }

            var type = (CustomerType)index;

            // カタログがあればプロフィールごと入れる（正解のお守り・相性の表・見た目がまとめてそろう）
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

            /*
                カタログがない、またはその種類が登録されていないときは、種類だけ合わせる
                相性の表はプレハブに入っているものがそのまま使われる
            */
            rescue.Setup(type);
        }
    }
}
