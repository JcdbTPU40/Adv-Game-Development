using System.Collections.Generic;
using UnityEngine;

namespace Toufuku.Rescue.Outline
{
    /*
        #45 の重さを調べるための、かんたんな客の集団。MockCrowdDirector はさわらずに、計測シーン用に別で作った
        近い・中・遠いの3つの帯に最大16人をならべて、お守り5種を順番に決める

        #59 から:
          ・色は OmamoriPalette、もようは OutlineStyle.PatternFor で、お守りの種類から決める（ゲームと同じ組み合わせ）
          ・greedyCount 人を欲張り客にして、内側に2色目の輪を出す（近い帯に1人、いちばん奥に1人。遠くでも読めるかを見る）
          ・もようの ON/OFF、照準が乗ったときの明るさ、欲張り客の1発目を、キーでためせる（OutlinePerfHud）
    */
    public class OutlinePerfDirector : MonoBehaviour
    {
        [System.Serializable]
        public class Band
        {
            public string label = "近";
            public Vector2 zRange = new Vector2(27f, 33f);
            public Vector2 xRange = new Vector2(-6f, 6f);
            public int slotCount = 6;
        }

        // 計測シーンの客1人ぶん
        struct Member
        {
            public OutlineTarget Target;
            // 今ほしいお守り
            public OmamoriType Current;
            // 最初にほしかったお守り（欲張り客をもとにもどす用）
            public OmamoriType First;
            // 欲張り客が次にほしいお守り
            public OmamoriType Next;
            // 残りの必要な発数 R
            public int Remaining;
            public bool Greedy;
            public bool Dark;
        }

        [SerializeField] GameObject customerPrefab;
        [SerializeField] Transform customerParent;
        [SerializeField] int targetCount = 16;
        [SerializeField] int randomSeed = 45;
        [SerializeField] float customerY = 1f;

        [Header("色ともよう（#59）")]
        [Tooltip("お守り5色のパレット（色の正しい値）。未設定なら下の予備の色を使う。")]
        [SerializeField] OmamoriPalette palette;

        [Tooltip("palette がないときの予備の色。OmamoriType の順（0:健康 1:学業成就 2:厄除け安全 3:縁結び 4:金運）。")]
        [SerializeField]
        Color[] fallbackColors =
        {
            new Color(63 / 255f, 191 / 255f, 95 / 255f),  // 健康: #3FBF5F
            new Color(47 / 255f, 127 / 255f, 216 / 255f), // 学業成就: #2F7FD8
            new Color(139 / 255f, 95 / 255f, 208 / 255f), // 厄除け安全: #8B5FD0
            new Color(232 / 255f, 95 / 255f, 143 / 255f), // 縁結び: #E85F8F
            new Color(232 / 255f, 185 / 255f, 63 / 255f), // 金運: #E8B93F
        };

        [Tooltip("欲張り客（輪が2本）にする人数。同時上限は2人（v8 8章）。暗色検証の1人目はのぞく。")]
        [Range(0, 2)]
        [SerializeField] int greedyCount = 2;

        [Header("暗色検証（#45 B）")]
        [Tooltip("ON なら先頭の1体を #44 黒客相当の暗い輪郭色にする。存在フラグ(A)方式の確認用。")]
        [SerializeField] bool includeDarkTestCustomer = true;

        [Tooltip("#44 MockCrowdDirector.blackCustomerColor 相当。")]
        [SerializeField] Color darkTestColor = new Color(0.04f, 0.04f, 0.06f);

        [SerializeField]
        Band[] bands =
        {
            new Band { label = "近", zRange = new Vector2(27f, 33f), xRange = new Vector2(-6f, 6f), slotCount = 6 },
            new Band { label = "中", zRange = new Vector2(19f, 26f), xRange = new Vector2(-9f, 9f), slotCount = 6 },
            new Band { label = "遠", zRange = new Vector2(12f, 18f), xRange = new Vector2(-11f, 11f), slotCount = 6 },
        };

        readonly List<GameObject> _spawned = new List<GameObject>(16);
        readonly List<Member> _members = new List<Member>(16);
        readonly List<Vector3> _slots = new List<Vector3>(18);
        bool _patternsEnabled = true;
        int _highlightIndex = -1;

        public int TargetCount => targetCount;
        public int AliveCount => _spawned.Count;
        // もようを出しているか（OFF のときは全員実線＝色だけ）
        public bool PatternsEnabled => _patternsEnabled;
        // 照準が乗っていることにしている客の番号（-1 はだれにも乗っていない）
        public int HighlightIndex => _highlightIndex;

