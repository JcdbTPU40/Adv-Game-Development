using System.Collections.Generic;
using UnityEngine;
using Toufuku.Aim;
using Toufuku.Playtest;

namespace Toufuku.Rescue
{
    /// <summary>
    /// 優先救済（二重円）の対象を決め、加点の可否を判定する — Issue #55（仕様書 v8 6章・7章／付録B B-2・PRIORITY.MARK）
    ///
    /// 候補集合（v8 6章「最危険マーク＝優先救済候補」）:
    ///   ・<b>画面内</b>にいる、active かつ 未救済・非黒客・R&gt;0 の客だけ。入場中・退場中・黒客は入れない。
    ///   ・その中で危険度 D が最大の 1 人。同値順は <see cref="PriorityTarget"/>（D 最大 → 遠い → active 化が早い → 生成ID 昇順）。
    ///   ・閾値は無い（付録B PRIORITY.MARK：対象数 1 人／D 閾値なし）。D&lt;50 でも必ず 1 人に決まる。
    ///
    /// 加点（v8 7章）:
    ///   ・通常弾の SwingAccepted 時点の対象IDを弾へ保存し（<see cref="OmamoriProjectile.PriorityTargetId"/>）、
    ///     着弾でその ID の客を<b>救済完了させた</b>ときだけ +50（<see cref="IsBonusHit"/>）。
    ///   ・飛翔 0.65 秒の間に二重円が別の客へ移っても、保存値は変えない＝表示どおり狙った加点は消えない。
    ///
    /// 同じフレーム内で何度呼んでも選び直さない（1 フレーム 1 回だけ計算してキャッシュする）。
    /// 二重円の表示（<see cref="CustomerGroundRing"/>）・発射時の保存（OnusaThrower）・計測ログ（#63）が
    /// この 1 か所を見るので、「画面で光っている客」と「加点対象の客」が食い違わない。
    /// </summary>
    public static class PriorityRescue
    {
        /// <summary>優先対象が居ない／保存していないことを表す ID。</summary>
        public const int NoTarget = 0;

        /// <summary>画面内判定の余白（ビューポート比）。端ぎりぎりの客を落とさないための遊び。</summary>
        public static float ScreenMargin = 0.02f;

        static readonly List<PriorityCandidate> s_candidates = new List<PriorityCandidate>();

        static int s_frame = -1;
        static int? s_target;
        static Camera s_camera;
        static OnusaAimController s_aim;

        /// <summary>距離を測る基準点の上書き（検証シーン用）。null ならプレイヤーの照準基準点 → カメラ位置の順で決める。</summary>
        public static Vector3? OriginOverride;
        /// <summary>画面内判定に使うカメラの上書き（検証シーン用）。null なら照準のカメラ → Camera.main の順で決める。</summary>
        public static Camera CameraOverride;
        /// <summary>
        /// 候補にする的の一覧の上書き（検証・テスト用）。null ならシーン上の有効な <see cref="HitZoneTarget"/> 全部。
        /// </summary>
        public static System.Func<IReadOnlyList<HitZoneTarget>> TargetSource;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_frame = -1;
            s_target = null;
            s_camera = null;
            s_aim = null;
            OriginOverride = null;
            CameraOverride = null;
            TargetSource = null;
            ScreenMargin = 0.02f;
        }

        /// <summary>いまの優先対象（二重円の客）の生成ID。候補が居なければ null。</summary>
        public static int? CurrentTargetId
        {
            get
            {
                if (s_frame != Time.frameCount) Refresh();
                return s_target;
            }
        }

        /// <summary>いまの優先対象の生成ID。居なければ <see cref="NoTarget"/>（＝0）。</summary>
        public static int CurrentTargetIdOrNone => CurrentTargetId ?? NoTarget;

        /// <summary>この客がいまの優先対象か。</summary>
        public static bool IsPriority(int customerId)
        {
            return customerId > 0 && CurrentTargetId == customerId;
        }

        /// <summary>
        /// この着弾で優先救済の +50 が入るか。
        /// 発射時に保存した対象IDと、<b>この弾が救済完了させた</b>客の ID が一致したときだけ true。
        /// 途中命中（欲張り客の1発目など）は救済完了ではないので加点しない（付録B B-2「部分点なし」）。
        /// </summary>
        /// <param name="savedTargetId">SwingAccepted 時点の優先対象ID（弾が保存した値）。</param>
        /// <param name="rescuedCustomerId">当たった客の生成ID。</param>
        /// <param name="rescued">この命中で救済が完了したか（R=0 になったか）。</param>
        public static bool IsBonusHit(int savedTargetId, int rescuedCustomerId, bool rescued)
        {
            return rescued
                && savedTargetId > NoTarget
                && rescuedCustomerId > NoTarget
                && savedTargetId == rescuedCustomerId;
        }

        /// <summary>このフレームの優先対象を選び直す（通常は自動。検証シーン・テストから明示的に呼ぶこともできる）。</summary>
        public static int? Refresh()
        {
            s_frame = Time.frameCount;
            s_target = PriorityTarget.Select(CollectCandidates());
            return s_target;
        }

        /// <summary>いまの候補集合（確認・検証用。戻り値は次の <see cref="Refresh"/> で上書きされる作業用リスト）。</summary>
        public static IReadOnlyList<PriorityCandidate> CollectCandidates()
        {
            Camera cam = ResolveCamera();
            Vector3 origin = ResolveOrigin(cam);

            s_candidates.Clear();
            IReadOnlyList<HitZoneTarget> active = TargetSource != null ? TargetSource() : HitZoneTarget.Active;
            if (active == null) return s_candidates;
            for (int i = 0; i < active.Count; i++)
            {
                HitZoneTarget target = active[i];
                if (target == null) continue;

                CustomerState state = target.GetComponent<CustomerState>();
                // 候補集合（v8 6章）: active かつ 未救済・非黒客・R>0 だけ。入場中・退場中・黒客はここで落ちる。
                if (state == null || !state.IsRescueTarget) continue;

                Vector3 center = target.Center;
                if (!IsOnScreen(cam, center)) continue;

                Vector3 flat = center - origin;
                flat.y = 0f;
                s_candidates.Add(new PriorityCandidate(
                    CustomerSpawnId.Of(target.gameObject), state.Danger, flat.magnitude, state.ActiveSinceTime));
            }
            return s_candidates;
        }

        /// <summary>画面内か。カメラが無いシーン（テスト・ヘッドレス）では全員を画面内として扱う。</summary>
        public static bool IsOnScreen(Camera cam, Vector3 worldPoint)
        {
            if (cam == null) return true;

            Vector3 v = cam.WorldToViewportPoint(worldPoint);
            if (v.z <= 0f) return false;

            float m = ScreenMargin;
            return v.x >= -m && v.x <= 1f + m && v.y >= -m && v.y <= 1f + m;
        }

        static Camera ResolveCamera()
        {
            if (CameraOverride != null) return CameraOverride;

            if (s_aim == null) s_aim = Object.FindAnyObjectByType<OnusaAimController>();
            if (s_aim != null && s_aim.ViewCamera != null) return s_aim.ViewCamera;

            if (s_camera == null) s_camera = Camera.main;
            return s_camera;
        }

        static Vector3 ResolveOrigin(Camera cam)
        {
            if (OriginOverride.HasValue) return OriginOverride.Value;
            if (s_aim != null) return s_aim.Origin;
            return cam != null ? cam.transform.position : Vector3.zero;
        }
    }
}
