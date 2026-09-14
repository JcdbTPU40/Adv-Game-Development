using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Toufuku.Playtest.EditorTools
{
    /// <summary>
    /// #63 計測ログ基盤を開いているシーンへ組み込む Editor 拡張。
    ///
    /// ・"PlaytestLogger" GameObject に PlaytestLogger を付ける（既にあれば選択するだけ）。
    /// ・シーンは保存しない（確認してから手動で保存する）。Undo で戻せる。
    /// </summary>
    public static class PlaytestSceneSetup
    {
        const string MenuPath = "Toufuku/Playtest/Setup Playtest Logger In Active Scene";
        const string OpenFolderMenuPath = "Toufuku/Playtest/Open Playtest Logs Folder";
        const string ObjectName = "PlaytestLogger";

        [MenuItem(MenuPath, priority = 330)]
        public static void Setup()
        {
            PlaytestLogger[] existing = Object.FindObjectsByType<PlaytestLogger>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (existing.Length > 0)
            {
                Selection.activeGameObject = existing[0].gameObject;
                Debug.Log($"[PlaytestSetup] {existing[0].gameObject.scene.name} には PlaytestLogger が既にあります。");
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Setup Playtest Logger");
            int group = Undo.GetCurrentGroup();

            var go = new GameObject(ObjectName);
            Undo.RegisterCreatedObjectUndo(go, "Create PlaytestLogger");
            Undo.AddComponent<PlaytestLogger>(go);

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;

            Debug.Log($"[PlaytestSetup] {go.scene.name} に PlaytestLogger を組み込みました。テストID・シード・責任者を Inspector で設定し、確認後にシーンを保存してください。");
        }

        [MenuItem(OpenFolderMenuPath, priority = 331)]
        public static void OpenLogsFolder()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "PlaytestLogs"));
            Directory.CreateDirectory(path);
            EditorUtility.RevealInFinder(path);
        }
    }
}
