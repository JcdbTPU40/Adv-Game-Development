using System;
using System.Collections.Generic;
using UnityEngine;
using Toufuku.Playtest;

namespace Toufuku.Rescue
{
    /// <summary>成立した伝播 1 回ぶん（HUD・演出・ログ用）。</summary>
    public readonly struct SmilePropagationInfo
    {
        /// <summary>笑顔を配った救済客。</summary>
        public readonly GameObject Rescuer;
        /// <summary>笑顔を受け取った客。</summary>
        public readonly GameObject Target;
        public readonly int RescuerId;
        public readonly int TargetId;
        /// <summary>この 1 回で入った縁（スナップショットで計算済み）。</summary>
        public readonly int Gain;
        /// <summary>この救済で何人目か（1〜上限）。</summary>
        public readonly int Order;
        /// <summary>救済完了時に確定した倍率。</summary>
        public readonly SmileMultiplierSnapshot Snapshot;

        public SmilePropagationInfo(GameObject rescuer, GameObject target, int rescuerId, int targetId,
            int gain, int order, SmileMultiplierSnapshot snapshot)
        {
            Rescuer = rescuer;
            Target = target;
            RescuerId = rescuerId;
            TargetId = targetId;
            Gain = gain;
            Order = order;
            Snapshot = snapshot;
        }
    }

    /// <summary>
    /// 笑顔を運ぶ人 — Issue #56（企画書 v8 6章「笑顔の伝播」）
    ///
    /// 救済完了の瞬間に救済客へ付け、退場歩行のあいだ<b>毎フレーム</b>近くの客との交差を見る。
    /// 交差した相手が対象条件を満たしていれば、その場で伝播させる（対象は<b>予約しない</b>）。
    ///   ・成立したら相手の危険度 D を 5 減らし（<see cref="CustomerState.ReceiveSmilePropagation"/>）、
    ///     縁 +20 ×（救済時のスナップショット倍率）を <see cref="ScoreManager"/> へ渡す。
    ///   ・規則（有向ペア1回・最大4人・黒客除外・3:00境界）はすべて <see cref="SmilePropagation"/> が判定する。
    ///     ここはその判定に「誰と交差したか」と「いまの接触時刻」を渡すだけにして、規則をテスト可能に保つ。
    ///
    /// 交差の判定:
    ///   退場歩行（参道を歩いて帰る演出）は別 Issue のため、いまは<b>水平距離が接触半径以内</b>を交差とみなす。
    ///   歩行が入っても毎フレーム位置を見る作りは変わらないので、このコンポーネントはそのまま使える
    ///   （歩くほど多くの相手とすれ違い、奥の遠方客ほど伝播人数が増える、という設計意図も歩行実装後に自然に効く）。
    ///
    /// 付け方は <see cref="AttachTo"/>（通常弾の救済は <c>OmamoriHitResolver</c> が呼ぶ）。
    /// 大祓による救済客は <see cref="SmileMultiplierSnapshot.Purification"/>（×1.0）で付ける。
    /// </summary>
    [DisallowMultipleComponent]
    public class SmileCarrier : MonoBehaviour
    {
        /// <summary>交差とみなす水平距離（m）の既定値。</summary>
        public const float DefaultContactRadius = 2.5f;

        [Header("交差の判定")]
        [Tooltip("この水平距離（m）以内に入った客を「すれ違った」とみなす。退場歩行の実装後もこの半径で判定する。")]
        [SerializeField, Min(0.1f)] float contactRadius = DefaultContactRadius;

        /// <summary>
        /// 新しく付ける <see cref="SmileCarrier"/> の接触半径（m）。確認シーンから広げ／狭めするための入口。
        /// </summary>
        public static float ContactRadiusForNewCarriers = DefaultContactRadius;

        /// <summary>伝播が 1 回成立した（HUD・灯りの粒・SE・確認シーンが購読する）。</summary>
        public static event Action<SmilePropagationInfo> Propagated;

        static readonly List<HitZoneTarget> s_buffer = new List<HitZoneTarget>();

        SmilePropagation _rules;

        /// <summary>この救済客の伝播の状態（人数・合計縁・スナップショット）。</summary>
        public SmilePropagation Rules => _rules;
        /// <summary>救済完了時に確定した倍率。</summary>
        public SmileMultiplierSnapshot Snapshot => _rules != null ? _rules.Snapshot : SmileMultiplierSnapshot.Purification;
        /// <summary>これまでに伝播した人数。</summary>
        public int PropagatedCount => _rules != null ? _rules.Count : 0;
        /// <summary>この救済客が生んだ伝播の縁の合計。</summary>
        public int PropagatedScore => _rules != null ? _rules.TotalScore : 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Propagated = null;
            ContactRadiusForNewCarriers = DefaultContactRadius;
        }

