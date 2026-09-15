using UnityEngine;
using Toufuku.Rescue;
using Toufuku.Rescue.Mock;

namespace Toufuku.Playtest
{
    /*
        黒客と帰っている途中の客も入れて、30人の負荷をかけつづけるクラス（#52。T6-USB）

        本番のプレイ画面（TestGame）と同じ客の集団（MockCrowdDirector ＋ Customer_MockVisibility.prefab・インバートハルの輪郭・
        頭の上のゲージ・Bloom）をそのまま使って、人数だけをいちばん重い描画に寄せる。MockCrowdDirector は変えない

        ・目標の人数 = 30 ＋ extraTarget。帰る（怒る → 余韻 → Destroy）のと補充のすきまで30人を下回らないように、多めにしておく
        ・黒客は MockCrowdDirector の blackCustomerCount でいつもまぜる
        ・帰っている途中の客: 毎秒 forcedExitsPerSecond 人の危険度Dを最大にして黒客にする → 余韻の間も描画に残って、
          鳥居から補充の客が歩いて入ってくる。本番の「黒客になって帰る」と同じ流れ
        ・描いている人数・黒客・帰っている途中の数を毎フレーム数えて、測っている間のいちばん少ない数と平均を出す（負荷の条件を満たしていたかを記録に残す）
    */
    [DefaultExecutionOrder(-50)]
    public class UsbLoadCrowd : MonoBehaviour
    {
        [SerializeField] MockCrowdDirector director;

        [Header("負荷")]
        [Tooltip("描画し続けたい体数（黒客・退場者込み）")]
        [SerializeField, Min(0)] int minimumBodies = UsbGatePlan.LoadBodies;
        [Tooltip("退場と補充の谷で下回らないための上乗せ（目標 = minimumBodies + extraTarget。定位置の数で頭打ち）")]
        [SerializeField, Min(0)] int extraTarget = 4;
        [Tooltip("毎秒この数の客を怒らせて退場させる（退場者の余韻と補充の歩行を常に画面に出す）")]
        [SerializeField, Min(0f)] float forcedExitsPerSecond = 1f;
        [SerializeField] int randomSeed = 52;

        System.Random _random;
        float _exitBudget;
        bool _active = true;

        long _frames;
        long _renderedSum;
        long _blackSum;
        long _exitingSum;
        int _renderedMin = int.MaxValue;

        public MockCrowdDirector Director => director;
        public int Rendered { get; private set; }
        public int Black { get; private set; }
        public int Exiting { get; private set; }

        public int? RenderedMin => _frames > 0 ? _renderedMin : (int?)null;
        public double? RenderedAverage => _frames > 0 ? (double)_renderedSum / _frames : (double?)null;
        public double? BlackAverage => _frames > 0 ? (double)_blackSum / _frames : (double?)null;
        public double? ExitingAverage => _frames > 0 ? (double)_exitingSum / _frames : (double?)null;

        // 客の集団を出すかどうか。止めると客をまとめてかくす（子どもの近い・遠いでは的だけを見せる）
        public bool LoadActive
        {
            get => _active;
            set
            {
                _active = value;
                if (director != null) director.gameObject.SetActive(value);
            }
        }

        void Awake()
        {
            if (director == null) director = FindAnyObjectByType<MockCrowdDirector>(FindObjectsInactive.Include);
            if (director == null)
                Debug.LogWarning("[T6-USB] MockCrowdDirector がありません（30 体負荷をかけられません）", this);
            _random = new System.Random(randomSeed);
        }

        void Start()
        {
            if (director != null) director.SetOverrideTarget(minimumBodies + extraTarget);
        }

        // 測る時間を始める（いちばん少ない数と平均を数えなおす）
        public void ResetWindow()
        {
            _frames = 0;
            _renderedSum = 0;
            _blackSum = 0;
            _exitingSum = 0;
            _renderedMin = int.MaxValue;
        }

        void Update()
        {
            if (!_active || director == null || !director.isActiveAndEnabled) return;

            _exitBudget += forcedExitsPerSecond * Time.deltaTime;
            while (_exitBudget >= 1f)
            {
                _exitBudget -= 1f;
                ForceOneExit();
            }
        }

        void LateUpdate()
        {
            if (!_active || director == null || !director.isActiveAndEnabled)
            {
                Rendered = Black = Exiting = 0;
                return;
            }

            int rendered = 0, black = 0, exiting = 0;
            foreach (MockCrowdDirector.Member m in director.Members)
            {
                if (m == null || m.Go == null || !m.Go.activeInHierarchy) continue;
                rendered++;
                bool finished = m.State != null && m.State.IsFinished;
                if (finished) exiting++;
                if ((m.Tag != null && m.Tag.IsBlack) || (m.State != null && m.State.IsBlack)) black++;
            }

            Rendered = rendered;
            Black = black;
            Exiting = exiting;

            _frames++;
            _renderedSum += rendered;
            _blackSum += black;
            _exitingSum += exiting;
            if (rendered < _renderedMin) _renderedMin = rendered;
        }

        void ForceOneExit()
        {
            var members = director.Members;
            int count = members.Count;
            if (count == 0) return;

            // 定位置に立っているふつうの客から選ぶ（黒客は数をキープしたいので怒らせない）
            int start = _random.Next(count);
            for (int i = 0; i < count; i++)
            {
                MockCrowdDirector.Member m = members[(start + i) % count];
                if (m == null || m.Go == null || m.State == null || m.State.IsFinished) continue;
                if (m.Tag != null && m.Tag.IsBlack) continue;
                if (m.Walker != null && m.Walker.IsWalking) continue;
                m.State.SetDangerForDebug(CustomerStateMachine.MaxDanger);
                return;
            }
        }
    }
}
