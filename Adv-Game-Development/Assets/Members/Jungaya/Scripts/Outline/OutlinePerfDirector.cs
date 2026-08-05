using System.Collections.Generic;
using UnityEngine;

namespace Toufuku.Rescue.Outline
{
    /// <summary>
    /// #45 負荷検証用の簡易群衆。MockCrowdDirector は触らず、計測シーン専用に独立実装。
    /// 近/中/遠の3帯に最大16体を配置し、お守り5色を巡回で割り当てる。
    /// </summary>
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

        [SerializeField] GameObject customerPrefab;
        [SerializeField] Transform customerParent;
        [SerializeField] int targetCount = 16;
        [SerializeField] int randomSeed = 45;
        [SerializeField] float customerY = 1f;

        [SerializeField]
        Color[] omamoriColors =
        {
            new Color(0.20f, 1.00f, 0.45f), // 健康：緑
            new Color(0.30f, 0.65f, 1.00f), // 学業：青
            new Color(1.00f, 0.40f, 0.70f), // 縁結び：桃
            new Color(1.00f, 0.85f, 0.15f), // 金運：金
            new Color(0.75f, 0.35f, 1.00f), // 厄除け：紫
        };

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
        readonly List<Vector3> _slots = new List<Vector3>(18);

        public int TargetCount => targetCount;
        public int AliveCount => _spawned.Count;
        public IReadOnlyList<Color> OmamoriColors => omamoriColors;

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

            if (customerPrefab == null || _slots.Count == 0) return;

            int n = Mathf.Min(targetCount, _slots.Count);
            for (int i = 0; i < n; i++)
            {
                var go = Instantiate(customerPrefab, _slots[i], Quaternion.identity, customerParent);
                go.name = $"OutlineCustomer_{i:00}";

                var outline = go.GetComponent<OutlineTarget>();
                if (outline == null) outline = go.AddComponent<OutlineTarget>();

                Color c = omamoriColors != null && omamoriColors.Length > 0
                    ? omamoriColors[i % omamoriColors.Length]
                    : Color.white;
                // 先頭1体を暗色に差し替え、暗い輪郭でも A>0 で検出できることを常時検証できるようにする。
                if (includeDarkTestCustomer && i == 0)
                {
                    c = darkTestColor;
                    go.name = $"OutlineCustomer_{i:00}_DarkTest";
                }
                outline.SetColor(c);
                outline.SetPattern(OutlinePattern.Solid);

                // 本体色をニュートラル灰に（輪郭を主役に）。
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
            }
        }
    }
}
