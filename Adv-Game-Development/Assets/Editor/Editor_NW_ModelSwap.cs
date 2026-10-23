using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// =====================================================================
//  NW/モデル差し替え ウィンドウ（Editor フォルダに置く）
//   キャラのモデルを新しくしたとき（04 → 05 など）、あちこちで指定している
//   モデルの参照を1回でまとめて付け替える。
//
//   1. 新しいモデルの Import 設定：Rig を Humanoid（Create From This Model）に。
//      倍率・ブレンドシェイプは今のモデルと同じにする
//   2. シーンのモデル差し替え：
//      ・新しいモデルを今のモデルと同じ親・位置・向き・大きさに置く
//      ・今のモデルと新しいモデルの骨・メッシュを 名前（同じ親の下→全体）→ Humanoid の骨 の順で対応づける
//      ・今のモデルに付けたスクリプト（揺れもの・視線など何でも）を、対応する所へ設定ごとコピー
//      ・Animator の Controller・Apply Root Motion・Update Mode・Culling Mode を引き継ぐ（Avatar は新しいモデルのもの）
//      ・マテリアルを引き継ぐ（同じ名前のメッシュに、前から順に）
//      ・揺れもののコライダーなど、あとから足したオブジェクトは丸ごとコピー
//      ・シーン中のスクリプトの参照（操作スクリプトの Animator、カメラの注視先、揺れものの骨・コライダー、
//        頭の骨 など）を、今のモデルの物から新しいモデルの物へ全部付け替える
//        （スクリプト名を決め打ちしていないので、あとで増えたスクリプトにも効く）
//      ・今のモデルは「非表示にして残す（名前に (旧)）」か「削除」。Ctrl+Z 1回で全部戻せる
//   3. モーションの付け替え：Copy From Other Avatar で今のモデルの Avatar を使っている FBX を全部探し、
//      新しいモデルの Avatar に付け替えて再インポート（これは Ctrl+Z では戻らない）
// =====================================================================
public class NW_ModelSwap : EditorWindow
{
    [MenuItem("NW/モデル差し替え")]
    static void Open() => GetWindow<NW_ModelSwap>("モデル差し替え");

    enum OldModelAction { Hide, Delete }
    static readonly string[] OldActionLabels = { "非表示にして残す（名前に (旧)）", "削除する" };

    GameObject _oldModel;
    GameObject _newModel;
    string _newName = "";
    bool _copyMaterials = true;
    bool _copyLayerTag = true;
    OldModelAction _oldAction = OldModelAction.Hide;

    Avatar _oldAvatar;
    Avatar _newAvatar;
    class MotionItem { public string path; public bool on = true; public bool done; }
    readonly List<MotionItem> _motions = new List<MotionItem>();
    bool _motionsScanned;

    class Report
    {
        public string title;
        public int matched;
        public readonly List<string> copied = new List<string>();
        public readonly List<string> cloned = new List<string>();
        public readonly List<string> missing = new List<string>();
        public readonly List<string> refs = new List<string>();
        public readonly List<string> leftRefs = new List<string>();
        public readonly List<string> notes = new List<string>();
        public bool foldCopied, foldCloned, foldMissing = true, foldRefs, foldLeft = true;
    }
    Report _report;
    Vector2 _scroll;

    // =====================================================================
    // 画面
    // =====================================================================
    void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.HelpBox(
            "入れ替え元（今のモデル）と入れ替え先（新しいモデル）をドラッグ＆ドロップで入れると、各スクリプトで指定しているモデル（Animator・骨・メッシュ）をまとめて付け替えます。\n" +
            "上から順に 1 → 2 → 3。2 は Ctrl+Z 1回で全部戻せます。", MessageType.Info);