        /// <summary>
        /// 救済された客に笑顔を持たせる。すでに持っていれば何もしない（同じ救済で二重に付けない）。
        /// </summary>
        /// <param name="customer">救済完了した客。</param>
        /// <param name="snapshot">
        /// 救済完了時に確定した倍率。通常弾は「救済で +1 したあとの福の連なり倍率」と「その弾の発射時のご加護倍率」。
        /// 大祓は <see cref="SmileMultiplierSnapshot.Purification"/>（×1.0）。
        /// </param>
        public static SmileCarrier AttachTo(GameObject customer, SmileMultiplierSnapshot snapshot)
        {
            if (customer == null) return null;
            if (customer.GetComponent<SmileCarrier>() != null) return null;

            int scorePerContact = SmilePropagation.DefaultScorePerContact;
            int maxTargets = SmilePropagation.DefaultMaxTargets;
            ScoreManager score = ScoreManager.Instance;
            if (score != null)
            {
                // 数値は付録B の写し（ScoreBonusTable）から。表が無ければ付録B と同じ既定値。
                scorePerContact = score.SmilePropagationBonus;
                maxTargets = score.SmilePropagationMaxTargets;
            }

            var carrier = customer.AddComponent<SmileCarrier>();
            carrier.contactRadius = ContactRadiusForNewCarriers;
            carrier._rules = new SmilePropagation(
                CustomerSpawnId.Of(customer), snapshot, scorePerContact, maxTargets, ResolveDeadlineSeconds());

            Debug.Log($"[伝播] ID {carrier._rules.RescuerId} が笑顔を持って退場（倍率 {snapshot} / 最大 {maxTargets}人 " +
                      $"= 最大 +{carrier._rules.MaxTotalScore}）", customer);
            return carrier;
        }

        void Update()
        {
            if (_rules == null || _rules.IsFull)
            {
                // 上限に達したらもう誰とも伝播しない。以後の距離計算をやめる。
                enabled = false;
                return;
            }

            float contactSeconds = ElapsedSeconds();
            if (contactSeconds >= _rules.DeadlineSeconds)
            {
                // 3:00 以後の退場歩行は演出のみ（v8 7章）。以後の接触は数えない。
                enabled = false;
                return;
            }

            ScanContacts(contactSeconds);
        }

        void ScanContacts(float contactSeconds)
        {
            IReadOnlyList<HitZoneTarget> active = HitZoneTarget.Active;
            if (active == null || active.Count == 0) return;

            // 走査中に相手が退場（Destroy）しても落ちないよう、いったん写してから回す。
            s_buffer.Clear();
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i] != null) s_buffer.Add(active[i]);
            }

            Vector3 here = transform.position;
            float radiusSqr = contactRadius * contactRadius;

            for (int i = 0; i < s_buffer.Count; i++)
            {
                if (_rules.IsFull) break;

                HitZoneTarget target = s_buffer[i];
                if (target == null || target.gameObject == gameObject) continue;

                Vector3 flat = target.Center - here;
                flat.y = 0f;
                if (flat.sqrMagnitude > radiusSqr) continue;

                TryPropagateTo(target.gameObject, contactSeconds);
            }

            s_buffer.Clear();
        }

        void TryPropagateTo(GameObject target, float contactSeconds)
        {
            CustomerState state = target.GetComponent<CustomerState>();

            // 対象条件（active・未救済・非黒客・R>0）は CustomerState が持つ。黒客はここで必ず落ちる。
            bool eligible = state != null && state.IsRescueTarget;
            int targetId = CustomerSpawnId.Of(target);

            SmilePropagationResult result = _rules.TryPropagate(targetId, eligible, contactSeconds);
            if (result != SmilePropagationResult.Applied) return;

            // D を 5 減らす（企画書 v8 6章の状態遷移表）。
            state.ReceiveSmilePropagation();

            // 縁は救済時のスナップショットで計算する。福の連なり C とご加護進捗 G は動かさない
            // （伝播は「連続救済」ではない。v8 7章／11章）。
            int gain = _rules.ScorePerPropagation;
            ScoreManager score = ScoreManager.Instance;
            if (score != null) gain = score.RegisterSmilePropagation(_rules.Snapshot);

            var info = new SmilePropagationInfo(
                gameObject, target, _rules.RescuerId, targetId, gain, _rules.Count, _rules.Snapshot);

            Debug.Log($"[伝播] ID {_rules.RescuerId} → ID {targetId}（{_rules.Count}/{_rules.MaxTargets}人目）" +
                      $" D−{CustomerStateMachine.SmileDangerRelief:0} 縁 +{gain}（{_rules.Snapshot}）", target);

            PlaytestLog.SmilePropagation(
                _rules.RescuerId, targetId, gain, _rules.Snapshot.Product, _rules.Count,
                score != null ? score.En : (int?)null);

            Propagated?.Invoke(info);
        }

        /// <summary>セッション開始からの秒。セッションが無い確認シーン・テストでは 0（＝境界にかからない）。</summary>
        static float ElapsedSeconds()
        {
            GameSession session = GameSession.Instance;
            return session != null ? session.ElapsedSeconds : 0f;
        }

        /// <summary>3:00 境界の秒数。セッションが無ければ境界なし。</summary>
        static float ResolveDeadlineSeconds()
        {
            GameSession session = GameSession.Instance;
            return session != null ? session.TotalSeconds : float.PositiveInfinity;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, contactRadius);
        }
#endif
    }
}
