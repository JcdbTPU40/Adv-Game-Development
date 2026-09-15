using System.Collections.Generic;
using UnityEngine;
using Toufuku.Playtest;

namespace Toufuku.Rescue.Mock
{
    /*
        視認性モック（#44）の中心になるクラス。「ずっと補充する」やり方で、境内の人数を目標の数に保ちつづける

        目的:
          お祭りを想定して最大15人が同時にならんだとき、小中学生がぱっと見て
          「どの客が何になやんでいるか」「ゲージがどれくらい残っているか」を見分けられるかを実際に測る
          本番の客の出し方ではないので、今ある RescueCustomerSpawner とは関係なく動く

        人数の決め方:
          目標 = overrideTargetCount が 0 以上ならその値
               それ以外は rankCapacity[ランク] + (お祭りON なら festivalBonus)
          rankCapacity は ShrineRank（C=0, B=1, A=2, S=3）の順番そのまま
          ふつうは {7,8,9,10} + お祭りで +5 なので、お祭りのときは 12〜15人

        補充のくり返し（毎フレーム）:
          1) いなくなった客（CustomerState が自分で Destroy した客）を片付けて、場所を空ける
          2) 目標との差のぶんだけ「補充チケット」を積む。多すぎたら減らす
          3) チケットは respawnDelay たったら発動する。でも前に出してから
             minSpawnInterval たっていなければ待つ（同時にいなくなったときに一気に出てこないようにする）
          4) 発動したら鳥居の位置に作って、空いている場所を予約して、walkDuration 秒かけて歩かせる

          ※ いなくなったのを CustomerState.onBlack/onRescued で受け取らないのは、
            CustomerState 側が退場の秒数（成功3秒、黒客4秒）を待ってから Destroy するから
            受け取るようにすると待ち時間が2回かかってしまう。「参照が null になったか」で見るのが正しい

        最初の危険度Dのばらつきと凍結は、CustomerState の公開されてるメソッド（SetDangerForDebug）だけでやっている

        #62 で足したもの（Inspector のスイッチで ON にする。ふつうは OFF なので #44 や #52 の今あるシーンの動きは変わらない）:
          ・assignKinds: 補充するたびに客の種類（通常・移動・遠方・欲張り）をくじで決めて、数値表から最初のR・基礎点・評価を入れる
          ・bandSpace = DistanceFromOrigin: 近い・中くらい・遠いを、照準の基準点（ShootPos）からの水平距離で決める
            （企画書 v8 8章: 近い 3〜7m、中 7〜12m、遠い 12〜18m）
          ・occupancyShare: 定位置にいる黒客じゃない客の目標のわりあい（付録B PLACEMENT で 40/40/20%）。いちばん足りない帯に補充して、遠方客は必ず遠い帯
          ・移動客: 定位置に着いたら左右3mを movingSpeed（1.0m/秒）で行ったり来たりする（CustomerMotion）。movingSpeed はプレイ中に変えられる
          ・exitWalk: 救済なら3秒、黒客なら4秒かけて手前の出口まで歩いていなくなる（CustomerMotion）
          ・救済されたり黒客になったりした客の定位置は、歩いていなくなるのを待たずにその瞬間に空ける（企画書 v8 6章「退場開始と同時に枠を空ける」）

        #57 で足したもの（useTimetable。ふつうは OFF）:
          ・同時上限と客の種類のわりあいを、180秒の時間割（SpawnTimetable）で決める。ランクと祭事モードは使わない
            （動的難易度・祭事演出は追加要素なので、ランクや祭事で人数を変えない。企画書 v8 8章・11章）
          ・時計は GameSession のプレイ開始からの秒。3:00 でプレイが終わったら新しい客を出さない（いる客はそのまま）
          ・解禁と負荷ウェーブの始まり・終わりを Console に出す
          ・useKindDangerSeconds: 客プレハブに残っている D の満タン秒数の上書きを外して、客の種類ごとの本番の秒数にする

        ※ 検証用の使い捨て。Mock/ フォルダごと消せる
    */
    public class MockCrowdDirector : MonoBehaviour, Toufuku.Tutorial.ILearningCustomerSource
    {
        // 定位置（スロット）の決め方
        public enum SlotPolicy
        {
            // 始めに1回だけくじで決めて、そのあとは同じならびを使いまわす（静止画でくらべるときに同じにしやすい）
            FixedSlots,
            // 補充するたびにくじで決めなおす（ならびのかたよりをなくしたいとき）
            RandomEachTime
        }

        // 帯の座標の取り方（#62）
        public enum BandSpace
        {
            // zRange をワールドのZ座標として使う（#44 からのふつうのやり方）
            WorldZ,
            // distanceRange を distanceOrigin からの水平距離として使う（企画書 v8 8章の「距離」）
            DistanceFromOrigin
        }

        // 境内の「近い・中・遠い」のうち1つぶんの設定
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

        // 境内にいる客1人ぶんの参照をまとめたもの。HUD はこれを読んで描く
        public class Member
        {
            public GameObject Go;
            public Transform Tr;
            public CustomerState State;
            public MockCustomerTag Tag;
            public MockCustomerOutline Outline;
            public MockCustomerWalker Walker;
            public int SlotIndex = -1;
            // 定位置がある帯（bands の番号）
            public int BandIndex = -1;
            // 客の種類（#62。assignKinds が OFF なら通常客）
            public CustomerKind Kind = CustomerKind.Normal;
            // 往復と退場の歩き（#62。どっちも使わない客は null）
            public CustomerMotion Motion;

            // 危険度を凍結している間、キープしておく値。マイナスならまだ取っていない
            public float FrozenDanger = -1f;

            // #58: 段階学習の同期パルスで今輪郭を明るくしている量（0〜1）
            public float LearningPulse;
        }

