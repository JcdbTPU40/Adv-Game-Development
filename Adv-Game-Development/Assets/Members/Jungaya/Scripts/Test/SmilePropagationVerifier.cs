using System.Collections.Generic;
using UnityEngine;
using Toufuku.Aim;
using Toufuku.Playtest;
using Toufuku.Rescue;

/*
    笑顔の伝播をたしかめるシーンの進行（#56 / 完了条件「PlayMode で密集配置・遠方配置の両方を再現確認できる」）

    見たいのは次の4つ。どれもキー1つで出せるようにする
    ・密集配置（F5）… 相手のまわりに6人を接触の半径の中に置く。1人救うと「4人で打ち止め」になって、
                      伝播の縁が +80 で止まること、同じ相手に2回入らないこと
    ・遠方配置（F6）… 参道ぞいの列のいちばん奥を救う。帰り道で次々にすれちがって、4人まで伸びること
                      （「奥から救うほど得」のもと）
    ・黒客混在（F7）… 相手のとなりの半分を黒客にする。黒客とすれちがっても縁が増えず、4人の枠も減らないこと
    ・作り直し（F8）
    投げずにためしたいときは F9（相手に正しい色を1発当てたことにする。本番と同じ OmamoriHitResolver を通る）

    帰り道の歩きは #62 の CustomerMotion にまかせる（救済3秒・黒客4秒で出口まで歩く）。
    このスクリプトは出口の位置を渡すだけで、歩きそのものは本番と同じものを使う

    客はこのスクリプトが実行中に作る。シーンには地面・カメラ・入力・スコアだけを置く
    投げ方: 1 キーで色（健康）をえらんで、クリックで振る
*/
[DisallowMultipleComponent]
public class SmilePropagationVerifier : MonoBehaviour
{
    public enum Layout
    {
        // 密集配置。相手のまわりに接触の半径の中で6人
        Dense,
        // 遠方配置。参道ぞいに列で並べて、いちばん奥を救う
        Distant,
        // 黒客混在。密集配置のうち何人かを最初から黒客にする
        BlackMixed
    }

    [Header("配置")]
    [Tooltip("客を並べる中心（入れなければ照準の基準点 → 原点の順できめる）。")]
    [SerializeField] Transform arcCenter;
    [Tooltip("基準点から相手までの距離（m）。密集配置で使う。")]
    [SerializeField, Min(2f)] float denseDistance = 12f;
    [Tooltip("密集配置で相手のまわりに置く人数。")]
    [SerializeField, Range(1, 8)] int denseNeighbors = 6;
    [Tooltip("密集配置の相手とまわりのあいだ（m）。接触の半径より内側にすること。")]
    [SerializeField, Min(0.5f)] float denseSpacing = 1.8f;

    [Tooltip("遠方配置で並べる人数（手前から奥へ）。")]
    [SerializeField, Range(2, 8)] int distantCount = 5;
    [Tooltip("遠方配置のいちばん手前までの距離（m）。")]
    [SerializeField, Min(2f)] float distantNear = 10f;
    [Tooltip("遠方配置のあいだ（m）。救済客は列の横を通って帰るので、接触の半径より少し広くても順に伝わる。")]
    [SerializeField, Min(0.5f)] float distantSpacing = 3.2f;
    [Tooltip("遠方配置で列を参道の左右どちらへ寄せるか（m）。0 だと救済客が列の上を通る。")]
    [SerializeField] float distantSideOffset = 1.2f;

    [Header("伝播")]
    [Tooltip("すれちがいとみなす水平の距離（m）。SmileCarrier へ渡す。")]
    [SerializeField, Min(0.1f)] float contactRadius = SmileCarrier.DefaultContactRadius;

    [Header("客の中身")]
    [Tooltip("当たり判定の半径（m）。")]
    [SerializeField, Min(0.2f)] float hitRadius = 0.9f;
    [Tooltip("客の種類ごとの数値表（付録B B-1）。入れなければ予備の値で動く。")]
    [SerializeField] CustomerKindTable kindTable;
    [Tooltip("全員がほしがる色。この色をえらんで当てれば救える。")]
    [SerializeField] OmamoriType correctOmamori = OmamoriType.Kenkou;
    [Tooltip("全員に固定する危険度 D。伝播で5減るのが見えるように、まんなかくらいにしておく。")]
    [SerializeField, Range(0f, 99f)] float fixedDanger = 60f;
    [Tooltip("黒客が出ていくまでの秒数。たしかめている間に消えないよう長くする（本番は4秒）。")]
    [SerializeField, Min(1f)] float blackExitSeconds = 600f;