        // 欲張り客の人数
        public int GreedyCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _members.Count; i++)
                    if (_members[i].Greedy) n++;
                return n;
            }
        }

        // 欲張り客の今の R（ひとりもいなければ 0）
        public int GreedyRemaining
        {
            get
            {
                for (int i = 0; i < _members.Count; i++)
                    if (_members[i].Greedy) return _members[i].Remaining;
                return 0;
            }
        }

        void Start()
        {
            if (customerParent == null) customerParent = transform;
            BuildSlots();
            RespawnAll();
        }

        public void SetTargetCount(int count)
        {
            targetCount = Mathf.Clamp(count, 0, _slots.Count > 0 ? _slots.Count : 16);
            RespawnAll();
        }

        // もようの ON/OFF。OFF にすると全員実線になって、色だけにたよったときとくらべられる
        public void SetPatternsEnabled(bool on)
        {
            _patternsEnabled = on;
            ApplyAll();
        }

        // 照準が乗った客を次の人にする（最後の次はだれにも乗っていない）。照準デバイスなしで「一段明るく」を見る用
        public void CycleHighlight()
        {
            int n = _members.Count;
            _highlightIndex = n == 0 ? -1 : (_highlightIndex + 2) % (n + 1) - 1;
            ApplyAll();
        }

        /*
            欲張り客に1発目を当てたことにする。R=2→1 で、内側の2色目が今の色になって輪が1本になる（v8 8章）
            全員もう R=1 なら、もとの R=2 にもどす（くり返し見られるように）
        */
        public void AdvanceGreedy()
        {
            bool anyTwo = false;
            for (int i = 0; i < _members.Count; i++)
                if (_members[i].Greedy && _members[i].Remaining >= 2) anyTwo = true;

            for (int i = 0; i < _members.Count; i++)
            {
                Member m = _members[i];
                if (!m.Greedy) continue;

                if (anyTwo)
                {
                    if (m.Remaining >= 2)
                    {
                        m.Current = m.Next;
                        m.Remaining = 1;
                    }
                }
                else
                {
                    m.Current = m.First;
                    m.Remaining = 2;
                }
                _members[i] = m;
            }
            ApplyAll();
        }

        void BuildSlots()
        {
            _slots.Clear();
            var prev = Random.state;
            Random.InitState(randomSeed);

            if (bands == null) return;
            for (int bi = 0; bi < bands.Length; bi++)
            {
                var b = bands[bi];
                if (b == null) continue;
                for (int i = 0; i < b.slotCount; i++)
                {
                    _slots.Add(new Vector3(
                        Random.Range(Mathf.Min(b.xRange.x, b.xRange.y), Mathf.Max(b.xRange.x, b.xRange.y)),
                        customerY,
                        Random.Range(Mathf.Min(b.zRange.x, b.zRange.y), Mathf.Max(b.zRange.x, b.zRange.y))));
                }
            }

            Random.state = prev;
        }

        void RespawnAll()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null) Destroy(_spawned[i]);
            }
            _spawned.Clear();
            _members.Clear();
            _highlightIndex = -1;

            if (customerPrefab == null || _slots.Count == 0) return;

            int kinds = System.Enum.GetValues(typeof(OmamoriType)).Length;
            int n = Mathf.Min(targetCount, _slots.Count);
            for (int i = 0; i < n; i++)
            {
                var go = Instantiate(customerPrefab, _slots[i], Quaternion.identity, customerParent);
                go.name = $"OutlineCustomer_{i:00}";

                var outline = go.GetComponent<OutlineTarget>();
                if (outline == null) outline = go.AddComponent<OutlineTarget>();

                // 先頭の1人を暗い色にかえて、暗い輪郭でも A>0 で見つけられるかをいつも確かめられるようにする
                bool dark = includeDarkTestCustomer && i == 0;
                bool greedy = !dark && IsGreedySlot(i, n);
                var type = (OmamoriType)(i % kinds);

                var member = new Member
                {
                    Target = outline,
                    Current = type,
                    First = type,
                    // 2色は必ずちがう色にする（v8 8章）。ずらす量は 1〜kinds-1
                    Next = (OmamoriType)((i % kinds + 1 + i % (kinds - 1)) % kinds),
                    Remaining = greedy ? 2 : 1,
                    Greedy = greedy,
                    Dark = dark,
                };
                if (dark) go.name += "_DarkTest";
                if (greedy) go.name += "_Greedy";

                // 本体の色はグレーにする（輪郭を目立たせるため）
                var rend = go.GetComponentInChildren<Renderer>();
                if (rend != null)
                {
                    var mpb = new MaterialPropertyBlock();
                    rend.GetPropertyBlock(mpb);
                    var body = new Color(0.72f, 0.70f, 0.66f);
                    mpb.SetColor("_BaseColor", body);
                    mpb.SetColor("_Color", body);
                    rend.SetPropertyBlock(mpb);
                    outline.SetRenderer(rend);
                }

                _spawned.Add(go);
                _members.Add(member);
            }

            ApplyAll();
        }

        // 欲張り客にする場所: 1人目は近い帯の2番目、2人目はいちばん奥
        bool IsGreedySlot(int index, int count)
        {
            if (greedyCount >= 1 && index == 1) return true;
            if (greedyCount >= 2 && index == count - 1 && index > 1) return true;
            return false;
        }

        // 全員の色・もよう・輪の数・明るさを OutlineTarget に入れなおす
        void ApplyAll()
        {
            for (int i = 0; i < _members.Count; i++)
            {
                Member m = _members[i];
                if (m.Target == null) continue;

                if (m.Dark)
                {
                    m.Target.SetCurrent(darkTestColor, OutlinePattern.Solid);
                    m.Target.ClearNext();
                }
                else
                {
                    m.Target.SetCurrent(ColorOf(m.Current), PatternOf(m.Current));
                    if (OutlineStyle.RingCount(m.Remaining, m.Greedy) >= 2)
                        m.Target.SetNext(ColorOf(m.Next), PatternOf(m.Next));
                    else
                        m.Target.ClearNext();
                }

                m.Target.SetHighlighted(i == _highlightIndex);
            }
        }

        Color ColorOf(OmamoriType type)
        {
            if (palette != null) return palette.GetColor(type);
            int index = (int)type;
            return fallbackColors != null && index < fallbackColors.Length ? fallbackColors[index] : Color.magenta;
        }

        OutlinePattern PatternOf(OmamoriType type)
            => _patternsEnabled ? OutlineStyle.PatternFor(type) : OutlinePattern.Solid;
    }
}
