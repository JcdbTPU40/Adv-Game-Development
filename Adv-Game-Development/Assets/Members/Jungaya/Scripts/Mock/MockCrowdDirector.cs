using System.Collections.Generic;
using UnityEngine;

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
    ///   1) 退場（CustomerMood が自分で Destroy した客）を回収してスロットを空ける
    ///   2) 目標との差ぶんだけ「補充チケット」を積む／余っていれば減らす
    ///   3) チケットは respawnDelay 経過で発火。ただし直前のスポーンから
    ///      minSpawnInterval 未満なら待つ（同時退場時の一斉湧きを抑える）
    ///   4) 発火 → 鳥居位置に生成 → 空きスロットを予約 → walkDuration 秒かけて歩く
    ///
    ///   ※ 退場の検知に CustomerMood.onAngry/onResolved を購読しないのは、
    ///     CustomerMood 側が resolveLingerTime の余韻を挟んでから Destroy するため。
    ///     購読するとディレイが二重に乗る。「参照が null になったか」で見るのが正しい。
    ///
    /// 既存コードは一切変更していない。初期ゲージのばらつきも
    /// CustomerMood の公開API(AddGauge/ReduceGauge)だけで実現している。
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
            public CustomerMood Mood;
            public MockCustomerTag Tag;
            public MockCustomerOutline Outline;
            public MockCustomerWalker Walker;
            public int SlotIndex = -1;

            /// <summary>ゲージ凍結中に維持する値。負なら未取得。</summary>
            public float FrozenGauge = -1f;
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
        [Header("輪郭発光5色（お守り5種に対応）")]
        [Tooltip("0:健康 1:学業 2:縁結び 3:金運 4:厄除け。実機テスト中に直接いじって調整する。")]
        [SerializeField]
        private Color[] omamoriColors =
        {
            new Color(0.20f, 1.00f, 0.45f),  // 健康：緑
            new Color(0.30f, 0.65f, 1.00f),  // 学業：青
            new Color(1.00f, 0.40f, 0.70f),  // 縁結び：桃
            new Color(1.00f, 0.85f, 0.15f),  // 金運：金
            new Color(0.75f, 0.35f, 1.00f),  // 厄除け：紫
        };

        [Tooltip("黒客の輪郭色。他5色と識別できるかが #44 の検証ポイント。")]
        [SerializeField] private Color blackCustomerColor = new Color(0.04f, 0.04f, 0.06f);

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

        // ── 客ごとのばらつき ────────────────────────────────────
        [Header("客ごとのばらつき")]
        [Tooltip("初期ゲージの範囲（0〜1の正規化）。残量差が視認できるようにばらけさせる。")]
        [SerializeField] private Vector2 startGaugeRange = new Vector2(0.15f, 0.85f);

        [Tooltip("maxGauge を逆算できなかったときのフォールバック値。")]
        [SerializeField] private float assumedMaxGauge = 100f;

        [Tooltip("常時混ぜておく黒客の数。")]
        [SerializeField] private int blackCustomerCount = 3;

        [Tooltip("ON にするとゲージの自然上昇を打ち消し、誰も退場しなくなる。" +
                 "12〜15体をきっちり並べて識別テストしたいときに使う（既定OFF＝補充テンポの検証用）。")]
        [SerializeField] private bool freezeGauges;

        // ── 輪郭の初期設定（デバッグ操作で実行中に切り替わる）──────
        [Header("輪郭の表現（実行中に切替可）")]
        [SerializeField] private MockCustomerOutline.OutlineMode outlineMode = MockCustomerOutline.OutlineMode.InvertedHull;
        [SerializeField] private MockCustomerOutline.WidthMode outlineWidthMode = MockCustomerOutline.WidthMode.ScreenConstant;

        // ── 内部状態 ────────────────────────────────────────────
        private readonly List<Member> _members = new List<Member>();
        private readonly List<Slot> _slots = new List<Slot>();
        private readonly List<float> _tickets = new List<float>();   // 補充チケットの残りディレイ

        private float _lastSpawnTime = -999f;
        private int _nextColorIndex;
        private int _spawnSerial;
        private bool _warnedSlotShortage;
        private bool _warnedMissingPrefab;

        // ── 公開API（HUD／デバッグ操作から読む）──────────────────

        /// <summary>境内にいる客（歩行中を含む）。</summary>
        public IReadOnlyList<Member> Members => _members;

        /// <summary>いま画面上にいる体数（歩行中を含む）。</summary>
        public int AliveCount => _members.Count;

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
        }

        private void LateUpdate()
        {
            ApplyGaugeFreeze();
        }

        /// <summary>
        /// ゲージ凍結。CustomerMood.Update が加算した自然上昇分を、その場で同じだけ削って打ち消す。
        ///
        /// なぜ必要か:
        ///   凍結なしだと naturalRiseRate によって常に誰かが怒って退場するため、
        ///   目標15体でも「定位置に立っている数」は 13 前後にしかならない（実測値）。
        ///   12〜15体をきっちり並べて識別可否を測る、という #44 の完了条件を満たせない。
        ///
        /// 実装:
        ///   LateUpdate は全 Update の後に走るので、この時点のゲージは「前フレーム値 + 上昇分」。
        ///   差分を ReduceGauge で戻せば値が固定される。CustomerMood の公開APIだけで完結し、
        ///   naturalRiseRate（private）に触る必要がない＝既存コードは無改変のまま。
        /// </summary>
        private void ApplyGaugeFreeze()
        {
            for (int i = 0; i < _members.Count; i++)
            {
                Member m = _members[i];
                if (m == null || m.Go == null || m.Mood == null) continue;

                if (!freezeGauges)
                {
                    m.FrozenGauge = -1f;         // 解除。次に凍結したとき現在値から拾い直す
                    continue;
                }

                if (m.FrozenGauge < 0f)
                {
                    m.FrozenGauge = m.Mood.Gauge;
                    continue;
                }

                float risen = m.Mood.Gauge - m.FrozenGauge;
                if (risen > 0.0001f)
                    m.Mood.ReduceGauge(risen, isBreakthrough: false);
            }
        }

        // ── 補充ループ ──────────────────────────────────────────

        /// <summary>
        /// 退場（CustomerMood が自分で Destroy した客）を回収し、スロットを空ける。
        /// </summary>
        private void SweepDeparted()
        {
            for (int i = _members.Count - 1; i >= 0; i--)
            {
                Member m = _members[i];
                if (m != null && m.Go != null) continue;

                FreeSlot(m);
                _members.RemoveAt(i);
            }
        }

        /// <summary>
        /// 「現在数 + 補充待ち」を目標体数に合わせる。
        /// 足りなければチケットを積み、余っていればチケット→実体の順に減らす。
        /// </summary>
        private void BalanceToTarget()
        {
            int want = TargetCount;
            int have = _members.Count + _tickets.Count;

            while (have < want)
            {
                _tickets.Add(respawnDelay);
                have++;
            }

            while (have > want)
            {
                if (_tickets.Count > 0)
                    _tickets.RemoveAt(_tickets.Count - 1);
                else
                    DespawnOne();
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

            int slotIndex = FindFreeSlot();
            if (slotIndex < 0) return null;

            Slot slot = _slots[slotIndex];

            // 毎回抽選し直すモードでは、ここで空き位置を引き直す。
            if (slotPolicy == SlotPolicy.RandomEachTime)
                slot.Position = RandomPointInBand(slot.BandIndex, onlyOccupied: true);

            Vector3 destination = slot.Position;
            Vector3 origin = ToriiPosition;

            GameObject go = Instantiate(
                customerPrefab,
                instant ? destination : origin,
                Quaternion.identity,
                customerParent);
            go.name = $"MockCustomer_{_spawnSerial++:00}";

            var member = new Member
            {
                Go = go,
                Tr = go.transform,
                Mood = go.GetComponent<CustomerMood>(),
                Tag = go.GetComponent<MockCustomerTag>(),
                Outline = go.GetComponent<MockCustomerOutline>(),
                Walker = go.GetComponent<MockCustomerWalker>(),
                SlotIndex = slotIndex,
            };

            slot.Occupant = member;

            AssignIdentity(member);
            RandomizeGauge(member.Mood);

            if (member.Walker != null)
            {
                if (instant) member.Walker.SnapTo(destination);
                else member.Walker.Begin(origin, destination, walkDuration, walkEase);
            }
            else
            {
                go.transform.position = destination;
            }

            _members.Add(member);
            return member;
        }

        /// <summary>
        /// 目標体数が下がったときに1体減らす。
        /// まだ歩行中の客（＝定位置に着いていない＝一番目立たない）を優先して消す。
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
        /// 輪郭色（お守り5色を巡回）と黒客フラグを割り当てる。
        /// 黒客は「不足数 ÷ 残り枠」の確率で選ぶので、まとめて先頭に固まらず自然に散る。
        /// </summary>
        private void AssignIdentity(Member m)
        {
            if (m == null || m.Tag == null) return;

            int deficit = Mathf.Max(0, blackCustomerCount - CountBlack());
            int slotsLeft = Mathf.Max(1, TargetCount - _members.Count);   // 自分を含む残り枠

            bool black = deficit > 0 &&
                         (deficit >= slotsLeft || Random.value < (float)deficit / slotsLeft);

            int index = -1;
            Color color;

            if (black)
            {
                color = blackCustomerColor;
            }
            else
            {
                index = NextColorIndex();
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
            }
        }

        /// <summary>5色を順に巡回させ、どの色も必ず出るようにする。</summary>
        private int NextColorIndex()
        {
            int n = (omamoriColors != null && omamoriColors.Length > 0) ? omamoriColors.Length : 5;
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
                if (m != null && m.Tag != null && m.Tag.IsBlack) n++;
            }
            return n;
        }

        /// <summary>
        /// 初期ゲージを客ごとにばらけさせる（残量差が視認できる状態にするため）。
        ///
        /// CustomerMood の maxGauge は private だが、Gauge / GaugeNormalized から逆算できる。
        /// 値の変更も公開API(AddGauge/ReduceGauge)だけで足りるので、CustomerMood は無改変で済む。
        /// </summary>
        private void RandomizeGauge(CustomerMood mood)
        {
            if (mood == null) return;

            float norm = mood.GaugeNormalized;
            float max = norm > 0.0001f ? mood.Gauge / norm : assumedMaxGauge;
            if (max <= 0.0001f) return;

            // 0 や満タンに触れると即 解消/怒り が確定してしまうので、両端は必ず避ける。
            float lo = Mathf.Clamp(Mathf.Min(startGaugeRange.x, startGaugeRange.y), 0.02f, 0.98f);
            float hi = Mathf.Clamp(Mathf.Max(startGaugeRange.x, startGaugeRange.y), 0.02f, 0.98f);

            float target = Random.Range(lo, hi) * max;
            float diff = target - mood.Gauge;

            if (diff > 0.01f) mood.AddGauge(diff);
            else if (diff < -0.01f) mood.ReduceGauge(-diff, isBreakthrough: false);
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
                        Position = RandomPointInBand(bi, onlyOccupied: false),
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

        private int FindFreeSlot()
        {
            // 空きの中からランダムに選ぶ（前詰めにすると手前ばかり埋まるため）。
            int free = 0;
            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i].Occupant == null) free++;

            if (free == 0) return -1;

            int pick = Random.Range(0, free);
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].Occupant != null) continue;
                if (pick == 0) return i;
                pick--;
            }
            return -1;
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
        private Vector3 RandomPointInBand(int bandIndex, bool onlyOccupied)
        {
            if (bands == null || bandIndex < 0 || bandIndex >= bands.Length)
                return new Vector3(0f, customerY, 0f);

            Band b = bands[bandIndex];
            Vector3 best = new Vector3(0f, customerY, 0f);
            float bestDistance = -1f;

            int attempts = Mathf.Max(1, slotPlacementAttempts);
            for (int i = 0; i < attempts; i++)
            {
                var candidate = new Vector3(
                    Random.Range(Mathf.Min(b.xRange.x, b.xRange.y), Mathf.Max(b.xRange.x, b.xRange.y)),
                    customerY,
                    Random.Range(Mathf.Min(b.zRange.x, b.zRange.y), Mathf.Max(b.zRange.x, b.zRange.y)));

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
        public bool FreezeGauges => freezeGauges;

        /// <summary>ゲージ凍結を切り替える。ON の間は誰も退場しないので体数が目標どおりに揃う。</summary>
        public void ToggleFreezeGauges() => freezeGauges = !freezeGauges;

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
        }
    }
}
