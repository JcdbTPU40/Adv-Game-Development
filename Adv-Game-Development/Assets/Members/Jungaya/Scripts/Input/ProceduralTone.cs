using UnityEngine;

namespace Toufuku.GameInput
{
    /// <summary>
    /// 素材が届くまでの仮SE を実行時に合成する — Issue #60
    /// クールダウン中の有効スイングで返す「短い低音」に使う（正式素材は #64）。
    /// </summary>
    public static class ProceduralTone
    {
        /// <summary>減衰する正弦波のモノラル AudioClip を作る。</summary>
        public static AudioClip Create(string name, float frequency, float seconds, float volume, int sampleRate = 44100)
        {
            int samples = Mathf.Max(1, Mathf.RoundToInt(seconds * sampleRate));
            int attack = Mathf.Max(1, Mathf.RoundToInt(0.004f * sampleRate)); // 立ち上がりのプチ音を避ける
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;
                float decay = 1f - (float)i / samples;
                float envelope = Mathf.Min(1f, (float)i / attack) * decay * decay;
                data[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * volume;
            }

            AudioClip clip = AudioClip.Create(name, samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
