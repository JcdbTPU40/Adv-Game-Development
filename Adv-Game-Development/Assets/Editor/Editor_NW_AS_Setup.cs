#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// NW_AS_Setup（エディタ専用ツール）
/// メニュー「NW → アニメーター自動作成」から開く。
/// クリップを入れてボタンを押すと、移動・攻撃スクリプトが送るパラメータ（Speed・IsGrounded・Jump など）に合わせた
/// Animator Controller（待機・歩き・走り・ジャンプ・落下・着地・瞬間ダッシュ・段階攻撃）を作る。
/// 「適用先」を入れておくと、コントローラーの割り当て・Root Motion オフ・再生速度スライダー（NW_AS）の追加・
/// 自分と親にあるスクリプトの「animator」欄への登録までまとめて行う。
/// ※ Assets/Editor フォルダに入れるのがおすすめ（ゲームのビルドには含まれない）。
/// </summary>
public class NW_AS_Setup : EditorWindow
{
    [MenuItem("NW/アニメーター自動作成")]
    static void Open() => GetWindow<NW_AS_Setup>("NW_AS Setup");

    // ---------- クリップ ----------
    public AnimationClip idle, walk, run;
    public float walkSpeedThreshold = 4f;   // 移動スクリプトの歩きの速さと同じ値
    public float runSpeedThreshold = 7f;    // 移動スクリプトの走りの速さと同じ値
    public AnimationClip dash;
    public AnimationClip jumpUp, fall, land;
    public AnimationClip airJump;           // 空なら跳び上がりのクリップを使う
    public AnimationClip[] attacks = new AnimationClip[4];

    // ---------- 遷移のタイミング ----------
    [Range(0f, 1f)] public float attackExitTime = 0.85f;
    [Range(0f, 1f)] public float dashExitTime = 0.8f;
    [Range(0f, 1f)] public float landExitTime = 0.6f;
    public float blendTime = 0.1f;

    // ---------- パラメータ名（移動・攻撃スクリプトが送る名前に合わせる） ----------
    public string speedParam = "Speed";
    public string verticalSpeedParam = "VerticalSpeed";
    public string groundedParam = "IsGrounded";
    public string jumpTrigger = "Jump";
    public string dashTrigger = "Dash";
    public string attackTriggerPrefix = "Attack"; // Attack1, Attack2 ...

    // ---------- 出力 ----------
    public string savePath = "Assets/NW_AS_Player.controller";
    public Animator targetAnimator;

    SerializedObject so;
    Vector2 scroll;
    bool showParams;

    void OnEnable() => so = new SerializedObject(this);

    void OnGUI()
    {
        so.Update();
        scroll = EditorGUILayout.BeginScrollView(scroll);

        Section("地上移動（Idle は必須）");
        P("idle", "待機 Idle");
        P("walk", "歩き Walk");
        P("run", "走り Run（ダッシュ中）");
        P("walkSpeedThreshold", "歩きになる速度");
        P("runSpeedThreshold", "走りになる速度");

        Section("瞬間ダッシュ（Dash trigger を送った瞬間）");
        P("dash", "瞬間ダッシュ");
        P("dashExitTime", "抜けるタイミング");

        Section("ジャンプ・空中");
        P("jumpUp", "跳び上がり");
        P("fall", "落下（ループ）");
        P("land", "着地");
        P("landExitTime", "着地を抜けるタイミング");
        P("airJump", "空中ジャンプ（空なら跳び上がり）");

        Section("段階攻撃（要素の順に Attack1, Attack2 …）");
        P("attacks", "攻撃クリップ");
        P("attackExitTime", "攻撃を抜けるタイミング");

        Section("共通");
        P("blendTime", "切り替えのブレンド時間 (秒)");

        showParams = EditorGUILayout.Foldout(showParams, "パラメータ名（自分のスクリプトが送る名前に合わせる）");
        if (showParams)
        {
            P("speedParam", "水平速度 float");
            P("verticalSpeedParam", "上下速度 float");
            P("groundedParam", "接地 bool");
            P("jumpTrigger", "ジャンプ trigger");
            P("dashTrigger", "瞬間ダッシュ trigger");
            P("attackTriggerPrefix", "攻撃 trigger の頭");
        }

        Section("出力");
        P("savePath", "保存先");
        EditorGUILayout.BeginHorizontal();
        P("targetAnimator", "適用先 Animator");
        if (GUILayout.Button("選択中から", GUILayout.Width(80)))
        {
            GameObject go = Selection.activeGameObject;
            if (go != null) targetAnimator = go.GetComponentInChildren<Animator>(true);
            so.Update();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndScrollView();
        so.ApplyModifiedProperties();

        EditorGUILayout.Space();
        if (GUILayout.Button("Animator Controller を作成", GUILayout.Height(32))) Build();
    }

    void Section(string title)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }

