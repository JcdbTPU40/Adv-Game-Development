using System.Collections.Generic;
using UnityEngine;

namespace Toufuku.Rescue
{
    // 集団客の種類（#93）。今は名前と人数のはばにだけ使う
    public enum CustomerGroupKind
    {
        Couple,   // カップル（2人）
        Friends,  // 友達（2〜4人）
        Family    // 家族（3〜4人）
    }

    // グループの中の1人の、当たる相手を決めるのに使う情報（MonoBehaviour なしでテストするための入れ物）
    public readonly struct GroupHitCandidate
    {
        // active（救われるのを待っている）か。入ってくる途中・救えた・黒客は false
        public readonly bool IsActive;
        // 投げたお守りがこの人の悩みに合うか（相性◯）
        public readonly bool IsGood;
        // 危険度 D（0〜100）
        public readonly float Danger;

        public GroupHitCandidate(bool isActive, bool isGood, float danger)
        {
            IsActive = isActive;
            IsGood = isGood;
            Danger = danger;
        }
    }

    /*
        集団客（家族・カップル・友達）のまとまり（#93）

        仕様（Issue #93 で決めたこと）:
          ・メンバーはそれぞれちがう悩み（正解のお守り）を持つ
          ・当たり判定はグループ全体で1つ。グループのだれに当てても、グループに当たったあつかい
              合う悩みのメンバーがいれば、そのうち1人の悩みが消える。2人以上いれば危険度 D が高い人から
              合う悩みのメンバーがいなければ、誤投擲（まちがった色）あつかい
          ・悩みが消えたメンバーはすぐには帰らないで、グループといっしょに残る
            残っている間は当たり判定を消す（企画書 v8 6章。となりの客をねらった弾をうばわないように）
          ・全員が終わったら（救えた or 黒客）、残っていたメンバーがそろって帰る
          ・危険度 D と黒客になるのは、メンバーごとに管理する（CustomerState は1人ずつのまま）
            黒客になった人は仲間を待たずに、ふつうの黒客と同じですぐ帰る（v8 6章「黒客化した瞬間に上限から外れる」）
          ・3:00 でゲームが終わったら、残っていたメンバーは仲間を待たずに帰る（ReleaseWaiting）

        黒客になったメンバーに当てたときは、ふつうの黒客と同じペナルティ（OmamoriHitResolver が先に処理する）
        当てる相手の決め方そのものは ChooseTarget（MonoBehaviour なし）にして、エディタのテストで確かめる（CustomerGroupTests）
    */
    public class CustomerGroup
    {
        readonly List<CustomerGroupMember> _members = new List<CustomerGroupMember>();
        readonly List<GroupHitCandidate> _candidates = new List<GroupHitCandidate>();

        public int Id { get; }
        public CustomerGroupKind Kind { get; }
        public IReadOnlyList<CustomerGroupMember> Members => _members;
        // 全員終わって（またはゲームが終わって）、待つのをやめたか
        public bool IsReleased { get; private set; }

        public CustomerGroup(int id, CustomerGroupKind kind)
        {
            Id = id;
            Kind = kind;
        }

        // メンバーを足す。足した人は、救えたあとグループ全員が終わるまで帰らない
        public void Add(CustomerGroupMember member)
        {
            if (member == null || _members.Contains(member)) return;
            _members.Add(member);
            if (member.State != null) member.State.HoldExit = !IsReleased;
        }

        /*
            グループのだれかにお守りが当たったときに、実際に当てる相手を返す（OmamoriHitResolver から呼ぶ）
            struck: 当たったメンバー
            type: 投げたお守り
        */
        public GameObject ResolveHitTarget(GameObject struck, OmamoriType type)
        {
            int struckIndex = -1;
            _candidates.Clear();
            for (int i = 0; i < _members.Count; i++)
            {
                CustomerGroupMember m = _members[i];
                bool alive = m != null && m.State != null;
                bool active = alive && m.State.IsActive;
                bool good = active && m.Rescue != null && m.Rescue.Evaluate(type) == Affinity.Good;
                _candidates.Add(new GroupHitCandidate(active, good, alive ? m.State.Danger : 0f));
                if (alive && m.gameObject == struck) struckIndex = i;
            }

            int pick = ChooseTarget(_candidates, struckIndex);
            return pick >= 0 ? _members[pick].gameObject : struck;
        }

