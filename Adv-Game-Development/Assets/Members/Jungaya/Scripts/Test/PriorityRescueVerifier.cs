using System.Collections.Generic;
using UnityEngine;
using Toufuku.Aim;
using Toufuku.Playtest;
using Toufuku.Rescue;

/// <summary>
/// 二重円と優先救済の確認シーン進行 — Issue #55（完了条件「PlayMode で同値ケース・移動ケースが再現確認できる」）
///
/// 確認したいのは次の 2 つ。どちらもキー 1 つで再現できるようにする。
///   ・同値ケース（F5）… 危険度 D も距離も同じ客を 3 人並べ、二重円が<b>必ず 1 人</b>に定まり、
///                        フレームごとにちらちら移らないことを見る。
///   ・移動ケース（F6）… 発射した直後に別の客の D を跳ね上げて二重円を移す。
///                        それでも<b>発射時に狙った客</b>を救済すれば +50 が入ることを、得点の内訳で見る。
///
/// 客はこのコンポーネントが実行時に作る（F8 で作り直せる）。シーンには地面・カメラ・入力・スコアだけを置く。
/// 操作: 1 キーで色（健康）を選び、クリック（＝振り）で投げる。F5 同値 / F6 移動 / F7 通常（D は時間で増える）/ F8 作り直し。
/// </summary>
[DisallowMultipleComponent]
public class PriorityRescueVerifier : MonoBehaviour
{
    public enum Mode
    {
        /// <summary>通常。D は時間で増える。</summary>
        Free,
        /// <summary>同値ケース。全員の D を同じ値に固定する。</summary>
        Tie,
        /// <summary>移動ケース。発射後に二重円が別の客へ移る。</summary>
        Moving
    }

    [Header("客の配置（照準の基準点からの距離と角度）")]
    [Tooltip("並べる人数。")]
    [SerializeField, Range(2, 6)] int customerCount = 3;
    [Tooltip("基準点からの距離（m）。同値ケースでは全員この距離に置くので、距離でも差が付かない。")]
    [SerializeField, Min(2f)] float distance = 12f;
    [Tooltip("左右に振る角度（度）。")]
    [SerializeField, Min(1f)] float spreadDegrees = 26f;
    [Tooltip("客を並べる中心（未設定なら照準の基準点 → 原点の順で決める）。")]
    [SerializeField] Transform arcCenter;

    [Header("客の中身")]
    [Tooltip("当たり判定の半径（m）。")]
    [SerializeField, Min(0.2f)] float hitRadius = 0.9f;
    [Tooltip("客種の数値表（付録B B-1）。未設定ならフォールバック値で動く。")]
    [SerializeField] CustomerKindTable kindTable;
    [Tooltip("全員が求める色。この色を選んで当てれば救済される。")]
    [SerializeField] OmamoriType correctOmamori = OmamoriType.Kenkou;

    [Header("同値ケース（F5）")]
    [Tooltip("全員に固定する危険度 D。")]
    [SerializeField, Range(0f, 99f)] float tieDanger = 64f;

    [Header("移動ケース（F6）")]
    [Tooltip("狙わせる客（左から何番目か）。この客が最初の二重円になる。")]
    [SerializeField, Min(0)] int movingTargetIndex = 0;
    [Tooltip("発射から二重円が移るまでの秒数。飛翔時間（0.25〜0.65秒）より短くすること。")]
    [SerializeField, Min(0f)] float switchDelaySeconds = 0.15f;
    [Tooltip("二重円を移す先の客の D。狙わせる客より高くする。")]
    [SerializeField, Range(1f, 99f)] float switchedDanger = 92f;
    [Tooltip("狙わせる客の D（移動ケース中の固定値）。")]
    [SerializeField, Range(0f, 99f)] float movingBaseDanger = 60f;

    [Header("キー")]
    [SerializeField] KeyCode tieKey = KeyCode.F5;
    [SerializeField] KeyCode movingKey = KeyCode.F6;
    [SerializeField] KeyCode freeKey = KeyCode.F7;
    [SerializeField] KeyCode rebuildKey = KeyCode.F8;

    readonly List<CustomerState> _customers = new List<CustomerState>();

    Mode _mode = Mode.Free;
    Material _bodyMaterial;
    Transform _origin;

    // 直近の 1 投の記録（HUD 用）
    int _savedAtSwing;
    int _priorityAtLanding;
    bool _hasThrow;
    bool _lastRescued;
    bool _lastPriorityBonus;
    int _lastGain;
    int _lastAccuracyBonus;
    int _lastPriorityGain;
    float _switchAt = -1f;

    void OnEnable()
    {
        OmamoriProjectile.AnyLaunched += HandleLaunched;
        OmamoriHitResolver.HitResolved += HandleHitResolved;
    }

    void OnDisable()
    {
        OmamoriProjectile.AnyLaunched -= HandleLaunched;
        OmamoriHitResolver.HitResolved -= HandleHitResolved;
    }

