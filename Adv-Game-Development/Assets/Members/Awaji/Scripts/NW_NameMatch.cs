using System.Collections.Generic;
using UnityEngine;

// =============================================================
//  NW_NameMatch.cs
//  名前で骨・メッシュを探し直すときの手伝い（揺れ方データ・モデル差し替えで使う）
//  ・Maya で名前に前置き（例：「C_BB_」）を付けた／外したモデルでも、同じ物として見つける
//      Back_Hair  ⇔  C_BB_Back_Hair
//      Kari:X_Hair_G  ⇔  Kari:X_C_BB_Hair_G（途中に入っても OK）
//  ・前置きは、見つからなかった名前どうしを比べて自動で見つける（3つ以上同じ前置きがあるときだけ）
// =============================================================

public static class NW_NameMatch
{
    /// <summary>
    /// 探したい名前（wanted）とモデルにある名前（have）から、付いた／外れた前置きを見つける。無ければ ""。
    /// added = true：have の方に付いている（新しいモデルで付けた）、false：wanted の方に付いている（外した）
    /// </summary>
    public static string FindPrefix(ICollection<string> wanted, ICollection<string> have, out bool added)
    {
        added = true;
        var haveSet = new HashSet<string>(have);
        var wantSet = new HashSet<string>(wanted);
        var lostW = new List<string>();
        foreach (var w in wantSet) if (!string.IsNullOrEmpty(w) && !haveSet.Contains(w)) lostW.Add(w);
        var lostH = new List<string>();
        foreach (var h in haveSet) if (!string.IsNullOrEmpty(h) && !wantSet.Contains(h)) lostH.Add(h);
        if (lostW.Count == 0 || lostH.Count == 0) return "";

        var addCount = new Dictionary<string, int>();
        var remCount = new Dictionary<string, int>();
        foreach (var w in lostW)
            foreach (var h in lostH)
            {
                if (h.Length > w.Length && h.EndsWith(w)) Tally(addCount, h.Substring(0, h.Length - w.Length));
                else if (w.Length > h.Length && w.EndsWith(h)) Tally(remCount, w.Substring(0, w.Length - h.Length));
            }
        string best = ""; int bestN = 0;
        foreach (var kv in addCount) if (kv.Value > bestN) { best = kv.Key; bestN = kv.Value; added = true; }
        foreach (var kv in remCount) if (kv.Value > bestN) { best = kv.Key; bestN = kv.Value; added = false; }
        if (bestN < 3) return "";
        return best;
    }

    // 前置きは「_」か「:」で終わるものだけ（L_Hair と R_L_Hair のような偶然を数えない）
    static void Tally(Dictionary<string, int> d, string prefix)
    {
        if (prefix.Length < 2) return;
        char last = prefix[prefix.Length - 1];
        if (last != '_' && last != ':') return;
        d.TryGetValue(prefix, out int n);
        d[prefix] = n + 1;
    }

    /// <summary>name の中の prefix を1か所だけ取る（無ければそのまま）</summary>
    public static string RemoveOnce(string name, string prefix)
    {
        if (string.IsNullOrEmpty(prefix) || string.IsNullOrEmpty(name)) return name;
        int i = name.IndexOf(prefix);
        return i < 0 ? name : name.Remove(i, prefix.Length);
    }

    /// <summary>
    /// 名前の前置きの違いを吸収して探す表。Find(古い名前) で新しい物が返る
    /// </summary>
    public class Lookup<T> where T : Object
    {
        readonly Dictionary<string, T> exact = new Dictionary<string, T>();
        readonly Dictionary<string, T> loose = new Dictionary<string, T>();
        readonly HashSet<string> looseDup = new HashSet<string>();
        public string Prefix { get; private set; } = "";
        public bool Added { get; private set; } = true;

        readonly HashSet<string> wantedSet;

        public Lookup(IEnumerable<T> items, ICollection<string> wanted)
        {
            wantedSet = new HashSet<string>(wanted);
            var names = new List<string>();
            foreach (var t in items)
            {
                if (t == null) continue;
                if (!exact.ContainsKey(t.name)) exact[t.name] = t;
                names.Add(t.name);
            }
            Prefix = FindPrefix(wanted, names, out bool added);
            Added = added;
            if (Prefix.Length == 0) return;
            // 前置きを付けた：モデル側の名前から前置きを取った名前でも引けるようにする
            // 前置きを外した：探す名前の方から前置きを取って引く（Find の中）
            if (!added) return;
            foreach (var t in items)
            {
                if (t == null) continue;
                string k = RemoveOnce(t.name, Prefix);
                if (k == t.name) continue;
                if (loose.ContainsKey(k)) looseDup.Add(k); else loose[k] = t;
            }
        }

        public T Find(string n)
        {
            if (string.IsNullOrEmpty(n)) return null;
            var t = FindOne(n);
            if (t != null) return t;
            // 最後の数字だけ消えた名前（Ribon1 → Ribon）も探す
            string trimmed = n.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            return trimmed.Length > 0 && trimmed != n && !wantedSet.Contains(trimmed) ? FindOne(trimmed) : null;
        }

        T FindOne(string n)
        {
            if (exact.TryGetValue(n, out var t)) return t;
            if (Prefix.Length == 0) return null;
            if (Added) return !looseDup.Contains(n) && loose.TryGetValue(n, out t) ? t : null;
            string k = RemoveOnce(n, Prefix);
            return k != n && exact.TryGetValue(k, out t) ? t : null;
        }
    }
}
