using System;
using System.Collections.Generic;
using UnityEngine;

namespace Toufuku.Rescue
{
    /*
        お守り5種 × 客のタイプ の相性の表（ScriptableObject）（#12）

        仕様:
          健康・学業成就・厄除け安全・縁結び・金運の5種類と客のタイプの相性を「データ」として持つ
          1つ1つのマスは相性◯か✗（Affinity）の2つだけ
          ◯＝その客に効くお守り（突破）、✗＝効かないお守り（まちがい）

        使い方:
          1) Project ウィンドウで右クリック → Create → Toufuku → 相性テーブル(OmamoriAffinityTable)
          2) 作ったアセットを選んで、Inspector の「デフォルト(1:1)で埋める」ボタンで初期化する
             （客のタイプと同じ名前のお守りだけ◯になる基本の形）
          3) 必要なマスを変えて相性を調整する
          4) CustomerRescue にこのアセットを入れる

        作るときのメモ:
          ・客のタイプごとに「◯になるお守りの集まり」を持つ（リストにない＝✗）
            2次元の bool のマス目より編集しやすいし、◯のマスが少ないという前提にも合う
          ・enum の値が増えたり減ったりしても表はこわれない（決まっていない客はふつう✗あつかい）
    */
    [CreateAssetMenu(
        fileName = "OmamoriAffinityTable",
        menuName = "Toufuku/相性テーブル (OmamoriAffinityTable)",
        order = 0)]
    public class OmamoriAffinityTable : ScriptableObject
    {
        // 客のタイプ1つぶんの相性（◯になるお守りの集まり）
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

        /*
            「この客に、このお守りは相性◯か✗か」を返す
            行が登録されていない、または good に入っていないときは Affinity.Bad
        */
        public Affinity GetAffinity(CustomerType customer, OmamoriType hit)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                Row r = rows[i];
                if (r == null || r.customer != customer) continue;
                if (r.goodOmamori != null && r.goodOmamori.Contains(hit))
                    return Affinity.Good;
                return Affinity.Bad; // 行はあるけど◯のリストにない
            }
            return Affinity.Bad; // 行そのものが登録されていない
        }

        // 指定した客のタイプの行を取る（なければ null）
        public Row GetRow(CustomerType customer)
        {
            for (int i = 0; i < rows.Count; i++)
                if (rows[i] != null && rows[i].customer == customer)
                    return rows[i];
            return null;
        }

        /*
            ぜんぶの客のタイプぶんの行を、客のタイプと同じ名前のお守りだけ◯にして初期化する
            今ある行は消えるので注意
        */
        [ContextMenu("デフォルト(1:1)で埋める")]
        public void FillDefaultOneToOne()
        {
            rows.Clear();
            foreach (CustomerType c in (CustomerType[])Enum.GetValues(typeof(CustomerType)))
            {
                var row = new Row { customer = c };
                // 客のタイプと同じ名前のお守りがあれば◯にする（enum の名前で合わせる）
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
