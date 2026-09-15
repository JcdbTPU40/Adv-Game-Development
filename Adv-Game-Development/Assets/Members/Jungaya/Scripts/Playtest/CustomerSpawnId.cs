using System;
using UnityEngine;

namespace Toufuku.Playtest
{
    /*
        客のID（出てきた順の通し番号）と分類（#63）

        ・出す側が作った直後に Assign する。IDは1から付けて、計測プレイが始まったとき（リトライも）に1にもどる
        ・決まったシードのくじは、このIDごとの乱数で引く（PlaytestRandom.TryFor）。作る前にくじを引きたいときは
          NextId を使う（作るのに失敗してIDを付けなければ、次の客が同じIDと同じくじの結果を使う）
        ・ログの相手IDや優先相手IDは、ぜんぶこのID
        ・分類（Category）は T2 でどれを選んだかの分布に使う。客の種類（通常・移動・欲張り・遠方）は #57 / #62 でここに入れる
    */
    [DisallowMultipleComponent]
    public class CustomerSpawnId : MonoBehaviour
    {
        public const string CategoryNormal = "Normal";
        public const string CategoryBlack = "Black";
        public const string CategoryUnregistered = "Unregistered";

        static int s_next = 1;

        // IDを付けた（作った直後。見た目やゲージの設定はこのあと同じフレームでやる）
        public static event Action<CustomerSpawnId> Spawned;
        // IDが付いた客が消された
        public static event Action<CustomerSpawnId> Despawned;

        int _id;
        string _category = CategoryUnregistered;

        public int Id => _id;
        public string Category => _category;

        Vector3? _destination;

        // 立つ場所（歩いて向かう先）。出す側が設定しなければ null（ログは作った場所を使う）
        public Vector3? Destination => _destination;

        public void SetDestination(Vector3 destination)
        {
            _destination = destination;
        }

        // 次に付けるID
        public static int NextId => s_next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_next = 1;
            Spawned = null;
            Despawned = null;
        }

        // IDを1から付けなおす（計測プレイが始まるときに PlaytestLogger が呼ぶ）
        public static void ResetSequence()
        {
            s_next = 1;
        }

        // 客にIDを付ける。もう付いていたら分類だけ変える
        public static CustomerSpawnId Assign(GameObject customer, string category = CategoryNormal)
        {
            if (customer == null) return null;

            CustomerSpawnId c = customer.GetComponent<CustomerSpawnId>();
            if (c == null) c = customer.AddComponent<CustomerSpawnId>();

            bool isNew = c._id <= 0;
            if (isNew) c._id = s_next++;
            c._category = string.IsNullOrEmpty(category) ? CategoryNormal : category;

            if (isNew) Spawned?.Invoke(c);
            return c;
        }

        // 客のIDを返す。出す側が付けていなければここで付ける（シーンに最初から置いてある客など）。null なら 0
        public static int Of(GameObject customer)
        {
            if (customer == null) return 0;
            CustomerSpawnId c = customer.GetComponent<CustomerSpawnId>();
            return c != null && c._id > 0 ? c._id : Assign(customer, CategoryUnregistered).Id;
        }

        public void SetCategory(string category)
        {
            if (!string.IsNullOrEmpty(category)) _category = category;
        }

        void OnDestroy()
        {
            if (_id > 0) Despawned?.Invoke(this);
        }
    }
}
