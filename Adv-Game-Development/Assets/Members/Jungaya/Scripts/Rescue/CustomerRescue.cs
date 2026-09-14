using UnityEngine;
using UnityEngine.Events;

namespace Toufuku.Rescue
{
    /// <summary>
    /// 救済判定（相性◯/✗ → D・R の更新）— Issue #13 / #54
    ///
    /// 仕様（企画書 v8 6章）:
    ///   ・相性◯（正色）命中 … 残り必要発数 R を1減らす。危険度 D は変えない。R=0 で救済完了。
    ///   ・相性✗（誤色）命中 … D も R も変えない。福の連なり C とご加護進捗 G だけが切れる（v8変更点2）。
    ///     誤色で D を下げないので、無限弾の誤色連打で黒客化を遅らせる抜け道は成立しない。
    ///
    /// 役割分担（#54 以降）:
    ///   D・R の値、状態遷移、時間経過、黒客化、退場は CustomerState が一元管理する。
    ///   ここは “相性判定だけ” に絞り、命中時に CustomerState の
    ///   ApplyCorrectColorHit() / ApplyWrongColorHit() を呼ぶ。
    ///   ※ 同じ GameObject に CustomerState が必要（RequireComponent）。
    /// </summary>
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
        public UnityEvent onGoodHit; // 相性◯ヒット（正色。R が1減る）
        public UnityEvent onBadHit;  // 相性✗ヒット（誤色。C と G が切れる。渋るリアクションは #14）

        private CustomerState _state;

        /// <summary>D・R・状態は CustomerState が正本。参照したい場合はこちらから。</summary>
        public CustomerState State => _state;
        /// <summary>この客のタイプ（相性テーブル判定に使う）。</summary>
        public CustomerType CustomerType => customerType;
        /// <summary>正解お守り（フォールバック判定に使う。Setup(profile) で客タイプと揃う）。#63 計測ログの客の色に使う。</summary>
        public OmamoriType CorrectOmamori => correctOmamori;
        /// <summary>終端状態（救済成功 or 黒客）。以後この客に得点も救済も発生しない。</summary>
        public bool IsFinished => _state != null && _state.IsFinished;

        private void Awake()
        {
            _state = GetComponent<CustomerState>();
        }

        /// <summary>
        /// スポーン時にこの客のタイプと相性テーブルを差し込む用（#16 のスポーン側から呼ぶ想定）。
        /// table を渡すとテーブル判定に切り替わる。
        /// </summary>
        public void Setup(CustomerType type, OmamoriAffinityTable table = null)
        {
            customerType = type;
            if (table != null) affinityTable = table;
        }

        /// <summary>
        /// 客タイプ定義(#16)を丸ごと差し込む版。スポーン側はカタログから引いた
        /// <see cref="CustomerProfile"/> を渡すだけで、客タイプ・正解お守り（フォールバック）が
        /// まとめて設定される。table を渡せば相性テーブル判定に切り替わる。
        /// 見た目(Sprite/Prefab/色)の適用はスポーン側の担当（このコンポーネントは判定のみ）。
        /// </summary>
        public void Setup(CustomerProfile profile, OmamoriAffinityTable table = null)
        {
            if (profile == null) return;
            customerType = profile.CustomerType;
            correctOmamori = profile.CorrectOmamori;
            if (table != null) affinityTable = table;
        }

        /// <summary>
        /// お守りが命中したときに OmamoriHitResolver から呼ぶ。
        /// 相性判定の結果を返すので、呼び出し側でコンボ/スコア処理に使える。
        /// </summary>
        /// <param name="hitType">当たったお守りの種類</param>
        /// <returns>相性◯/✗の判定結果</returns>
        public Affinity ApplyHit(OmamoriType hitType)
        {
            // 相性テーブル(#12)があれば客タイプで判定。無ければ従来の正解一致で判定。
            // ※ テーブル設定時、correctOmamori は判定に使われない。customerType の設定漏れ
            //   （correctOmamori だけオーバーライド等）は全員 Kenkou 扱いになるので注意。
            Affinity affinity = affinityTable != null
                ? AffinityResolver.Resolve(affinityTable, customerType, hitType)
                : AffinityResolver.Resolve(correctOmamori, hitType);

            // 終端状態（救済成功 / 黒客）では D も R も動かさない（二重判定防止）。
            if (_state == null || !_state.IsActive) return affinity;

            if (affinity == Affinity.Good)
            {
                _state.ApplyCorrectColorHit(); // R を1減らす。D は変えない。R=0 で救済完了。
                onGoodHit?.Invoke();
            }
            else
            {
                _state.ApplyWrongColorHit();   // D も R も変えない（v8変更点2）。
                onBadHit?.Invoke();            // ← 福の連なり C とご加護進捗 G のリセット／渋るリアクション（#14）
            }

            return affinity;
        }
    }
}