        // ---- モデルの指定 ----
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("モデル", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        _oldModel = (GameObject)EditorGUILayout.ObjectField(new GUIContent("入れ替え元（シーン）", "シーンに置いてある今のモデル（Animator の付いたオブジェクト）を Hierarchy からドラッグ＆ドロップ"), _oldModel, typeof(GameObject), true);
        _newModel = (GameObject)EditorGUILayout.ObjectField(new GUIContent("入れ替え先", "新しいモデルの FBX（Project）か、シーンに置いた新しいモデルをドラッグ＆ドロップ"), _newModel, typeof(GameObject), true);
        if (EditorGUI.EndChangeCheck())
        {
            if (_oldModel != null && EditorUtility.IsPersistent(_oldModel))
            {
                ShowNotification(new GUIContent("今のモデルはシーンに置いてある物を入れてください"));
                _oldModel = null;
            }
            if (_oldModel != null) _oldAvatar = AvatarOf(_oldModel);
            if (_newModel != null) _newAvatar = AvatarOf(_newModel);
            _report = null;
            _motionsScanned = false;
        }
        if (_oldModel != null && _newModel != null && (_newModel == _oldModel || _newModel.transform.IsChildOf(_oldModel.transform)))
            EditorGUILayout.HelpBox("新しいモデルに今のモデル（またはその中の物）が入っています。", MessageType.Error);

        // ---- 1. Import 設定 ----
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("1. 新しいモデルの Import 設定（Rig）", EditorStyles.boldLabel);
        DrawImportSection();

        // ---- 2. シーンの差し替え ----
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("2. シーンのモデル差し替え", EditorStyles.boldLabel);
        DrawSwapSection();

        // ---- 3. モーション ----
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("3. モーションの Copy From Other Avatar を付け替え", EditorStyles.boldLabel);
        DrawMotionSection();

        // ---- 結果 ----
        if (_report != null)
        {
            EditorGUILayout.Space();
            DrawReport(_report);
        }

        EditorGUILayout.EndScrollView();
    }

    void DrawImportSection()
    {
        var imp = _newModel != null ? ImporterOf(_newModel) : null;
        if (imp == null)
        {
            EditorGUILayout.HelpBox(_newModel == null
                ? "新しいモデルを入れると状態を表示します。"
                : "新しいモデルの FBX が見つかりません（シーンだけの物なら、この手順は飛ばしてかまいません）。", MessageType.None);
            return;
        }
        bool ok = imp.animationType == ModelImporterAnimationType.Human && imp.avatarSetup == ModelImporterAvatarSetup.CreateFromThisModel;
        var av = AvatarOf(_newModel);
        string state = "Animation Type：" + imp.animationType + "　Avatar：" +
                       (av == null ? "なし" : (av.isHuman && av.isValid ? "Humanoid（OK）" : "Humanoid になっていない"));
        EditorGUILayout.LabelField(state, EditorStyles.miniLabel);
        if (!ok) EditorGUILayout.HelpBox("Rig が Humanoid（Create From This Model）になっていません。", MessageType.Warning);
        using (new EditorGUI.DisabledScope(ok && av != null && av.isHuman && av.isValid))
        {
            if (GUILayout.Button("Rig を Humanoid（Create From This Model）にする"))
            {
                SetupNewImport(imp);
                GUIUtility.ExitGUI();
            }
        }
        EditorGUILayout.HelpBox("骨の割り当ては自動です。手足がおかしいときは FBX の Rig → Configure で確認してください。", MessageType.None);
    }