        private class Slot
        {
            public int BandIndex;
            public Vector3 Position;
            public Member Occupant;   // null なら空いている
        }

        // ---- 作るもと ----
        [Header("生成元")]
        [Tooltip("モック用の客プレハブ（Editor拡張が生成する MockCustomer.prefab）。")]
        [SerializeField] private GameObject customerPrefab;

        [Tooltip("生成した客をぶら下げる親。未設定ならこの GameObject の下に置く。")]
        [SerializeField] private Transform customerParent;

        [Tooltip("輪郭マテリアル。設定するとプレハブ側の設定より優先される。")]
        [SerializeField] private Material outlineMaterial;

        // ---- 色 ----
        [Header("色の正（お守り5色パレット v3 §3）")]
        [Tooltip("お守り5色パレット（唯一の正）。未設定時のみ下のフォールバック配列を使う。")]
        [SerializeField] private OmamoriPalette palette;

        [Header("フォールバック用（palette 未設定時のみ使用）")]
        [Tooltip("0:健康 1:学業成就 2:厄除け安全 3:縁結び 4:金運（企画書v3 §3 の enum 順）。正は OmamoriPalette。palette 設定時は反映されない。")]
        [SerializeField]
        private Color[] omamoriColors =
        {
            new Color(0.20f, 1.00f, 0.45f),  // 健康: 緑
            new Color(0.30f, 0.65f, 1.00f),  // 学業成就: 青
            new Color(0.75f, 0.35f, 1.00f),  // 厄除け安全: 紫
            new Color(1.00f, 0.40f, 0.70f),  // 縁結び: ピンク
            new Color(1.00f, 0.85f, 0.15f),  // 金運: 金
        };

        [Tooltip("黒客の輪郭色のフォールバック。正は OmamoriPalette.BlackCustomerColor。他5色と識別できるかが #44 の検証ポイント。")]
        [SerializeField] private Color blackCustomerColor = new Color(0.04f, 0.04f, 0.06f);

        [Header("客本体の色")]
        [Tooltip("客本体の色。輪郭を主役にするためニュートラルな灰にしてある。")]
        [SerializeField] private Color bodyColor = new Color(0.72f, 0.70f, 0.66f);

        // ---- 人数 ----
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

        // ---- 補充のテンポ ----
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

        // ---- 鳥居 ----
        [Header("鳥居（スポーン位置）")]
        [Tooltip("鳥居の Transform。設定するとこちらが優先される。")]
        [SerializeField] private Transform toriiTransform;

        [Tooltip("鳥居の座標（toriiTransform 未設定時に使う）。")]
        [SerializeField] private Vector3 toriiPosition = new Vector3(0f, 1f, 7f);

        // ---- 定位置 ----
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

        // ---- 客の種類（#62） ----
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

        // ---- 時間割（#57） ----
        [Header("時間割（#57：解禁スケジュールと無演出負荷ウェーブ）")]
        [Tooltip("ON なら同時上限と客種の抽選率を 180秒の時間割（SpawnTimetable）で決める。ランク・祭事モードは使わない（動的難易度・祭事演出は追加要素）。" +
                 "OFF なら従来どおり（#44/#52 の既存シーン）。assignKinds も ON にすること。")]
        [SerializeField] private bool useTimetable;

        [Tooltip("時間割の数値（付録B）。T3 の大負荷ウェーブ +3／+4／+5 は finalWaveAdd で切り替える（実行中の変更も次のフレームから効く）。")]
        [SerializeField] private SpawnTimetable timetable = new SpawnTimetable();

        [Tooltip("ON なら客プレハブの危険度D満タン秒数の上書き（#44 の 66.7秒）を外し、客種ごとの本番値（付録B B-1）を使う。useTimetable のときだけ効く。")]
        [SerializeField] private bool useKindDangerSeconds = true;

        [Tooltip("T0-3M（#53）で測った実操作周期の中央値（秒）。0 なら未測定。右クリックメニュー「負荷ウェーブの算術」で使う。")]
        [Min(0f)]
        [SerializeField] private float measuredCycleMedian;

        [Tooltip("T0-3M（#53）で測った実操作周期の p75（秒）。0 なら未測定。")]
        [Min(0f)]
        [SerializeField] private float measuredCycleP75;

        // ---- 移動客（#62） ----
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

        // ---- 退場の歩き（#62） ----
        [Header("退場歩行（#62）")]
        [Tooltip("ON なら救済・黒客化した客が退場秒数（救済3秒／黒客4秒）かけて出口まで歩く。OFF ならその場で消える（#44 以来の挙動）。")]
        [SerializeField] private bool exitWalk;

        [Tooltip("出口（ワールド座標）。客は左右位置が近い方の出口へまっすぐ歩く。手前に置くと、奥の客ほど帰路が長くなり、" +
                 "手前の客の間を通り抜けるのですれ違う人数が増える（企画書 v8 6章・7章 遠方客）。")]
        [SerializeField] private Vector3[] exitPoints = { new Vector3(-9f, 1f, 37f), new Vector3(9f, 1f, 37f) };

        // ---- 客ごとのばらつき ----
        [Header("客ごとのばらつき")]
        [Tooltip("初期の危険度Dの範囲（0〜1の正規化）。残量差が視認できるようにばらけさせる。")]
        [SerializeField] private Vector2 startDangerRange = new Vector2(0.15f, 0.85f);

        [Tooltip("常時混ぜておく黒客の数。")]
        [SerializeField] private int blackCustomerCount = 3;

        [Tooltip("ON にすると危険度Dの進行を打ち消し、誰も退場しなくなる。" +
                 "12〜15体をきっちり並べて識別テストしたいときに使う（既定OFF＝補充テンポの検証用）。")]
        [SerializeField] private bool freezeDanger;

