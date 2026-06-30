using System;
using System.Collections.Generic;
using UnityEngine;

namespace Toufuku.Rescue
{
    /// <summary>
    /// お守り5種 × 客タイプ の相性テーブル（ScriptableObject）— Issue #12
    ///
    /// 仕様:
    ///   健康・学業成就・厄除け安全・縁結び・金運の5種と客タイプの相性を「データ」として保持する。
    ///   各マスは相性◯/✗（<see cref="Affinity"/>）の2値。
    ///   ◯＝その客に効くお守り（突破）、✗＝効かないお守り（誤投擲）。
    ///
    /// 使い方:
    ///   1) Project ウィンドウで右クリック → Create → Toufuku → 相性テーブル(OmamoriAffinityTable)
    ///   2) 生成したアセットを選択し、Inspector の「デフォルト(1:1)で埋める」ボタンで初期化
    ///      （客タイプと同名のお守りだけ◯になる基本形）
    ///   3) 必要なマスを編集して相性を調整
    ///   4) CustomerRescue にこのアセットを割り当てる
    ///
    /// 設計メモ:
    ///   ・客タイプごとに「◯になるお守りの集合」を持つ（リストに無い＝✗）。
    ///     2次元 bool グリッドより編集が直感的で、◯セルが少ない前提に合う。
    ///   ・enum の値が増減してもテーブルは壊れない（未定義の客は既定で✗扱い）。
    /// </summary>
    [CreateAssetMenu(
        fileName = "OmamoriAffinityTable",
        menuName = "Toufuku/相性テーブル (OmamoriAffinityTable)",
        order = 0)]
    public class OmamoriAffinityTable : ScriptableObject
    {
        /// <summary>1客タイプぶんの相性（◯になるお守りの集合）。</summary>
        [Serializable]
        public class Row
        {
            [Tooltip("この行が表す客タイプ。")]
            public CustomerType customer;

            [Tooltip("この客に相性◯のお守り。ここに無いお守りは✗扱い。")]
            public List<OmamoriType> goodOmamori = new List<OmamoriType>();
        }

        [Tooltip("客タイプごとの相性行。客タイプ1種につき1行を推奨。")]
        [SerializeField] private List<Row> rows = new List<Row>();

        /// <summary>
        /// 「この客に、このお守りは相性◯か✗か」を返す。
        /// 行が未登録／goodに含まれない場合は <see cref="Affinity.Bad"/>。
        /// </summary>
        public Affinity GetAffinity(CustomerType customer, OmamoriType hit)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                Row r = rows[i];
                if (r == null || r.customer != customer) continue;
                if (r.goodOmamori != null && r.goodOmamori.Contains(hit))
                    return Affinity.Good;
                return Affinity.Bad; // 行はあるが◯リストに無い
            }
            return Affinity.Bad; // 行そのものが未登録
        }

        /// <summary>指定客タイプの行を取得（無ければ null）。</summary>
        public Row GetRow(CustomerType customer)
        {
            for (int i = 0; i < rows.Count; i++)
                if (rows[i] != null && rows[i].customer == customer)
                    return rows[i];
            return null;
        }

        /// <summary>
        /// 全客タイプぶんの行を、客タイプと同名のお守りだけ◯にして初期化する。
        /// 既存の行はクリアされるので注意。
        /// </summary>
        [ContextMenu("デフォルト(1:1)で埋める")]
        public void FillDefaultOneToOne()
        {
            rows.Clear();
            foreach (CustomerType c in (CustomerType[])Enum.GetValues(typeof(CustomerType)))
            {
                var row = new Row { customer = c };
                // 客タイプと同名のお守りがあれば◯にする（enum名で対応付け）。
                if (Enum.TryParse(c.ToString(), out OmamoriType matched))
                    row.goodOmamori.Add(matched);
                rows.Add(row);
            }
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
    }
}
