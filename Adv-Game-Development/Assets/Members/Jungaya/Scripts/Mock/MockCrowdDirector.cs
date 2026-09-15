using System.Collections.Generic;
using UnityEngine;
using Toufuku.Playtest;

namespace Toufuku.Rescue.Mock
{
    /// <summary>
    /// 視認性モック(#44)の中枢。「常時補充制」で境内の体数を目標値に保ち続ける。
    ///
    /// 目的:
    ///   祭事想定の最大15体が同時に並んだとき、小中学生が一瞬で
    ///   「どの客が何の悩みか」「ゲージ残量がどれくらいか」を識別できるかを実測する。
    ///   本番のスポーン制御ではないので、既存の <see cref="RescueCustomerSpawner"/> とは独立。
    ///
    /// 体数の決め方:
    ///   目標 = overrideTargetCount >= 0 ? overrideTargetCount
    ///        : rankCapacity[ランク] + (祭事ON ? festivalBonus : 0)
    ///   rankCapacity は ShrineRank(C=0,B=1,A=2,S=3) の順にそのまま対応する。
    ///   既定 {7,8,9,10} + 祭事 +5 ＝ 祭事時 12〜15人。
    ///
    /// 補充ループ（毎フレーム）:
    ///   1) 退場（CustomerState が自分で Destroy した客）を回収してスロットを空ける
    ///   2) 目標との差ぶんだけ「補充チケット」を積む／余っていれば減らす
    ///   3) チケットは respawnDelay 経過で発火。ただし直前のスポーンから
    ///      minSpawnInterval 未満なら待つ（同時退場時の一斉湧きを抑える）
    ///   4) 発火 → 鳥居位置に生成 → 空きスロットを予約 → walkDuration 秒かけて歩く
    ///
    ///   ※ 退場の検知に CustomerState.onBlack/onRescued を購読しないのは、
    ///     CustomerState 側が退場秒数（成功3秒／黒客4秒）を挟んでから Destroy するため。
    ///     購読するとディレイが二重に乗る。「参照が null になったか」で見るのが正しい。
    ///
    /// 初期の危険度Dのばらつき・凍結は CustomerState の公開API(SetDangerForDebug)だけで実現している。
    ///
    /// #62 で追加（Inspector のスイッチで ON。既定は OFF なので #44/#52 の既存シーンの挙動は変わらない）:
    ///   ・assignKinds … 補充のたびに客種（通常・移動・遠方・欲張り）を抽選し、数値表で初期R・基礎点・評価を差し込む。
    ///   ・bandSpace = DistanceFromOrigin … 近／中／遠を照準の基準点（ShootPos）からの水平距離で決める
    ///     （企画書 v8 8章：近3〜7／中7〜12／遠12〜18m）。
    ///   ・occupancyShare … 定位置にいる非黒客の目標占有率（付録B PLACEMENT 40／40／20%）。不足率が最大の帯へ補充し、遠方客は必ず遠。
    ///   ・移動客 … 定位置に着いたら左右3mを movingSpeed（1.0m/s）で往復する（CustomerMotion）。movingSpeed は実行中に変えられる。
    ///   ・exitWalk … 救済3秒／黒客4秒かけて手前の出口まで歩いて退場する（CustomerMotion）。
    ///   ・救済・黒客化した客の定位置は、退場を待たずにその瞬間に空ける（企画書 v8 6章「退場開始と同時に枠を空ける」）。
    ///
    /// ※ 検証用の使い捨て。Mock/ ごと削除できる。
    /// </summary>
    public class MockCrowdDirector : MonoBehaviour
    {
        /// <summary>定位置（スロット）の決め方。</summary>
        public enum SlotPolicy
        {
            /// <summary>起動時に一度だけ抽選し、以後は同じ配置を使い回す（静止画比較の再現性が高い）。</summary>
            FixedSlots,
            /// <summary>補充のたびに抽選し直す（配置の偏りを潰したいとき）。</summary>
            RandomEachTime
        }

        /// <summary>帯の座標の取り方（#62）。</summary>
        public enum BandSpace
        {
            /// <summary>zRange をワールドZ座標として使う（#44 以来の既定）。</summary>
            WorldZ,
            /// <summary>distanceRange を distanceOrigin からの水平距離として使う（企画書 v8 8章の「距離」）。</summary>
            DistanceFromOrigin
        }

        /// <summary>境内の「近／中／遠」1レンジぶんの定義。</summary>
        [System.Serializable]
        public class Band
        {
            [Tooltip("Inspector とデバッグ表示用のラベル。")]
            public string label = "近";
            [Tooltip("Z座標の範囲（カメラは +Z 側から -Z を向いている＝値が大きいほど手前）。")]
            public Vector2 zRange = new Vector2(27f, 33f);
            [Tooltip("X座標の範囲。")]
            public Vector2 xRange = new Vector2(-6f, 6f);
            [Tooltip("基準点からの水平距離の範囲（m）。bandSpace が DistanceFromOrigin のときだけ使う。企画書 v8 8章：近3〜7／中7〜12／遠12〜18。")]
            public Vector2 distanceRange = new Vector2(3f, 7f);
            [Tooltip("定位置にいる非黒客の目標占有率（付録B PLACEMENT：近40／中40／遠20）。全帯が0なら帯を問わず空きから選ぶ（#44 以来の挙動）。")]
            [Min(0f)] public float occupancyShare;
            [Tooltip("このレンジに用意する定位置の数。")]
            public int slotCount = 6;
            [Tooltip("Scene ビューのギズモ色。")]
            public Color gizmoColor = new Color(0.3f, 0.9f, 1f, 0.35f);
        }

        /// <summary>境内にいる客1体ぶんの参照束。HUD 側はこれを読んで描画する。</summary>
        public class Member
        {
            public GameObject Go;
            public Transform Tr;
            public CustomerState State;
            public MockCustomerTag Tag;
            public MockCustomerOutline Outline;
            public MockCustomerWalker Walker;
            public int SlotIndex = -1;
            /// <summary>定位置のある帯（bands の添字）。</summary>
            public int BandIndex = -1;
            /// <summary>客種（#62。assignKinds が OFF なら通常客）。</summary>
            public CustomerKind Kind = CustomerKind.Normal;
            /// <summary>往復・退場歩行（#62。どちらも使わない客は null）。</summary>
            public CustomerMotion Motion;

            /// <summary>危険度の凍結中に維持する値。負なら未取得。</summary>
            public float FrozenDanger = -1f;
        }

        private class Slot
        {
            public int BandIndex;
            public Vector3 Position;
            public Member Occupant;   // null なら空き
        }

        // ── 生成元 ───────────────────────────────────────────────
        [Header("生成元")]
        [Tooltip("モック用の客プレハブ（Editor拡張が生成する MockCustomer.prefab）。")]
        [SerializeField] private GameObject customerPrefab;

        [Tooltip("生成した客をぶら下げる親。未設定ならこの GameObject の下に置く。")]
        [SerializeField] private Transform customerParent;

        [Tooltip("輪郭マテリアル。設定するとプレハブ側の設定より優先される。")]
        [SerializeField] private Material outlineMaterial;

        // ── 色 ──────────────────────────────────────────────────
        [Header("色の正（お守り5色パレット v3 §3）")]
        [Tooltip("お守り5色パレット（唯一の正）。未設定時のみ下のフォールバック配列を使う。")]
        [SerializeField] private OmamoriPalette palette;

