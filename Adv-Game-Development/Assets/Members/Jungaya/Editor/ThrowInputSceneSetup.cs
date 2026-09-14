using Toufuku.GameInput;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Toufuku.GameInput.EditorTools
{
    /// <summary>
    /// #51 入力状態機械を開いているシーンへ組み込む Editor 拡張。
    ///
    /// ・"ThrowInput" GameObject に ThrowInputController と生入力（ConecteController があれば Esp32RawSource、
    ///   無ければ KeyboardMouseRawSource）を付ける。
    /// ・TestShooter / OmamoriSelector / OnusaAim の inputProviderSource と PlayerDirect.inputController を差し替える。
    /// ・シーンは保存しない（確認してから手動で保存する）。Undo で戻せる。
    /// </summary>
    public static class ThrowInputSceneSetup
    {
        const string MenuPath = "Toufuku/Input/Setup Throw Input In Active Scene";
        const string ObjectName = "ThrowInput";

        [MenuItem(MenuPath, priority = 300)]
        public static void Setup()
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Setup Throw Input");
            int group = Undo.GetCurrentGroup();

            GameObject go = GameObject.Find(ObjectName);
            if (go == null)
            {
                go = new GameObject(ObjectName);
                Undo.RegisterCreatedObjectUndo(go, "Create ThrowInput");
            }

            var controller = go.GetComponent<ThrowInputController>();
            if (controller == null) controller = Undo.AddComponent<ThrowInputController>(go);

            var audio = go.GetComponent<AudioSource>();
            if (audio == null)
            {
                audio = Undo.AddComponent<AudioSource>(go);
                audio.playOnAwake = false;
            }

            var con = Object.FindAnyObjectByType<ConecteController>(FindObjectsInactive.Include);
            MonoBehaviour raw;
            if (con != null)
            {
                var esp = go.GetComponent<Esp32RawSource>();
                if (esp == null) esp = Undo.AddComponent<Esp32RawSource>(go);
                SetReference(esp, "con", con);
                raw = esp;
            }
            else
            {
                var kb = go.GetComponent<KeyboardMouseRawSource>();
                if (kb == null) kb = Undo.AddComponent<KeyboardMouseRawSource>(go);
                raw = kb;
            }

            SetReference(controller, "rawSourceSource", raw);
            SetReference(controller, "seSource", audio);

            int rewired = 0;
            rewired += RewireAll<TestShooter>("inputProviderSource", controller);
            rewired += RewireAll<OmamoriSelector>("inputProviderSource", controller);
            rewired += RewireAll<OnusaAim>("inputProviderSource", controller);
            rewired += RewireAll<PlayerDirect>("inputController", controller);

            foreach (var shoot2 in Object.FindObjectsByType<Shoot2>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (shoot2.enabled)
                    Debug.LogWarning(
                        "[ThrowInputSetup] Shoot2 は独自の振り判定とクールダウンで発射します。" +
                        "SwingAccepted を唯一の発火点にするため、TestShooter 側で撃つ場合は無効化してください。", shoot2);
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;

            Debug.Log(
                $"[ThrowInputSetup] {go.scene.name} に組み込みました（生入力: {raw.GetType().Name}、差し替え {rewired} 件）。" +
                "確認後にシーンを保存してください。");
        }

        static int RewireAll<T>(string propertyName, Object value) where T : Component
        {
            int count = 0;
            foreach (var c in Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (SetReference(c, propertyName, value)) count++;
            }
            return count;
        }

        static bool SetReference(Object target, string propertyName, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(propertyName);
            if (prop == null)
            {
                Debug.LogWarning($"[ThrowInputSetup] {target.GetType().Name}.{propertyName} が見つかりません", target);
                return false;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedProperties();
            return true;
        }
    }
}
