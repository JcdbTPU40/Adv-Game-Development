using Toufuku.Hud;
using Toufuku.Rescue;
using Toufuku.Rescue.Mock;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Toufuku.Tutorial.EditorTools
{
    /// <summary>
    /// 段階学習（#58）を今開いているシーンに置く Editor 拡張。何回実行しても同じ状態になる。
    ///
    /// 置くもの: StagedLearning（StagedLearningDirector ＋ LearningCueView）
    /// つなぐもの: 学習の客を置く側（シーンの MockCrowdDirector）・お守り5色パレット・日本語フォント
    /// </summary>
    public static class StagedLearningSceneSetup
    {
        const string MenuPath = "Toufuku/Tutorial/Setup Staged Learning In Active Scene (#58)";
        const string ObjectName = "StagedLearning";
        const string PalettePath = "Assets/Members/Jungaya/Scripts/Rescue/OmamoriPalette.asset";

        [MenuItem(MenuPath)]
        public static void Setup()
        {
            Scene scene = SceneManager.GetActiveScene();

            StagedLearningDirector director = Object.FindAnyObjectByType<StagedLearningDirector>(FindObjectsInactive.Include);
            GameObject go;
            if (director == null)
            {
                go = new GameObject(ObjectName);
                Undo.RegisterCreatedObjectUndo(go, "Setup Staged Learning");
                director = Undo.AddComponent<StagedLearningDirector>(go);
            }
            else
            {
                go = director.gameObject;
            }

            LearningCueView cue = go.GetComponent<LearningCueView>();
            if (cue == null) cue = Undo.AddComponent<LearningCueView>(go);

            var cueSo = new SerializedObject(cue);
            OmamoriPalette palette = AssetDatabase.LoadAssetAtPath<OmamoriPalette>(PalettePath);
            if (palette != null) cueSo.FindProperty("palette").objectReferenceValue = palette;
            Font font = HudUi.FindProjectJapaneseFont();
            if (font != null) cueSo.FindProperty("font").objectReferenceValue = font;
            cueSo.ApplyModifiedProperties();

            var directorSo = new SerializedObject(director);
            directorSo.FindProperty("cueView").objectReferenceValue = cue;
            MockCrowdDirector crowd = Object.FindAnyObjectByType<MockCrowdDirector>(FindObjectsInactive.Include);
            if (crowd != null) directorSo.FindProperty("customerSource").objectReferenceValue = crowd;
            directorSo.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = go;

            Debug.Log($"[Learning] {scene.name} に段階学習を置きました（客を置く側: {(crowd != null ? crowd.name : "なし。実行時にシーンから探します")}、" +
                      $"パレット: {(palette != null ? "あり" : "なし")}、フォント: {(font != null ? font.name : "組み込み")}）。シーンを保存してください。", go);
        }
    }
}