        // ---- 輪郭の最初の設定（デバッグ操作でプレイ中に切りかわる） ----
        [Header("輪郭の表現（実行中に切替可）")]
        [SerializeField] private MockCustomerOutline.OutlineMode outlineMode = MockCustomerOutline.OutlineMode.InvertedHull;
        [SerializeField] private MockCustomerOutline.WidthMode outlineWidthMode = MockCustomerOutline.WidthMode.ScreenConstant;

        /*
            太さをこっちでも持っている理由:
              プレハブの値だけだと、プレイ中に [ / ] で太さを変えても「そのときにいる客」にしか反映されなくて、
              あとから補充された客は元の細さで出てきて、画面がバラバラになる
              こっちでプラスの値を持っておけば、出すときにも同じ値を入れられるので、
              測りながら太さを変えても全員そろう
        */
        [Tooltip("輪郭の太さ（ワールド単位）。0以下ならプレハブ側の値をそのまま使う。実行中に [ / ] で増減できる。")]
        [SerializeField] private float outlineWidth = 0.09f;

        [Tooltip("[ / ] キー1回あたりの太さの増減量。")]
        [SerializeField] private float outlineWidthStep = 0.015f;

        [Tooltip("実行中に振れる太さの下限・上限。")]
        [SerializeField] private Vector2 outlineWidthRange = new Vector2(0.02f, 0.4f);

        // ---- 中で使う変数 ----
        private readonly List<Member> _members = new List<Member>();
        private readonly List<Slot> _slots = new List<Slot>();
        private readonly List<float> _tickets = new List<float>();   // 補充チケットの残りの待ち時間
        private readonly List<int> _candidates = new List<int>();    // 定位置の候補（使いまわす）
        private float[] _bandShares;
        private int[] _bandOccupied;
        private bool[] _bandHasFree;

        private float _lastSpawnTime = -999f;
        private int _nextColorIndex;
        private bool _layoutReshuffled;
        private bool _warnedSlotShortage;
        private bool _warnedMissingPrefab;

        // 時間割のログ用に、前のフレームの解禁の状態とウェーブを覚えておく（#57）
        private int _trackedUnlocked = -1;
        private int _trackedWave = int.MinValue;
        private float _trackedSeconds = -1f;

        // 解禁をログに出す種類（通常客は最初から）
        private static readonly CustomerKind[] UnlockOrder =
        {
            CustomerKind.Greedy, CustomerKind.Distant, CustomerKind.Moving, CustomerKind.Boss
        };

        // ---- 外から使うもの（HUD やデバッグ操作から読む） ----

        // 境内にいる客（歩いている途中の客も入る）
        public IReadOnlyList<Member> Members => _members;

        // 今画面にいる人数（歩いている途中の客も入る）
        public int AliveCount => _members.Count;

        /*
            救済も黒客化もしていない人数（入ってくる途中の客も入る）。補充の目標とくらべるのはこの数
            帰っている途中の客はもう場所を空けているので数えない（企画書 v8 6章）
        */
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

        // 移動客の歩く速さ（m/秒）。変えると、歩いている移動客にも次のフレームから反映される（#62）
        public float MovingSpeed
        {
            get => movingSpeed;
            set => movingSpeed = Mathf.Max(0f, value);
        }

        // 帯の数
        public int BandCount => bands != null ? bands.Length : 0;

        // 帯の名前（近い・中・遠い）
        public string BandLabel(int band) =>
            bands != null && band >= 0 && band < bands.Length && bands[band] != null ? bands[band].label : "?";

        // その帯の定位置にいる（向かっている）、黒客じゃない客の数
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

        // その帯の目標の人数（わりあいが設定されていなければ 0）
        public int QuotaForBand(int band)
        {
            if (!RefreshBandCounts()) return 0;
            int[] quotas = PlacementBands.Quotas(TargetCount, _bandShares);
            return band >= 0 && band < quotas.Length ? quotas[band] : 0;
        }

        // 距離を測る基準点（DistanceFromOrigin のとき）
        public Vector3 DistanceOriginPosition
        {
            get
            {
                if (distanceOrigin != null) return distanceOrigin.position;
                Camera cam = Camera.main;
                return cam != null ? cam.transform.position : Vector3.zero;
            }
        }

        // 補充待ち（まだ作られていない）の数
        public int PendingCount => _tickets.Count;

        // 用意してある定位置の数
        public int SlotCount => _slots.Count;

        // 今のランク（ShrineRating と連動するのが ON なら、そっちを使う）
        public ShrineRank Rank => EffectiveRank;

        // お祭りモードかどうか
        public bool FestivalMode => festivalMode;

        // ---- #58 段階学習（ILearningCustomerSource） ----

        [Header("段階学習（#58）")]
        [Tooltip("同期パルスで輪郭を白に寄せる最大の割合。1 で真っ白。")]
        [Range(0f, 1f)]
        [SerializeField] private float learningPulseWhiten = 0.6f;

        /*
            ON の間は補充チケットを積まない（段階学習の 0:00〜0:30 は StagedLearningDirector が1人ずつ置く）
            いる客（学習の客もふくむ）はそのまま残す。OFF にもどすと、次のフレームから時間割の上限まで補充する
        */
        public bool AutoSpawnSuspended { get; set; }

        /*
            色 color の通常客を、帯 bandIndex の空いている定位置のうち正面にいちばん近いところに1人置く（#58）
            色の順番（5色を順に回す）は使わないので、学習のあとの補充の色の順番は学習の有無で変わらない
            instant: true なら鳥居から歩かせずに定位置にすぐ置く。置けなかったら null
        */
        public GameObject SpawnLearningCustomer(OmamoriType color, int bandIndex, bool instant)
        {
            if (bands == null || bands.Length == 0) return null;
            Member m = SpawnOne(instant, (int)color, Mathf.Clamp(bandIndex, 0, bands.Length - 1));
            return m != null ? m.Go : null;
        }

