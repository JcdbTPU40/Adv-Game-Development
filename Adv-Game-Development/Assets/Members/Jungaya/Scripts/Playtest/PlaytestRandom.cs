using UnityEngine;

namespace Toufuku.Playtest
{
    /// <summary>乱数の用途。値を変えると同じシードでも列が変わるので、並べ替え・使い回しをしないこと。</summary>
    public static class PlaytestStreams
    {
        /// <summary>定位置の敷き詰め（MockCrowdDirector）。</summary>
        public const int SlotLayout = 1;
        /// <summary>スポーン位置（空き定位置の選択・X 座標）。</summary>
        public const int Placement = 2;
        /// <summary>黒客かどうか。</summary>
        public const int Identity = 3;
        /// <summary>初期ゲージ（視認性モックの初期危険度）。</summary>
        public const int Gauge = 4;
        /// <summary>客タイプ（正解お守り）。</summary>
        public const int Profile = 5;
        /// <summary>危険度Dが100になるまでの秒数（客種ごとの幅。通常客 15〜25秒。付録B B-1）。</summary>
        public const int DangerSeconds = 6;
        /// <summary>客種（通常・移動・遠方・欲張り。#62）。</summary>
        public const int Kind = 7;
        /// <summary>移動客の往復で歩き出す向き（#62）。</summary>
        public const int Motion = 8;
    }

    /// <summary>
    /// 計測プレイのシード — Issue #63
    ///
    /// ・<see cref="PlaytestLogger"/> がシードを決めて <see cref="Control"/> する。シードはログのファイル名とヘッダーに残る。
    /// ・スポーン側は <see cref="TryFor"/> で「用途 × 客ID」の列を受け取る。計測ロガーの無いシーン（未制御）では null が返り、
    ///   従来どおり UnityEngine.Random を使う（#44 のモックシーンなどの挙動は変わらない）。
    /// ・null を渡しても使える <see cref="Value"/> / <see cref="Range(DeterministicRandom, int, int)"/> で、呼び出し側の分岐を省く。
    /// </summary>
    public static class PlaytestRandom
    {
        /// <summary>計測ロガーがシードを握っているか。</summary>
        public static bool IsControlled { get; private set; }

        /// <summary>現在のシード（<see cref="IsControlled"/> が false の間は意味を持たない）。</summary>
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

        /// <summary>新しいシード（正の値）を作る。</summary>
        public static int NewSeed()
        {
            int seed = (int)(DeterministicRandom.Mix((ulong)System.DateTime.UtcNow.Ticks) & 0x7FFFFFFFUL);
            return seed == 0 ? 1 : seed;
        }

        /// <summary>用途 × 番号の列。未制御なら null。</summary>
        public static DeterministicRandom TryFor(int stream, int index)
        {
            return IsControlled ? DeterministicRandom.Derive(Seed, stream, index) : null;
        }

        /// <summary>客（<see cref="CustomerSpawnId"/> 付き）ごとの列。未制御・ID なしなら null。</summary>
        public static DeterministicRandom TryForCustomer(GameObject customer, int stream)
        {
            if (!IsControlled || customer == null) return null;
            CustomerSpawnId id = customer.GetComponent<CustomerSpawnId>();
            return id != null && id.Id > 0 ? DeterministicRandom.Derive(Seed, stream, id.Id) : null;
        }

        /// <summary>用途ごとの子シード。</summary>
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
