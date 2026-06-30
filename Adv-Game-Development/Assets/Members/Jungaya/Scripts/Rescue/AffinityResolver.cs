namespace Toufuku.Rescue
{
    /// <summary>
    /// 「この客に、このお守りは相性◯か✗か」を返すリゾルバ。
    ///
    /// #12 で <see cref="OmamoriAffinityTable"/>（ScriptableObject）を導入したため、
    /// 相性は原則テーブル参照で解決する。テーブル未設定の現場や旧コードのために、
    /// 「正解お守りと一致するか」だけを見る従来版オーバーロードも残してある。
    /// 呼び出し側は Resolve(...) を呼ぶだけなので、テーブル有無で書き換える必要はない。
    /// </summary>
    public static class AffinityResolver
    {
        /// <summary>
        /// 【従来版・フォールバック】客が求めているお守り(correctType)と、
        /// 当たったお守り(hitType)を直接比較する。テーブルが無い場合に使う。
        /// </summary>
        public static Affinity Resolve(OmamoriType correctType, OmamoriType hitType)
        {
            return hitType == correctType ? Affinity.Good : Affinity.Bad;
        }

        /// <summary>
        /// 【#12 推奨】相性テーブル(ScriptableObject)を参照して、
        /// 客タイプ(customer)と当たったお守り(hitType)の相性を返す。
        /// table が null の場合は安全側で <see cref="Affinity.Bad"/> を返す。
        /// </summary>
        public static Affinity Resolve(OmamoriAffinityTable table, CustomerType customer, OmamoriType hitType)
        {
            if (table == null) return Affinity.Bad;
            return table.GetAffinity(customer, hitType);
        }
    }
}
