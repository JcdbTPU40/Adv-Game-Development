using UnityEngine;

namespace Toufuku.Menu
{
    /*
        全体の音量（メインメニューの設定）

        ・AudioListener.volume をそのまま使うので、どのシーンにも効く
        ・PlayerPrefs に残して、アプリを起動しなおしても同じ音量から始める（展示機の音量はアテンドが1回決めれば済むように）
    */
    public static class MasterVolume
    {
        const string Key = "Toufuku.MasterVolume";
        public const float Step = 0.1f;

        public static float Value => AudioListener.volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Load()
        {
            AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(Key, 1f));
        }

        public static void Set(float volume)
        {
            // 0.1 きざみにそろえる（足し引きをくり返してもずれないように）
            volume = Mathf.Round(Mathf.Clamp01(volume) / Step) * Step;
            AudioListener.volume = volume;
            PlayerPrefs.SetFloat(Key, volume);
            PlayerPrefs.Save();
        }

        public static void Add(float delta) => Set(Value + delta);
    }
}
