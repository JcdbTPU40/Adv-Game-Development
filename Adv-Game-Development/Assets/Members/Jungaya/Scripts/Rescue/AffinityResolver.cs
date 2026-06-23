namespace Toufuku.Rescue
{
    /// <summary>
    /// 「この客に、このお守りは相性◯か✗か」を返す暫定リゾルバ。
    ///
    /// ★ これは #13（救済判定）を単体で動かすための仮実装です。
    ///   正式には #12「お守り5種×客タイプの相性テーブルをScriptableObject化」
    ///   と #16「客タイプ→悩み→正解お守り」の確定後に、
    ///   ScriptableObject 参照に差し替えてください。
    ///   差し替えても CustomerRescue 側は Resolve(...) を呼ぶだけなので影響しません。
    /// </summary>
    public static class AffinityResolver
    {
        /// <summary>
        /// 客が求めているお守り(correctType)と、当たったお守り(hitType)を比較する。
        /// </summary>
        public static Affinity Resolve(OmamoriType correctType, OmamoriType hitType)
        {
            return hitType == correctType ? Affinity.Good : Affinity.Bad;
        }
    }
}
