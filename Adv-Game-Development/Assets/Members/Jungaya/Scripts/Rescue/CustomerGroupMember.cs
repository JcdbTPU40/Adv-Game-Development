using UnityEngine;

namespace Toufuku.Rescue
{
    /*
        集団客の1人に付けるコンポーネント（#93）。どのグループに入っているかを持って、終わったらグループに知らせる
        出す側（MockCrowdDirector）が作った直後に Join で入れる
    */
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CustomerState))]
    public class CustomerGroupMember : MonoBehaviour
    {
        public CustomerGroup Group { get; private set; }
        public CustomerState State { get; private set; }
        public CustomerRescue Rescue { get; private set; }
        // 消されている途中（グループの「全員終わったか」の判定から外す）
        public bool IsLeaving { get; private set; }

        // その客が入っているグループ。一人客なら null
        public static CustomerGroup GroupOf(GameObject customer)
        {
            if (customer == null) return null;
            CustomerGroupMember m = customer.GetComponent<CustomerGroupMember>();
            return m != null ? m.Group : null;
        }

        void Awake()
        {
            State = GetComponent<CustomerState>();
            Rescue = GetComponent<CustomerRescue>();
        }

        void OnEnable()
        {
            if (State == null) return;
            State.onRescued.AddListener(HandleFinished);
            State.onBlack.AddListener(HandleFinished);
        }

        void OnDisable()
        {
            if (State == null) return;
            State.onRescued.RemoveListener(HandleFinished);
            State.onBlack.RemoveListener(HandleFinished);
        }

        public void Join(CustomerGroup group)
        {
            Group = group;
            group?.Add(this);
        }

        void HandleFinished()
        {
            if (Group != null) Group.NotifyMemberFinished();
        }

        void OnDestroy()
        {
            // 帰るより前に消された（リトライなど）ときも、残りのメンバーが帰れなくならないようにする
            IsLeaving = true;
            if (Group != null && !Group.IsReleased) Group.NotifyMemberFinished();
        }
    }
}
