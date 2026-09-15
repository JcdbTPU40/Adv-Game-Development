using System.Collections.Generic;
using UnityEngine;
using Toufuku.Aim;
using Toufuku.Playtest;

namespace Toufuku.Rescue
{
    /*
        優先救済（二重円）の相手を決めて、ボーナスが入るかを判定するクラス（#55 / 企画書 v8 6章・7章、付録B B-2・PRIORITY.MARK）

        候補の集め方（v8 6章「最危険マーク＝優先救済候補」）:
          ・画面の中にいる、active で、まだ救われていない・黒客じゃない・R>0 の客だけ。入ってくる途中・帰っている途中・黒客は入れない
          ・その中で危険度 D がいちばん大きい1人。同じ値のときの順番は PriorityTarget（D が大きい → 遠い → active になったのが早い → 生成IDが小さい）
          ・しきい値はない（付録B PRIORITY.MARK: 相手は1人、D のしきい値なし）。D が 50 より小さくても必ず1人に決まる

        ボーナス（v8 7章）:
          ・ふつうの弾の SwingAccepted の時点の相手のIDを弾に保存して（OmamoriProjectile.PriorityTargetId）、
            落ちたときにそのIDの客を救えたときだけ +50（IsBonusHit）
          ・飛んでいる 0.65 秒の間に二重円が別の客に移っても、保存した値は変えない＝表示を見てねらったボーナスは消えない

        同じフレームの中で何回呼んでも選びなおさない（1フレームに1回だけ計算して、とっておく）
        二重円の表示（CustomerGroundRing）・発射したときの保存（OnusaThrower）・計測ログ（#63）が
        この1か所を見るので、「画面で光っている客」と「ボーナスの相手の客」がずれない
    */
    public static class PriorityRescue
    {
        // 優先の相手がいない・保存していないことを表すID
        public const int NoTarget = 0;

        // 画面の中かを判定するときのよゆう（ビューポートのわりあい）。はしギリギリの客を落とさないための遊び
        public static float ScreenMargin = 0.02f;

        static readonly List<PriorityCandidate> s_candidates = new List<PriorityCandidate>();

        static int s_frame = -1;
        static int? s_target;
        static Camera s_camera;
        static OnusaAimController s_aim;

        // 距離を測る基準点を上書きする（検証のシーン用）。null ならプレイヤーの照準の基準点 → カメラの位置 の順で決める
        public static Vector3? OriginOverride;
        // 画面の中かを判定するカメラを上書きする（検証のシーン用）。null なら照準のカメラ → Camera.main の順で決める
        public static Camera CameraOverride;
        // 候補にする的の一覧を上書きする（検証・テスト用）。null ならシーンにある有効な HitZoneTarget ぜんぶ
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

        // 今の優先の相手（二重円の客）の生成ID。候補がいなければ null
        public static int? CurrentTargetId
        {
            get
            {
                if (s_frame != Time.frameCount) Refresh();
                return s_target;
            }
        }

        // 今の優先の相手の生成ID。いなければ NoTarget（＝0）
        public static int CurrentTargetIdOrNone => CurrentTargetId ?? NoTarget;

        // この客が今の優先の相手かどうか
        public static bool IsPriority(int customerId)
        {
            return customerId > 0 && CurrentTargetId == customerId;
        }

        /*
            この着弾で優先救済の +50 が入るかどうか
            発射したときに保存した相手のIDと、この弾で救えた客のIDが同じときだけ true
            とちゅうの当たり（欲張り客の1発目など）は救えたわけじゃないのでボーナスは入れない（付録B B-2「部分点なし」）
            savedTargetId: SwingAccepted の時点の優先の相手のID（弾が保存した値）
            rescuedCustomerId: 当たった客の生成ID
            rescued: この当たりで救えたかどうか（R=0 になったか）
        */
        public static bool IsBonusHit(int savedTargetId, int rescuedCustomerId, bool rescued)
        {
            return rescued
                && savedTargetId > NoTarget
                && rescuedCustomerId > NoTarget
                && savedTargetId == rescuedCustomerId;
        }

        // このフレームの優先の相手を選びなおす（ふつうは自動。検証のシーンやテストからわざと呼ぶこともできる）
        public static int? Refresh()
        {
            s_frame = Time.frameCount;
            s_target = PriorityTarget.Select(CollectCandidates());
            return s_target;
        }

        // 今の候補の集まり（確認・検証用。返すリストは次の Refresh で上書きされる作業用のもの）
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
                // 候補の集め方（v8 6章）: active で、まだ救われていない・黒客じゃない・R>0 の客だけ。入ってくる途中・帰っている途中・黒客はここで落ちる
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

        // 画面の中かどうか。カメラがないシーン（テストや画面なし）では、全員を画面の中としてあつかう
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
