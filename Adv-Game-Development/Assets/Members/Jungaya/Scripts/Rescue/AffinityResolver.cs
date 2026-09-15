namespace Toufuku.Rescue
{
    /*
        「この客に、このお守りは相性◯か✗か」を返すクラス

        #12 で OmamoriAffinityTable（ScriptableObject）を入れたので、
        相性は基本的に表を見て決める。表が入っていない場所や古いコードのために、
        「正解のお守りと同じか」だけを見る前のバージョンのオーバーロードも残してある
        呼ぶ側は Resolve(...) を呼ぶだけなので、表があってもなくても書きかえなくていい
    */
    public static class AffinityResolver
    {
        /*
            【前のバージョン・予備】客がほしいお守り（correctType）と、
            当たったお守り（hitType）をそのままくらべる。表がないときに使う
        */
        public static Affinity Resolve(OmamoriType correctType, OmamoriType hitType)
        {
            return hitType == correctType ? Affinity.Good : Affinity.Bad;
        }

        /*
            【#12 のおすすめ】相性の表（ScriptableObject）を見て、
            客のタイプ（customer）と当たったお守り（hitType）の相性を返す
            table が null のときは、安全なほうに倒して Affinity.Bad を返す
        */
        public static Affinity Resolve(OmamoriAffinityTable table, CustomerType customer, OmamoriType hitType)
        {
            if (table == null) return Affinity.Bad;
            return table.GetAffinity(customer, hitType);
        }
    }
}