    [Header("キー")]
    [SerializeField] KeyCode denseKey = KeyCode.F5;
    [SerializeField] KeyCode distantKey = KeyCode.F6;
    [SerializeField] KeyCode blackKey = KeyCode.F7;
    [SerializeField] KeyCode rebuildKey = KeyCode.F8;
    [SerializeField] KeyCode forceRescueKey = KeyCode.F9;

    readonly List<CustomerState> _customers = new List<CustomerState>();
    // 客ごとの「もどす先の D」。伝わったら5下げるので、D−5 が時間でうまって見えなくなることがない
    readonly List<float> _targetDanger = new List<float>();
    readonly List<string> _log = new List<string>();

    Layout _layout = Layout.Dense;
    Material _bodyMaterial;
    Transform _origin;

    // いちばん新しい1救済の記録（HUD 用）
    int _rescuedId;
    bool _hasRescue;
    EnMultiplierSnapshot _snapshot;
    int _propagatedCount;
    int _propagatedEn;
    int _rescueGain;

    void OnEnable()
    {
        OmamoriHitResolver.HitResolved += HandleHitResolved;
        SmileCarrier.Propagated += HandlePropagated;
    }

    void OnDisable()
    {
        OmamoriHitResolver.HitResolved -= HandleHitResolved;
        SmileCarrier.Propagated -= HandlePropagated;
    }

    void Start()
    {
        _origin = ResolveOrigin();
        SmileCarrier.ContactRadiusForNewCarriers = contactRadius;
        Rebuild();
    }

    void Update()
    {
        if (Input.GetKeyDown(denseKey)) SetLayout(Layout.Dense);
        if (Input.GetKeyDown(distantKey)) SetLayout(Layout.Distant);
        if (Input.GetKeyDown(blackKey)) SetLayout(Layout.BlackMixed);
        if (Input.GetKeyDown(rebuildKey)) Rebuild();
        if (Input.GetKeyDown(forceRescueKey)) ForceRescueTarget();
    }

    void LateUpdate()
    {
        // CustomerState.Update が D を進めたあとに、決めた値へもどす
        for (int i = 0; i < _customers.Count; i++)
        {
            CustomerState c = _customers[i];
            if (c == null || !c.IsActive) continue;
            // 黒客にするつもりで D=100 にした客はさわらない（CustomerState の LateUpdate で黒客が決まる）
            if (c.Danger >= CustomerStateMachine.MaxDanger) continue;

            // 伝わった客はもどす先そのものが5下がっているので、D−5 が時間でうまらずに見える
            float target = i < _targetDanger.Count ? _targetDanger[i] : fixedDanger;
            if (c.Danger > target) c.SetDangerForDebug(target);
        }
    }

    void SetLayout(Layout layout)
    {
        _layout = layout;
        Rebuild();
    }

    static string LayoutLabel(Layout layout)
    {
        switch (layout)
        {
            case Layout.Distant: return "遠方配置（参道ぞいの列。いちばん奥を救って帰り道で伝える）";
            case Layout.BlackMixed: return "黒客混在（となりが黒客）";
            default: return "密集配置（相手のまわりに近くで並べる）";
        }
    }

    // ---- 客を作る ----

