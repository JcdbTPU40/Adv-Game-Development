using System;
using System.Collections.Generic;
using UnityEngine;
using Toufuku.Rescue;

namespace Toufuku.Aim
{
    // 着弾の結果（確認とHUD用）
    public readonly struct LandingResult
    {
        // SwingAccepted のときに決めた着弾目標点
        public readonly Vector3 TargetPoint;
        // 実際に落ちた場所
        public readonly Vector3 LandedPoint;
        // 当たった客。外れたら null
        public readonly HitZoneTarget Hit;
        // 中心からの距離が判定半径の何割か。外れたら +∞
        public readonly float NormalizedDistance;
        // スコアに渡したゾーン（相性✗と外れは Miss）
        public readonly HitZone Zone;
        public readonly OmamoriType Type;
        // 発射したときに決めた飛ぶ時間（秒）
        public readonly float PlannedSeconds;
        // 実際にかかった時間（秒）。フレームごとに進むので、最大1フレームぶん長くなる
        public readonly float ElapsedSeconds;
        // スコアに入れたかどうか（ゲームの時間外に落ちた弾は入れない）
        public readonly bool Scored;

        public LandingResult(Vector3 targetPoint, Vector3 landedPoint, HitZoneTarget hit, float normalizedDistance,
            HitZone zone, OmamoriType type, float plannedSeconds, float elapsedSeconds, bool scored)
        {
            TargetPoint = targetPoint;
            LandedPoint = landedPoint;
            Hit = hit;
            NormalizedDistance = normalizedDistance;
            Zone = zone;
            Type = type;
            PlannedSeconds = plannedSeconds;
            ElapsedSeconds = elapsedSeconds;
            Scored = scored;
        }
    }

    /*
        飛んでいるお守り1発ぶんのクラス（#60 / 企画書 v8 4章・5章）

        ・スタート地点から着弾目標点までを ThrowFlight のカーブでつないで、飛ぶ時間ぴったりで目標点に着く。物理は使っていない
        ・着いた瞬間に LandingJudge で相手を決めて、救済の判定と命中精度をスコアに渡す
          当たり判定が消えている客（救済の演出中）は候補に入らないので、弾は通りぬけてうしろの客で判定される
        ・OnusaThrower が実行中に AddComponent して Launch を呼ぶ
    */
    public class OmamoriProjectile : MonoBehaviour
    {
        // どの弾でも、落ちたら呼ばれる（確認用HUDとログ用）
        public static event Action<LandingResult> AnyLanded;
        // どの弾でも、飛び始めたら呼ばれる（#63 の計測ログが発射と着弾をつなげるのに使う）
        public static event Action<OmamoriProjectile> AnyLaunched;
        // この弾が落ちたときに呼ばれる
        public event Action<LandingResult> Landed;

        static readonly List<LandingCandidate> s_candidates = new List<LandingCandidate>();
        static readonly List<HitZoneTarget> s_targets = new List<HitZoneTarget>();

        // #49: 軌跡を遅らせて出している間に、こっちで消した見た目のリスト（最初から消えていたものはさわらない）
        readonly List<Renderer> _hiddenRenderers = new List<Renderer>();

        Vector3 _start;
        Vector3 _target;
        float _seconds;
        float _arcHeight;
        float _elapsed;
        float _lingerSeconds;
        float _visualDelaySeconds;
        double _impactRealtime;
        OmamoriType _type;
        bool _flying;
        int _priorityTargetId;

        public Vector3 TargetPoint => _target;
        public float FlightSeconds => _seconds;
        public OmamoriType Type => _type;
        public bool IsFlying => _flying;

        /*
            発射（SwingAccepted）した瞬間の優先対象（二重円の客）の生成ID（#55）
            飛んでいる間に二重円が別の客に移っても、ここは変えない
            落ちたときにこのIDと救済した客のIDが同じだったときだけ +50 が入る（企画書 v8 4章「通常弾」、7章 得点表）
        */
        public int PriorityTargetId => _priorityTargetId;