        [Header("フォールバック用（palette 未設定時のみ使用）")]
        [Tooltip("0:健康 1:学業成就 2:厄除け安全 3:縁結び 4:金運（企画書v3 §3 の enum 順）。正は OmamoriPalette。palette 設定時は反映されない。")]
        [SerializeField]
        private Color[] omamoriColors =
        {
            new Color(0.20f, 1.00f, 0.45f),  // 健康：緑
            new Color(0.30f, 0.65f, 1.00f),  // 学業成就：青
            new Color(0.75f, 0.35f, 1.00f),  // 厄除け安全：紫
            new Color(1.00f, 0.40f, 0.70f),  // 縁結び：桃
            new Color(1.00f, 0.85f, 0.15f),  // 金運：金
        };

        [Tooltip("黒客の輪郭色のフォールバック。正は OmamoriPalette.BlackCustomerColor。他5色と識別できるかが #44 の検証ポイント。")]
        [SerializeField] private Color blackCustomerColor = new Color(0.04f, 0.04f, 0.06f);

        [Header("客本体の色")]
        [Tooltip("客本体の色。輪郭を主役にするためニュートラルな灰にしてある。")]
        [SerializeField] private Color bodyColor = new Color(0.72f, 0.70f, 0.66f);

        // ── 体数 ─────────────────────────────────────────────────
        [Header("体数（ランク別上限＋祭事）")]
        [Tooltip("神社ランク。モックでは手動指定する。")]
        [SerializeField] private ShrineRank rank = ShrineRank.S;

        [Tooltip("ランク別の同時表示上限。ShrineRank の C,B,A,S 順。")]
        [SerializeField] private int[] rankCapacity = { 7, 8, 9, 10 };

        [Tooltip("祭事モード。ON で festivalBonus 人ぶん増える。")]
        [SerializeField] private bool festivalMode = true;

        [Tooltip("祭事時の上乗せ人数。既定 +5 ＝ 祭事時 12〜15人。")]
        [SerializeField] private int festivalBonus = 5;

        [Tooltip("目標体数の直接指定。0以上ならランク計算を無視してこの値を使う。-1 で無効。")]
        [SerializeField] private int overrideTargetCount = -1;

        [Tooltip("ON かつシーンに ShrineRating があれば、そちらのランクを使う。")]
        [SerializeField] private bool useShrineRatingIfPresent;

        // ── 補充テンポ ──────────────────────────────────────────
        [Header("補充テンポ（#44 の実測対象）")]
        [Tooltip("退場してからスポーンするまでの待ち（秒）。")]
        [SerializeField] private float respawnDelay = 2.0f;

        [Tooltip("鳥居から定位置まで歩く時間（秒）。")]
        [SerializeField] private float walkDuration = 3.5f;

        [Tooltip("連続補充の最小間隔（秒）。同時退場時の一斉湧きを抑える。")]
        [SerializeField] private float minSpawnInterval = 0.3f;

        [Tooltip("歩行のイージング。")]
        [SerializeField] private AnimationCurve walkEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("ON なら開始時だけ歩行を省いて一括で定位置に配置する。")]
        [SerializeField] private bool prefillInstant = true;

        // ── 鳥居 ─────────────────────────────────────────────────
        [Header("鳥居（スポーン位置）")]
        [Tooltip("鳥居の Transform。設定するとこちらが優先される。")]
        [SerializeField] private Transform toriiTransform;

        [Tooltip("鳥居の座標（toriiTransform 未設定時に使う）。")]
        [SerializeField] private Vector3 toriiPosition = new Vector3(0f, 1f, 7f);

        // ── 定位置 ───────────────────────────────────────────────
        [Header("定位置（近／中／遠の3レンジ）")]
        [Tooltip("定位置の決め方。")]
        [SerializeField] private SlotPolicy slotPolicy = SlotPolicy.FixedSlots;

        [Tooltip("近／中／遠のレンジ定義。合計 slotCount が最大体数以上になるようにすること。")]
        [SerializeField]
        private Band[] bands =
        {
            new Band { label = "近", zRange = new Vector2(27f, 33f), xRange = new Vector2(-6f, 6f),   slotCount = 6, gizmoColor = new Color(0.3f, 1.0f, 0.6f, 0.35f) },
            new Band { label = "中", zRange = new Vector2(19f, 26f), xRange = new Vector2(-9f, 9f),   slotCount = 6, gizmoColor = new Color(0.3f, 0.8f, 1.0f, 0.35f) },
            new Band { label = "遠", zRange = new Vector2(12f, 18f), xRange = new Vector2(-11f, 11f), slotCount = 6, gizmoColor = new Color(0.9f, 0.6f, 1.0f, 0.35f) },
        };

        [Tooltip("定位置どうしの最小間隔。近すぎる配置を弾く。")]
        [SerializeField] private float minSlotDistance = 2.2f;

        [Tooltip("最小間隔を満たす位置を探す試行回数。満たせなければ一番マシな候補を採用。")]
        [SerializeField] private int slotPlacementAttempts = 30;

        [Tooltip("客のY座標（カプセルの中心。地面 y=0 に立たせるなら 1）。")]
        [SerializeField] private float customerY = 1f;

        [Tooltip("固定スロットの抽選シード。変えると配置パターンが変わる。")]
        [SerializeField] private int randomSeed = 12345;

        [Tooltip("帯の座標の取り方。WorldZ は zRange をワールドZとして使う（#44 以来）。" +
                 "DistanceFromOrigin は distanceRange を基準点からの水平距離として使う（#62）。")]
        [SerializeField] private BandSpace bandSpace = BandSpace.WorldZ;

        [Tooltip("距離の基準点（照準の origin＝ShootPos）。未設定なら Main Camera の位置。" +
                 "DistanceFromOrigin のときだけ使う。奥行きはカメラが向いている -Z 方向に取る。")]
        [SerializeField] private Transform distanceOrigin;

        [Tooltip("DistanceFromOrigin のとき、正面から左右に振ってよい角度（度）。近い帯で客が画面の横へはみ出さないようにする。")]
        [Range(0f, 89f)]
        [SerializeField] private float maxLateralAngle = 40f;

        // ── 客種（#62）──────────────────────────────────────────
        [Header("客種（#62：通常・移動・遠方・欲張り）")]
        [Tooltip("ON なら補充のたびに客種を抽選し、数値表で初期R・D満タン秒数・基礎点・評価を差し込む。" +
                 "OFF なら全員が客プレハブの設定のまま（#44/#52 の既存シーン）。")]
        [SerializeField] private bool assignKinds;

        [Tooltip("客種ごとの数値表（付録B B-1）。未設定なら客プレハブの CustomerState に設定された表を使う。")]
        [SerializeField] private CustomerKindTable kindTable;

        [Tooltip("客種の抽選率（付録B SPAWN.TYPE.*）。解禁スケジュールによる切り替えは #57 の担当。" +
                 "既定は4種がすべて出る11月（通常45／移動30／遠方15／欲張り10／ボス0）。")]
        [SerializeField] private CustomerKindWeights kindWeights = CustomerKindWeights.November;

        [Tooltip("遠方客を必ず置く帯（bands の添字）。")]
        [SerializeField] private int distantBandIndex = 2;

        // ── 移動客（#62）────────────────────────────────────────
        [Header("移動客（#62）")]
        [Tooltip("歩行速度（m/秒）。付録B MOVE.SPEED＝1.0。実行中に変えると、歩いている移動客にも次のフレームから反映する（T2 の変数変更テスト用）。")]
        [Min(0f)]
        [SerializeField] private float movingSpeed = 1f;

        [Tooltip("往復の幅（m、端から端）。企画書 v8 10章「左右3mの往復経路」。奥行きは変えない。")]
        [Min(0f)]
        [SerializeField] private float movingPatrolWidth = 3f;