    void P(string name, string label) =>
        EditorGUILayout.PropertyField(so.FindProperty(name), new GUIContent(label), true);

    // ======================================================================

    void Build()
    {
        if (idle == null)
        {
            EditorUtility.DisplayDialog("NW_AS", "待機（Idle）のクリップは必須です。", "OK");
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(savePath) != null &&
            !EditorUtility.DisplayDialog("NW_AS", savePath + " は既にあります。作り直しますか？\n（手で加えた変更は消えます）", "作り直す", "やめる"))
            return;

        AssetDatabase.DeleteAsset(savePath);
        AnimatorController ctrl = AnimatorController.CreateAnimatorControllerAtPath(savePath);

        // ---------- パラメータ ----------
        ctrl.AddParameter(speedParam, AnimatorControllerParameterType.Float);
        ctrl.AddParameter(verticalSpeedParam, AnimatorControllerParameterType.Float);
        ctrl.AddParameter(groundedParam, AnimatorControllerParameterType.Bool);
        ctrl.AddParameter(jumpTrigger, AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter(dashTrigger, AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter(NW_AS.AirJumpTrigger, AnimatorControllerParameterType.Trigger);
        // 攻撃トリガーはクリップが空の段も作っておく（スクリプトから送られても警告を出さないため）
        for (int i = 0; i < attacks.Length; i++)
            ctrl.AddParameter(attackTriggerPrefix + (i + 1), AnimatorControllerParameterType.Trigger);

        // 再生速度の倍率（NW_AS のスライダーから送られる）
        ctrl.AddParameter(NW_AS.LocomotionParam, AnimatorControllerParameterType.Float);
        ctrl.AddParameter(NW_AS.DashParam, AnimatorControllerParameterType.Float);
        ctrl.AddParameter(NW_AS.JumpUpParam, AnimatorControllerParameterType.Float);
        ctrl.AddParameter(NW_AS.FallParam, AnimatorControllerParameterType.Float);
        ctrl.AddParameter(NW_AS.LandParam, AnimatorControllerParameterType.Float);
        ctrl.AddParameter(NW_AS.AirJumpSpeedParam, AnimatorControllerParameterType.Float);
        for (int i = 0; i < attacks.Length; i++)
            ctrl.AddParameter(NW_AS.AttackParamPrefix + (i + 1), AnimatorControllerParameterType.Float);

        // 接地状態を true から、速度倍率を 1 から始める
        var ps = ctrl.parameters;
        foreach (var p in ps)
        {
            if (p.name == groundedParam) p.defaultBool = true;
            if (p.name.StartsWith("Spd")) p.defaultFloat = 1f;
        }
        ctrl.parameters = ps;

        AnimatorStateMachine sm = ctrl.layers[0].stateMachine;
        sm.anyStatePosition = new Vector3(20, -120, 0);
        sm.entryPosition = new Vector3(20, 0, 0);
        sm.exitPosition = new Vector3(20, 360, 0);

        // ---------- 地上移動（Speed で Idle/Walk/Run をブレンド） ----------
        AnimatorState loco = ctrl.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = speedParam;
        tree.useAutomaticThresholds = false;
        tree.AddChild(idle, 0f);
        if (walk != null) tree.AddChild(walk, walkSpeedThreshold);
        if (run != null) tree.AddChild(run, runSpeedThreshold);
        sm.defaultState = loco;
        SetPosition(sm, loco, new Vector3(300, 0, 0));
        UseSpeedParam(loco, NW_AS.LocomotionParam);

        // ---------- 空中 ----------
        AnimatorState jumpS = jumpUp != null ? AddState(sm, "JumpUp", jumpUp, new Vector3(560, -120, 0)) : null;
        AnimatorState fallS = fall != null ? AddState(sm, "Fall", fall, new Vector3(820, 0, 0)) : null;
        AnimatorState landS = land != null ? AddState(sm, "Land", land, new Vector3(560, 120, 0)) : null;
        UseSpeedParam(jumpS, NW_AS.JumpUpParam);
        UseSpeedParam(fallS, NW_AS.FallParam);
        UseSpeedParam(landS, NW_AS.LandParam);
        AnimatorState airEntry = fallS != null ? fallS : jumpS; // 足場から落ちたときに入る状態

        if (airEntry != null)
        {
            // 地面を離れたら空中へ
            Trans(loco, airEntry).AddCondition(AnimatorConditionMode.IfNot, 0, groundedParam);

            // 跳び上がり → 下降に転じたら落下へ
            if (jumpS != null && fallS != null)
                Trans(jumpS, fallS).AddCondition(AnimatorConditionMode.Less, 0, verticalSpeedParam);

            // 空中 → 着地
            AnimatorState landTarget = landS != null ? landS : loco;
            if (jumpS != null) Trans(jumpS, landTarget).AddCondition(AnimatorConditionMode.If, 0, groundedParam);
            if (fallS != null) Trans(fallS, landTarget).AddCondition(AnimatorConditionMode.If, 0, groundedParam);

            if (landS != null)
            {
                // 着地中にすぐ動き出したら地上移動へ。動かなければモーションの終わりで戻る
                Trans(landS, airEntry).AddCondition(AnimatorConditionMode.IfNot, 0, groundedParam);
                Trans(landS, loco).AddCondition(AnimatorConditionMode.Greater, 0.5f, speedParam);
                ExitTrans(landS, loco, landExitTime);
            }
        }

        // ---------- AnyState からの割り込み（上ほど優先） ----------
        if (dash != null)
        {
            AnimatorState dashS = AddState(sm, "Dash", dash, new Vector3(300, -240, 0));
            UseSpeedParam(dashS, NW_AS.DashParam);
            AnyTrans(sm, dashS, dashTrigger, 0.05f, true); // 続けて出したら頭から再生
            if (airEntry != null)
            {
                var toAir = ExitTrans(dashS, airEntry, dashExitTime);
                toAir.AddCondition(AnimatorConditionMode.IfNot, 0, groundedParam);
            }
            ExitTrans(dashS, loco, dashExitTime);
        }

        for (int i = 0; i < attacks.Length; i++)
        {
            if (attacks[i] == null) continue;
            string trig = attackTriggerPrefix + (i + 1);
            AnimatorState atk = AddState(sm, trig, attacks[i], new Vector3(40 + 240 * i, 240, 0));
            UseSpeedParam(atk, NW_AS.AttackParamPrefix + (i + 1));
            AnyTrans(sm, atk, trig, 0.05f, false);
            ExitTrans(atk, loco, attackExitTime, 0.15f);
        }

        if (jumpS != null) AnyTrans(sm, jumpS, jumpTrigger, 0.05f, false);

        // ---------- 空中ジャンプ（落下中・跳び上がり中でも頭から再生） ----------
        Motion airClip = airJump != null ? airJump : jumpUp;
        if (airClip != null)
        {
            AnimatorState airJ = AddState(sm, "AirJump", airClip, new Vector3(820, -120, 0));
            UseSpeedParam(airJ, NW_AS.AirJumpSpeedParam);
            AnyTrans(sm, airJ, NW_AS.AirJumpTrigger, 0.05f, true); // 続けて跳んでも頭から
            if (fallS != null) Trans(airJ, fallS).AddCondition(AnimatorConditionMode.Less, 0, verticalSpeedParam);
            Trans(airJ, landS != null ? landS : loco).AddCondition(AnimatorConditionMode.If, 0, groundedParam);
        }

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();

        // ---------- 適用 ----------
        string applied = "";
        if (targetAnimator != null) applied = ApplyTo(targetAnimator, ctrl);

        Selection.activeObject = ctrl;
        EditorGUIUtility.PingObject(ctrl);
        Debug.Log($"[NW_AS] Animator Controller を作成しました: {savePath}{applied}");
    }

    string ApplyTo(Animator anim, AnimatorController ctrl)
    {
        Undo.RecordObject(anim, "NW_AS Setup");
        anim.runtimeAnimatorController = ctrl;
        anim.applyRootMotion = false; // 移動はスクリプトが行うのでモーション側では動かさない
        EditorUtility.SetDirty(anim);

        // 自分と親にあるスクリプトの「animator」欄に自動で登録
        int linked = 0;
        foreach (MonoBehaviour mb in anim.GetComponentsInParent<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            var s = new SerializedObject(mb);
            SerializedProperty p = s.FindProperty("animator");
            if (p == null || p.propertyType != SerializedPropertyType.ObjectReference) continue;
            p.objectReferenceValue = anim;
            s.ApplyModifiedProperties();
            linked++;
        }

        // 再生速度スライダーを付ける（既にあればスライダーの値はそのまま、切り替え速度だけ更新）
        NW_AS spd = anim.GetComponent<NW_AS>();
        if (spd == null) spd = Undo.AddComponent<NW_AS>(anim.gameObject);
        Undo.RecordObject(spd, "NW_AS Setup");
        spd.walkSpeedThreshold = walkSpeedThreshold;
        spd.runSpeedThreshold = runSpeedThreshold;
        spd.speedParam = speedParam;
        spd.jumpTrigger = jumpTrigger;
        spd.groundedParam = groundedParam;
        if (spd.attacks == null || spd.attacks.Length < attacks.Length)
        {
            var arr = new float[attacks.Length];
            for (int i = 0; i < arr.Length; i++)
                arr[i] = spd.attacks != null && i < spd.attacks.Length ? spd.attacks[i] : 1f;
            spd.attacks = arr;
        }
        EditorUtility.SetDirty(spd);

        if (anim.avatar == null)
            Debug.LogWarning("[NW_AS] Animator に Avatar が入っていません。モデルの FBX から作られた Avatar を入れてください。", anim);

        return $"\n{anim.name} に割り当て、{linked} 個のスクリプトの Animator 欄に登録しました。";
    }

    // ---------- 小物 ----------

    static AnimatorState AddState(AnimatorStateMachine sm, string name, Motion clip, Vector3 pos)
    {
        AnimatorState s = sm.AddState(name, pos);
        s.motion = clip;
        return s;
    }

    static void UseSpeedParam(AnimatorState state, string param)
    {
        if (state == null) return;
        state.speedParameterActive = true;
        state.speedParameter = param;
    }

    static void SetPosition(AnimatorStateMachine sm, AnimatorState state, Vector3 pos)
    {
        var states = sm.states;
        for (int i = 0; i < states.Length; i++)
            if (states[i].state == state) states[i].position = pos;
        sm.states = states;
    }

    AnimatorStateTransition Trans(AnimatorState from, AnimatorState to)
    {
        AnimatorStateTransition t = from.AddTransition(to);
        t.hasExitTime = false;
        t.hasFixedDuration = true;
        t.duration = blendTime;
        return t;
    }

    AnimatorStateTransition ExitTrans(AnimatorState from, AnimatorState to, float exitTime, float duration = -1f)
    {
        AnimatorStateTransition t = from.AddTransition(to);
        t.hasExitTime = true;
        t.exitTime = exitTime;
        t.hasFixedDuration = true;
        t.duration = duration >= 0f ? duration : blendTime;
        return t;
    }

    static void AnyTrans(AnimatorStateMachine sm, AnimatorState to, string trigger, float duration, bool toSelf)
    {
        AnimatorStateTransition t = sm.AddAnyStateTransition(to);
        t.AddCondition(AnimatorConditionMode.If, 0, trigger);
        t.hasExitTime = false;
        t.hasFixedDuration = true;
        t.duration = duration;
        t.canTransitionToSelf = toSelf;
    }
}
#endif
