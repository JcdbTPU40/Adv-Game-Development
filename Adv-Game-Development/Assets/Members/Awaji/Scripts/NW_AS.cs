using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// NW_AS
/// モーションごとの再生速度を、インスペクターのスライダーで調整する。
/// 再生中に動かしても即反映される。
/// Animator と同じオブジェクト（モデル側）に付ける。
/// 使う Animator Controller は「NW → アニメーター自動作成」で作ったもの。
/// また、移動スクリプトが送る移動速度（Speed）をなめらかにして、待機・歩き・走りの切り替えを自然にする。
/// （移動スクリプトより後に動くよう実行順を 100 にしてある）
/// 空中で Jump トリガーが送られたら、Jump の代わりに AirJump トリガーを送り、跳び上がりを頭から再生する。
/// （接地は Animator の IsGrounded パラメータで判断するので、どの移動スクリプトでも使える）
/// </summary>
[RequireComponent(typeof(Animator))]
[DefaultExecutionOrder(100)]
public class NW_AS : MonoBehaviour
{
    // Animator のパラメータ名（自動作成ツールと共通）
    public const string LocomotionParam = "SpdLocomotion";
    public const string DashParam = "SpdDash";
    public const string JumpUpParam = "SpdJumpUp";
    public const string FallParam = "SpdFall";
    public const string LandParam = "SpdLand";
    public const string AirJumpSpeedParam = "SpdAirJump";
    public const string AirJumpTrigger = "AirJump";
    public const string AttackParamPrefix = "SpdAttack"; // SpdAttack1, SpdAttack2 ...

    [Header("全体（すべてのモーションに掛かる倍率）")]
    [Range(0.1f, 3f)] public float master = 1f;

    [Header("切り替えのなめらかさ（待機⇔歩き⇔走り）")]
    [Tooltip("速くなるときに、モーションが切り替わりきるまでの時間 (秒)。0 で即切り替え")]
    [Range(0f, 1f)] public float accelSmoothTime = 0.15f;
    [Tooltip("遅くなるときに、モーションが切り替わりきるまでの時間 (秒)。走り→歩き・歩き→待機をゆっくりにしたいときは大きく")]
    [Range(0f, 1f)] public float decelSmoothTime = 0.25f;

    [Header("地上移動")]
    [Range(0.1f, 3f)] public float idle = 1f;
    [Range(0.1f, 3f)] public float walk = 1f;
    [Range(0.1f, 3f)] public float run = 1f;

    [Tooltip("待機と歩きの切り替わる速度。移動スクリプトの歩きの速さと同じ値（ツールで作成時に自動で入る）")]
    [Min(0.01f)] public float walkSpeedThreshold = 4f;
    [Tooltip("歩きと走りの切り替わる速度。移動スクリプトの走りの速さと同じ値（ツールで作成時に自動で入る）")]
    [Min(0.01f)] public float runSpeedThreshold = 7f;

    [Header("瞬間ダッシュ")]
    [Range(0.1f, 3f)] public float dash = 1f;

    [Header("ジャンプ・空中")]
    [Range(0.1f, 3f)] public float jumpUp = 1f;
    [Range(0.1f, 3f)] public float fall = 1f;
    [Range(0.1f, 3f)] public float land = 1f;
    [Range(0.1f, 3f)] public float airJump = 1f;

    [Header("空中ジャンプの判定（移動スクリプトが送るパラメータ名）")]
    [Tooltip("ジャンプの trigger 名")]
    public string jumpTrigger = "Jump";
    [Tooltip("接地の bool 名。これが false のときに Jump が来たら空中ジャンプにする")]
    public string groundedParam = "IsGrounded";
    [Tooltip("地面を離れてからこの秒数までは、まだ地上のジャンプとして扱う（足場の端で跳んだとき用）")]
    [Min(0f)] public float coyoteTime = 0.15f;

    [Header("段階攻撃（要素0 = 1段目）")]
    [Range(0.1f, 3f)] public float[] attacks = { 1f, 1f, 1f, 1f };

    [Tooltip("水平速度のパラメータ名（移動スクリプト側と同じ）")]
    public string speedParam = "Speed";

    Animator animator;
    readonly HashSet<string> existing = new HashSet<string>();
    float smoothedSpeed;
    float speedVelocity;
    float lastGroundedTime = -999f;

    void Awake()
    {
        animator = GetComponent<Animator>();
        CacheParameters();
    }

    /// <summary>空中ジャンプのモーションを頭から再生する（自分のスクリプトから直接呼んでもよい）</summary>
    public void AirJump()
    {
        if (animator == null || !existing.Contains(AirJumpTrigger)) return;
        if (existing.Contains(jumpTrigger)) animator.ResetTrigger(jumpTrigger);
        animator.SetTrigger(AirJumpTrigger);
    }

    // このフレームに Jump が送られていて、空中にいたら AirJump に差し替える
    void CheckAirJump()
    {
        if (!existing.Contains(groundedParam) || !existing.Contains(jumpTrigger)) return;
        if (animator.GetBool(groundedParam)) { lastGroundedTime = Time.time; return; }
        if (!animator.GetBool(jumpTrigger)) return;
        if (Time.time - lastGroundedTime <= Mathf.Max(coyoteTime, Time.deltaTime * 2f)) return;
        AirJump();
    }

    // 古い Controller（速度パラメータが無いもの）でも警告を出さないよう、存在するものだけ使う
    void CacheParameters()
    {
        existing.Clear();
        if (animator == null || animator.runtimeAnimatorController == null) return;
        foreach (AnimatorControllerParameter p in animator.parameters) existing.Add(p.name);
    }

    void Update()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        if (existing.Count == 0) CacheParameters();
        CheckAirJump();

        // 移動スクリプトがこのフレームに送った速度を受け取り、なめらかにして上書きする
        float s = 0f;
        if (existing.Contains(speedParam))
        {
            float raw = animator.GetFloat(speedParam);
            float smooth = raw > smoothedSpeed ? accelSmoothTime : decelSmoothTime;
            smoothedSpeed = smooth <= 0f
                ? raw
                : Mathf.SmoothDamp(smoothedSpeed, raw, ref speedVelocity, smooth);
            if (Mathf.Abs(smoothedSpeed - raw) < 0.001f) smoothedSpeed = raw;
            animator.SetFloat(speedParam, smoothedSpeed);
            s = smoothedSpeed;
        }

        // 待機・歩き・走りは1つのブレンドツリーなので、今の移動速度に応じて倍率を混ぜる
        float loco;
        if (s <= walkSpeedThreshold)
        {
            loco = Mathf.Lerp(idle, walk, s / walkSpeedThreshold);
        }
        else
        {
            float span = Mathf.Max(0.01f, runSpeedThreshold - walkSpeedThreshold);
            loco = Mathf.Lerp(walk, run, (s - walkSpeedThreshold) / span);
        }

        Set(LocomotionParam, loco);
        Set(DashParam, dash);
        Set(JumpUpParam, jumpUp);
        Set(FallParam, fall);
        Set(LandParam, land);
        Set(AirJumpSpeedParam, airJump);
        for (int i = 0; i < attacks.Length; i++) Set(AttackParamPrefix + (i + 1), attacks[i]);
    }

    void Set(string param, float value)
    {
        if (existing.Contains(param)) animator.SetFloat(param, value * master);
    }
}
