using System.Collections.Generic;
using UnityEngine;

namespace Toufuku.Rescue
{
    /*
        ぜんぶの客のタイプの決まり（CustomerProfile）をまとめたカタログ（#16）

        どういうものか:
          ・「客のタイプ → なやみ → 正解のお守り＋見た目」の一覧と、取り出す入り口
            出す側はこのカタログ1つだけを見れば、客のタイプから
            プロフィール（正解のお守り・仮の見た目）を取れる
          ・相性の表（#12）もここに1つだけ持たせておいて、出すときに
            プロフィールといっしょに客に入れられるようにする（見る場所を1つにまとめる）

        使い方:
          1) Project で右クリック → Create → Toufuku → 客タイプカタログ(CustomerProfileCatalog)
          2) profiles に客のタイプ1つぶんずつ CustomerProfile を登録する（5種類そろえる）
          3) affinityTable に相性の表を入れる
          4) スポナーがこのカタログを見て、Get(type) / GetRandom() で客を作る
    */
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

        // 登録してあるプロフィール（読むだけ用）
        public IReadOnlyList<CustomerProfile> Profiles => profiles;

        // このカタログが持っている相性の表（入っていなければ null）
        public OmamoriAffinityTable AffinityTable => affinityTable;

        // 登録してある数
        public int Count => profiles != null ? profiles.Count : 0;

        // 客のタイプからプロフィールを取る。登録されていなければ null
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

        // 客のタイプからプロフィールを取る（取れたかを bool で返すバージョン）
        public bool TryGet(CustomerType type, out CustomerProfile profile)
        {
            profile = Get(type);
            return profile != null;
        }

        // 登録してあるものからランダムに1つ返す。空なら null。客を出すときのくじに使う
        public CustomerProfile GetRandom()
        {
            return GetRandom(null);
        }

        // #63: 決まったシードの乱数（Toufuku.Playtest.PlaytestRandom.TryFor）でくじを引くバージョン。null なら UnityEngine.Random
        public CustomerProfile GetRandom(Toufuku.Playtest.DeterministicRandom rng)
        {
            if (profiles == null || profiles.Count == 0) return null;

            // null のものはさけてくじを引く
            int valid = 0;
            for (int i = 0; i < profiles.Count; i++)
                if (profiles[i] != null) valid++;
            if (valid == 0) return null;

            int pick = Toufuku.Playtest.PlaytestRandom.Range(rng, 0, valid);
            for (int i = 0; i < profiles.Count; i++)
            {
                if (profiles[i] == null) continue;
                if (pick == 0) return profiles[i];
                pick--;
            }
            return null;
        }

#if UNITY_EDITOR
        /*
            登録もれ・重なり・入っていないものを Inspector でチェックして警告を出す（エディタだけ）
            仕様には関係ない。データを決める作業（#16）でのとりこぼしを防ぐため
        */
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

            // ぜんぶの客のタイプがそろっているか（enum を正しい一覧としてチェックする）
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