    // 客を作り直す（F8）
    public void Rebuild()
    {
        for (int i = 0; i < _customers.Count; i++)
        {
            if (_customers[i] != null) Destroy(_customers[i].gameObject);
        }
        _customers.Clear();
        _targetDanger.Clear();
        _log.Clear();
        _hasRescue = false;
        _propagatedCount = 0;
        _propagatedEn = 0;

        Vector3 center = _origin != null ? _origin.position : Vector3.zero;
        center.y = 0f;
        Vector3 forward = ForwardDirection();
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

        switch (_layout)
        {
            case Layout.Distant:
                // 参道ぞいに1列（手前 → 奥）。列を少し横へ寄せて、奥の客が帰るときに列の横を通るようにする
                for (int i = 0; i < distantCount; i++)
                {
                    Vector3 position = center
                                       + forward * (distantNear + i * distantSpacing)
                                       + right * distantSideOffset;
                    BuildCustomer(i, position, black: false);
                }
                break;

            default:
                // 相手（0番）をまんなかに置いて、そのまわりへ均等に並べる
                Vector3 targetPos = center + forward * denseDistance;
                BuildCustomer(0, targetPos, black: false);

                for (int i = 0; i < denseNeighbors; i++)
                {
                    float angle = 360f * i / denseNeighbors;
                    Vector3 offset = Quaternion.AngleAxis(angle, Vector3.up) * right * denseSpacing;
                    // 黒客混在では1人おきに黒客にする（相手のまわりの半分が黒客）
                    bool black = _layout == Layout.BlackMixed && i % 2 == 0;
                    BuildCustomer(i + 1, targetPos + offset, black);
                }
                break;
        }

        SmileCarrier.ContactRadiusForNewCarriers = contactRadius;
        Debug.Log($"[#56確認] {LayoutLabel(_layout)}：客を {_customers.Count} 人作り直しました（接触の半径 {contactRadius:0.0}m）", this);
    }

    CustomerState BuildCustomer(int index, Vector3 position, bool black)
    {
        var go = new GameObject($"SmileCustomer{index}");
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
            if (shader != null) _bodyMaterial = new Material(shader) { name = "SmileCustomerBody" };
        }
        if (_bodyMaterial != null) body.GetComponent<MeshRenderer>().sharedMaterial = _bodyMaterial;

        var zone = go.AddComponent<HitZoneTarget>();
        SetPrivateField(zone, "outerRadius", hitRadius);

        var state = go.AddComponent<CustomerState>();
        // 黒客はたしかめている間ずっと残ってほしいので、出ていくまでを長くしておく（本番は4秒）
        SetPrivateField(state, "blackExitSeconds", blackExitSeconds);
        state.Setup(CustomerKind.Normal, kindTable);

        var rescue = go.AddComponent<CustomerRescue>();
        SetPrivateField(rescue, "correctOmamori", correctOmamori);

        // 帰り道は本番と同じ #62 の CustomerMotion にまかせる。出口は振る人のところ（参道の出口）
        var motion = go.AddComponent<CustomerMotion>();
        motion.SetExitPoints(new[] { ExitPoint() });

        CustomerSpawnId.Assign(go);

        // D=100 にすると、このフレームの LateUpdate（CustomerState 側）で黒客が決まる
        state.SetDangerForDebug(black ? CustomerStateMachine.MaxDanger : fixedDanger);

