using System;
using System.Collections.Generic;
using UnityEngine;
using Toufuku.Playtest;

namespace Toufuku.Rescue
{
    // 成立した伝播1回ぶん（HUD・演出・ログ用）
    public readonly struct SmilePropagationInfo
    {
        // 笑顔をくばった救済客
        public readonly GameObject Rescuer;
        // 笑顔をうけとった客
        public readonly GameObject Target;
        public readonly int RescuerId;
        public readonly int TargetId;
        // この1回で入った縁（保存した倍率で計算ずみ）
        public readonly int Gain;
        // この救済で何人目か（1〜上限）
        public readonly int Order;
        // 救えたときに確定した倍率
        public readonly EnMultiplierSnapshot Snapshot;

        public SmilePropagationInfo(GameObject rescuer, GameObject target, int rescuerId, int targetId,
            int gain, int order, EnMultiplierSnapshot snapshot)
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

    /*
        笑顔を運ぶ人（#56 / 企画書 v8 6章「笑顔の伝播」）

        救えた瞬間に救済客へ付いて、帰り道（CustomerMotion の退場歩行。#62）のあいだ毎フレーム
        近くの客とのすれちがいを見る。すれちがった相手が条件を満たしていれば、その場で伝える（相手を先に決めておかない）
        ・伝わったら相手の危険度 D を5減らして（CustomerState.ReceiveSmilePropagation）、
          縁 20 ×（救えたときに保存した倍率）を ScoreManager.RegisterPropagation へ渡す
        ・ルール（同じ向きの組は1回・最大4人・黒客は相手にしない・3:00 の境目）は全部 SmilePropagation が決める
          ここは「だれとすれちがったか」と「まだ 3:00 前か」を渡すだけにして、ルールをテストしやすくしておく

        すれちがいの見かた:
          いまは「水平の距離が接触の半径の中」をすれちがいとする。当たり判定の形が決まったら、ここだけ入れかえる
          歩くほど多くの人とすれちがうので、奥の遠方客ほど伝わる人数が増える（v8 7章のねらい）

        付けかたは AttachTo（通常弾の救済は OmamoriHitResolver が呼ぶ）
        大祓（追加T5）で救えた客は、両方 ×1.0 の EnMultiplierSnapshot を渡して付ける
    */
    [DisallowMultipleComponent]
    public class SmileCarrier : MonoBehaviour
    {
        // すれちがいとみなす水平の距離（m）のふつうの値
        public const float DefaultContactRadius = 2.5f;

        [Header("すれちがいの判定")]
        [Tooltip("この水平の距離（m）の中に入った客を「すれちがった」とみなす。")]
        [SerializeField, Min(0.1f)] float contactRadius = DefaultContactRadius;

        // これから付ける SmileCarrier の接触の半径（m）。検証のシーンから広げたり縮めたりするための入口
        public static float ContactRadiusForNewCarriers = DefaultContactRadius;

        // 伝播が1回成立した（HUD・灯りの粒・効果音・検証のシーンが受け取る）
        public static event Action<SmilePropagationInfo> Propagated;

        static readonly List<HitZoneTarget> s_buffer = new List<HitZoneTarget>();

        SmilePropagation _rules;

        // この救済客の伝播の状態（人数・縁の合計・保存した倍率）
        public SmilePropagation Rules => _rules;
        // 救えたときに確定した倍率
        public EnMultiplierSnapshot Snapshot => _rules != null ? _rules.Snapshot : EnMultiplierSnapshot.None;
        // ここまでに伝わった人数
        public int PropagatedCount => _rules != null ? _rules.Count : 0;
        // この救済客が生んだ伝播の縁の合計
        public int PropagatedScore => _rules != null ? _rules.TotalScore : 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Propagated = null;
            ContactRadiusForNewCarriers = DefaultContactRadius;
        }

        /*
            救えた客に笑顔を持たせる。もう持っていれば何もしない（同じ救済で2回付けない）
            customer: 救済できた客
            snapshot: 救えたときに確定した倍率（#61）。通常弾は ScoreManager.LastRescueSnapshot、
                      大祓は両方 ×1.0 の値を渡す
        */
        public static SmileCarrier AttachTo(GameObject customer, EnMultiplierSnapshot snapshot)
        {
            if (customer == null || !snapshot.IsValid) return null;
            if (customer.GetComponent<SmileCarrier>() != null) return null;

            int points = EnFormula.DefaultPropagationPoints;
            int maxTargets = SmilePropagation.DefaultMaxTargets;
            ScoreManager score = ScoreManager.Instance;
            if (score != null)
            {
                // 数値は付録B の写し（ScoreBonusTable）から。表がなければ付録B と同じ予備の値
                points = score.PropagationPoints;
                maxTargets = score.PropagationMaxTargets;
            }

            var carrier = customer.AddComponent<SmileCarrier>();
            carrier.contactRadius = ContactRadiusForNewCarriers;
            carrier._rules = new SmilePropagation(CustomerSpawnId.Of(customer), snapshot, points, maxTargets);

            Debug.Log($"[伝播] ID {carrier._rules.RescuerId} が笑顔を持って帰る（{snapshot} / 最大 {maxTargets}人 " +
                      $"= 最大 +{carrier._rules.MaxTotalScore}）", customer);
            return carrier;
        }

        void Update()
        {
            if (_rules == null || _rules.IsFull)
            {
                // 上限まで伝えたら、もうだれにも伝えない。これ以上の距離の計算をやめる
                enabled = false;
                return;
            }

            double contactSeconds;
            if (!AcceptsPropagationNow(out contactSeconds))
            {
                // 3:00 以後の帰り道は見た目だけ（v8 7章）。これ以後のすれちがいは数えない
                enabled = false;
                return;
            }

            ScanContacts(contactSeconds);
        }

        void ScanContacts(double contactSeconds)
        {
            IReadOnlyList<HitZoneTarget> active = HitZoneTarget.Active;
            if (active == null || active.Count == 0) return;

            // 見ている途中で相手が消えても落ちないように、いったん写してから回す
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

        void TryPropagateTo(GameObject target, double contactSeconds)
        {
            CustomerState state = target.GetComponent<CustomerState>();

            // 相手の条件（active・未救済・非黒客・R>0）は CustomerState が持っている。黒客はここで必ず落ちる
            bool eligible = state != null && state.IsRescueTarget;
            int targetId = CustomerSpawnId.Of(target);

            SmilePropagationResult result = _rules.TryPropagate(targetId, eligible, true);
            if (result != SmilePropagationResult.Applied) return;

            // D を5減らす（v8 6章の状態遷移表）
            state.ReceiveSmilePropagation();

            /*
                縁は救えたときに保存した倍率で計算する（ScoreManager.RegisterPropagation / EnFormula）
                伝播は「連続で救えた」わけじゃないので、福の連なり C を伸ばさないし切りもしない。ご加護の進み G も数えない（v8 7章・10章）
            */
            int gain = _rules.ScorePerPropagation;
            ScoreManager score = ScoreManager.Instance;
            if (score != null) gain = score.RegisterPropagation(_rules.Snapshot, contactSeconds);

            var info = new SmilePropagationInfo(
                gameObject, target, _rules.RescuerId, targetId, gain, _rules.Count, _rules.Snapshot);

            Debug.Log($"[伝播] ID {_rules.RescuerId} → ID {targetId}（{_rules.Count}/{_rules.MaxTargets}人目）" +
                      $" D−{CustomerStateMachine.SmileDangerRelief:0} 縁 +{gain}（{_rules.Snapshot}）", target);

            PlaytestLog.SmilePropagation(
                _rules.RescuerId, targetId, gain, _rules.Snapshot.ChainMultiplier * _rules.Snapshot.BlessingMultiplier,
                _rules.Count, score != null ? score.En : (int?)null);

            Propagated?.Invoke(info);
        }

        /*
            いま伝播していい時刻か（#65 の GameSession が時計の正本）
            ゲームの管理を置かない検証のシーンやテストでは、いつでも伝播していいことにする
        */
        static bool AcceptsPropagationNow(out double contactSeconds)
        {
            GameSession session = GameSession.Instance;
            if (session == null)
            {
                contactSeconds = double.NaN;
                return true;
            }

            contactSeconds = session.ElapsedTime;
            return session.AcceptsPropagationAt(contactSeconds);
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
