using System.Collections.Generic;
using UnityEngine;
using Toufuku.Aim;
using Toufuku.Playtest;
using Toufuku.Rescue;

/// <summary>
/// 笑顔の伝播の確認シーン進行 — Issue #56（完了条件「PlayMode で密集配置・遠方配置の両方を再現確認できる」）
///
/// 見たいのは次の 4 つ。どれもキー 1 つで再現できるようにする。
///   ・密集配置（F5）… 対象のまわりに 6 人を接触半径の内側へ置く。1 人救うと<b>4 人で打ち止め</b>になり、
///                      伝播の縁が +80 で止まること、同じ相手に二度入らないことを見る。
///   ・遠方配置（F6）… 奥の参道沿いに列で並べ、<b>一番奥</b>を救う。救済客が手前へ帰るあいだに
///                      次々とすれ違い、4 人まで伝播が伸びることを見る（「奥から救うほど得」の土台）。
///   ・黒客混在（F7）… 対象の隣を黒客にする。黒客とすれ違っても<b>縁が増えず、4 人枠も減らない</b>ことを見る。
///   ・作り直し（F8）
/// 投げずに確かめたいときは F9（対象へ正色を 1 発当てたことにする。本番と同じ OmamoriHitResolver を通る）。
///
/// 退場歩行（参道を歩いて帰る演出）は別 Issue なので、<b>この確認シーンの中だけ</b>救済客を手前へ歩かせる
/// （<see cref="simulateExitWalk"/>）。本番でも歩行が入れば同じ経路で伝播が起きる。
///
/// 客はこのコンポーネントが実行時に作る。シーンには地面・カメラ・入力・スコアだけを置く。
/// 操作: 1 キーで色（健康）を選び、クリック（＝振り）で投げる。
/// </summary>
[DisallowMultipleComponent]
public class SmilePropagationVerifier : MonoBehaviour
{
    public enum Layout
    {
        /// <summary>密集配置。対象のまわりに接触半径の内側で 6 人。</summary>
        Dense,
        /// <summary>遠方配置。奥の参道沿いに間隔を空けて並べる。</summary>
        Distant,
        /// <summary>黒客混在。密集配置のうち何人かを最初から黒客にする。</summary>
        BlackMixed
    }

    [Header("配置")]
    [Tooltip("客を並べる中心（未設定なら照準の基準点 → 原点の順で決める）。")]
    [SerializeField] Transform arcCenter;
    [Tooltip("基準点から対象までの距離（m）。密集配置で使う。")]
    [SerializeField, Min(2f)] float denseDistance = 12f;
    [Tooltip("密集配置で対象のまわりに置く人数。")]
    [SerializeField, Range(1, 8)] int denseNeighbors = 6;
    [Tooltip("密集配置の対象と周囲の間隔（m）。接触半径より内側にすること。")]
    [SerializeField, Min(0.5f)] float denseSpacing = 1.8f;

    [Tooltip("遠方配置で並べる人数（手前から奥へ）。")]
    [SerializeField, Range(2, 8)] int distantCount = 5;
    [Tooltip("遠方配置の一番手前までの距離（m）。")]
    [SerializeField, Min(2f)] float distantNear = 10f;
    [Tooltip("遠方配置の間隔（m）。救済客はこの列の横を通って帰るので、接触半径より少し広くても順に伝播する。")]
    [SerializeField, Min(0.5f)] float distantSpacing = 3.2f;
    [Tooltip("遠方配置で列を参道の左右どちらへ寄せるか（m）。0 だと救済客が列の上を通る。")]
    [SerializeField] float distantSideOffset = 1.2f;

    [Header("伝播")]
    [Tooltip("交差とみなす水平距離（m）。SmileCarrier へ渡す。")]
    [SerializeField, Min(0.1f)] float contactRadius = SmileCarrier.DefaultContactRadius;
    [Tooltip("ON なら救済客を手前（参道の出口）へ歩かせる。退場歩行は別 Issue なので、この確認シーン限定の仮実装。")]
    [SerializeField] bool simulateExitWalk = true;
    [Tooltip("仮の退場歩行の速さ（m/s）。救済の退場は 3 秒なので、この速さ×3秒ぶんだけ参道を戻る。")]
    [SerializeField, Min(0.5f)] float exitWalkSpeed = 4.5f;

    [Header("客の中身")]
    [Tooltip("当たり判定の半径（m）。")]
    [SerializeField, Min(0.2f)] float hitRadius = 0.9f;
    [Tooltip("客種の数値表（付録B B-1）。未設定ならフォールバック値で動く。")]
    [SerializeField] CustomerKindTable kindTable;
    [Tooltip("全員が求める色。この色を選んで当てれば救済される。")]
    [SerializeField] OmamoriType correctOmamori = OmamoriType.Kenkou;
    [Tooltip("全員に固定する危険度 D。伝播で 5 減るのが見えるように中くらいにしておく。")]
    [SerializeField, Range(0f, 99f)] float fixedDanger = 60f;
    [Tooltip("黒客の退場秒数。確認中に消えないよう長めにする（本番は 4 秒）。")]
    [SerializeField, Min(1f)] float blackExitSeconds = 600f;