        _customers.Add(state);
        _targetDanger.Add(black ? CustomerStateMachine.MaxDanger : fixedDanger);
        return state;
    }

    // 帰り道の出口（振る人のところ）
    Vector3 ExitPoint()
    {
        Vector3 exit = _origin != null ? _origin.position : Vector3.zero;
        exit.y = 0f;
        return exit;
    }

    // 相手に正しい色を1発当てたことにする（F9）。本番と同じ OmamoriHitResolver を通る
    void ForceRescueTarget()
    {
        CustomerState target = ResolveTarget();
        if (target == null)
        {
            Debug.Log("[#56確認] 救える客がいません（F8 で作り直してください）", this);
            return;
        }

        OmamoriHitResolver.ApplyHit(target.gameObject, correctOmamori, HitZone.Center);
    }

    /*
        この配置で救わせたい客
        密集・黒客混在はまんなか（0番）、遠方配置は「いちばん奥」（帰り道がいちばん長い客）
    */
    CustomerState ResolveTarget()
    {
        if (_layout == Layout.Distant)
        {
            for (int i = _customers.Count - 1; i >= 0; i--)
            {
                if (_customers[i] != null && _customers[i].IsRescueTarget) return _customers[i];
            }
            return null;
        }

        for (int i = 0; i < _customers.Count; i++)
        {
            if (_customers[i] != null && _customers[i].IsRescueTarget) return _customers[i];
        }
        return null;
    }

    // たしかめるシーン限定: インスペクタ用の private の値を実行中に入れる
    static void SetPrivateField(Object target, string field, object value)
    {
        if (target == null) return;

        System.Reflection.FieldInfo info = target.GetType().GetField(field,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (info == null)
        {
            Debug.LogWarning($"[#56確認] {target.GetType().Name}.{field} が見つかりません（名前が変わった？）");
            return;
        }
        info.SetValue(target, value);
    }

    // ---- 記録 ----

    void HandleHitResolved(OmamoriHitInfo info)
    {
        if (!info.Rescued) return;

        _rescuedId = CustomerSpawnId.Of(info.Customer);
        _hasRescue = true;
        _propagatedCount = 0;
        _propagatedEn = 0;
        _rescueGain = ScoreManager.Instance != null ? ScoreManager.Instance.LastGain : 0;
        _snapshot = info.RescueSnapshot;
        _log.Clear();

        Debug.Log($"[#56確認] 救済 ID {_rescuedId}：縁 +{_rescueGain}／伝播に使う倍率 {_snapshot}", this);
    }

    void HandlePropagated(SmilePropagationInfo info)
    {
        if (info.RescuerId != _rescuedId) return;

        _propagatedCount = info.Order;
        _propagatedEn += info.Gain;
        _log.Add($"{info.Order}人目: ID {info.TargetId} へ  D−{CustomerStateMachine.SmileDangerRelief:0} / 縁 +{info.Gain}");

        // うけとった客の「もどす先の D」も下げる（D−5 が時間でうまって見えなくなるのをふせぐ）
        int index = _customers.IndexOf(info.Target != null ? info.Target.GetComponent<CustomerState>() : null);
        if (index >= 0 && index < _targetDanger.Count)
            _targetDanger[index] = Mathf.Max(0f, _targetDanger[index] - CustomerStateMachine.SmileDangerRelief);
    }

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
        const float width = 470f;
        GUILayout.BeginArea(new Rect(12f, 12f, width, 520f), GUI.skin.box);

        GUILayout.Label($"#56 笑顔の伝播のたしかめ  —  {LayoutLabel(_layout)}");
        GUILayout.Label($"{denseKey}=密集  {distantKey}=遠方  {blackKey}=黒客混在  {rebuildKey}=作り直し  {forceRescueKey}=相手を強制救済");
        GUILayout.Label($"投げ方: 1 キーで色（{correctOmamori}）→ クリックで振る");

        ScoreManager score = ScoreManager.Instance;
        if (score != null)
        {
            GUILayout.Label($"伝播 1回 +{score.PropagationPoints} ／ 上限 {score.PropagationMaxTargets}人" +
                            $"（1救済の伝播は最大 +{score.MaxPropagationEnPerRescue}）  接触の半径 {contactRadius:0.0}m");
        }
        GUILayout.Space(6f);

        for (int i = 0; i < _customers.Count; i++)
        {
            CustomerState c = _customers[i];
            if (c == null) { GUILayout.Label($"  [{i}] 退場ずみ"); continue; }

            int id = CustomerSpawnId.Of(c.gameObject);
            var carrier = c.GetComponent<SmileCarrier>();
            string carried = carrier != null ? $"  笑顔を運搬中 {carrier.PropagatedCount}人 (+{carrier.PropagatedScore})" : "";
            GUILayout.Label($"  [{i}] ID {id}  D={c.Danger:0.0}  R={c.Remaining}  {c.Phase}{carried}");
        }

        GUILayout.Space(6f);
        if (!_hasRescue)
        {
            GUILayout.Label($"まだ救えていません（{forceRescueKey} で相手を強制救済できます）。");
        }
        else
        {
            GUILayout.Label("いちばん新しい1救済:");
            GUILayout.Label($"  救えた客: ID {_rescuedId}（救済の縁 +{_rescueGain}）");
            GUILayout.Label($"  伝播に使う倍率（救済時に保存）: {_snapshot}");
            GUILayout.Label($"  伝わった人数: {_propagatedCount} 人 ／ 伝播で入った縁: +{_propagatedEn}");
            for (int i = 0; i < _log.Count; i++) GUILayout.Label($"    {_log[i]}");

            if (score != null) GUILayout.Label($"  縁の合計: {score.En}");
        }

        GUILayout.EndArea();
    }
}