    void Start()
    {
        _origin = ResolveOrigin();
        Rebuild();
    }

    void Update()
    {
        if (Input.GetKeyDown(tieKey)) SetMode(Mode.Tie);
        if (Input.GetKeyDown(movingKey)) SetMode(Mode.Moving);
        if (Input.GetKeyDown(freeKey)) SetMode(Mode.Free);
        if (Input.GetKeyDown(rebuildKey)) Rebuild();
    }

    void LateUpdate()
    {
        // CustomerState.Update が D を進めたあとに上書きする（固定したい値へ毎フレーム戻す）。
        switch (_mode)
        {
            case Mode.Tie:
                for (int i = 0; i < _customers.Count; i++)
                {
                    if (Alive(_customers[i])) _customers[i].SetDangerForDebug(tieDanger);
                }
                break;

            case Mode.Moving:
                bool switched = _switchAt >= 0f && Time.time >= _switchAt;
                for (int i = 0; i < _customers.Count; i++)
                {
                    if (!Alive(_customers[i])) continue;

                    bool isAimTarget = i == Mathf.Clamp(movingTargetIndex, 0, _customers.Count - 1);
                    float danger = isAimTarget
                        ? movingBaseDanger
                        : (switched && i == SwitchToIndex() ? switchedDanger : 5f);
                    _customers[i].SetDangerForDebug(danger);
                }
                break;
        }
    }

    /// <summary>移動ケースで二重円を移す先（狙わせる客以外の 1 人目）。</summary>
    int SwitchToIndex()
    {
        int aimIndex = Mathf.Clamp(movingTargetIndex, 0, Mathf.Max(0, _customers.Count - 1));
        for (int i = 0; i < _customers.Count; i++)
        {
            if (i != aimIndex && Alive(_customers[i])) return i;
        }
        return aimIndex;
    }

    void SetMode(Mode mode)
    {
        _mode = mode;
        _switchAt = -1f;
        Debug.Log($"[#55確認] モード: {ModeLabel(mode)}", this);
    }

    static string ModeLabel(Mode mode)
    {
        switch (mode)
        {
            case Mode.Tie: return "同値ケース（全員 D が同じ）";
            case Mode.Moving: return "移動ケース（飛翔中に二重円が移る）";
            default: return "通常（D は時間で増える）";
        }
    }

    // ---- 客を作る ----

    /// <summary>客を作り直す（F8）。</summary>
    public void Rebuild()
    {
        for (int i = 0; i < _customers.Count; i++)
        {
            if (_customers[i] != null) Destroy(_customers[i].gameObject);
        }
        _customers.Clear();

        Vector3 center = _origin != null ? _origin.position : Vector3.zero;
        center.y = 0f;

        for (int i = 0; i < customerCount; i++)
        {
            float t = customerCount == 1 ? 0.5f : i / (float)(customerCount - 1);
            float angle = Mathf.Lerp(-spreadDegrees, spreadDegrees, t);

            // 基準点から同じ距離の弧の上に置く。距離で差が付かないので、同値ケースでは
            // 「active 化が早い方 → 生成IDが小さい方」まで進んで 1 人に決まる。
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * ForwardDirection();
            Vector3 position = center + direction * distance;

            _customers.Add(BuildCustomer(i, position));
        }

        _hasThrow = false;
        _switchAt = -1f;
        Debug.Log($"[#55確認] 客を {_customers.Count} 人作り直しました（{ModeLabel(_mode)}）", this);
    }

    CustomerState BuildCustomer(int index, Vector3 position)
    {
        var go = new GameObject($"VerifyCustomer{index + 1}");
        go.transform.position = position;

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(go.transform, false);
        body.transform.localPosition = new Vector3(0f, 1f, 0f);
        foreach (Collider c in body.GetComponentsInChildren<Collider>(true)) Destroy(c);

        if (_bodyMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader != null) _bodyMaterial = new Material(shader) { name = "VerifyCustomerBody" };
        }
        if (_bodyMaterial != null) body.GetComponent<MeshRenderer>().sharedMaterial = _bodyMaterial;

        var zone = go.AddComponent<HitZoneTarget>();
        SetPrivateField(zone, "outerRadius", hitRadius);

        var state = go.AddComponent<CustomerState>();
        state.Setup(CustomerKind.Normal, kindTable);

        // 全員に同じ色を求めさせる（この確認の主題は色の当てっこではない）。
        // 相性テーブルを割り当てないので、CustomerRescue はフォールバックの正解色で判定する。
        var rescue = go.AddComponent<CustomerRescue>();
        SetPrivateField(rescue, "correctOmamori", correctOmamori);

        // 優先対象の照合は生成ID で行う（#55）。ここで確定させておく。
        CustomerSpawnId.Assign(go);