    [Header("キー")]
    [SerializeField] KeyCode denseKey = KeyCode.F5;
    [SerializeField] KeyCode distantKey = KeyCode.F6;
    [SerializeField] KeyCode blackKey = KeyCode.F7;
    [SerializeField] KeyCode rebuildKey = KeyCode.F8;
    [SerializeField] KeyCode forceRescueKey = KeyCode.F9;

    readonly List<CustomerState> _customers = new List<CustomerState>();
    // 客ごとの「戻す先の D」。伝播を受けたら 5 下げるので、D−5 が時間で埋まって見えなくなることがない。
    readonly List<float> _targetDanger = new List<float>();
    // 仮の退場歩行をさせている救済客（笑顔を運んでいる間だけ）。
    readonly List<SmileCarrier> _walkers = new List<SmileCarrier>();
    readonly List<string> _log = new List<string>();

    Layout _layout = Layout.Dense;
    Material _bodyMaterial;
    Transform _origin;

    // 直近の 1 救済の記録（HUD 用）
    int _rescuedId;
    bool _hasRescue;
    SmileMultiplierSnapshot _snapshot;
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

        WalkRescuedCustomersHome();
    }

    /// <summary>
    /// 救済客を参道の出口（＝振る人の側）へ歩かせる。退場歩行の本実装が入るまでの仮置き。
    /// SmileCarrier は毎フレーム交差を見るので、歩かせるだけで「すれ違った人へ伝播する」動きになる。
    /// </summary>
    void WalkRescuedCustomersHome()
    {
        if (!simulateExitWalk || _walkers.Count == 0) return;

        Vector3 home = _origin != null ? _origin.position : Vector3.zero;
        float step = exitWalkSpeed * Time.deltaTime;

        for (int i = _walkers.Count - 1; i >= 0; i--)
        {
            SmileCarrier carrier = _walkers[i];
            if (carrier == null) { _walkers.RemoveAt(i); continue; }

            Transform t = carrier.transform;
            Vector3 to = home - t.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.04f) { _walkers.RemoveAt(i); continue; }

            t.position += to.normalized * step;
        }
    }

    void LateUpdate()
    {
        // CustomerState.Update が D を進めたあとに固定値へ戻す。
        // 伝播を受けた客だけは D が 5 下がったまま見えるよう、下がっている側は戻さない。
        for (int i = 0; i < _customers.Count; i++)
        {
            CustomerState c = _customers[i];
            if (c == null || !c.IsActive) continue;
            // 黒客にするつもりで D=100 にした客は触らない（CustomerState の LateUpdate で黒客化が確定する）。
            if (c.Danger >= CustomerStateMachine.MaxDanger) continue;

            // 伝播を受けた客は目標値そのものが 5 下がっているので、D−5 が時間で埋まらずに見える。
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
            case Layout.Distant: return "遠方配置（参道沿いの列。一番奥を救って帰路で伝播）";
            case Layout.BlackMixed: return "黒客混在（隣が黒客）";
            default: return "密集配置（対象のまわりに近接で並べる）";
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
        _targetDanger.Clear();
        _walkers.Clear();
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
                // 参道沿いに 1 列（手前 → 奥）。列を少し横へ寄せて、奥の客が帰るときに列の横を通るようにする。
                for (int i = 0; i < distantCount; i++)
                {
                    Vector3 position = center
                                       + forward * (distantNear + i * distantSpacing)
                                       + right * distantSideOffset;
                    BuildCustomer(i, position, black: false);
                }
                break;

            default:
                // 対象（0 番）を真ん中に置き、そのまわりへ均等に並べる。
                Vector3 targetPos = center + forward * denseDistance;
                BuildCustomer(0, targetPos, black: false);

                for (int i = 0; i < denseNeighbors; i++)
                {
                    float angle = 360f * i / denseNeighbors;
                    Vector3 offset = Quaternion.AngleAxis(angle, Vector3.up) * right * denseSpacing;
                    // 黒客混在では 1 人おきに黒客にする（対象のまわりの半数が黒客）。
                    bool black = _layout == Layout.BlackMixed && i % 2 == 0;
                    BuildCustomer(i + 1, targetPos + offset, black);
                }
                break;
        }

        SmileCarrier.ContactRadiusForNewCarriers = contactRadius;
        Debug.Log($"[#56確認] {LayoutLabel(_layout)}：客を {_customers.Count} 人作り直しました（接触半径 {contactRadius:0.0}m）", this);
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
        // 黒客は確認のあいだ残ってほしいので、退場までを長くしておく（本番は 4 秒）。
        SetPrivateField(state, "blackExitSeconds", blackExitSeconds);
        state.Setup(CustomerKind.Normal, kindTable);

        var rescue = go.AddComponent<CustomerRescue>();
        SetPrivateField(rescue, "correctOmamori", correctOmamori);

        CustomerSpawnId.Assign(go);

        // D=100 にすると、このフレームの LateUpdate（CustomerState 側）で黒客が確定する。
        state.SetDangerForDebug(black ? CustomerStateMachine.MaxDanger : fixedDanger);

        _customers.Add(state);
        _targetDanger.Add(black ? CustomerStateMachine.MaxDanger : fixedDanger);
        return state;
    }

    /// <summary>対象へ正色を 1 発当てたことにする（F9）。本番と同じ OmamoriHitResolver を通る。</summary>
    void ForceRescueTarget()
    {
        CustomerState target = ResolveTarget();
        if (target == null)
        {
            Debug.Log("[#56確認] 救済できる客がいません（F8 で作り直してください）", this);
            return;
        }

        OmamoriHitResolver.ApplyHit(target.gameObject, correctOmamori, HitZone.Center);
    }

    /// <summary>
    /// この配置で救済させたい客。密集・黒客混在は真ん中（0 番）、
    /// 遠方配置は<b>一番奥</b>（帰路が一番長い客）を選ぶ。
    /// </summary>
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

    /// <summary>検証シーン限定：インスペクタ用の private 値を実行時に差し込む。</summary>
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
        _log.Clear();

        var carrier = info.Customer != null ? info.Customer.GetComponent<SmileCarrier>() : null;
        _snapshot = carrier != null ? carrier.Snapshot : SmileMultiplierSnapshot.Purification;

        // 退場歩行の仮実装：笑顔を持ったまま参道を手前へ帰らせる（本実装が入るまでの確認用）。
        if (carrier != null && simulateExitWalk) _walkers.Add(carrier);

        Debug.Log($"[#56確認] 救済 ID {_rescuedId}：縁 +{_rescueGain}／伝播用スナップショット {_snapshot}", this);
    }

    void HandlePropagated(SmilePropagationInfo info)
    {
        if (info.RescuerId != _rescuedId) return;

        _propagatedCount = info.Order;
        _propagatedEn += info.Gain;
        _log.Add($"{info.Order}人目: ID {info.TargetId} へ  D−{CustomerStateMachine.SmileDangerRelief:0} / 縁 +{info.Gain}");

        // 受け取った客の「戻す先の D」も下げる（D−5 が時間経過で埋まって見えなくなるのを防ぐ）。
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

        GUILayout.Label($"#56 笑顔の伝播の確認  —  {LayoutLabel(_layout)}");
        GUILayout.Label($"{denseKey}=密集  {distantKey}=遠方  {blackKey}=黒客混在  {rebuildKey}=作り直し  {forceRescueKey}=対象を強制救済");
        GUILayout.Label($"投げ方: 1 キーで色（{correctOmamori}）→ クリックで振る");

        ScoreManager score = ScoreManager.Instance;
        if (score != null)
        {
            GUILayout.Label($"伝播 1回 +{score.SmilePropagationBonus} ／ 上限 {score.SmilePropagationMaxTargets}人" +
                            $"（1救済の伝播は最大 +{score.MaxSmilePropagationBonusPerRescue}）  接触半径 {contactRadius:0.0}m");
        }
        GUILayout.Space(6f);

        for (int i = 0; i < _customers.Count; i++)
        {
            CustomerState c = _customers[i];
            if (c == null) { GUILayout.Label($"  [{i}] 退場済み"); continue; }

            int id = CustomerSpawnId.Of(c.gameObject);
            var carrier = c.GetComponent<SmileCarrier>();
            string carried = carrier != null ? $"  笑顔を運搬中 {carrier.PropagatedCount}人 (+{carrier.PropagatedScore})" : "";
            GUILayout.Label($"  [{i}] ID {id}  D={c.Danger:0.0}  R={c.Remaining}  {c.Phase}{carried}");
        }

        GUILayout.Space(6f);
        if (!_hasRescue)
        {
            GUILayout.Label($"まだ救済していません（{forceRescueKey} で対象を強制救済できます）。");
        }
        else
        {
            GUILayout.Label("直近の1救済:");
            GUILayout.Label($"  救済した客: ID {_rescuedId}（救済の縁 +{_rescueGain}）");
            GUILayout.Label($"  伝播用の倍率スナップショット: {_snapshot}");
            GUILayout.Label($"  伝播した人数: {_propagatedCount} 人 ／ 伝播で入った縁: +{_propagatedEn}");
            for (int i = 0; i < _log.Count; i++) GUILayout.Label($"    {_log[i]}");

            if (score != null)
                GUILayout.Label($"  このプレイの伝播: {score.PropagationCount}回 / +{score.PropagationEn}（縁 {score.En}）");
        }

        GUILayout.EndArea();
    }
}
