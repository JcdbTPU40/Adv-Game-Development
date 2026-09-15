using UnityEngine;

namespace Toufuku.Playtest
{
    // 乱数の使いみち。値を変えると同じシードでも乱数が変わるので、ならべかえたり使いまわしたりしないこと
    public static class PlaytestStreams
    {
        // 定位置のしきつめ（MockCrowdDirector）
        public const int SlotLayout = 1;
        // 出てくる位置（空いている定位置を選ぶのと X 座標）
        public const int Placement = 2;
        // 黒客かどうか
        public const int Identity = 3;
        // 最初のゲージ（視認性モックの最初の危険度）
        public const int Gauge = 4;
        // 客のタイプ（正解のお守り）
        public const int Profile = 5;
        // 危険度Dが100になるまでの秒数（客の種類ごとのはば。通常客は 15〜25秒。付録B B-1）
        public const int DangerSeconds = 6;
        // 客の種類（通常・移動・遠方・欲張り。#62）
        public const int Kind = 7;
        // 移動客が往復するときに歩き出す向き（#62）
        public const int Motion = 8;
    }

    /*
        計測プレイのシードを管理するクラス（#63）

        ・PlaytestLogger がシードを決めて Control する。シードはログのファイル名とヘッダーに残る
        ・出す側は TryFor で「使いみち × 客ID」の乱数を受け取る。計測ロガーがないシーン（管理されていない）では null が返ってきて、
          前と同じで UnityEngine.Random を使う（#44 のモックのシーンなどの動きは変わらない）
        ・null を渡しても使える Value / Range(DeterministicRandom, int, int) で、呼ぶ側が if で分けなくてすむようにしている
    */
    public static class PlaytestRandom
    {
        // 計測ロガーがシードを管理しているかどうか
        public static bool IsControlled { get; private set; }

        // 今のシード（IsControlled が false の間は意味がない）
        public static int Seed { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            IsControlled = false;
            Seed = 0;
        }

        public static void Control(int seed)
        {
            Seed = seed;
            IsControlled = true;
        }

        public static void Release()
        {
            IsControlled = false;
        }

        // 新しいシード（プラスの値）を作る
        public static int NewSeed()
        {
            int seed = (int)(DeterministicRandom.Mix((ulong)System.DateTime.UtcNow.Ticks) & 0x7FFFFFFFUL);
            return seed == 0 ? 1 : seed;
        }

        // 使いみち × 番号の乱数。管理されていなければ null
        public static DeterministicRandom TryFor(int stream, int index)
        {
            return IsControlled ? DeterministicRandom.Derive(Seed, stream, index) : null;
        }

        // 客（CustomerSpawnId が付いている）ごとの乱数。管理されていない・IDがないなら null
        public static DeterministicRandom TryForCustomer(GameObject customer, int stream)
        {
            if (!IsControlled || customer == null) return null;
            CustomerSpawnId id = customer.GetComponent<CustomerSpawnId>();
            return id != null && id.Id > 0 ? DeterministicRandom.Derive(Seed, stream, id.Id) : null;
        }

        // 使いみちごとの子どものシード
        public static int DeriveSeed(int stream)
        {
            return DeterministicRandom.DeriveSeed(Seed, stream);
        }

        public static float Value(DeterministicRandom rng)
        {
            return rng != null ? rng.Value() : Random.value;
        }

        public static int Range(DeterministicRandom rng, int minInclusive, int maxExclusive)
        {
            return rng != null ? rng.Range(minInclusive, maxExclusive) : Random.Range(minInclusive, maxExclusive);
        }

        public static float Range(DeterministicRandom rng, float min, float max)
        {
            return rng != null ? rng.Range(min, max) : Random.Range(min, max);
        }
    }
}