        /*
            弾を飛ばし始める
            lingerSeconds: 落ちたあと軌跡を残しておいて、消えるまでの秒数
            visualDelaySeconds: #49 T0-A/B 用。見た目（弾と軌跡）が出るまでの秒数。0 なら発射と同時
            priorityTargetId: #55 用。この瞬間の優先対象（二重円の客）の生成ID。0 なら優先救済のボーナスはなし
        */
        public void Launch(Vector3 start, Vector3 target, float flightSeconds, float arcHeight, OmamoriType type,
            float lingerSeconds = 0.2f, float visualDelaySeconds = 0f, int priorityTargetId = PriorityRescue.NoTarget)
        {
            _start = start;
            _target = target;
            _seconds = Mathf.Max(0.0001f, flightSeconds);
            _arcHeight = arcHeight;
            _type = type;
            _lingerSeconds = Mathf.Max(0f, lingerSeconds);
            _elapsed = 0f;
            // 色や着弾点と同じで、優先対象もこの瞬間に決めてしまう（v8 4章）。このあと二重円がだれになっても変えない
            _priorityTargetId = priorityTargetId;
            // #64: 命中音と救済音の遅れは、この「落ちる予定の時刻」から測る（フレーム単位で着くぶんの遅れも入れる）
            _impactRealtime = Time.realtimeSinceStartupAsDouble + _seconds;
            _flying = true;
            transform.position = start;

            _visualDelaySeconds = Mathf.Clamp(visualDelaySeconds, 0f, _seconds);
            if (_visualDelaySeconds > 0f) HideVisuals();

            AnyLaunched?.Invoke(this);
        }

        // 軌跡を遅らせて出す間だけ、見た目を消しておく
        void HideVisuals()
        {
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled) continue;
                if (r is TrailRenderer trail) trail.emitting = false;
                r.enabled = false;
                _hiddenRenderers.Add(r);
            }
        }

        // 今いる場所から見た目を出す（軌跡も発射地点からじゃなくて、ここから引き始める）
        void ShowVisuals()
        {
            foreach (Renderer r in _hiddenRenderers)
            {
                if (r == null) continue;
                if (r is TrailRenderer trail)
                {
                    trail.Clear();
                    trail.emitting = true;
                }
                r.enabled = true;
            }
            _hiddenRenderers.Clear();
        }

        void Update()
        {
            if (!_flying) return;

            _elapsed += Time.deltaTime;
            float t = _elapsed / _seconds;

            Vector3 prev = transform.position;
            Vector3 pos = ThrowFlight.Evaluate(_start, _target, _arcHeight, t);
            transform.position = pos;

            Vector3 velocity = pos - prev;
            if (velocity.sqrMagnitude > 1e-8f)
                transform.rotation = Quaternion.LookRotation(velocity);

            if (_hiddenRenderers.Count > 0 && _elapsed >= _visualDelaySeconds) ShowVisuals();

            if (t >= 1f) Land();
        }

        void Land()
        {
            _flying = false;

            HitZoneTarget hit = null;
            float normalized = float.PositiveInfinity;
            HitZone zone = HitZone.Miss;

            // ゲームが終わったあと（リザルト中）に落ちた弾はスコアに入れない（#32）
            bool scored = GameSession.Instance == null || GameSession.Instance.IsPlaying;
            if (scored)
            {
                hit = FindTarget(_target, out normalized);
                if (hit != null)
                    zone = OmamoriHitResolver.ApplyHit(hit.gameObject, _type, HitAccuracy.ZoneOf(normalized), _impactRealtime, _priorityTargetId);
                else
                    OmamoriHitResolver.ApplyMiss();
            }

            var result = new LandingResult(_target, transform.position, hit, normalized, zone, _type, _seconds, _elapsed, scored);
            Landed?.Invoke(result);
            AnyLanded?.Invoke(result);

            // 本体は消して、軌跡が消えるのを待ってから削除する
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                if (!(r is TrailRenderer)) r.enabled = false;
            }
            Destroy(gameObject, _lingerSeconds);
        }

        // 地面の上の点で当たる客をさがす。だれにも当たらなかったら null
        public static HitZoneTarget FindTarget(Vector3 point, out float normalizedDistance)
        {
            s_targets.Clear();
            s_candidates.Clear();

            IReadOnlyList<HitZoneTarget> active = HitZoneTarget.Active;
            for (int i = 0; i < active.Count; i++)
            {
                HitZoneTarget target = active[i];
                if (target == null) continue;
                s_targets.Add(target);
                s_candidates.Add(new LandingCandidate(target.Center, target.Radius, target.IsHittable));
            }

            int best = LandingJudge.FindBest(s_candidates, point, out normalizedDistance);
            return best >= 0 ? s_targets[best] : null;
        }
    }
}