        /*
            学習の客の輪郭を amount01（0〜1）の分だけ白に寄せて明るくする（#58 同期パルス。ボタンの脈動と同じ値を毎フレーム渡す）
            0 でもとの色にもどる
        */
        public void SetLearningPulse(GameObject customer, float amount01)
        {
            if (customer == null) return;
            for (int i = 0; i < _members.Count; i++)
            {
                Member m = _members[i];
                if (m == null || m.Go != customer) continue;
                if (m.Outline == null || m.Tag == null) return;

                float a = Mathf.Clamp01(amount01);
                if (Mathf.Abs(a - m.LearningPulse) < 0.002f) return;
                m.LearningPulse = a;
                m.Outline.Setup(Color.Lerp(m.Tag.AssignedColor, Color.white, a * learningPulseWhiten), bodyColor, null);
                return;
            }
        }

        // 目標の人数を直接決めた値。-1 なら決めていない（ランクから計算する）
        public int OverrideTargetCount => overrideTargetCount;

        // 時間割で人数と客の種類を決めているか（#57）
        public bool UseTimetable => useTimetable;

        // 時間割（#57）。T3 の条件（大負荷ウェーブの人数）はここから切りかえられる
        public SpawnTimetable Timetable => timetable;

        // 時間割の時計（秒）。GameSession があればプレイ開始からの秒、なければシーンが始まってからの秒
        public float TimetableSeconds =>
            GameSession.Instance != null ? GameSession.Instance.ElapsedSeconds : Time.timeSinceLevelLoad;

        // 3:00 でプレイが終わって、新しい客を出すのを止めているか（#57。時間割を使っているときだけ）
        public bool SpawningStopped =>
            useTimetable && GameSession.Instance != null && !GameSession.Instance.IsPlaying;

        // いなくなってから出てくるまでの待ち時間（秒）
        public float RespawnDelay => respawnDelay;

        // 鳥居から定位置まで歩く時間（秒）
        public float WalkDuration => walkDuration;

        // 今の輪郭の表し方
        public MockCustomerOutline.OutlineMode OutlineMode => outlineMode;

        // 今の輪郭の太さのモード
        public MockCustomerOutline.WidthMode OutlineWidthMode => outlineWidthMode;

