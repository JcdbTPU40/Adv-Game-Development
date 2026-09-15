using UnityEngine;
using UnityEngine.Events;

namespace Toufuku.Rescue
{
    /*
        救済の判定（相性◯/✗ → D と R を更新する）（#13 / #54）

        仕様（企画書 v8 6章）:
          ・相性◯（正しい色）で当たった: 残りの必要な発数 R を1減らす。危険度 D は変えない。R=0 で救えた
          ・相性✗（まちがった色）で当たった: D も R も変えない。福の連なり C とご加護の進み G だけが切れる（v8 の変更点2）
            まちがった色で D を下げないので、弾が無限なのを使ってまちがった色を連打して黒客になるのを遅らせる、というずるはできない

        役割分担（#54 から）:
          D と R の値、状態の変わり方、時間の経過、黒客になること、帰ることは CustomerState がまとめて管理する
          ここは「相性の判定だけ」にしぼって、当たったときに CustomerState の
          ApplyCorrectColorHit() / ApplyWrongColorHit() を呼ぶ
          ※ 同じ GameObject に CustomerState が必要（RequireComponent）
    */
    [RequireComponent(typeof(CustomerState))]
    public class CustomerRescue : MonoBehaviour
    {
        [Header("相性テーブル（#12）")]
        [Tooltip("お守り5種×客タイプの相性テーブル(ScriptableObject)。割り当てると下の客タイプで相性を判定する。未設定なら従来どおり correctOmamori との一致で判定。")]
        [SerializeField] private OmamoriAffinityTable affinityTable;
        [Tooltip("この客のタイプ。affinityTable 設定時に使う。正式には #16 のスポーン側から設定する想定。" +
                 "※ affinityTable が設定されている場合、判定に使われるのは customerType であり correctOmamori ではない。" +
                 "シーン上でオーバーライドするときは必ず両方を揃えること。")]
        [SerializeField] private CustomerType customerType = CustomerType.Kenkou;

        [Header("この客が求めているお守り（正解／フォールバック）")]
        [Tooltip("相性テーブル未設定のときに使う正解お守り。暫定。正式には #16 の客タイプ→正解お守りデータから設定する。")]
        [SerializeField] private OmamoriType correctOmamori = OmamoriType.Kenkou;

        [Header("判定時イベント（SE/スコア接続用）")]
        public UnityEvent onGoodHit; // 相性◯で当たった（正しい色。R が1減る）
        public UnityEvent onBadHit;  // 相性✗で当たった（まちがった色。C と G が切れる。渋るリアクションは #14）

        private CustomerState _state;

        // D・R・状態は CustomerState が正しい値を持っている。見たいときはここから
        public CustomerState State => _state;
        // この客のタイプ（相性の表での判定に使う）
        public CustomerType CustomerType => customerType;
        // 正解のお守り（予備の判定に使う。Setup(profile) で客のタイプとそろう）。#63 の計測ログの客の色にも使う
        public OmamoriType CorrectOmamori => correctOmamori;
        // 終わりの状態（救えた、または黒客）。このあとこの客で得点も救済も起きない
        public bool IsFinished => _state != null && _state.IsFinished;

        private void Awake()
        {
            _state = GetComponent<CustomerState>();
        }

        /*
            出てくるときに、この客のタイプと相性の表を入れる用（#16 の出す側から呼ぶつもり）
            table を渡すと表での判定に切りかわる
        */
        public void Setup(CustomerType type, OmamoriAffinityTable table = null)
        {
            customerType = type;
            if (table != null) affinityTable = table;
        }

        /*
            客のタイプの決まり（#16）をまるごと入れるバージョン。出す側はカタログから取った
            CustomerProfile を渡すだけで、客のタイプと正解のお守り（予備）が
            まとめて設定される。table を渡せば相性の表での判定に切りかわる
            見た目（Sprite/Prefab/色）を入れるのは出す側の担当（このコンポーネントは判定だけ）
        */
        public void Setup(CustomerProfile profile, OmamoriAffinityTable table = null)
        {
            if (profile == null) return;
            customerType = profile.CustomerType;
            correctOmamori = profile.CorrectOmamori;
            if (table != null) affinityTable = table;
        }

        /*
            お守りが当たったときに OmamoriHitResolver から呼ぶ
            相性の判定の結果を返すので、呼ぶ側でコンボやスコアの処理に使える
            hitType: 当たったお守りの種類
            返す値: 相性◯/✗の判定結果
        */
        public Affinity ApplyHit(OmamoriType hitType)
        {
            /*
                相性の表（#12）があれば客のタイプで判定する。なければ前と同じで正解と同じかで判定する
                ※ 表を入れているときは correctOmamori は判定に使われない。customerType の設定をわすれる
                  （correctOmamori だけ上書きした、など）と全員 Kenkou あつかいになるので注意
            */
            Affinity affinity = affinityTable != null
                ? AffinityResolver.Resolve(affinityTable, customerType, hitType)
                : AffinityResolver.Resolve(correctOmamori, hitType);

            // 終わりの状態（救えた / 黒客）では D も R も動かさない（2回判定しないように）
            if (_state == null || !_state.IsActive) return affinity;

            if (affinity == Affinity.Good)
            {
                _state.ApplyCorrectColorHit(); // R を1減らす。D は変えない。R=0 で救えた
                onGoodHit?.Invoke();
            }
            else
            {
                _state.ApplyWrongColorHit();   // D も R も変えない（v8 の変更点2）
                onBadHit?.Invoke();            // ← 福の連なり C とご加護の進み G のリセット、渋るリアクション（#14）
            }

            return affinity;
        }
    }
}