        return state;
    }

    /// <summary>検証シーン限定：インスペクタ用の private 値を実行時に差し込む。</summary>
    static void SetPrivateField(Object target, string field, object value)
    {
        if (target == null) return;

        System.Reflection.FieldInfo info = target.GetType().GetField(field,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (info == null)
        {
            Debug.LogWarning($"[#55確認] {target.GetType().Name}.{field} が見つかりません（名前が変わった？）");
            return;
        }
        info.SetValue(target, value);
    }

    static bool Alive(CustomerState state)
    {
        return state != null && state.IsRescueTarget;
    }

    // ---- 1 投の記録 ----

    void HandleLaunched(OmamoriProjectile projectile)
    {
        if (projectile == null) return;

        _savedAtSwing = projectile.PriorityTargetId;
        _priorityAtLanding = 0;
        _hasThrow = true;
        _lastRescued = false;
        _lastPriorityBonus = false;
        _lastGain = 0;
        _lastAccuracyBonus = 0;
        _lastPriorityGain = 0;

        // 移動ケース：飛翔の途中で二重円を別の客へ移す。
        if (_mode == Mode.Moving) _switchAt = Time.time + switchDelaySeconds;
    }

    void HandleHitResolved(OmamoriHitInfo info)
    {
        _priorityAtLanding = PriorityRescue.CurrentTargetIdOrNone;
        _lastRescued = info.Rescued;
        _lastPriorityBonus = info.PriorityRescue;

        if (ScoreManager.Instance != null)
        {
            _lastGain = ScoreManager.Instance.LastGain;
            _lastAccuracyBonus = ScoreManager.Instance.LastBonus;
            _lastPriorityGain = ScoreManager.Instance.LastPriorityBonus;
        }

        Debug.Log($"[#55確認] 着弾: 発射時の二重円ID={_savedAtSwing} 着弾時の二重円ID={_priorityAtLanding} " +
                  $"救済={_lastRescued} 優先救済加点={(_lastPriorityBonus ? _lastPriorityGain : 0)} 獲得={_lastGain}", this);
    }

    /// <summary>客を並べる向き（カメラの前方）。カメラが無ければ基準点の前方。</summary>
    Vector3 ForwardDirection()
    {
        Camera cam = Camera.main;
        Vector3 forward = cam != null
            ? cam.transform.forward
            : (_origin != null ? _origin.forward : Vector3.forward);

        forward.y = 0f;
        return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
    }

    Transform ResolveOrigin()
    {
        if (arcCenter != null) return arcCenter;

        var aim = FindAnyObjectByType<OnusaAimController>();
        if (aim != null) return aim.transform;

        Camera cam = Camera.main;
        return cam != null ? cam.transform : transform;
    }

    // ---- HUD ----

    void OnGUI()
    {
        const float width = 430f;
        GUILayout.BeginArea(new Rect(12f, 12f, width, 420f), GUI.skin.box);

        GUILayout.Label($"#55 二重円と優先救済の確認  —  {ModeLabel(_mode)}");
        GUILayout.Label($"{tieKey}=同値ケース  {movingKey}=移動ケース  {freeKey}=通常  {rebuildKey}=客を作り直す");
        GUILayout.Label($"投げ方: 1 キーで色（{correctOmamori}）→ クリックで振る");
        GUILayout.Space(6f);

        int? priority = PriorityRescue.CurrentTargetId;
        GUILayout.Label($"いまの二重円: {(priority.HasValue ? $"ID {priority.Value}" : "なし")}");

        for (int i = 0; i < _customers.Count; i++)
        {
            CustomerState c = _customers[i];
            if (c == null) { GUILayout.Label($"  [{i + 1}] 退場済み"); continue; }

            int id = CustomerSpawnId.Of(c.gameObject);
            string mark = priority.HasValue && priority.Value == id ? "◎二重円" : "　";
            GUILayout.Label($"  [{i + 1}] ID {id}  D={c.Danger:0.0}  R={c.Remaining}  {c.Phase}  {mark}");
        }

        GUILayout.Space(6f);
        if (!_hasThrow)
        {
            GUILayout.Label("まだ投げていません。");
        }
        else
        {
            GUILayout.Label($"直近の1投:");
            GUILayout.Label($"  発射時に弾へ保存した二重円ID: {(_savedAtSwing > 0 ? _savedAtSwing.ToString() : "なし")}");
            GUILayout.Label($"  着弾時の二重円ID            : {(_priorityAtLanding > 0 ? _priorityAtLanding.ToString() : "なし")}");
            GUILayout.Label($"  救済完了: {(_lastRescued ? "した" : "していない")}");
            GUILayout.Label($"  優先救済の加点: {(_lastPriorityBonus ? $"+{_lastPriorityGain}" : "なし")}（精度 +{_lastAccuracyBonus}）");
            GUILayout.Label($"  この救済で入った縁: {_lastGain}");

            if (_savedAtSwing > 0 && _priorityAtLanding > 0 && _savedAtSwing != _priorityAtLanding)
                GUILayout.Label("  ※ 飛翔中に二重円が移った1投（保存値で判定されていること）");
        }

        GUILayout.EndArea();
    }
}
