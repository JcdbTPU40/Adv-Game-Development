using System;
using UnityEngine;

namespace Toufuku.Playtest
{
    /// <summary>
    /// 客の ID（スポーン順の通し番号）と分類 — Issue #63
    ///
    /// ・スポーン側が生成直後に <see cref="Assign"/> する。ID は 1 から振り、計測プレイの開始（リトライ含む）で 1 に戻る。
    /// ・固定シードの抽選はこの ID ごとの列で行う（<see cref="PlaytestRandom.TryFor"/>）。生成前に抽選したいときは
    ///   <see cref="NextId"/> を使う（生成に失敗して ID を振らなければ、次の客が同じ ID と同じ抽選結果を使う）。
    /// ・ログの対象ID・優先対象ID はすべてこの ID。
    /// ・分類（<see cref="Category"/>）は T2 の選択の分布に使う。客種（通常・移動・欲張り・遠方）は #57 / #62 でここへ入れる。
    /// </summary>
    [DisallowMultipleComponent]
    public class CustomerSpawnId : MonoBehaviour
    {
        public const string CategoryNormal = "Normal";
        public const string CategoryBlack = "Black";
        public const string CategoryUnregistered = "Unregistered";

        static int s_next = 1;

        /// <summary>ID を振った（生成直後。見た目・ゲージの設定はこのあと同じフレームで行われる）。</summary>
        public static event Action<CustomerSpawnId> Spawned;
        /// <summary>ID 付きの客が破棄された。</summary>
        public static event Action<CustomerSpawnId> Despawned;

        int _id;
        string _category = CategoryUnregistered;

        public int Id => _id;
        public string Category => _category;

        Vector3? _destination;

        /// <summary>立ち位置（歩いて向かう先）。スポーン側が設定しなければ null（ログは生成位置を使う）。</summary>
        public Vector3? Destination => _destination;

        public void SetDestination(Vector3 destination)
        {
            _destination = destination;
        }

        /// <summary>次に振る ID。</summary>
        public static int NextId => s_next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_next = 1;
            Spawned = null;
            Despawned = null;
        }

        /// <summary>ID を 1 から振り直す（計測プレイの開始時に PlaytestLogger が呼ぶ）。</summary>
        public static void ResetSequence()
        {
            s_next = 1;
        }

        /// <summary>客に ID を振る。振り済みなら分類だけ更新する。</summary>
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

        /// <summary>客の ID。スポーン側が振っていなければここで振る（シーンに最初から置いた客など）。null なら 0。</summary>
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