        // 今キープしようとしている目標の人数（定位置の数より多くはならない）
        public int TargetCount
        {
            get
            {
                int want;
                if (overrideTargetCount >= 0)
                {
                    want = overrideTargetCount;
                }
                else if (useTimetable)
                {
                    // #57: 基準10人＋負荷ウェーブの加算（総上限15人）。ランクと祭事は使わない
                    want = timetable.CapAt(TimetableSeconds);
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

        // 鳥居のワールド座標
        public Vector3 ToriiPosition =>
            toriiTransform != null ? toriiTransform.position : toriiPosition;

        private ShrineRank EffectiveRank =>
            useShrineRatingIfPresent && ShrineRating.Instance != null
                ? ShrineRating.Instance.Rank
                : rank;

        // ---- 始まりと毎フレームの処理 ----

        private void Start()
        {
            // v3 §3 の色の統一が効いていないことに気づけるように、palette が入っていなかったら1回だけ警告を出す
            if (palette == null)
                Debug.LogWarning("[MockCrowd] palette(OmamoriPalette) が未設定です。フォールバック色を使います（v3 §3 の色統一が効いていません）。", this);

            if (customerParent == null) customerParent = transform;

            if (useTimetable)
            {
                foreach (string problem in timetable.Validate())
                    Debug.LogWarning($"[MockCrowd] 時間割: {problem}", this);
                if (!assignKinds)
                    Debug.LogWarning("[MockCrowd] useTimetable が ON ですが assignKinds が OFF なので、客種の解禁は効きません（人数だけ時間割に従います）。", this);
            }

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
            if (useTimetable) ResetIfClockRewound();

            SweepDeparted();

            // #57: 3:00 でプレイが終わったら新しい客は出さない（企画書 v8 8章「3:00.000 で新規スポーン停止」）。いる客はそのまま
            // #58: 段階学習の間（AutoSpawnSuspended）も補充しない。客は StagedLearningDirector が置く
            if (SpawningStopped || AutoSpawnSuspended)
            {
                _tickets.Clear();
            }
            else
            {
                BalanceToTarget();
                TickTickets();
            }

            ApplyMovingSpeed();
            if (useTimetable) TrackTimetable();
        }

        /*
            リトライ（GameSession.Retry）で時計が 0 にもどったら、前のプレイの続きを持ちこまないように最初にもどす（#57）
            補充チケット・前に出した時刻・輪郭の色の順番がのこっていると、同じシードでも2回目の出現列がずれる（T3 はリトライをはさんで同じシードでくらべる）
            残っている客は GameSession.Retry も消すけど、Customer タグが付いていない客もいるかもしれないので、ここでも片付ける
        */
        private void ResetIfClockRewound()
        {
            float t = TimetableSeconds;
            bool rewound = t + 0.0001f < _trackedSeconds;
            _trackedSeconds = t;
            if (!rewound) return;

            for (int i = _members.Count - 1; i >= 0; i--)
            {
                Member m = _members[i];
                FreeSlot(m);
                if (m != null && m.Go != null) Destroy(m.Go);
            }
            _members.Clear();
            _tickets.Clear();
            _lastSpawnTime = -999f;
            _nextColorIndex = 0;
            _trackedUnlocked = -1;
            _trackedWave = int.MinValue;

            Debug.Log("[MockCrowd] 時計が 0 にもどったので、補充待ち・色の順番・時間割のログを最初にもどしました（リトライ）。", this);
        }

        // 解禁とウェーブの始まり・終わりを Console に出す（#57。T3 の区間を目で追えるように）
        private void TrackTimetable()
        {
            if (SpawningStopped) return;

            float t = TimetableSeconds;
            int unlocked = 0;
            for (int i = 0; i < UnlockOrder.Length; i++)
            {
                if (timetable.IsUnlocked(UnlockOrder[i], t)) unlocked |= 1 << i;
            }

            LoadWaveState wave = timetable.WaveAt(t);
            bool unlockChanged = unlocked != _trackedUnlocked;
            bool waveChanged = wave.Index != _trackedWave;
            if (!unlockChanged && !waveChanged) return;

            string clock = SpawnTimetable.FormatClock(t);
            if (unlockChanged)
            {
                Debug.Log($"[MockCrowd] {clock} {SpawnTimetable.MonthLabel(timetable.MonthAt(t))} 解禁: {timetable.DescribeUnlocked(t)}" +
                          $"（抽選率 {timetable.WeightsAt(t).Describe()}）", this);
            }
            if (waveChanged)
            {
                Debug.Log(wave.IsActive
                    ? $"[MockCrowd] {clock} {wave.Label} 開始: 上限 {timetable.BaseCap}人 → {Mathf.Min(timetable.MaxTotalCap, timetable.BaseCap + wave.Added)}人" +
                      $"（{timetable.WaveRampSeconds:0.#}秒かけて +{wave.Added}、{SpawnTimetable.FormatClock(wave.EndSeconds)} まで）／抽選率 {timetable.WeightsAt(t).Describe()}"
                    : $"[MockCrowd] {clock} 通常: 上限 {timetable.CapAt(t)}人（多いぶんは帰らせず自然に減るのを待つ）／抽選率 {timetable.WeightsAt(t).Describe()}", this);
            }

            _trackedUnlocked = unlocked;
            _trackedWave = wave.Index;
        }

        /*
            T0-3M の実操作周期で、時間割の3つの負荷ウェーブに追いつけるかを計算して Console に出す（#57。企画書 v8 8章）
            Inspector の measuredCycleMedian / measuredCycleP75 に #53 の実測値を入れてから使う
        */
        [ContextMenu("負荷ウェーブの算術を Console に出す (#57)")]
        public void LogWaveLoadArithmetic()
        {
            Debug.Log($"[MockCrowd] {timetable.DescribeLoad(kindTable, measuredCycleMedian, measuredCycleP75)}", this);
            foreach (string problem in timetable.Validate())
                Debug.LogWarning($"[MockCrowd] 時間割: {problem}", this);
        }

        private void LateUpdate()
        {
            ApplyDangerFreeze();
        }

        /*
            危険度の凍結。CustomerState.Update で進んだぶんを、その場で元の値にもどして打ち消す

            なんで必要か:
              凍結しないと危険度が進んでいつもだれかが黒客になっていなくなるので、
              目標15人でも「定位置に立っている数」は13人くらいにしかならない（実際に測った）
              12〜15人をちゃんとならべて見分けられるかを測る、という #44 の完了条件を満たせない

            やり方:
              LateUpdate はぜんぶの Update のあとに動くので、このときの D は「前のフレームの値＋進んだぶん」
              SetDangerForDebug で元の値にもどせば止まる。CustomerState の公開されてるメソッドだけでできる
        */
        private void ApplyDangerFreeze()
        {
            for (int i = 0; i < _members.Count; i++)
            {
                Member m = _members[i];
                if (m == null || m.Go == null || m.State == null) continue;

                if (!freezeDanger)
                {
                    m.FrozenDanger = -1f;         // 解除。次に凍結したときに今の値から取りなおす
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

        // ---- 補充のくり返し ----

        /*
            いなくなった客（CustomerState が自分で Destroy した客）を片付けて、場所を空ける
            救済されたり黒客になったりした客の場所は、Destroy を待たずにその時点で空ける（企画書 v8 6章:
            救済は「退場開始と同時に枠を空ける」、黒客は「黒客化した瞬間に上限から外れる」）
            歩いて帰っている途中の客は Members に残しておく（HUD や #52 の描画人数に数えるため）
        */
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

        /*
            「今いる数＋補充待ち」を目標の人数に合わせる
            足りなければチケットを積んで、多すぎたらまだ発動していないチケットだけを取り消す
        */
        private void BalanceToTarget()
        {
            int want = TargetCount;
            int have = LiveCount + _tickets.Count;

            while (have < want)
            {
                _tickets.Add(respawnDelay);
                have++;
            }

            /*
                企画書 v3 §7/§8: 目標の人数より多くても、もういる客をむりやり帰らせることはしない
                解消や怒りで自然に減るのを待つ（補充だけが新しい上限にしたがう）
                取り消せるのはまだ発動していない補充チケットだけ。チケットがなくなっても
                have > want のままなら、客は消さずにそのままループをぬける
            */
            while (have > want && _tickets.Count > 0)
            {
                _tickets.RemoveAt(_tickets.Count - 1);
                have--;
            }
        }

        // チケットの待ち時間を進めて、条件をみたした1つだけを発動させる
        private void TickTickets()
        {
            if (_tickets.Count == 0) return;

            float dt = Time.deltaTime;
            for (int i = 0; i < _tickets.Count; i++)
                _tickets[i] -= dt;

            // 一気に出てこないようにする。前に出してから最低の間かくがあくまで待つ
            if (Time.time - _lastSpawnTime < minSpawnInterval) return;

            // 時間切れのチケットのうち、いちばん長く待っている（残りがいちばん少ない）1つだけ発動させる
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
                _tickets.Add(0f);   // 作るのに失敗した（空いている場所がないなど）ので、次のフレームでもう一回ためす
        }

        // ---- 作る・帰らせる ----

        // forcedColor / forcedBand: #58 段階学習で色と帯を決めて置くとき（-1 ならふつうの補充と同じにくじと順番で決める）
        private Member SpawnOne(bool instant, int forcedColor = -1, int forcedBand = -1)
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

            /*
                #63: 計測プレイ中は「客ID × 使いみち」で決まったシードの乱数でくじを引く（管理されていなければ null で UnityEngine.Random を使う）
                IDは作るのに成功したときだけ使うので、失敗しても次の客が同じIDと同じくじの結果を使う
            */
            int id = CustomerSpawnId.NextId;
            DeterministicRandom placement = PlaytestRandom.TryFor(PlaytestStreams.Placement, id);

            // #62: 黒客かどうかと客の種類は、定位置より先に決める（遠方客は置ける帯が決まっているから。企画書 v8 8章）
            // #58: 学習の客は黒客にしないで、通常客にする（18章「この30秒は黒客化しない」、8章「通常客（段階学習では1色ずつ）」）
            bool learning = forcedColor >= 0;
            bool black = !learning && DecideBlack(PlaytestRandom.TryFor(PlaytestStreams.Identity, id));
            CustomerKind kind = assignKinds && !black && !learning
                ? PickKind(PlaytestRandom.TryFor(PlaytestStreams.Kind, id))
                : CustomerKind.Normal;

            int slotIndex = forcedBand >= 0 ? FindLearningSlot(forcedBand) : FindSlotFor(kind, placement);
            if (slotIndex < 0) return null;

            Slot slot = _slots[slotIndex];

            // 毎回くじを引きなおすモードでは、ここで空いている場所を引きなおす
            if (slotPolicy == SlotPolicy.RandomEachTime)
                slot.Position = RandomPointInBand(slot.BandIndex, onlyOccupied: true, placement);

            // 移動客は往復する道が帯の左右の範囲に入るように、真ん中に寄せる（奥行きは変えない。企画書 v8 10章）
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

            AssignIdentity(member, black, forcedColor);
            if (assignKinds && member.State != null)
            {
                // #57: 時間割で動かすときは、検証用の D の満タン秒数の上書き（#44）を外して、客の種類ごとの本番の秒数にする
                if (useTimetable && useKindDangerSeconds) member.State.DangerFullSecondsOverride = 0f;

                // 最初のR・Dが満タンになる秒数・基礎点・評価を、客の種類から入れる。D はここで 0 にもどるので、最初のばらつきはこのあとで付ける
                member.State.Setup(kind, kindTable,
                    PlaytestRandom.Value(PlaytestRandom.TryFor(PlaytestStreams.DangerSeconds, spawnId.Id)));
            }
            // #63 の分類（T2 でどれを選んだかの分布）。客の種類を入れる。assignKinds が OFF なら全員 Normal（前と同じ）
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

        // #62: 移動客の往復と、帰るときの歩きを付ける。どっちも使わない客には何も足さない（今あるシーンの動きを変えないため）
        private CustomerMotion SetupMotion(Member m, Vector3 destination, int spawnId)
        {
            bool patrol = m.Kind == CustomerKind.Moving;
            if (!patrol && !exitWalk) return null;

            CustomerMotion motion = m.Go.GetComponent<CustomerMotion>();
            if (motion == null) motion = m.Go.AddComponent<CustomerMotion>();

            motion.SetExitPoints(exitWalk ? exitPoints : null);
            if (patrol)
            {
                // 歩き出す向きも「客ID × 使いみち」のシードで決める（同じシードなら同じ道になる）
                int startSign = PlaytestRandom.Value(PlaytestRandom.TryFor(PlaytestStreams.Motion, spawnId)) < 0.5f ? -1 : 1;
                motion.ConfigurePatrol(destination, movingPatrolWidth, movingSpeed, startSign);
            }
            return motion;
        }

        // #62: Inspector の movingSpeed を毎フレーム移動客に反映する（T2 で数値を変えるテスト用）
        private void ApplyMovingSpeed()
        {
            for (int i = 0; i < _members.Count; i++)
            {
                Member m = _members[i];
                if (m != null && m.Motion != null && m.Kind == CustomerKind.Moving)
                    m.Motion.PatrolSpeed = movingSpeed;
            }
        }

        /*
            1人減らす。まだ歩いている途中の客（＝定位置に着いていない＝いちばん目立たない客）から先に消す
            ※ シーンのリセットや、わざと全部消すとき専用。BalanceToTarget からは呼ばないこと（v3 §7/§8:
              目標の人数より多くても、もういる客はむりやり帰らせないで、自然に減るのを待つ）
        */
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

        // ---- 見分けるための情報を決める ----

        /*
            黒客（モックの黒札）にするかどうかを決める
            「足りない数 ÷ 残りの枠」の確率で選ぶので、最初のほうにかたまらずに自然にばらける
        */
        private bool DecideBlack(DeterministicRandom rng)
        {
            if (customerPrefab == null || customerPrefab.GetComponent<MockCustomerTag>() == null) return false;

            int deficit = Mathf.Max(0, blackCustomerCount - CountBlack());
            int slotsLeft = Mathf.Max(1, TargetCount - LiveCount);   // 自分も入れた残りの枠

            return deficit > 0 &&
                   (deficit >= slotsLeft || PlaytestRandom.Value(rng) < (float)deficit / slotsLeft);
        }

        // 輪郭の色（お守り5色を順番に。forcedColor が 0 以上ならその色）と、黒客かどうかを決める
        private void AssignIdentity(Member m, bool black, int forcedColor = -1)
        {
            if (m == null || m.Tag == null) return;

            int index = -1;
            Color color;

            if (black)
            {
                // 色の正しい値は OmamoriPalette（v3 §3）。入っていないときだけ予備の色を使う
                color = palette != null ? palette.BlackCustomerColor : blackCustomerColor;
            }
            else
            {
                index = forcedColor >= 0 ? forcedColor : NextColorIndex();
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
                // 0以下ならプレハブ側の値を使う（前と同じ動き）
                if (outlineWidth > 0f) m.Outline.SetOutlineWidth(outlineWidth);
            }
        }

        // 5色を順番に回して、どの色も必ず出るようにする
        private int NextColorIndex()
        {
            // お守りの種類の数の正しい値は palette.Count（v3 §3）。入っていないときだけ予備の配列から出す
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

        /*
            客の種類をくじで決める。欲張り客2人・ボス客1人の同時の上限に届いた種類は外す（企画書 v8 10章）
            #57: 時間割を使うときは、そのときの月・解禁・負荷ウェーブで決まったわりあいを使う
        */
        private CustomerKind PickKind(DeterministicRandom rng)
        {
            CustomerKindWeights weights = useTimetable ? timetable.WeightsAt(TimetableSeconds) : kindWeights;
            return CustomerKindPicker.Pick(weights, PlaytestRandom.Value(rng),
                CountLiveKind(CustomerKind.Greedy), CountLiveKind(CustomerKind.Boss));
        }

        // 最初の危険度Dを客ごとにばらけさせる（残りの量のちがいが見てわかるようにするため）
        private void RandomizeDanger(CustomerState state, DeterministicRandom rng)
        {
            if (state == null) return;

            // 満タンにふれるとすぐ黒客になってしまうので、上のはしは必ずさける
            float lo = Mathf.Clamp(Mathf.Min(startDangerRange.x, startDangerRange.y), 0f, 0.98f);
            float hi = Mathf.Clamp(Mathf.Max(startDangerRange.x, startDangerRange.y), 0f, 0.98f);

            state.SetDangerForDebug(PlaytestRandom.Range(rng, lo, hi) * CustomerStateMachine.MaxDanger);
        }

        // ---- 定位置の管理 ----

        /*
            近い・中・遠いの帯に定位置をしきつめる
            決まったシードでくじを引くので、作りなおさないかぎり毎回同じならびになる
            ＝ 一時停止して静止画でくらべる検証を、何回やっても同じ条件でできる
        */
        private void BuildSlots()
        {
            _slots.Clear();
            _warnedSlotShortage = false;

            if (bands == null || bands.Length == 0)
            {
                Debug.LogWarning("[MockCrowd] bands が空です。定位置を作れません。", this);
                return;
            }

            /*
                #63: 計測プレイ中は計測用のシードからしきつめる（同じシードなら同じならび）
                計測ロガーがないシーンや、手でくじを引きなおしたあとは、前と同じで randomSeed を使って UnityEngine.Random を使う
            */
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

        // 定位置をくじで決めなおす（デバッグ操作から呼ばれる）。いる客はその場に残る
        [ContextMenu("定位置を抽選し直す (Reshuffle Slots)")]
        public void ReshuffleSlots()
        {
            randomSeed = Random.Range(int.MinValue, int.MaxValue);
            _layoutReshuffled = true;

            // いる客をいったん全部片付けてからしきなおす（場所と客の対応がずれないようにするため）
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

        /*
            客の種類に合わせて、空いている定位置を選ぶ（#62）
              1) 帯を決める（ChooseBandFor）。遠方客は遠い帯、わりあいがあればいちばん足りない帯、なければどの帯でもいい
              2) その帯の空きからくじで選ぶ（前からつめると手前ばかりうまるから）。assignKinds が ON のときは、
                 移動客の往復の道と重ならない候補（LaneMargin が 0 以上）だけを使う
              3) 選んだ帯に重ならない候補がなければ、重ならない候補がある帯の中でいちばん足りない帯から選ぶ（遠方客は遠い帯から動かさない）
                 それもなければ、選んだ帯でいちばん余裕がある候補にする
        */
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

            // 選んだ帯だと重なる。遠方客じゃなければ、重ならない候補がある帯の中でいちばん足りない帯に回す
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

        /*
            band 番の帯（-1 ならぜんぶの帯）の空いている定位置のうち、ほかの客と重ならないものを _candidates に集める
            返す値: 重ならない候補がないときに使う、いちばん余裕がある空きの定位置。空きがなければ -1
        */
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

        /*
            補充する帯を選ぶ（企画書 v8 8章）。-1 ならどの帯でもいい
              ・遠方客は遠い帯（distantBandIndex）。遠い帯に空きがなければ次に足りない帯に回して、そのときの様子をログに出す
              ・わりあい（occupancyShare）が設定されていれば、今の目標の人数を 40/40/20% で整数にして、いちばん足りない帯を選ぶ
        */
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

        // 帯ごとのわりあい・いる人数（黒客じゃない客）・空きがあるかを数えなおす。帯がなければ false
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

        // 「近 4/6 中 5/6 遠 3/3」みたいな形で、いる人数（黒客じゃない客）と目標の枠を文字にする
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

        // 移動客の往復の真ん中。道が帯の左右の範囲に入るように、定位置を内側に寄せる
        private Vector3 LaneCenterFor(Slot slot)
        {
            Vector3 p = slot.Position;
            if (bands == null || slot.BandIndex < 0 || slot.BandIndex >= bands.Length || bands[slot.BandIndex] == null) return p;

            Band b = bands[slot.BandIndex];
            p.x = PatrolPath.ClampCenter(p.x, movingPatrolWidth, b.xRange.x, b.xRange.y);
            return p;
        }

        /*
            レーン（はばが0なら点）を置いたときに、定位置にいる・向かっている全員とどれくらい間があいているか（m）。0 以上なら重ならない
            どっちかが移動客なら往復の道全体で測って movingLaneClearance、どっちも立っている客なら minSlotDistance を必要な間かくにする
        */
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

        /*
            #58: 段階学習の客を置く定位置。band の帯の空きのうち、ほかの客と重ならず、正面（基準点の真ん前）にいちばん近いもの
            （18章「配置」で導線を作るので、学習の客は画面の真ん中に寄せる）。重ならない空きがなければ、いちばん正面に近い空き
            その帯に空きがなければ、ふつうの補充と同じ決め方でほかの帯から選ぶ
        */
        private int FindLearningSlot(int band)
        {
            float centerX = bandSpace == BandSpace.DistanceFromOrigin ? DistanceOriginPosition.x : 0f;
            int best = -1, fallback = -1;
            float bestScore = float.MaxValue, fallbackScore = float.MaxValue;
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot s = _slots[i];
                if (s.Occupant != null || s.BandIndex != band) continue;

                float score = Mathf.Abs(s.Position.x - centerX);
                if (LaneMargin(s.Position, 0f) >= 0f)
                {
                    if (score < bestScore) { bestScore = score; best = i; }
                }
                else if (score < fallbackScore)
                {
                    fallbackScore = score;
                    fallback = i;
                }
            }

            if (best < 0) best = fallback;
            if (best < 0) best = FindSlotFor(CustomerKind.Normal, null);
            return best;
        }

        private void FreeSlot(Member m)
        {
            if (m == null) return;
            if (m.SlotIndex < 0 || m.SlotIndex >= _slots.Count) return;
            if (_slots[m.SlotIndex].Occupant == m)
                _slots[m.SlotIndex].Occupant = null;
        }

        /*
            帯の中のランダムな1点を取る。ほかの定位置と minSlotDistance 以上はなれるまで
            slotPlacementAttempts 回ためして、だめならいちばんマシな候補を返す
        */
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

        /*
            基準点から水平距離が distanceRange の中にある1点を取る（#62）
            左右は xRange（見えない壁の内側）と maxLateralAngle の両方に入れて、奥行きはカメラが向いている -Z の方向に取る
        */
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

        // ---- デバッグ操作から呼ばれる切りかえ ----

        // 目標の人数を直接決める（8/12/15 の切りかえ用）
        public void SetOverrideTarget(int count)
        {
            overrideTargetCount = Mathf.Max(0, count);
            _warnedSlotShortage = false;
        }

        // 直接決めた人数をやめて、ランク＋お祭りの計算にもどす
        public void ClearOverrideTarget()
        {
            overrideTargetCount = -1;
            _warnedSlotShortage = false;
        }

        // お祭りモードを切りかえる
        public void ToggleFestival() => festivalMode = !festivalMode;

        // ゲージを凍結しているかどうか（デバッグ表示用）
        public bool FreezeGauges => freezeDanger;

        // ゲージの凍結を切りかえる。ON の間はだれもいなくならないので、人数が目標どおりにそろう
        public void ToggleFreezeGauges() => freezeDanger = !freezeDanger;

        // ランクを C→B→A→S→C の順に回す
        public void CycleRank()
        {
            rank = (ShrineRank)(((int)rank + 1) % 4);
            useShrineRatingIfPresent = false;   // 手で回したら連動はやめる
        }

        // 輪郭の表し方を全員ぶん切りかえる
        public void SetOutlineMode(MockCustomerOutline.OutlineMode next)
        {
            outlineMode = next;
            for (int i = 0; i < _members.Count; i++)
            {
                Member m = _members[i];
                if (m != null && m.Outline != null) m.Outline.SetMode(next);
            }
        }

        // 輪郭の表し方を切りかえる（トグル）
        public void ToggleOutlineMode()
        {
            SetOutlineMode(outlineMode == MockCustomerOutline.OutlineMode.InvertedHull
                ? MockCustomerOutline.OutlineMode.Emission
                : MockCustomerOutline.OutlineMode.InvertedHull);
        }

        // 輪郭の太さのモードを全員ぶん切りかえる
        public void SetOutlineWidthMode(MockCustomerOutline.WidthMode next)
        {
            outlineWidthMode = next;
            for (int i = 0; i < _members.Count; i++)
            {
                Member m = _members[i];
                if (m != null && m.Outline != null) m.Outline.SetWidthMode(next);
            }
        }

        // 今の輪郭の太さ（ワールド単位）。0以下ならプレハブ側の値を使っている
        public float OutlineWidth => outlineWidth;

        /*
            輪郭の太さを全員ぶん変える。このあと出てくる客にも同じ値が入る
            測りながら「何ミリなら小中学生が見分けられるか」を決めていくための操作
        */
        public void SetOutlineWidth(float world)
        {
            outlineWidth = Mathf.Clamp(world, outlineWidthRange.x, outlineWidthRange.y);
            for (int i = 0; i < _members.Count; i++)
            {
                Member m = _members[i];
                if (m != null && m.Outline != null) m.Outline.SetOutlineWidth(outlineWidth);
            }
        }

        // 輪郭の太さを1段階ぶん増やしたり減らしたりする（キー操作から呼ばれる）
        public void StepOutlineWidth(int direction)
        {
            // outlineWidth が 0以下（プレハブまかせ）のときは、まずふつうの値から始める
            float basis = outlineWidth > 0f ? outlineWidth : outlineWidthRange.x;
            SetOutlineWidth(basis + outlineWidthStep * direction);
        }

        // 輪郭の太さのモードを切りかえる（トグル）
        public void ToggleOutlineWidthMode()
        {
            SetOutlineWidthMode(outlineWidthMode == MockCustomerOutline.WidthMode.World
                ? MockCustomerOutline.WidthMode.ScreenConstant
                : MockCustomerOutline.WidthMode.World);
        }

        // ---- Scene ビューに表示する ----

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

            // プレイ中は、実際にしいた定位置も表示する
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

        // 距離の帯を、基準点を中心にした2本の弧（内側と外側）で描く
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
