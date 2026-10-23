// ============================================================
//  Editor_NW_SettingsSearch  （Editor フォルダに入れる）
//  メニュー NW → 設定を探す
//  ・自分で書いたスクリプト（シーンのコンポーネント）と ScriptableObject のデータの
//    設定項目を、名前・説明（Tooltip）で探す。例：「スタミナ」「Speed」「Radius」
//  ・探す対象：Assets フォルダにあるスクリプトだけ（Unity やパッケージの物は除く）。
//    「名前の頭」に NW_ などを入れると、その名前で始まるスクリプトだけに絞れる
//  ・見つかった項目はその場で値を変えられる（Ctrl+Z で戻せる）。「選択」でその物を選ぶ
// ============================================================
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public class NW_SettingsSearchWindow : EditorWindow
{
    [MenuItem("NW/設定を探す")]
    static void Open() => GetWindow<NW_SettingsSearchWindow>("設定を探す");

    class Hit
    {
        public Object target;
        public string path;
        public string label;
        public string tooltip;
    }

    string query = "";
    string lastQuery = null;
    bool includeScene = true;
    bool includeAssets = true;
    string prefix = "";
    const string PrefixKey = "NW_SettingsSearch.Prefix";
    Vector2 scroll;
    readonly List<Hit> hits = new List<Hit>();
    readonly Dictionary<Object, SerializedObject> serialized = new Dictionary<Object, SerializedObject>();
    const int MaxHits = 300;

    void OnEnable() => prefix = EditorPrefs.GetString(PrefixKey, "");
    void OnHierarchyChange() => lastQuery = null;
    void OnProjectChange() => lastQuery = null;

    void OnGUI()
    {
        EditorGUILayout.BeginHorizontal();
        GUI.SetNextControlName("NW_Search");
        query = EditorGUILayout.TextField(query, EditorStyles.toolbarSearchField);
        if (GUILayout.Button("探し直す", GUILayout.Width(80))) lastQuery = null;
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.BeginHorizontal();
        includeScene = GUILayout.Toggle(includeScene, "シーンのスクリプト", GUILayout.Width(150));
        includeAssets = GUILayout.Toggle(includeAssets, "データ（ファイル）", GUILayout.Width(150));
        EditorGUILayout.EndHorizontal();
        EditorGUI.BeginChangeCheck();
        prefix = EditorGUILayout.TextField(new GUIContent("名前の頭（空なら全部）", "スクリプトのクラス名がこれで始まる物だけ探す（例：NW_）。空なら Assets にある全部のスクリプト"), prefix);
        if (EditorGUI.EndChangeCheck()) EditorPrefs.SetString(PrefixKey, prefix);
        if (GUI.changed) lastQuery = null;

        string q = query.Trim();
        if (q.Length == 0)
        {
            EditorGUILayout.HelpBox("設定の名前や説明の言葉を入れてください（例：スタミナ、ヒットストップ、会心、Radius）。", MessageType.Info);
            return;
        }
        if (q != lastQuery) Search(q);

        EditorGUILayout.LabelField(hits.Count >= MaxHits ? $"{MaxHits} 件以上（言葉を足して絞ってください）" : $"{hits.Count} 件", EditorStyles.miniLabel);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        Object current = null;
        foreach (var h in hits)
        {
            if (h.target == null) continue;
            if (h.target != current)
            {
                current = h.target;
                EditorGUILayout.Space(6);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(Describe(h.target), EditorStyles.boldLabel);
                if (GUILayout.Button("選択", GUILayout.Width(50)))
                {
                    Selection.activeObject = h.target is Component c ? c.gameObject : h.target;
                    EditorGUIUtility.PingObject(h.target);
                }
                EditorGUILayout.EndHorizontal();
            }
            if (!serialized.TryGetValue(h.target, out var so) || so.targetObject == null)
                serialized[h.target] = so = new SerializedObject(h.target);
            so.Update();
            var prop = so.FindProperty(h.path);
            if (prop == null) continue;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(prop, new GUIContent(h.label, h.tooltip), true);
            if (EditorGUI.EndChangeCheck()) so.ApplyModifiedProperties();
            if (!string.IsNullOrEmpty(h.tooltip)) EditorGUILayout.LabelField("　" + h.tooltip, EditorStyles.wordWrappedMiniLabel);
        }
        EditorGUILayout.EndScrollView();
    }

    static string Describe(Object o)
    {
        if (o is Component c) return $"{c.gameObject.name}（{c.GetType().Name}）";
        return $"{o.name}（{o.GetType().Name}・{AssetDatabase.GetAssetPath(o)}）";
    }

    void Search(string q)
    {
        lastQuery = q;
        hits.Clear();
        serialized.Clear();
        ownScript.Clear();
        var words = q.ToLowerInvariant().Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries);

        var targets = new List<Object>();
        if (includeScene)
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (mb != null && !EditorUtility.IsPersistent(mb) && IsTarget(mb)) targets.Add(mb);
        if (includeAssets)
            foreach (var g in AssetDatabase.FindAssets("t:ScriptableObject"))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (!p.StartsWith("Assets/")) continue;
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(p);
                if (so != null && IsTarget(so)) targets.Add(so);
            }

        foreach (var t in targets)
        {
            var so = new SerializedObject(t);
            var it = so.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = true;
                if (it.propertyPath == "m_Script") { enter = false; continue; }
                string tip = TooltipOf(t.GetType(), it.propertyPath);
                string text = (it.displayName + " " + it.name + " " + tip + " " + t.GetType().Name).ToLowerInvariant();
                bool all = true;
                foreach (var w in words) if (!text.Contains(w)) { all = false; break; }
                if (!all) continue;
                hits.Add(new Hit { target = t, path = it.propertyPath, label = it.displayName, tooltip = tip });
                enter = false;   // 見つかった項目の中身は PropertyField で出るので重ねない
                if (hits.Count >= MaxHits) return;
            }
        }
    }

    readonly Dictionary<Type, bool> ownScript = new Dictionary<Type, bool>();

    // 探す対象か：名前の頭が合い、スクリプトが Assets フォルダにある（Unity・パッケージの物は除く）
    bool IsTarget(Object o)
    {
        var t = o.GetType();
        string pre = prefix.Trim();
        if (pre.Length > 0 && !t.Name.StartsWith(pre)) return false;
        if (!ownScript.TryGetValue(t, out bool own))
        {
            MonoScript ms = o is MonoBehaviour mb ? MonoScript.FromMonoBehaviour(mb)
                          : o is ScriptableObject so ? MonoScript.FromScriptableObject(so) : null;
            string p = ms != null ? AssetDatabase.GetAssetPath(ms) : "";
            ownScript[t] = own = p.StartsWith("Assets/");
        }
        return own;
    }

    static readonly Dictionary<(Type, string), string> tipCache = new Dictionary<(Type, string), string>();

    /// <summary>項目の説明（Tooltip）。入れ子・配列もたどる</summary>
    static string TooltipOf(Type root, string path)
    {
        if (tipCache.TryGetValue((root, path), out var cached)) return cached;
        string result = "";
        Type type = root;
        FieldInfo field = null;
        var parts = path.Replace(".Array.data[", ".[").Split('.');
        foreach (var part in parts)
        {
            if (type == null) break;
            if (part.StartsWith("["))
            {
                // 配列・リストの中身の型へ
                if (type.IsArray) type = type.GetElementType();
                else if (type.IsGenericType) type = type.GetGenericArguments()[0];
                continue;
            }
            field = FindField(type, part);
            if (field == null) { type = null; break; }
            type = field.FieldType;
        }
        if (field != null)
        {
            var a = field.GetCustomAttribute<TooltipAttribute>();
            if (a != null) result = a.tooltip;
            var h = field.GetCustomAttribute<HeaderAttribute>();
            if (h != null) result = string.IsNullOrEmpty(result) ? h.header : h.header + "：" + result;
        }
        tipCache[(root, path)] = result;
        return result;
    }

    static FieldInfo FindField(Type t, string name)
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        for (var x = t; x != null && x != typeof(object); x = x.BaseType)
        {
            var f = x.GetField(name, F);
            if (f != null) return f;
        }
        return null;
    }
}