        /*
            当てる相手の番号を決める。だれも当てられなければ -1（呼ぶ側は当たったメンバーにそのまま当てる）
              1) 合う悩みの active なメンバーのうち、危険度 D がいちばん高い人。同じ D なら当たった本人、それもちがえば番号が小さい人
              2) 合う人がいなければ誤投擲。当たった本人が active なら本人に、そうでなければ番号がいちばん小さい active な人に当てる
                 （まちがった色の当たりとして、福の連なりとご加護の進みを切る・渋るリアクションをさせるため）
        */
        public static int ChooseTarget(IReadOnlyList<GroupHitCandidate> candidates, int struckIndex)
        {
            if (candidates == null) return -1;

            int best = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                GroupHitCandidate c = candidates[i];
                if (!c.IsActive || !c.IsGood) continue;
                if (best < 0) { best = i; continue; }

                float bestDanger = candidates[best].Danger;
                if (c.Danger > bestDanger + 0.0001f) best = i;
                else if (Mathf.Abs(c.Danger - bestDanger) <= 0.0001f && i == struckIndex) best = i;
            }
            if (best >= 0) return best;

            if (struckIndex >= 0 && struckIndex < candidates.Count && candidates[struckIndex].IsActive) return struckIndex;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].IsActive) return i;
            }
            return -1;
        }

        /*
            メンバーのだれかが終わった（救えた / 黒客）ときに CustomerGroupMember から呼ぶ
            全員終わっていたら、そろって帰らせる。消えてしまったメンバーは終わったあつかい
        */
        public void NotifyMemberFinished()
        {
            if (IsReleased) return;
            for (int i = 0; i < _members.Count; i++)
            {
                CustomerGroupMember m = _members[i];
                if (m != null && !m.IsLeaving && m.State != null && !m.State.IsFinished) return;
            }

            ReleaseWaiting();
        }

        /*
            待つのをやめて、残っていたメンバーを帰らせる。まだ悩みが残っている人は、このあと終わったらすぐ帰る
            全員終わったときのほかに、3:00 でゲームが終わったときに出す側（MockCrowdDirector）から呼ぶ
            （D が進まなくなって、残りの人が終わらないまま仲間を待ちつづけないように）
        */
        public void ReleaseWaiting()
        {
            if (IsReleased) return;
            IsReleased = true;
            for (int i = 0; i < _members.Count; i++)
            {
                CustomerGroupMember m = _members[i];
                if (m != null && !m.IsLeaving && m.State != null) m.State.ReleaseExit();
            }
        }

        // メンバーの真ん中の位置（向きをそろえるのに使う）。いなければ fallback
        public Vector3 Centroid(Vector3 fallback)
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            for (int i = 0; i < _members.Count; i++)
            {
                CustomerGroupMember m = _members[i];
                if (m == null || m.IsLeaving) continue;
                sum += m.transform.position;
                n++;
            }
            return n > 0 ? sum / n : fallback;
        }

        /*
            並び方。グループの定位置から見た index 番目の人のずれ（m）
            横にならべて、1人おきに少し前後にずらす（一列にそろいすぎないように）
            count: 人数 / spacing: となりの人との間（m）
        */
        public static Vector3 FormationOffset(int index, int count, float spacing)
        {
            if (count <= 1) return Vector3.zero;
            float x = (index - (count - 1) * 0.5f) * spacing;
            float z = (index % 2 == 0 ? 1f : -1f) * spacing * 0.25f;
            return new Vector3(x, 0f, z);
        }

        // 横に並んだときの、はしの人からはしの人までのはば（m）
        public static float Span(int count, float spacing) => Mathf.Max(0, count - 1) * spacing;

        /*
            補充1回ぶんを集団客にするかと、その種類・人数を決める（MonoBehaviour なしでテストするため、乱数は 0〜1 の値で受け取る）
            groupChance: 集団客にする確率 / room: 目標人数まで、あと何人入れるか（補充待ちの人も入れる）
            chanceRoll / kindRoll / sizeRoll: 0〜1 の乱数
            返す値: 集団客にするなら true。room が2人未満なら false（一人客）
            人数は種類ごとのはばから引いて room までにおさえる。家族の人数にとどかなかったら友達にする
        */
        public static bool TryPlan(float groupChance, int room, float chanceRoll, float kindRoll, float sizeRoll,
            out CustomerGroupKind kind, out int size)
        {
            kind = CustomerGroupKind.Friends;
            size = 1;
            if (groupChance <= 0f || room < 2 || chanceRoll >= groupChance) return false;

            kind = (CustomerGroupKind)Mathf.Min(2, Mathf.FloorToInt(Mathf.Clamp01(kindRoll) * 3f));
            Vector2Int range = SizeRangeFor(kind);
            int rolled = range.x + Mathf.Min(range.y - range.x, Mathf.FloorToInt(Mathf.Clamp01(sizeRoll) * (range.y - range.x + 1)));
            size = Mathf.Min(rolled, room);
            if (size < range.x) kind = CustomerGroupKind.Friends;
            return true;
        }

        // グループの種類に合う人数のはば（両はしをふくむ）
        public static Vector2Int SizeRangeFor(CustomerGroupKind kind)
        {
            switch (kind)
            {
                case CustomerGroupKind.Couple: return new Vector2Int(2, 2);
                case CustomerGroupKind.Family: return new Vector2Int(3, 4);
                default: return new Vector2Int(2, 4);
            }
        }
    }
}
