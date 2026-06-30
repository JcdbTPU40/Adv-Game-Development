using System.Collections.Generic;
using UnityEngine;

namespace Toufuku.Rescue
{
    /// <summary>
    /// 全客タイプの定義(<see cref="CustomerProfile"/>)を束ねるカタログ — Issue #16
    ///
    /// 位置づけ:
    ///   ・「客タイプ → 悩み → 正解お守り＋見た目」の“一覧と引き口”。
    ///     スポーン側はこのカタログ 1 つだけを参照すれば、客タイプから
    ///     プロフィール（正解お守り・プレースホルダー見た目）を引ける。
    ///   ・相性テーブル(#12)もここに 1 本だけ持たせておき、スポーン時に
    ///     プロフィールと一緒に客へ差し込めるようにする（参照点を 1 つに集約）。
    ///
    /// 使い方:
    ///   1) Project で右クリック → Create → Toufuku → 客タイプカタログ(CustomerProfileCatalog)。
    ///   2) profiles に客タイプ1種ぶんの CustomerProfile を登録（5種そろえる）。
    ///   3) affinityTable に相性テーブルを割り当て。
    ///   4) スポナーがこのカタログを参照し、Get(type) / GetRandom() で客を生成する。
    /// </summary>
    [CreateAssetMenu(
        fileName = "CustomerProfileCatalog",
        menuName = "Toufuku/客タイプカタログ (CustomerProfileCatalog)",
        order = 2)]
    public class CustomerProfileCatalog : ScriptableObject
    {
        [Tooltip("登録済みの客タイプ定義。客タイプ1種につき1つを推奨（重複は先勝ち）。")]
        [SerializeField] private List<CustomerProfile> profiles = new List<CustomerProfile>();

        [Tooltip("お守り5種×客タイプの相性テーブル(#12)。スポーン時に客へ一緒に差し込む。")]
        [SerializeField] private OmamoriAffinityTable affinityTable;

        /// <summary>登録済みプロフィール（読み取り用）。</summary>
        public IReadOnlyList<CustomerProfile> Profiles => profiles;

        /// <summary>このカタログが持つ相性テーブル（未設定なら null）。</summary>
        public OmamoriAffinityTable AffinityTable => affinityTable;

        /// <summary>登録数。</summary>
        public int Count => profiles != null ? profiles.Count : 0;

        /// <summary>
        /// 客タイプからプロフィールを引く。未登録なら null。
        /// </summary>
        public CustomerProfile Get(CustomerType type)
        {
            if (profiles == null) return null;
            for (int i = 0; i < profiles.Count; i++)
            {
                CustomerProfile p = profiles[i];
                if (p != null && p.CustomerType == type)
                    return p;
            }
            return null;
        }

        /// <summary>
        /// 客タイプからプロフィールを引く（成否を bool で返す版）。
        /// </summary>
        public bool TryGet(CustomerType type, out CustomerProfile profile)
        {
            profile = Get(type);
            return profile != null;
        }

        /// <summary>
        /// 登録済みからランダムに 1 つ返す。空なら null。スポーンの抽選に使う。
        /// </summary>
        public CustomerProfile GetRandom()
        {
            if (profiles == null || profiles.Count == 0) return null;

            // null 要素を避けて抽選する。
            int valid = 0;
            for (int i = 0; i < profiles.Count; i++)
                if (profiles[i] != null) valid++;
            if (valid == 0) return null;

            int pick = Random.Range(0, valid);
            for (int i = 0; i < profiles.Count; i++)
            {
                if (profiles[i] == null) continue;
                if (pick == 0) return profiles[i];
                pick--;
            }
            return null;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 登録漏れ・重複・未割り当てを Inspector 上でチェックして警告する（エディタ専用）。
        /// 仕様には影響しない。データ確定作業(#16)の取りこぼし防止用。
        /// </summary>
        [ContextMenu("登録内容を検証 (Validate)")]
        public void ValidateCatalog()
        {
            var seen = new HashSet<CustomerType>();

            if (profiles != null)
            {
                for (int i = 0; i < profiles.Count; i++)
                {
                    CustomerProfile p = profiles[i];
                    if (p == null)
                    {
                        Debug.LogWarning($"[Catalog] profiles[{i}] が未割り当て(None)です。", this);
                        continue;
                    }
                    if (!seen.Add(p.CustomerType))
                        Debug.LogWarning($"[Catalog] 客タイプ {p.CustomerType} が重複登録されています（先勝ちで解決）。", this);
                }
            }

            // 全客タイプがそろっているか（enum を正本にチェック）。
            foreach (CustomerType c in (CustomerType[])System.Enum.GetValues(typeof(CustomerType)))
                if (!seen.Contains(c))
                    Debug.LogWarning($"[Catalog] 客タイプ {c} の CustomerProfile が未登録です。", this);

            if (affinityTable == null)
                Debug.LogWarning("[Catalog] affinityTable が未設定です（相性テーブル未割り当て）。", this);

            Debug.Log($"[Catalog] 検証完了: 登録 {Count} 件 / 客タイプ {seen.Count} 種。", this);
        }
#endif
    }
}