        [Tooltip("移動客の往復の経路と、ほかの客（ほかの移動客の経路を含む）との最小間隔（m）。" +
                 "体の直径 1m ＋余裕。これより近いと歩いて体が重なる。立っている客どうしは minSlotDistance で測る。")]
        [Min(0f)]
        [SerializeField] private float movingLaneClearance = 1.2f;

        // ── 退場歩行（#62）──────────────────────────────────────
        [Header("退場歩行（#62）")]
        [Tooltip("ON なら救済・黒客化した客が退場秒数（救済3秒／黒客4秒）かけて出口まで歩く。OFF ならその場で消える（#44 以来の挙動）。")]
        [SerializeField] private bool exitWalk;

        [Tooltip("出口（ワールド座標）。客は左右位置が近い方の出口へまっすぐ歩く。手前に置くと、奥の客ほど帰路が長くなり、" +
                 "手前の客の間を通り抜けるのですれ違う人数が増える（企画書 v8 6章・7章 遠方客）。")]
        [SerializeField] private Vector3[] exitPoints = { new Vector3(-9f, 1f, 37f), new Vector3(9f, 1f, 37f) };

        // ── 客ごとのばらつき ────────────────────────────────────
        [Header("客ごとのばらつき")]
        [Tooltip("初期の危険度Dの範囲（0〜1の正規化）。残量差が視認できるようにばらけさせる。")]
        [SerializeField] private Vector2 startDangerRange = new Vector2(0.15f, 0.85f);

        [Tooltip("常時混ぜておく黒客の数。")]
        [SerializeField] private int blackCustomerCount = 3;

        [Tooltip("ON にすると危険度Dの進行を打ち消し、誰も退場しなくなる。" +
                 "12〜15体をきっちり並べて識別テストしたいときに使う（既定OFF＝補充テンポの検証用）。")]
        [SerializeField] private bool freezeDanger;

        // ── 輪郭の初期設定（デバッグ操作で実行中に切り替わる）──────
        [Header("輪郭の表現（実行中に切替可）")]
        [SerializeField] private MockCustomerOutline.OutlineMode outlineMode = MockCustomerOutline.OutlineMode.InvertedHull;
        [SerializeField] private MockCustomerOutline.WidthMode outlineWidthMode = MockCustomerOutline.WidthMode.ScreenConstant;

        // 太さをディレクタ側でも持つ理由:
        //   プレハブの値だけだと、実行中に [ / ] で太さを変えても「その時点で居る客」にしか効かず、
        //   その後に補充された客が元の細さで出てきて画面がまだらになる。
        //   ディレクタが正の値を持っていればスポーン時にも同じ値を焼けるので、
        //   実測しながら太さを振っても全員が揃う。
        [Tooltip("輪郭の太さ（ワールド単位）。0以下ならプレハブ側の値をそのまま使う。実行中に [ / ] で増減できる。")]
        [SerializeField] private float outlineWidth = 0.09f;

        [Tooltip("[ / ] キー1回あたりの太さの増減量。")]
        [SerializeField] private float outlineWidthStep = 0.015f;

        [Tooltip("実行中に振れる太さの下限・上限。")]
        [SerializeField] private Vector2 outlineWidthRange = new Vector2(0.02f, 0.4f);

        // ── 内部状態 ────────────────────────────────────────────
        private readonly List<Member> _members = new List<Member>();
        private readonly List<Slot> _slots = new List<Slot>();
        private readonly List<float> _tickets = new List<float>();   // 補充チケットの残りディレイ
        private readonly List<int> _candidates = new List<int>();    // 定位置の候補（使い回し）
        private float[] _bandShares;
        private int[] _bandOccupied;
        private bool[] _bandHasFree;

        private float _lastSpawnTime = -999f;
        private int _nextColorIndex;
        private bool _layoutReshuffled;
        private bool _warnedSlotShortage;
        private bool _warnedMissingPrefab;

        // ── 公開API（HUD／デバッグ操作から読む）──────────────────

        /// <summary>境内にいる客（歩行中を含む）。</summary>
        public IReadOnlyList<Member> Members => _members;

        /// <summary>いま画面上にいる体数（歩行中を含む）。</summary>
        public int AliveCount => _members.Count;