    void DrawSwapSection()
    {
        _newName = EditorGUILayout.TextField(new GUIContent("新しいモデルの名前", "空ならファイル名のまま"), _newName);
        _copyMaterials = EditorGUILayout.Toggle(new GUIContent("マテリアルを引き継ぐ", "同じ名前のメッシュに、今のマテリアル（NW/CharToon など）を前から順に入れる"), _copyMaterials);
        _copyLayerTag = EditorGUILayout.Toggle(new GUIContent("タグ・レイヤーを引き継ぐ"), _copyLayerTag);
        _oldAction = (OldModelAction)EditorGUILayout.Popup("今のモデルは", (int)_oldAction, OldActionLabels);

        bool ready = _oldModel != null && _newModel != null && _newModel != _oldModel && !_newModel.transform.IsChildOf(_oldModel.transform);
        using (new EditorGUI.DisabledScope(!ready))
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("確認する（まだ変えない）", GUILayout.Height(26))) _report = Preview();
            if (GUILayout.Button("差し替える", GUILayout.Height(26)))
            {
                if (EditorUtility.DisplayDialog("モデル差し替え",
                        "「" + _oldModel.name + "」を「" + _newModel.name + "」に差し替えます。\nCtrl+Z 1回で元に戻せます。", "差し替える", "やめる"))
                    _report = Apply();
                GUIUtility.ExitGUI();
            }
        }
    }

    void DrawMotionSection()
    {
        _oldAvatar = (Avatar)EditorGUILayout.ObjectField(new GUIContent("今の Avatar", "モーションの FBX が Copy From Other Avatar で使っている Avatar（今のモデルの物）"), _oldAvatar, typeof(Avatar), false);
        _newAvatar = (Avatar)EditorGUILayout.ObjectField(new GUIContent("新しい Avatar", "新しいモデルの Avatar"), _newAvatar, typeof(Avatar), false);
        if (_newAvatar != null && !(_newAvatar.isHuman && _newAvatar.isValid))
            EditorGUILayout.HelpBox("新しい Avatar が Humanoid になっていません（先に 1 を）。", MessageType.Warning);

        using (new EditorGUI.DisabledScope(_oldAvatar == null))
        {
            if (GUILayout.Button("今の Avatar を使っているモーションを探す")) ScanMotions();
        }
        if (!_motionsScanned) return;
        if (_motions.Count == 0)
        {
            EditorGUILayout.HelpBox("今の Avatar を使っている FBX はありません。", MessageType.None);
            return;
        }
        foreach (var m in _motions)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(m.done))
                    m.on = EditorGUILayout.ToggleLeft(m.path, m.on && !m.done);
                if (m.done) GUILayout.Label("付け替え済み", EditorStyles.miniLabel, GUILayout.Width(70));
            }
        }
        bool any = _motions.Any(x => x.on && !x.done);
        using (new EditorGUI.DisabledScope(!any || _newAvatar == null || _newAvatar == _oldAvatar))
        {
            if (GUILayout.Button("チェックしたモーションを新しい Avatar に付け替える", GUILayout.Height(26)))
            {
                int n = _motions.Count(x => x.on && !x.done);
                if (EditorUtility.DisplayDialog("モーションの付け替え",
                        n + " 本の FBX の Source（Copy From Other Avatar）を「" + _newAvatar.name + "」にして再インポートします。\n" +
                        "これは Ctrl+Z では戻せません（戻すときは今の Avatar を「新しい Avatar」に入れてもう一度）。", "付け替える", "やめる"))
                    RetargetMotions();
                GUIUtility.ExitGUI();
            }
        }
    }

    void DrawReport(Report r)
    {
        EditorGUILayout.LabelField(r.title, EditorStyles.boldLabel);
        if (r.matched > 0) EditorGUILayout.LabelField("対応した骨・メッシュ：" + r.matched + " 個");
        foreach (var n in r.notes) EditorGUILayout.HelpBox(n, MessageType.None);
        r.foldRefs = List("付け替える参照", r.refs, r.foldRefs, MessageType.None);
        r.foldCopied = List("コピーするスクリプト・設定", r.copied, r.foldCopied, MessageType.None);
        r.foldCloned = List("丸ごとコピーするオブジェクト（あとから足した物）", r.cloned, r.foldCloned, MessageType.None);
        r.foldMissing = List("新しいモデルに見つからない物（そのまま）", r.missing, r.foldMissing, MessageType.Warning);
        r.foldLeft = List("付け替え先が無い参照（今のモデルを指したまま）", r.leftRefs, r.foldLeft, MessageType.Warning);
    }

    static bool List(string label, List<string> items, bool fold, MessageType type)
    {
        if (items.Count == 0) return fold;
        fold = EditorGUILayout.Foldout(fold, label + "（" + items.Count + "）", true);
        if (!fold) return fold;
        EditorGUI.indentLevel++;
        int shown = Mathf.Min(items.Count, 300);
        for (int i = 0; i < shown; i++)
        {
            if (type == MessageType.Warning)
            {
                var c = GUI.color; GUI.color = new Color(1f, 0.85f, 0.5f);
                EditorGUILayout.LabelField(items[i], EditorStyles.wordWrappedMiniLabel);
                GUI.color = c;
            }
            else EditorGUILayout.LabelField(items[i], EditorStyles.wordWrappedMiniLabel);
        }
        if (items.Count > shown) EditorGUILayout.LabelField("…ほか " + (items.Count - shown) + " 件", EditorStyles.miniLabel);
        EditorGUI.indentLevel--;
        return fold;
    }

    // =====================================================================
    // 1. Import 設定
    // =====================================================================
    void SetupNewImport(ModelImporter imp)
    {
        var oldImp = _oldModel != null ? ImporterOf(_oldModel) : null;
        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        if (oldImp != null && oldImp != imp)
        {
            imp.globalScale = oldImp.globalScale;
            imp.useFileScale = oldImp.useFileScale;
            imp.importBlendShapes = oldImp.importBlendShapes;
        }
        imp.SaveAndReimport();
        _newAvatar = AvatarOf(_newModel);

        _report = new Report { title = "Import 設定の結果" };
        if (_newAvatar != null && _newAvatar.isHuman && _newAvatar.isValid)
            _report.notes.Add("Rig を Humanoid にしました。Avatar：" + _newAvatar.name);
        else
            _report.notes.Add("Humanoid の Avatar が作れませんでした。FBX の Rig → Configure で骨の割り当てを確認してください。");
    }

    // =====================================================================
    // 2. 差し替え：対応づけ
    // =====================================================================
    class Plan
    {
        public Transform oldRoot, newRoot;
        public readonly Dictionary<Transform, Transform> map = new Dictionary<Transform, Transform>();
        public readonly List<Transform> clones = new List<Transform>();   // 丸ごとコピーする古い側のオブジェクト（親は map にある）
        public readonly List<Transform> missing = new List<Transform>();
        // Maya で名前に付けた／外した前置き（例：C_BB_）。無ければ ""
        public string prefix = "";
        public bool prefixAdded = true;

        /// <summary>新しい側の名前 nn と古い側の名前 on が同じ物か（前置きの違いは無視）</summary>
        public bool Same(string nn, string on)
        {
            if (nn == on) return true;
            if (prefix.Length == 0) return false;
            return prefixAdded ? NW_NameMatch.RemoveOnce(nn, prefix) == on : NW_NameMatch.RemoveOnce(on, prefix) == nn;
        }

        /// <summary>古い側の名前を、新しい側で探すときの名前に直す</summary>
        public string KeyOfNew(string nn) => prefix.Length > 0 && prefixAdded ? NW_NameMatch.RemoveOnce(nn, prefix) : nn;
        public string KeyOfOld(string on) => prefix.Length > 0 && !prefixAdded ? NW_NameMatch.RemoveOnce(on, prefix) : on;
    }

    static Plan MakePlan(Transform oldRoot, Transform newRoot)
    {
        var p = new Plan { oldRoot = oldRoot, newRoot = newRoot };
        // Maya で名前に前置き（C_BB_ など）を付けた／外したモデルでも同じ物として対応づける
        var oldNames = oldRoot.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToList();
        var newNames = newRoot.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToList();
        p.prefix = NW_NameMatch.FindPrefix(oldNames, newNames, out p.prefixAdded);
        var byName = new Dictionary<string, List<Transform>>();
        foreach (var t in newRoot.GetComponentsInChildren<Transform>(true))
        {
            foreach (var key in p.KeyOfNew(t.name) == t.name ? new[] { t.name } : new[] { t.name, p.KeyOfNew(t.name) })
            {
                if (!byName.TryGetValue(key, out var l)) byName[key] = l = new List<Transform>();
                l.Add(t);
            }
        }
        var oldHuman = HumanBones(oldRoot.GetComponent<Animator>());
        var newAnim = newRoot.GetComponent<Animator>();
        var used = new HashSet<Transform> { newRoot };
        p.map[oldRoot] = newRoot;

        // 1回目：同じ親の下の同じ名前だけで、モデル全体を先に対応づける
        //  （先に名前だけで探すと、同じ名前のメッシュと骨（リボンなど）を取り違えて骨が抜けることがあるため）
        MatchByPath(p, oldRoot, newRoot, used);

        // 2回目：残った物を モデル全体で同じ名前（1つだけ・骨/メッシュの種類も同じ）→ Humanoid の同じ骨 で探す
        var olds = oldRoot.GetComponentsInChildren<Transform>(true);
        foreach (var o in olds)
        {
            if (p.map.ContainsKey(o)) continue;
            Transform nc = null;
            if (byName.TryGetValue(o.name, out var l) || byName.TryGetValue(p.KeyOfOld(o.name), out l))
            {
                Transform only = null; int count = 0;
                foreach (var x in l)
                    if (!used.Contains(x) && IsMesh(x) == IsMesh(o)) { only = x; count++; }
                if (count == 1) nc = only;
            }
            if (nc == null && newAnim != null && newAnim.isHuman && oldHuman.TryGetValue(o, out var hb))
            {
                var t = newAnim.GetBoneTransform(hb);
                if (t != null && !used.Contains(t)) nc = t;
            }
            if (nc == null) continue;
            p.map[o] = nc;
            used.Add(nc);
            MatchByPath(p, o, nc, used);
        }

        // 残り：あとから足した物は丸ごとコピー、それ以外は見つからない物
        foreach (var o in olds)
        {
            if (o == oldRoot || p.map.ContainsKey(o)) continue;
            if (p.clones.Any(c => o.IsChildOf(c))) continue;
            if (p.map.ContainsKey(o.parent) && IsExtra(o)) p.clones.Add(o);
            else p.missing.Add(o);
        }
        return p;
    }

    static void MatchByPath(Plan p, Transform o, Transform n, HashSet<Transform> used)
    {
        foreach (Transform oc in o)
        {
            if (p.map.ContainsKey(oc)) continue;
            Transform nc = null;
            foreach (Transform c in n)
                if (c.name == oc.name && !used.Contains(c)) { nc = c; break; }
            if (nc == null)
                foreach (Transform c in n)
                    if (p.Same(c.name, oc.name) && !used.Contains(c)) { nc = c; break; }
            if (nc == null) continue;
            p.map[oc] = nc;
            used.Add(nc);
            MatchByPath(p, oc, nc, used);
        }
    }

    static bool IsMesh(Transform t) => t.GetComponent<Renderer>() != null;

    static Dictionary<Transform, HumanBodyBones> HumanBones(Animator a)
    {
        var d = new Dictionary<Transform, HumanBodyBones>();
        if (a == null || !a.isHuman) return d;
        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
        {
            var t = a.GetBoneTransform((HumanBodyBones)i);
            if (t != null && !d.ContainsKey(t)) d[t] = (HumanBodyBones)i;
        }
        return d;
    }

    // あとから足したオブジェクト（揺れもののコライダーなど）か。モデルの FBX に元からある物は false
    static bool IsExtra(Transform t)
    {
        var go = t.gameObject;
        if (go.name.StartsWith("NW_SC_")) return true;
        if (PrefabUtility.IsAddedGameObjectOverride(go)) return true;
        var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(go);
        if (src != null && src != go && PrefabUtility.GetPrefabAssetType(src) == PrefabAssetType.Model) return false;
        return go.GetComponent<MonoBehaviour>() != null && go.GetComponent<Renderer>() == null;
    }

    // =====================================================================
    // 2. 確認（何も変えない）
    // =====================================================================
    Report Preview()
    {
        var r = new Report { title = "確認の結果（まだ何も変えていません）" };
        GameObject tmp = null;
        Transform nt;
        if (EditorUtility.IsPersistent(_newModel))
        {
            tmp = (GameObject)Instantiate(_newModel);
            tmp.hideFlags = HideFlags.HideAndDontSave;
            nt = tmp.transform;
        }
        else nt = _newModel.transform;

        try
        {
            var oldT = _oldModel.transform;
            var p = MakePlan(oldT, nt);
            r.matched = p.map.Count;
            if (p.prefix.Length > 0) r.notes.Add($"名前の前置き「{p.prefix}」を{(p.prefixAdded ? "付けた" : "外した")}モデルとして対応づけました");
            foreach (var c in p.clones) r.cloned.Add(PathIn(c, oldT));
            foreach (var m in p.missing) r.missing.Add(PathIn(m, oldT));

            foreach (var kv in p.map)
            {
                var counts = new Dictionary<Type, int>();
                foreach (var oc in kv.Key.GetComponents<Component>())
                {
                    if (oc == null || oc is Transform) continue;
                    var type = oc.GetType();
                    counts.TryGetValue(type, out int idx); counts[type] = idx + 1;
                    bool exists = idx < kv.Value.GetComponents(type).Count(x => x.GetType() == type);
                    string where = PathIn(kv.Key, oldT);
                    if (oc is Animator) r.copied.Add(where + "：Animator の設定（Controller・Apply Root Motion など）");
                    else if (oc is Renderer) { if (_copyMaterials && ((Renderer)oc).sharedMaterials.Length > 0) r.copied.Add(where + "：マテリアル"); }
                    else if (!exists) r.copied.Add(where + "：" + type.Name + "（新しく付ける）");
                    else if (oc is MonoBehaviour) r.copied.Add(where + "：" + type.Name + "（設定を上書き）");
                }
            }

            foreach (var c in SceneComponents(oldT))
            {
                var so = new SerializedObject(c);
                var it = so.GetIterator();
                while (it.Next(true))
                {
                    if (!IsUserRef(it)) continue;
                    var v = it.objectReferenceValue;
                    if (v == null || !IsInside(v, oldT)) continue;
                    var vt = v is GameObject g ? g.transform : ((Component)v).transform;
                    bool ok = p.map.ContainsKey(vt) || p.clones.Any(x => vt.IsChildOf(x));
                    (ok ? r.refs : r.leftRefs).Add(RefLabel(c, it, v));
                }
            }
            if (r.refs.Count > 0) r.foldRefs = true;
        }
        finally
        {
            if (tmp != null) DestroyImmediate(tmp);
        }
        return r;
    }

    // =====================================================================
    // 2. 差し替え（本番）
    // =====================================================================
    Report Apply()
    {
        var r = new Report { title = "差し替えの結果" };
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("NW モデル差し替え");

        var oldGo = _oldModel;
        var oldT = oldGo.transform;
        if (_oldAvatar == null) _oldAvatar = AvatarOf(oldGo);

        // ---- 新しいモデルを置く ----
        GameObject ng;
        if (EditorUtility.IsPersistent(_newModel))
        {
            ng = PrefabUtility.InstantiatePrefab(_newModel, oldGo.scene) as GameObject;
            if (ng == null) ng = (GameObject)Instantiate(_newModel);
            Undo.RegisterCreatedObjectUndo(ng, "NW モデル差し替え");
        }
        else ng = _newModel;
        var nt = ng.transform;
        if (nt.parent != oldT.parent) Undo.SetTransformParent(nt, oldT.parent, "NW モデル差し替え");
        Undo.RecordObject(nt, "NW モデル差し替え");
        nt.localPosition = oldT.localPosition;
        nt.localRotation = oldT.localRotation;
        nt.localScale = oldT.localScale;
        nt.SetSiblingIndex(oldT.GetSiblingIndex() + 1);
        Undo.RecordObject(ng, "NW モデル差し替え");
        if (!string.IsNullOrEmpty(_newName)) ng.name = _newName;
        if (_copyLayerTag) ng.tag = oldGo.tag;
        if (ng.activeSelf != oldGo.activeSelf) ng.SetActive(oldGo.activeSelf);
        var newAvatar = AvatarOf(ng);
        if (newAvatar != null) _newAvatar = newAvatar;

        // ---- 対応づけ ----
        var p = MakePlan(oldT, nt);
        r.matched = p.map.Count;
        if (p.prefix.Length > 0) r.notes.Add($"名前の前置き「{p.prefix}」を{(p.prefixAdded ? "付けた" : "外した")}モデルとして対応づけました");
        foreach (var m in p.missing) r.missing.Add(PathIn(m, oldT));

        // ---- スクリプト・設定のコピー ----
        var compMap = new Dictionary<Component, Component>();
        foreach (var kv in p.map.ToList())
        {
            if (_copyLayerTag && kv.Value.gameObject.layer != kv.Key.gameObject.layer)
            {
                Undo.RecordObject(kv.Value.gameObject, "NW モデル差し替え");
                kv.Value.gameObject.layer = kv.Key.gameObject.layer;
            }
            CopyComponents(kv.Key, kv.Value, compMap, r, oldT);
        }

        // ---- あとから足したオブジェクトを丸ごとコピー ----
        foreach (var oc in p.clones)
        {
            var parent = p.map[oc.parent];
            var c = (GameObject)Instantiate(oc.gameObject, parent, false);
            c.name = oc.name;
            Undo.RegisterCreatedObjectUndo(c, "NW モデル差し替え");
            MapParallel(oc, c.transform, p.map, compMap);
            r.cloned.Add(PathIn(oc, oldT));
        }

        // ---- シーン中の参照を付け替え ----
        foreach (var c in SceneComponents(oldT))
        {
            var so = new SerializedObject(c);
            var it = so.GetIterator();
            bool changed = false;
            while (it.Next(true))
            {
                if (!IsUserRef(it)) continue;
                var v = it.objectReferenceValue;
                if (v == null) continue;
                var nv = Remap(v, p.map, compMap);
                if (nv != null && nv != v)
                {
                    r.refs.Add(RefLabel(c, it, v) + " → " + nv.name);
                    it.objectReferenceValue = nv;
                    changed = true;
                }
                else if (nv == null && IsInside(v, oldT))
                {
                    r.leftRefs.Add(RefLabel(c, it, v));
                }
            }
            if (changed) so.ApplyModifiedProperties();
        }

        // ---- 今のモデル ----
        bool deleted = false;
        if (_oldAction == OldModelAction.Delete)
        {
            bool nested = PrefabUtility.IsPartOfPrefabInstance(oldGo) &&
                          PrefabUtility.GetOutermostPrefabInstanceRoot(oldGo) != oldGo &&
                          !PrefabUtility.IsAddedGameObjectOverride(oldGo);
            if (nested) r.notes.Add("今のモデルはプレハブの中にあるので削除できませんでした。非表示にしてあります。");
            else { Undo.DestroyObjectImmediate(oldGo); deleted = true; }
        }
        if (!deleted)
        {
            Undo.RecordObject(oldGo, "NW モデル差し替え");
            if (!oldGo.name.EndsWith(" (旧)")) oldGo.name += " (旧)";
            oldGo.SetActive(false);
        }

        EditorSceneManager.MarkSceneDirty(ng.scene);
        Undo.CollapseUndoOperations(group);

        r.notes.Insert(0, "「" + ng.name + "」に差し替えました。元に戻すときは Ctrl+Z を1回。" +
                          (deleted ? "" : "\n今のモデルは非表示で残しています（動作確認できたら削除してかまいません）。"));
        if (r.missing.Count > 0)
            r.notes.Add("新しいモデルに見つからない骨・メッシュがあります（下の一覧）。名前が変わっていたら、手で入れ直してください。揺れものの骨が増えた・変わったときは、揺れもの側の設定もやり直してください。");
        if (r.refs.Count > 0) r.foldRefs = true;

        Selection.activeGameObject = ng;
        _oldModel = ng;
        _newModel = null;
        _newName = "";
        if (_oldAvatar != null) ScanMotions();
        return r;
    }

    void CopyComponents(Transform o, Transform n, Dictionary<Component, Component> compMap, Report r, Transform oldRoot)
    {
        var counts = new Dictionary<Type, int>();
        string where = PathIn(o, oldRoot);
        foreach (var oc in o.GetComponents<Component>())
        {
            if (oc == null || oc is Transform) continue;
            var type = oc.GetType();
            counts.TryGetValue(type, out int idx); counts[type] = idx + 1;
            var existing = n.GetComponents(type).Where(x => x.GetType() == type).ToArray();
            Component nc = idx < existing.Length ? existing[idx] : null;
            bool added = false;
            if (nc == null)
            {
                nc = Undo.AddComponent(n.gameObject, type);
                if (nc == null) { r.notes.Add(where + "：" + type.Name + " を付けられませんでした。"); continue; }
                added = true;
            }

            if (oc is Animator oa)
            {
                CopyAnimator(oa, (Animator)nc);
                r.copied.Add(where + "：Animator の設定（Controller・Apply Root Motion など）");
            }
            else if (oc is Renderer orr && !added)
            {
                if (_copyMaterials) CopyMaterials(orr, (Renderer)nc, r, where);
            }
            else if (added || oc is MonoBehaviour)
            {
                if (!added) Undo.RecordObject(nc, "NW モデル差し替え");
                EditorUtility.CopySerialized(oc, nc);
                r.copied.Add(where + "：" + type.Name + (added ? "" : "（設定を上書き）"));
            }
            compMap[oc] = nc;
        }
    }

    void CopyAnimator(Animator o, Animator n)
    {
        Undo.RecordObject(n, "NW モデル差し替え");
        n.runtimeAnimatorController = o.runtimeAnimatorController;
        n.applyRootMotion = o.applyRootMotion;
        n.updateMode = o.updateMode;
        n.cullingMode = o.cullingMode;
        n.enabled = o.enabled;
        if (n.avatar == null && _newAvatar != null) n.avatar = _newAvatar;
    }

    static void CopyMaterials(Renderer o, Renderer n, Report r, string where)
    {
        var om = o.sharedMaterials;
        var nm = n.sharedMaterials;
        if (om.Length == 0) return;
        int count = Mathf.Min(om.Length, nm.Length);
        if (om.Length != nm.Length)
            r.notes.Add(where + "：マテリアルの枠の数が違います（" + om.Length + " → " + nm.Length + "）。前から " + count + " 枠だけ引き継ぎました。");
        var res = (Material[])nm.Clone();
        for (int i = 0; i < count; i++) res[i] = om[i];
        Undo.RecordObject(n, "NW モデル差し替え");
        n.sharedMaterials = res;
        r.copied.Add(where + "：マテリアル " + count + " 枠");
    }

    static void MapParallel(Transform o, Transform n, Dictionary<Transform, Transform> map, Dictionary<Component, Component> compMap)
    {
        map[o] = n;
        var oc = o.GetComponents<Component>();
        var nc = n.GetComponents<Component>();
        for (int i = 0; i < Mathf.Min(oc.Length, nc.Length); i++)
            if (oc[i] != null && nc[i] != null && oc[i].GetType() == nc[i].GetType()) compMap[oc[i]] = nc[i];
        for (int i = 0; i < Mathf.Min(o.childCount, n.childCount); i++)
            MapParallel(o.GetChild(i), n.GetChild(i), map, compMap);
    }

    // =====================================================================
    // 参照の付け替え
    // =====================================================================
    // シーン（プレハブ編集中ならそのプレハブ）のスクリプトと Constraint。今のモデルの中の物は除く
    static IEnumerable<Component> SceneComponents(Transform exclude)
    {
        var roots = new List<GameObject>();
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && exclude.gameObject.scene == stage.scene) roots.Add(stage.prefabContentsRoot);
        else
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isLoaded) roots.AddRange(s.GetRootGameObjects());
            }
        foreach (var root in roots)
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                if (c.transform.IsChildOf(exclude)) continue;
                if (c is MonoBehaviour || c is UnityEngine.Animations.IConstraint) yield return c;
            }
    }

    static readonly HashSet<string> SkipProps = new HashSet<string>
    {
        "m_Script", "m_GameObject", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset"
    };

    static bool IsUserRef(SerializedProperty it)
    {
        return it.propertyType == SerializedPropertyType.ObjectReference && !SkipProps.Contains(it.propertyPath);
    }

    static Object Remap(Object v, Dictionary<Transform, Transform> map, Dictionary<Component, Component> compMap)
    {
        if (v is GameObject g) return map.TryGetValue(g.transform, out var t) ? t.gameObject : null;
        if (v is Transform tr) return map.TryGetValue(tr, out var t2) ? t2 : null;
        if (v is Component c) return compMap.TryGetValue(c, out var c2) ? c2 : null;
        return null;
    }

    static bool IsInside(Object v, Transform root)
    {
        if (EditorUtility.IsPersistent(v)) return false;
        if (v is GameObject g) return g.transform.IsChildOf(root);
        if (v is Component c) return c.transform.IsChildOf(root);
        return false;
    }

    static string RefLabel(Component c, SerializedProperty it, Object v)
    {
        string prop = it.propertyPath.Replace(".Array.data[", "[");
        return FullPath(c.transform) + " の " + c.GetType().Name + "." + prop + "（" + v.name + "）";
    }

    // =====================================================================
    // 3. モーション
    // =====================================================================
    void ScanMotions()
    {
        _motions.Clear();
        _motionsScanned = true;
        if (_oldAvatar == null) return;
        foreach (var guid in AssetDatabase.FindAssets("t:Model"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null || imp.sourceAvatar == null) continue;
            if (imp.sourceAvatar == _oldAvatar) _motions.Add(new MotionItem { path = path });
        }
    }

    void RetargetMotions()
    {
        var r = new Report { title = "モーションの付け替えの結果" };
        var todo = _motions.Where(x => x.on && !x.done).ToList();
        try
        {
            for (int i = 0; i < todo.Count; i++)
            {
                var m = todo[i];
                EditorUtility.DisplayProgressBar("モーションの付け替え", m.path, (float)i / todo.Count);
                var imp = AssetImporter.GetAtPath(m.path) as ModelImporter;
                if (imp == null) { r.notes.Add(m.path + "：見つかりませんでした。"); continue; }
                imp.sourceAvatar = _newAvatar;
                imp.SaveAndReimport();
                m.done = true;
                r.copied.Add(m.path);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        r.notes.Add(r.copied.Count + " 本のモーションを「" + _newAvatar.name + "」に付け替えました。Animator Controller のクリップはそのまま使えます。");
        r.foldCopied = true;
        _report = r;
    }

    // =====================================================================
    // 小物
    // =====================================================================
    static ModelImporter ImporterOf(GameObject go)
    {
        string path = AssetDatabase.GetAssetPath(go);
        if (string.IsNullOrEmpty(path))
        {
            var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(go);
            if (src != null) path = AssetDatabase.GetAssetPath(src);
        }
        return string.IsNullOrEmpty(path) ? null : AssetImporter.GetAtPath(path) as ModelImporter;
    }

    static Avatar AvatarOf(GameObject go)
    {
        if (go == null) return null;
        var a = go.GetComponent<Animator>();
        if (a != null && a.avatar != null) return a.avatar;
        string path = AssetDatabase.GetAssetPath(go);
        if (string.IsNullOrEmpty(path))
        {
            var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(go);
            if (src != null) path = AssetDatabase.GetAssetPath(src);
        }
        if (string.IsNullOrEmpty(path)) return null;
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
    }

    static string PathIn(Transform t, Transform root)
    {
        if (t == root) return root.name;
        var parts = new List<string>();
        for (var x = t; x != null && x != root; x = x.parent) parts.Add(x.name);
        parts.Add(root.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    static string FullPath(Transform t)
    {
        var parts = new List<string>();
        for (var x = t; x != null; x = x.parent) parts.Add(x.name);
        parts.Reverse();
        return string.Join("/", parts);
    }
}
