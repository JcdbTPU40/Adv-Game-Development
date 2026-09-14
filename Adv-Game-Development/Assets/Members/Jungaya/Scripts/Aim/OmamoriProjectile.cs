using System;
using System.Collections.Generic;
using UnityEngine;
using Toufuku.Rescue;

namespace Toufuku.Aim
{
    /// <summary>着弾の結果（確認・HUD 用）。</summary>
    public readonly struct LandingResult
    {
        /// <summary>SwingAccepted 時に固定した着弾目標点。</summary>
        public readonly Vector3 TargetPoint;
        /// <summary>実際に着いた位置。</summary>
        public readonly Vector3 LandedPoint;
        /// <summary>当たった客。外しなら null。</summary>
        public readonly HitZoneTarget Hit;
        /// <summary>判定半径に対する中心からの距離の割合。外しなら +∞。</summary>
        public readonly float NormalizedDistance;
        /// <summary>スコアへ渡したゾーン（相性✗・外しは Miss）。</summary>
        public readonly HitZone Zone;
        public readonly OmamoriType Type;
        /// <summary>発射時に決めた飛翔時間（秒）。</summary>
        public readonly float PlannedSeconds;
        /// <summary>実際にかかった時間（秒）。フレーム単位で進むので最大 1 フレーム分長い。</summary>
        public readonly float ElapsedSeconds;
        /// <summary>スコアへ計上したか（セッション外の着弾は計上しない）。</summary>
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

    /// <summary>
    /// 飛んでいるお守り 1 発 — Issue #60（仕様書 v8 4章・5章）
    ///
    /// ・開始点 → 着弾目標点を <see cref="ThrowFlight"/> のスプラインで結び、飛翔時間ちょうどで目標点に着く。物理は使わない。
    /// ・着いた瞬間に <see cref="LandingJudge"/> で対象を決め、救済判定と命中精度をスコアへ渡す。
    ///   当たり判定が消えている客（救済演出中）は候補に入らないので、弾は通過して後方の客で判定される。
    /// ・OnusaThrower が実行時に AddComponent して <see cref="Launch"/> する。
    /// </summary>
    public class OmamoriProjectile : MonoBehaviour
    {
        /// <summary>どの弾でも着弾したら発火する（確認用 HUD・ログ用）。</summary>
        public static event Action<LandingResult> AnyLanded;
        /// <summary>どの弾でも飛ばし始めたら発火する（#63 計測ログが発射と着弾を結び付ける）。</summary>
        public static event Action<OmamoriProjectile> AnyLaunched;
        /// <summary>この弾が着弾した。</summary>
        public event Action<LandingResult> Landed;

        static readonly List<LandingCandidate> s_candidates = new List<LandingCandidate>();
        static readonly List<HitZoneTarget> s_targets = new List<HitZoneTarget>();

        Vector3 _start;
        Vector3 _target;
        float _seconds;
        float _arcHeight;
        float _elapsed;
        float _lingerSeconds;
        double _impactRealtime;
        OmamoriType _type;
        bool _flying;

        public Vector3 TargetPoint => _target;
        public float FlightSeconds => _seconds;
        public OmamoriType Type => _type;
        public bool IsFlying => _flying;

        /// <summary>飛ばし始める。</summary>
        /// <param name="lingerSeconds">着弾後に軌跡を残してから消えるまでの秒数</param>
        public void Launch(Vector3 start, Vector3 target, float flightSeconds, float arcHeight, OmamoriType type, float lingerSeconds = 0.2f)
        {
            _start = start;
            _target = target;
            _seconds = Mathf.Max(0.0001f, flightSeconds);
            _arcHeight = arcHeight;
            _type = type;
            _lingerSeconds = Mathf.Max(0f, lingerSeconds);
            _elapsed = 0f;
            // #64: 命中音・救済音の遅延はこの「着弾予定時刻」から測る（フレーム単位で着くぶんの遅れも含める）
            _impactRealtime = Time.realtimeSinceStartupAsDouble + _seconds;
            _flying = true;
            transform.position = start;
            AnyLaunched?.Invoke(this);
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

            if (t >= 1f) Land();
        }

        void Land()
        {
            _flying = false;

            HitZoneTarget hit = null;
            float normalized = float.PositiveInfinity;
            HitZone zone = HitZone.Miss;

            // セッション終了後（リザルト中）に着いた弾は計上しない（#32）
            bool scored = GameSession.Instance == null || GameSession.Instance.IsPlaying;
            if (scored)
            {
                hit = FindTarget(_target, out normalized);
                if (hit != null)
                    zone = OmamoriHitResolver.ApplyHit(hit.gameObject, _type, HitAccuracy.ZoneOf(normalized), _impactRealtime);
                else
                    OmamoriHitResolver.ApplyMiss();
            }

            var result = new LandingResult(_target, transform.position, hit, normalized, zone, _type, _seconds, _elapsed, scored);
            Landed?.Invoke(result);
            AnyLanded?.Invoke(result);

            // 本体は消し、軌跡が消えるのを待ってから破棄する
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                if (!(r is TrailRenderer)) r.enabled = false;
            }
            Destroy(gameObject, _lingerSeconds);
        }

        /// <summary>地面上の点で当たる客を探す。誰にも当たらなければ null。</summary>
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
