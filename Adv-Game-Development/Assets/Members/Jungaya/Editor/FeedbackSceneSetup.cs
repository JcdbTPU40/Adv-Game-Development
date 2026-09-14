using Toufuku.GameInput;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Toufuku.Feedback.EditorTools
{
    /// <summary>
    /// #64 SE 7 種・振動・BGM を開いているシーンへ組み込む Editor 拡張。
    ///
    /// ・"GameFeedback" GameObject に GameFeedbackDirector を付け、子 "BGM" に BgmLoopPlayer を付ける。
    /// ・シーン内の ThrowInputController.throwFeedbackSource に GameFeedbackDirector を結線する（投擲SE はこちらで鳴る）。
    /// ・シーンは保存しない（確認してから手動で保存する）。Undo で戻せる。
    /// </summary>
    public static class FeedbackSceneSetup
    {
        const string MenuPath = "Toufuku/Feedback/Setup SE And Haptics In Active Scene";
        const string ObjectName = "GameFeedback";
        const string BgmObjectName = "BGM";

        [MenuItem(MenuPath, priority = 320)]
        public static void Setup()
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Setup SE And Haptics");
            int group = Undo.GetCurrentGroup();

            GameObject go = GameObject.Find(ObjectName);
            if (go == null)
            {
                go = new GameObject(ObjectName);
                Undo.RegisterCreatedObjectUndo(go, "Create GameFeedback");
            }

            var director = go.GetComponent<GameFeedbackDirector>();
            if (director == null) director = Undo.AddComponent<GameFeedbackDirector>(go);

            Transform bgm = go.transform.Find(BgmObjectName);
            if (bgm == null)
            {
                var bgmGo = new GameObject(BgmObjectName);
                Undo.RegisterCreatedObjectUndo(bgmGo, "Create BGM");
                Undo.SetTransformParent(bgmGo.transform, go.transform, "Parent BGM");
                bgm = bgmGo.transform;
            }
            if (bgm.GetComponent<BgmLoopPlayer>() == null)
                Undo.AddComponent<BgmLoopPlayer>(bgm.gameObject);

            int wired = 0;
            foreach (var input in Object.FindObjectsByType<ThrowInputController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (SetReference(input, "throwFeedbackSource", director)) wired++;
            }
            if (wired == 0)
                Debug.LogWarning("[FeedbackSetup] ThrowInputController がありません。投擲SE は Toufuku/Input/Setup Throw Input In Active Scene の後に結線されます");

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;

            Debug.Log($"[FeedbackSetup] {go.scene.name} に組み込みました（投擲SE の結線 {wired} 件）。確認後にシーンを保存してください。");
        }

        static bool SetReference(Object target, string propertyName, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(propertyName);
            if (prop == null)
            {
                Debug.LogWarning($"[FeedbackSetup] {target.GetType().Name}.{propertyName} が見つかりません", target);
                return false;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedProperties();
            return true;
        }
    }
}