        /// <summary>
        /// 救済・黒客化していない体数（入場の歩行中を含む）。補充の目標と比べるのはこの数。
        /// 退場中の客は枠を空けているので数えない（企画書 v8 6章）。
        /// </summary>
        public int LiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _members.Count; i++)
                {
                    Member m = _members[i];
                    if (m != null && m.Go != null && !IsFinished(m)) n++;
                }
                return n;
            }
        }

        /// <summary>移動客の歩行速度（m/秒）。変えると歩いている移動客にも次のフレームから反映する（#62）。</summary>
        public float MovingSpeed
        {
            get => movingSpeed;
            set => movingSpeed = Mathf.Max(0f, value);
        }

        /// <summary>帯の数。</summary>
        public int BandCount => bands != null ? bands.Length : 0;

        /// <summary>帯のラベル（近／中／遠）。</summary>
        public string BandLabel(int band) =>
            bands != null && band >= 0 && band < bands.Length && bands[band] != null ? bands[band].label : "?";

        /// <summary>帯の定位置にいる（向かっている）非黒客の数。</summary>
        public int OccupiedInBand(int band)
        {
            int n = 0;
            for (int i = 0; i < _slots.Count; i++)
            {
                Member o = _slots[i].Occupant;
                if (o != null && _slots[i].BandIndex == band && !IsBlackTag(o)) n++;
            }
            return n;
        }

        /// <summary>帯の目標占有枠（占有率が未設定なら 0）。</summary>
        public int QuotaForBand(int band)
        {
            if (!RefreshBandCounts()) return 0;
            int[] quotas = PlacementBands.Quotas(TargetCount, _bandShares);
            return band >= 0 && band < quotas.Length ? quotas[band] : 0;
        }

        /// <summary>距離を測る基準点（DistanceFromOrigin のとき）。</summary>
        public Vector3 DistanceOriginPosition
        {
            get
            {
                if (distanceOrigin != null) return distanceOrigin.position;
                Camera cam = Camera.main;
                return cam != null ? cam.transform.position : Vector3.zero;
            }
        }

        /// <summary>補充待ち（まだ生成されていない）の数。</summary>
        public int PendingCount => _tickets.Count;

        /// <summary>用意されている定位置の総数。</summary>
        public int SlotCount => _slots.Count;

        /// <summary>現在のランク（ShrineRating 連動 ON ならそちらを反映）。</summary>
        public ShrineRank Rank => EffectiveRank;

        /// <summary>祭事モードか。</summary>
        public bool FestivalMode => festivalMode;

        /// <summary>目標体数の直接指定値。-1 なら未指定（ランク計算を使う）。</summary>
        public int OverrideTargetCount => overrideTargetCount;

        /// <summary>退場からスポーンまでの待ち（秒）。</summary>
        public float RespawnDelay => respawnDelay;

        /// <summary>鳥居から定位置までの歩行時間（秒）。</summary>
        public float WalkDuration => walkDuration;

        /// <summary>現在の輪郭表現。</summary>
        public MockCustomerOutline.OutlineMode OutlineMode => outlineMode;

        /// <summary>現在の輪郭の太さモード。</summary>
        public MockCustomerOutline.WidthMode OutlineWidthMode => outlineWidthMode;

        /// <summary>いま維持しようとしている目標体数（スロット数で頭打ち）。</summary>
        public int TargetCount
        {
            get
            {
                int want;
                if (overrideTargetCount >= 0)
                {
                    want = overrideTargetCount;
                }
                else
                {
                    int cap = 10;
                    if (rankCapacity != null && rankCapacity.Length > 0)
                        cap = rankCapacity[Mathf.Clamp((int)EffectiveRank, 0, rankCapacity.Length - 1)];
                    want = cap + (festivalMode ? festivalBonus : 0);
                }

                if (_slots.Count > 0 && want > _slots.Count && !_warnedSlotShortage)
                {
                    _warnedSlotShortage = true;
                    Debug.LogWarning(
                        $"[MockCrowd] 目標体数 {want} に対して定位置が {_slots.Count} しかありません。" +
                        $"bands の slotCount を増やしてください。", this);
                }

                return Mathf.Clamp(want, 0, Mathf.Max(0, _slots.Count));
            }
        }

        /// <summary>鳥居のワールド座標。</summary>
        public Vector3 ToriiPosition =>
            toriiTransform != null ? toriiTransform.position : toriiPosition;

        private ShrineRank EffectiveRank =>
            useShrineRatingIfPresent && ShrineRating.Instance != null
                ? ShrineRating.Instance.Rank
                : rank;

        // ── ライフサイクル ──────────────────────────────────────

        private void Start()
        {
            // v3 §3 の色統一が効いていないことに気づけるよう、palette 未設定は一度だけ警告する。
            if (palette == null)
                Debug.LogWarning("[MockCrowd] palette(OmamoriPalette) が未設定です。フォールバック色を使います（v3 §3 の色統一が効いていません）。", this);

            if (customerParent == null) customerParent = transform;

            BuildSlots();

            if (prefillInstant)
            {
                int want = TargetCount;
                for (int i = 0; i < want; i++)
                    SpawnOne(instant: true);
            }
        }

        private void Update()
        {
            SweepDeparted();
            BalanceToTarget();
            TickTickets();
            ApplyMovingSpeed();
        }

        private void LateUpdate()
        {
            ApplyDangerFreeze();
        }

        /// <summary>
        /// 危険度の凍結。CustomerState.Update が進めた分を、その場で元の値へ戻して打ち消す。
        ///
        /// なぜ必要か:
        ///   凍結なしだと危険度の進行によって常に誰かが黒客化して退場するため、
        ///   目標15体でも「定位置に立っている数」は 13 前後にしかならない（実測値）。
        ///   12〜15体をきっちり並べて識別可否を測る、という #44 の完了条件を満たせない。
        ///
        /// 実装:
        ///   LateUpdate は全 Update の後に走るので、この時点の D は「前フレーム値 + 進行分」。
        ///   SetDangerForDebug で元の値へ戻せば固定される。CustomerState の公開APIだけで完結する。
        /// </summary>
        private void ApplyDangerFreeze()
        {
            for (int i = 0; i < _members.Count; i++)
            {
                Member m = _members[i];
                if (m == null || m.Go == null || m.State == null) continue;

                if (!freezeDanger)
                {
                    m.FrozenDanger = -1f;         // 解除。次に凍結したとき現在値から拾い直す
                    continue;
                }

                if (m.FrozenDanger < 0f)
                {
                    m.FrozenDanger = m.State.Danger;
                    continue;
                }

                if (m.State.Danger - m.FrozenDanger > 0.0001f)
                    m.State.SetDangerForDebug(m.FrozenDanger);
            }
        }

        // ── 補充ループ ──────────────────────────────────────────

        /// <summary>
        /// 退場（CustomerState が自分で Destroy した客）を回収し、スロットを空ける。
        /// 救済・黒客化した客のスロットは、Destroy を待たずにその時点で空ける（企画書 v8 6章：
        /// 救済は「退場開始と同時に枠を空ける」、黒客は「黒客化した瞬間に上限から外れる」）。
        /// 退場歩行中の体は Members に残す（HUD や #52 の描画体数に数えるため）。
        /// </summary>
        private void SweepDeparted()
        {
            for (int i = _members.Count - 1; i >= 0; i--)
            {
                Member m = _members[i];
                if (m != null && m.Go != null)
                {
                    if (IsFinished(m)) FreeSlot(m);
                    continue;
                }

                FreeSlot(m);
                _members.RemoveAt(i);
            }
        }

        /// <summary>
        /// 「現在数 + 補充待ち」を目標体数に合わせる。
        /// 足りなければチケットを積み、余っていれば未発火のチケットだけを取り消す。
        /// </summary>
        private void BalanceToTarget()
        {
            int want = TargetCount;
            int have = LiveCount + _tickets.Count;

            while (have < want)
            {
                _tickets.Add(respawnDelay);
                have++;
            }

            // 企画書v3 §7/§8：目標体数を下回っても既にいる客は強制退場させない。
            // 解消/怒りによる自然減で追いつくのを待つ（補充だけが新上限に従う）。
            // 取り消せるのは未発火の補充チケットのみ。チケットを使い切っても
            // have > want のままなら、実体は消さずにそのままループを抜ける。
            while (have > want && _tickets.Count > 0)
            {
                _tickets.RemoveAt(_tickets.Count - 1);
                have--;
            }
        }

        /// <summary>チケットのディレイを進め、条件を満たした1件を発火させる。</summary>
        private void TickTickets()
        {
            if (_tickets.Count == 0) return;

            float dt = Time.deltaTime;
            for (int i = 0; i < _tickets.Count; i++)
                _tickets[i] -= dt;

            // 一斉湧き抑制。前回スポーンから最小間隔が空くまで待つ。
            if (Time.time - _lastSpawnTime < minSpawnInterval) return;

            // 期限切れのうち、最も長く待っている（残りが最小の）1件だけ発火させる。
            int ready = -1;
            float lowest = float.MaxValue;
            for (int i = 0; i < _tickets.Count; i++)
            {
                if (_tickets[i] > 0f) continue;
                if (_tickets[i] < lowest) { lowest = _tickets[i]; ready = i; }
            }
            if (ready < 0) return;

            _tickets.RemoveAt(ready);

            if (SpawnOne(instant: false) != null)
                _lastSpawnTime = Time.time;
            else
                _tickets.Add(0f);   // 生成に失敗（空きスロット無しなど）→ 次フレーム再挑戦
        }

        // ── 生成／退場 ──────────────────────────────────────────

        private Member SpawnOne(bool instant)
        {
            if (customerPrefab == null)
            {
                if (!_warnedMissingPrefab)
                {
                    _warnedMissingPrefab = true;
                    Debug.LogWarning("[MockCrowd] customerPrefab が未設定です。", this);
                }
                return null;
            }

            // #63: 計測プレイ中は「客ID × 用途」の固定シードの列で抽選する（未制御なら null → UnityEngine.Random）。
            //       ID は生成に成功したときだけ消費するので、失敗しても次の客が同じ ID・同じ抽選結果を使う。
            int id = CustomerSpawnId.NextId;
            DeterministicRandom placement = PlaytestRandom.TryFor(PlaytestStreams.Placement, id);

            // #62: 黒客かどうかと客種は定位置より先に決める（遠方客は置ける帯が決まっているため。企画書 v8 8章）。
            bool black = DecideBlack(PlaytestRandom.TryFor(PlaytestStreams.Identity, id));
            CustomerKind kind = assignKinds && !black
                ? PickKind(PlaytestRandom.TryFor(PlaytestStreams.Kind, id))
                : CustomerKind.Normal;

            int slotIndex = FindSlotFor(kind, placement);
            if (slotIndex < 0) return null;

            Slot slot = _slots[slotIndex];

            // 毎回抽選し直すモードでは、ここで空き位置を引き直す。
            if (slotPolicy == SlotPolicy.RandomEachTime)
                slot.Position = RandomPointInBand(slot.BandIndex, onlyOccupied: true, placement);

            // 移動客は往復の経路が帯の左右範囲に収まるよう中心を寄せる（奥行きは変えない。企画書 v8 10章）。
            Vector3 destination = kind == CustomerKind.Moving ? LaneCenterFor(slot) : slot.Position;
            Vector3 origin = ToriiPosition;

            GameObject go = Instantiate(
                customerPrefab,
                instant ? destination : origin,
                Quaternion.identity,
                customerParent);
            go.name = $"MockCustomer_{id:00}";
            CustomerSpawnId spawnId = CustomerSpawnId.Assign(go);

            var member = new Member
            {
                Go = go,
                Tr = go.transform,
                State = go.GetComponent<CustomerState>(),
                Tag = go.GetComponent<MockCustomerTag>(),
                Outline = go.GetComponent<MockCustomerOutline>(),
                Walker = go.GetComponent<MockCustomerWalker>(),
                SlotIndex = slotIndex,
                BandIndex = slot.BandIndex,
                Kind = kind,
            };

            slot.Occupant = member;

            AssignIdentity(member, black);
            if (assignKinds && member.State != null)
            {
                // 初期R・D満タン秒数・基礎点・評価を客種で差し込む。D はここで 0 に戻るので、初期のばらつきはこの後で付ける。
                member.State.Setup(kind, kindTable,
                    PlaytestRandom.Value(PlaytestRandom.TryFor(PlaytestStreams.DangerSeconds, spawnId.Id)));
            }
            // #63 の分類（T2 の選択の分布）。客種を入れる。assignKinds が OFF なら全員 Normal（従来どおり）。
            spawnId.SetCategory(member.Tag != null && member.Tag.IsBlack ? CustomerSpawnId.CategoryBlack : kind.ToString());
            spawnId.SetDestination(destination);
            RandomizeDanger(member.State, PlaytestRandom.TryFor(PlaytestStreams.Gauge, spawnId.Id));

            member.Motion = SetupMotion(member, destination, spawnId.Id);

            if (member.Walker != null)
            {
                if (instant)
                {
                    member.Walker.SnapTo(destination);
                    if (member.Motion != null) member.Motion.NotifyArrived();
                }
                else
                {
                    if (member.Motion != null) member.Walker.Arrived += member.Motion.NotifyArrived;
                    member.Walker.Begin(origin, destination, walkDuration, walkEase);
                    if (member.Motion != null && !member.Walker.IsWalking) member.Motion.NotifyArrived();
                }
            }
            else
            {
                go.transform.position = destination;
                if (member.Motion != null) member.Motion.NotifyArrived();
            }

            _members.Add(member);
            return member;
        }

        /// <summary>
        /// #62: 移動客の往復と退場歩行を載せる。どちらも使わない客には何も足さない（既存シーンの挙動を変えない）。
        /// </summary>
        private CustomerMotion SetupMotion(Member m, Vector3 destination, int spawnId)
        {
            bool patrol = m.Kind == CustomerKind.Moving;
            if (!patrol && !exitWalk) return null;

            CustomerMotion motion = m.Go.GetComponent<CustomerMotion>();
            if (motion == null) motion = m.Go.AddComponent<CustomerMotion>();

            motion.SetExitPoints(exitWalk ? exitPoints : null);
            if (patrol)
            {
                // 歩き出す向きも「客ID × 用途」の固定シードで決める（同じシードなら同じ経路）。
                int startSign = PlaytestRandom.Value(PlaytestRandom.TryFor(PlaytestStreams.Motion, spawnId)) < 0.5f ? -1 : 1;
                motion.ConfigurePatrol(destination, movingPatrolWidth, movingSpeed, startSign);
            }
            return motion;
        }

        /// <summary>#62: Inspector の movingSpeed を移動客へ毎フレーム反映する（T2 の変数変更テスト用）。</summary>
        private void ApplyMovingSpeed()
        {
            for (int i = 0; i < _members.Count; i++)
            {
                Member m = _members[i];
                if (m != null && m.Motion != null && m.Kind == CustomerKind.Moving)
                    m.Motion.PatrolSpeed = movingSpeed;
            }
        }

        /// <summary>
        /// 1体減らす。まだ歩行中の客（＝定位置に着いていない＝一番目立たない）を優先して消す。
        /// ※ シーンリセット/明示的な全消し専用。BalanceToTarget からは呼ばないこと（v3 §7/§8：
        ///   目標体数を下回っても既にいる客は強制退場させず、自然減を待つ）。
        /// </summary>
        private void DespawnOne()
        {
            if (_members.Count == 0) return;

            int pick = -1;
            for (int i = _members.Count - 1; i >= 0; i--)
            {
                Member m = _members[i];
                if (m != null && m.Walker != null && m.Walker.IsWalking) { pick = i; break; }
            }
            if (pick < 0) pick = _members.Count - 1;

            Member target = _members[pick];
            FreeSlot(target);
            _members.RemoveAt(pick);

            if (target != null && target.Go != null)
                Destroy(target.Go);
        }

        // ── 識別情報の割り当て ──────────────────────────────────

        /// <summary>
        /// 黒客（モックの黒札）にするかを決める。
        /// 「不足数 ÷ 残り枠」の確率で選ぶので、まとめて先頭に固まらず自然に散る。
        /// </summary>
        private bool DecideBlack(DeterministicRandom rng)
        {
            if (customerPrefab == null || customerPrefab.GetComponent<MockCustomerTag>() == null) return false;

            int deficit = Mathf.Max(0, blackCustomerCount - CountBlack());
            int slotsLeft = Mathf.Max(1, TargetCount - LiveCount);   // 自分を含む残り枠

            return deficit > 0 &&
                   (deficit >= slotsLeft || PlaytestRandom.Value(rng) < (float)deficit / slotsLeft);
        }

        /// <summary>輪郭色（お守り5色を巡回）と黒客フラグを割り当てる。</summary>
        private void AssignIdentity(Member m, bool black)
        {
            if (m == null || m.Tag == null) return;

            int index = -1;
            Color color;

            if (black)
            {
                // 色の正は OmamoriPalette（v3 §3）。未設定時のみフォールバックを使う。
                color = palette != null ? palette.BlackCustomerColor : blackCustomerColor;
            }
            else
            {
                index = NextColorIndex();
                if (palette != null)
                    color = palette.GetColor(index);
                else
                    color = (omamoriColors != null && omamoriColors.Length > 0)
                        ? omamoriColors[index % omamoriColors.Length]
                        : Color.white;
            }

            m.Tag.Assign(index, color, black);

            if (m.Outline != null)
            {
                m.Outline.Setup(color, bodyColor, outlineMaterial);
                m.Outline.SetMode(outlineMode);
                m.Outline.SetWidthMode(outlineWidthMode);
                // 0以下ならプレハブ側の値を尊重する（従来どおりの挙動）。
                if (outlineWidth > 0f) m.Outline.SetOutlineWidth(outlineWidth);
            }
        }

        /// <summary>5色を順に巡回させ、どの色も必ず出るようにする。</summary>
        private int NextColorIndex()
        {
            // お守り種別数の正は palette.Count（v3 §3）。未設定時のみフォールバック配列から求める。
            int n = (palette != null && palette.Count > 0)
                ? palette.Count
                : ((omamoriColors != null && omamoriColors.Length > 0) ? omamoriColors.Length : 5);
            int index = _nextColorIndex % n;
            _nextColorIndex = (_nextColorIndex + 1) % n;
            return index;
        }

        private int CountBlack()
        {
            int n = 0;
            for (int i = 0; i < _members.Count; i++)
            {
                Member m = _members[i];
                if (m != null && m.Go != null && IsBlackTag(m) && !IsFinished(m)) n++;
            }
            return n;
        }

        private static bool IsFinished(Member m) => m.State != null && m.State.IsFinished;

        private static bool IsBlackTag(Member m) => m.Tag != null && m.Tag.IsBlack;

        private int CountLiveKind(CustomerKind kind)
        {
            int n = 0;
            for (int i = 0; i < _members.Count; i++)
            {
                Member m = _members[i];
                if (m != null && m.Go != null && m.Kind == kind && !IsFinished(m)) n++;
            }
            return n;
        }

        /// <summary>客種を抽選する。欲張り客2人・ボス客1人の同時上限に達した客種は外す（企画書 v8 10章）。</summary>
        private CustomerKind PickKind(DeterministicRandom rng)
        {
            return CustomerKindPicker.Pick(kindWeights, PlaytestRandom.Value(rng),
                CountLiveKind(CustomerKind.Greedy), CountLiveKind(CustomerKind.Boss));
        }

        /// <summary>
        /// 初期の危険度Dを客ごとにばらけさせる（残量差が視認できる状態にするため）。
        /// </summary>
        private void RandomizeDanger(CustomerState state, DeterministicRandom rng)
        {
            if (state == null) return;

            // 満タンに触れると即 黒客化 が確定してしまうので、上端は必ず避ける。
            float lo = Mathf.Clamp(Mathf.Min(startDangerRange.x, startDangerRange.y), 0f, 0.98f);
            float hi = Mathf.Clamp(Mathf.Max(startDangerRange.x, startDangerRange.y), 0f, 0.98f);

            state.SetDangerForDebug(PlaytestRandom.Range(rng, lo, hi) * CustomerStateMachine.MaxDanger);
        }

        // ── スロット管理 ────────────────────────────────────────

        /// <summary>
        /// 近／中／遠バンドに定位置を敷き詰める。
        /// 固定シードで抽選するので、作り直さないかぎり毎回同じ配置になる
        /// ＝ 一時停止して静止画で比較する検証の再現性が確保できる。
        /// </summary>
        private void BuildSlots()
        {
            _slots.Clear();
            _warnedSlotShortage = false;

            if (bands == null || bands.Length == 0)
            {
                Debug.LogWarning("[MockCrowd] bands が空です。定位置を作れません。", this);
                return;
            }

            // #63: 計測プレイ中は計測シードから敷き詰める（同じシード → 同じ配置）。
            //       計測ロガーの無いシーン・手動で抽選し直した後は従来どおり randomSeed で UnityEngine.Random を使う。
            DeterministicRandom layout = PlaytestRandom.IsControlled && !_layoutReshuffled
                ? new DeterministicRandom((uint)PlaytestRandom.DeriveSeed(PlaytestStreams.SlotLayout))
                : null;

            Random.State previous = Random.state;
            Random.InitState(randomSeed);

            for (int bi = 0; bi < bands.Length; bi++)
            {
                int count = Mathf.Max(0, bands[bi].slotCount);
                for (int i = 0; i < count; i++)
                {
                    _slots.Add(new Slot
                    {
                        BandIndex = bi,
                        Position = RandomPointInBand(bi, onlyOccupied: false, layout),
                        Occupant = null,
                    });
                }
            }

            Random.state = previous;
        }

        /// <summary>定位置を抽選し直す（デバッグ操作から呼ばれる）。生存中の客はその場に残る。</summary>
        [ContextMenu("定位置を抽選し直す (Reshuffle Slots)")]
        public void ReshuffleSlots()
        {
            randomSeed = Random.Range(int.MinValue, int.MaxValue);
            _layoutReshuffled = true;

            // 生存中の客を一旦すべて片付けてから敷き直す（占有と位置の対応がずれないように）。
            for (int i = _members.Count - 1; i >= 0; i--)
            {
                Member m = _members[i];
                if (m != null && m.Go != null) Destroy(m.Go);
            }
            _members.Clear();
            _tickets.Clear();

            BuildSlots();

            if (prefillInstant)
            {
                int want = TargetCount;
                for (int i = 0; i < want; i++)
                    SpawnOne(instant: true);
            }
        }

        /// <summary>
        /// 客種に合わせて空き定位置を選ぶ（#62）。
        ///   1) 帯を決める（<see cref="ChooseBandFor"/>）。遠方客は遠、占有率があれば不足率が最大の帯、無ければ帯を問わない。
        ///   2) その帯の空きから抽選する（前詰めにすると手前ばかり埋まるため）。assignKinds が ON のときは、
        ///      移動客の往復の経路と重ならない候補（<see cref="LaneMargin"/> が 0 以上）だけを使う。
        ///   3) 選んだ帯に重ならない候補が無ければ、重ならない候補がある帯のうち不足率が最大の帯から選ぶ（遠方客は遠から動かさない）。
        ///      それも無ければ、選んだ帯でいちばん余裕のある候補にする。
        /// </summary>
        private int FindSlotFor(CustomerKind kind, DeterministicRandom rng)
        {
            int band = ChooseBandFor(kind);

            if (!assignKinds)
            {
                _candidates.Clear();
                for (int i = 0; i < _slots.Count; i++)
                {
                    Slot s = _slots[i];
                    if (s.Occupant == null && (band < 0 || s.BandIndex == band)) _candidates.Add(i);
                }
                return _candidates.Count == 0 ? -1 : _candidates[PlaytestRandom.Range(rng, 0, _candidates.Count)];
            }

            int fallback = CollectClearSlots(kind, band);
            if (_candidates.Count > 0) return _candidates[PlaytestRandom.Range(rng, 0, _candidates.Count)];

            // 選んだ帯では重なる。遠方客以外は、重ならない候補がある帯のうち不足率が最大の帯へ回す。
            if (band >= 0 && kind != CustomerKind.Distant && RefreshBandCounts() && _bandShares != null)
            {
                int[] quotas = PlacementBands.Quotas(TargetCount, _bandShares);
                int bestBand = -1;
                double bestRate = double.NegativeInfinity;
                for (int b = 0; b < bands.Length; b++)
                {
                    if (b == band || !_bandHasFree[b]) continue;
                    CollectClearSlots(kind, b);
                    if (_candidates.Count == 0) continue;

                    double rate = PlacementBands.DeficitRate(quotas[b], _bandOccupied[b]);
                    if (bestBand < 0 || rate > bestRate + 1e-9)
                    {
                        bestBand = b;
                        bestRate = rate;
                    }
                }

                if (bestBand >= 0)
                {
                    CollectClearSlots(kind, bestBand);
                    return _candidates[PlaytestRandom.Range(rng, 0, _candidates.Count)];
                }
            }

            return fallback;
        }

        /// <summary>
        /// 帯 <paramref name="band"/>（-1 なら全帯）の空き定位置のうち、ほかの客と重ならないものを _candidates に集める。
        /// </summary>
        /// <returns>重ならない候補が無いときに使う、いちばん余裕のある空き定位置。空きが無ければ -1。</returns>
        private int CollectClearSlots(CustomerKind kind, int band)
        {
            float width = kind == CustomerKind.Moving ? movingPatrolWidth : 0f;

            _candidates.Clear();
            int fallback = -1;
            float fallbackMargin = float.NegativeInfinity;
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot s = _slots[i];
                if (s.Occupant != null) continue;
                if (band >= 0 && s.BandIndex != band) continue;

                Vector3 center = kind == CustomerKind.Moving ? LaneCenterFor(s) : s.Position;
                float margin = LaneMargin(center, width);
                if (margin >= 0f)
                {
                    _candidates.Add(i);
                }
                else if (margin > fallbackMargin)
                {
                    fallbackMargin = margin;
                    fallback = i;
                }
            }
            return fallback;
        }

        /// <summary>
        /// 補充する帯を選ぶ（企画書 v8 8章）。-1 なら帯を問わない。
        ///   ・遠方客は遠（distantBandIndex）。遠に空きがなければ次に不足する帯へ回し、そのときの占有をログに残す。
        ///   ・占有率（occupancyShare）が設定されていれば、現在の目標体数を 40／40／20% の整数枠に丸め、不足率が最大の帯。
        /// </summary>
        private int ChooseBandFor(CustomerKind kind)
        {
            if (!RefreshBandCounts()) return -1;
            int n = bands.Length;

            int required = assignKinds && kind == CustomerKind.Distant && distantBandIndex >= 0 && distantBandIndex < n
                ? distantBandIndex
                : -1;

            bool anyShare = false;
            for (int i = 0; i < n; i++) anyShare |= _bandShares[i] > 0f;

            int chosen;
            bool fellBack;
            if (anyShare)
            {
                int[] quotas = PlacementBands.Quotas(TargetCount, _bandShares);
                chosen = PlacementBands.Choose(quotas, _bandOccupied, _bandHasFree, required, out fellBack);
            }
            else
            {
                fellBack = required >= 0 && !_bandHasFree[required];
                chosen = required >= 0 && !fellBack ? required : -1;
            }

            if (fellBack)
            {
                Debug.Log($"[MockCrowd] 遠方客を置く帯「{BandLabel(required)}」に空きがないため、" +
                          $"{(chosen >= 0 ? $"「{BandLabel(chosen)}」" : "空いている定位置")}に置きます（占有 {DescribeOccupancy()}）。", this);
            }
            return chosen;
        }

        /// <summary>帯ごとの比率・占有（非黒客）・空きの有無を数え直す。帯が無ければ false。</summary>
        private bool RefreshBandCounts()
        {
            int n = bands != null ? bands.Length : 0;
            if (n == 0) return false;

            if (_bandShares == null || _bandShares.Length != n)
            {
                _bandShares = new float[n];
                _bandOccupied = new int[n];
                _bandHasFree = new bool[n];
            }

            for (int i = 0; i < n; i++)
            {
                _bandShares[i] = bands[i] != null ? Mathf.Max(0f, bands[i].occupancyShare) : 0f;
                _bandOccupied[i] = 0;
                _bandHasFree[i] = false;
            }

            for (int i = 0; i < _slots.Count; i++)
            {
                Slot s = _slots[i];
                if (s.BandIndex < 0 || s.BandIndex >= n) continue;
                if (s.Occupant == null) _bandHasFree[s.BandIndex] = true;
                else if (!IsBlackTag(s.Occupant)) _bandOccupied[s.BandIndex]++;
            }
            return true;
        }

        /// <summary>「近 4/6 中 5/6 遠 3/3」の形の占有（非黒客／目標枠）。</summary>
        private string DescribeOccupancy()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < BandCount; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(BandLabel(i)).Append(' ').Append(OccupiedInBand(i)).Append('/').Append(QuotaForBand(i));
            }
            return sb.ToString();
        }

        /// <summary>移動客の往復の中心。経路が帯の左右範囲に収まるよう、定位置を内側へ寄せる。</summary>
        private Vector3 LaneCenterFor(Slot slot)
        {
            Vector3 p = slot.Position;
            if (bands == null || slot.BandIndex < 0 || slot.BandIndex >= bands.Length || bands[slot.BandIndex] == null) return p;

            Band b = bands[slot.BandIndex];
            p.x = PatrolPath.ClampCenter(p.x, movingPatrolWidth, b.xRange.x, b.xRange.y);
            return p;
        }

        /// <summary>
        /// レーン（幅0なら点）を置いたときの、定位置にいる／向かっている全員との間隔の余裕（m）。0 以上なら重ならない。
        /// どちらかが移動客なら往復の経路全体で測って movingLaneClearance、どちらも立っている客なら minSlotDistance を要る間隔にする。
        /// </summary>
        private float LaneMargin(Vector3 center, float width)
        {
            float margin = float.PositiveInfinity;
            for (int i = 0; i < _slots.Count; i++)
            {
                Member o = _slots[i].Occupant;
                if (o == null) continue;

                bool otherMoving = o.Motion != null && o.Motion.HasPatrol;
                Vector3 c = otherMoving ? o.Motion.PatrolCenter : _slots[i].Position;
                float w = otherMoving ? o.Motion.PatrolWidth : 0f;

                float required = otherMoving || width > 0f ? movingLaneClearance : minSlotDistance;
                float m = PatrolPath.LaneToLane(center, width, c, w) - required;
                if (m < margin) margin = m;
            }
            return margin;
        }

        private void FreeSlot(Member m)
        {
            if (m == null) return;
            if (m.SlotIndex < 0 || m.SlotIndex >= _slots.Count) return;
            if (_slots[m.SlotIndex].Occupant == m)
                _slots[m.SlotIndex].Occupant = null;
        }

        /// <summary>
        /// バンド内にランダムな1点を取る。他の定位置と minSlotDistance 以上離れるまで
        /// slotPlacementAttempts 回リトライし、満たせなければ一番マシな候補を返す。
        /// </summary>
        private Vector3 RandomPointInBand(int bandIndex, bool onlyOccupied, DeterministicRandom rng)
        {
            if (bands == null || bandIndex < 0 || bandIndex >= bands.Length)
                return new Vector3(0f, customerY, 0f);

            Band b = bands[bandIndex];
            Vector3 best = new Vector3(0f, customerY, 0f);
            float bestDistance = -1f;

            int attempts = Mathf.Max(1, slotPlacementAttempts);
            for (int i = 0; i < attempts; i++)
            {
                Vector3 candidate = bandSpace == BandSpace.DistanceFromOrigin
                    ? SampleByDistance(b, rng)
                    : new Vector3(
                        PlaytestRandom.Range(rng, Mathf.Min(b.xRange.x, b.xRange.y), Mathf.Max(b.xRange.x, b.xRange.y)),
                        customerY,
                        PlaytestRandom.Range(rng, Mathf.Min(b.zRange.x, b.zRange.y), Mathf.Max(b.zRange.x, b.zRange.y)));

                float nearest = NearestSlotDistance(candidate, onlyOccupied);
                if (nearest >= minSlotDistance) return candidate;

                if (nearest > bestDistance)
                {
                    bestDistance = nearest;
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// 基準点から水平距離 distanceRange の中の1点を取る（#62）。
        /// 左右は xRange（見えない壁の内側）と maxLateralAngle の両方に収め、奥行きはカメラが向く -Z 方向に取る。
        /// </summary>
        private Vector3 SampleByDistance(Band b, DeterministicRandom rng)
        {
            Vector3 o = DistanceOriginPosition;

            float rMin = Mathf.Max(0.01f, Mathf.Min(b.distanceRange.x, b.distanceRange.y));
            float rMax = Mathf.Max(rMin, Mathf.Max(b.distanceRange.x, b.distanceRange.y));
            float r = PlaytestRandom.Range(rng, rMin, rMax);

            float lateral = r * Mathf.Sin(Mathf.Clamp(maxLateralAngle, 0f, 89f) * Mathf.Deg2Rad);
            float bandMinX = Mathf.Min(b.xRange.x, b.xRange.y);
            float bandMaxX = Mathf.Max(b.xRange.x, b.xRange.y);
            float xMin = Mathf.Max(bandMinX, o.x - lateral);
            float xMax = Mathf.Min(bandMaxX, o.x + lateral);
            if (xMin > xMax) xMin = xMax = Mathf.Clamp(o.x, bandMinX, bandMaxX);

            float x = PlaytestRandom.Range(rng, xMin, xMax);
            float depth = PlacementBands.DepthAtDistance(r, x - o.x);
            return new Vector3(x, customerY, o.z - depth);
        }

        private float NearestSlotDistance(Vector3 point, bool onlyOccupied)
        {
            float min = float.MaxValue;
            for (int i = 0; i < _slots.Count; i++)
            {
                if (onlyOccupied && _slots[i].Occupant == null) continue;
                float d = Vector3.Distance(point, _slots[i].Position);
                if (d < min) min = d;
            }
            return min;
        }

        // ── デバッグ操作から呼ばれる切替 ────────────────────────

        /// <summary>目標体数を直接指定する（8/12/15 の切替用）。</summary>
        public void SetOverrideTarget(int count)
        {
            overrideTargetCount = Mathf.Max(0, count);
            _warnedSlotShortage = false;
        }

        /// <summary>直接指定を解除し、ランク＋祭事の計算に戻す。</summary>
        public void ClearOverrideTarget()
        {
            overrideTargetCount = -1;
            _warnedSlotShortage = false;
        }

        /// <summary>祭事モードを切り替える。</summary>
        public void ToggleFestival() => festivalMode = !festivalMode;

        /// <summary>ゲージ凍結中か（デバッグ表示用）。</summary>
        public bool FreezeGauges => freezeDanger;

        /// <summary>ゲージ凍結を切り替える。ON の間は誰も退場しないので体数が目標どおりに揃う。</summary>
        public void ToggleFreezeGauges() => freezeDanger = !freezeDanger;

        /// <summary>ランクを C→B→A→S→C と巡回させる。</summary>
        public void CycleRank()
        {
            rank = (ShrineRank)(((int)rank + 1) % 4);
            useShrineRatingIfPresent = false;   // 手動で回したら連動は切る
        }

        /// <summary>輪郭の表現方式を全員ぶん切り替える。</summary>
        public void SetOutlineMode(MockCustomerOutline.OutlineMode next)
        {
            outlineMode = next;
            for (int i = 0; i < _members.Count; i++)
            {
                Member m = _members[i];
                if (m != null && m.Outline != null) m.Outline.SetMode(next);
            }
        }

        /// <summary>輪郭の表現方式をトグルする。</summary>
        public void ToggleOutlineMode()
        {
            SetOutlineMode(outlineMode == MockCustomerOutline.OutlineMode.InvertedHull
                ? MockCustomerOutline.OutlineMode.Emission
                : MockCustomerOutline.OutlineMode.InvertedHull);
        }

        /// <summary>輪郭の太さモードを全員ぶん切り替える。</summary>
        public void SetOutlineWidthMode(MockCustomerOutline.WidthMode next)
        {
            outlineWidthMode = next;
            for (int i = 0; i < _members.Count; i++)
            {
                Member m = _members[i];
                if (m != null && m.Outline != null) m.Outline.SetWidthMode(next);
            }
        }

        /// <summary>現在の輪郭の太さ（ワールド単位）。0以下ならプレハブ側の値を使っている。</summary>
        public float OutlineWidth => outlineWidth;

        /// <summary>
        /// 輪郭の太さを全員ぶん変える。以後スポーンする客にも同じ値が焼かれる。
        /// 実測しながら「何ミリなら小中学生が判別できるか」を詰めるための操作。
        /// </summary>
        public void SetOutlineWidth(float world)
        {
            outlineWidth = Mathf.Clamp(world, outlineWidthRange.x, outlineWidthRange.y);
            for (int i = 0; i < _members.Count; i++)
            {
                Member m = _members[i];
                if (m != null && m.Outline != null) m.Outline.SetOutlineWidth(outlineWidth);
            }
        }

        /// <summary>輪郭の太さを1段階ぶん増減する（キー操作から呼ばれる）。</summary>
        public void StepOutlineWidth(int direction)
        {
            // outlineWidth が 0以下（プレハブ任せ）のときは、まず既定値から始める。
            float basis = outlineWidth > 0f ? outlineWidth : outlineWidthRange.x;
            SetOutlineWidth(basis + outlineWidthStep * direction);
        }

        /// <summary>輪郭の太さモードをトグルする。</summary>
        public void ToggleOutlineWidthMode()
        {
            SetOutlineWidthMode(outlineWidthMode == MockCustomerOutline.WidthMode.World
                ? MockCustomerOutline.WidthMode.ScreenConstant
                : MockCustomerOutline.WidthMode.World);
        }

        // ── Scene ビューの可視化 ────────────────────────────────

        private void OnDrawGizmosSelected()
        {
            if (bands != null)
            {
                for (int i = 0; i < bands.Length; i++)
                {
                    Band b = bands[i];
                    if (b == null) continue;

                    if (bandSpace == BandSpace.DistanceFromOrigin)
                    {
                        DrawDistanceBandGizmo(b);
                        continue;
                    }

                    var center = new Vector3(
                        (b.xRange.x + b.xRange.y) * 0.5f,
                        customerY,
                        (b.zRange.x + b.zRange.y) * 0.5f);
                    var size = new Vector3(
                        Mathf.Abs(b.xRange.y - b.xRange.x),
                        0.05f,
                        Mathf.Abs(b.zRange.y - b.zRange.x));

                    Gizmos.color = b.gizmoColor;
                    Gizmos.DrawCube(center, size);
                    Gizmos.color = new Color(b.gizmoColor.r, b.gizmoColor.g, b.gizmoColor.b, 1f);
                    Gizmos.DrawWireCube(center, size);
                }
            }

            // 実行中は実際に敷かれた定位置も出す。
            Gizmos.color = Color.yellow;
            for (int i = 0; i < _slots.Count; i++)
                Gizmos.DrawWireSphere(_slots[i].Position, 0.35f);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(ToriiPosition, 0.6f);

            if (exitWalk && exitPoints != null)
            {
                Gizmos.color = Color.magenta;
                for (int i = 0; i < exitPoints.Length; i++)
                    Gizmos.DrawWireSphere(exitPoints[i], 0.6f);
            }
        }

        /// <summary>距離帯を、基準点を中心にした2本の弧（内側・外側）で描く。</summary>
        private void DrawDistanceBandGizmo(Band b)
        {
            Vector3 o = DistanceOriginPosition;
            float angle = Mathf.Clamp(maxLateralAngle, 0f, 89f);
            const int steps = 16;

            Gizmos.color = new Color(b.gizmoColor.r, b.gizmoColor.g, b.gizmoColor.b, 1f);
            DrawArc(o, b.distanceRange.x, angle, steps);
            DrawArc(o, b.distanceRange.y, angle, steps);
        }

        private void DrawArc(Vector3 origin, float radius, float halfAngle, int steps)
        {
            Vector3 previous = Vector3.zero;
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Lerp(-halfAngle, halfAngle, (float)i / steps) * Mathf.Deg2Rad;
                var p = new Vector3(origin.x + Mathf.Sin(a) * radius, customerY, origin.z - Mathf.Cos(a) * radius);
                if (i > 0) Gizmos.DrawLine(previous, p);
                previous = p;
            }
        }
    }
}
