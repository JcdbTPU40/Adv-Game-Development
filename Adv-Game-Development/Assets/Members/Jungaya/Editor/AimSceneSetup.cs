using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Toufuku.GameInput;
using Toufuku.GameInput.EditorTools;

namespace Toufuku.Aim.EditorTools
{
    /// <summary>
    /// #60 照準・着弾予測点・弾の飛翔を開いているシーンへ組み込む Editor 拡張。
    ///
    /// ・ThrowInputController が無ければ、先に #51 のセットアップ（ThrowInputSceneSetup）を実行する。
    /// ・"OnusaAim" GameObject に OnusaAimController / AimReticleView / OnusaThrower を付けて結線する。
    /// ・TestShooter の発射位置と弾プレハブを引き継ぎ、TestShooter は無効化する（二重発射防止）。
    /// ・シーンは保存しない（確認してから手動で保存する）。Undo で戻せる。
    /// </summary>
    public static class AimSceneSetup
    {
        const string MenuPath = "Toufuku/Aim/Setup Aim And Flight In Active Scene";
        const string ObjectName = "OnusaAim";

        [MenuItem(MenuPath, priority = 310)]
        public static void Setup()
        {
            var input = Object.FindAnyObjectByType<ThrowInputController>(FindObjectsInactive.Include);
            if (input == null)
            {
                ThrowInputSceneSetup.Setup();
                input = Object.FindAnyObjectByType<ThrowInputController>(FindObjectsInactive.Include);
                if (input == null)
                {
                    Debug.LogError("[AimSetup] ThrowInputController を用意できませんでした");
                    return;
                }
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Setup Aim And Flight");
            int group = Undo.GetCurrentGroup();

            GameObject go = GameObject.Find(ObjectName);
            if (go == null)
            {
                go = new GameObject(ObjectName);
                Undo.RegisterCreatedObjectUndo(go, "Create OnusaAim");
            }

            var aim = GetOrAdd<OnusaAimController>(go);
            var view = GetOrAdd<AimReticleView>(go);
            var thrower = GetOrAdd<OnusaThrower>(go);

            Transform spawn = null;
            Object bulletPrefab = null;
            foreach (var shooter in Object.FindObjectsByType<TestShooter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(shooter);
                if (spawn == null) spawn = so.FindProperty("spawnPoint")?.objectReferenceValue as Transform;
                if (bulletPrefab == null) bulletPrefab = so.FindProperty("bulletPrefab")?.objectReferenceValue;

                if (shooter.enabled)
                {
                    Undo.RecordObject(shooter, "Disable TestShooter");
                    shooter.enabled = false;
                    Debug.Log("[AimSetup] TestShooter を無効化しました（OnusaThrower と二重発射になるため）", shooter);
                }
            }

            SetReference(aim, "input", input);
            if (spawn != null) SetReference(aim, "origin", spawn);
            SetReference(view, "aim", aim);
            SetReference(thrower, "input", input);
            SetReference(thrower, "aim", aim);
            SetReference(thrower, "reticle", view);
            if (spawn != null) SetReference(thrower, "spawnPoint", spawn);
            if (bulletPrefab != null) SetReference(thrower, "projectilePrefab", bulletPrefab);
            SetReference(input, "aim", aim);

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;

            Debug.Log(
                $"[AimSetup] {go.scene.name} に組み込みました（基準点・発射位置: {(spawn != null ? spawn.name : "未設定")}、" +
                $"弾の見た目: {(bulletPrefab != null ? bulletPrefab.name : "球")}）。確認後にシーンを保存してください。");
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : Undo.AddComponent<T>(go);
        }

        static void SetReference(Object target, string propertyName, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(propertyName);
            if (prop == null)
            {
                Debug.LogWarning($"[AimSetup] {target.GetType().Name}.{propertyName} が見つかりません", target);
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }
    }
}
