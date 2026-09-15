using UnityEngine;

namespace Toufuku.GameInput
{
    /*
        ちゃんとした素材が来るまでの仮の効果音を、実行中に作るクラス（#60）
        クールダウン中に振ったときに返す「短い低い音」に使う（ちゃんとした素材は #64）
    */
    public static class ProceduralTone
    {
        // だんだん小さくなるサイン波のモノラル AudioClip を作る
        public static AudioClip Create(string name, float frequency, float seconds, float volume, int sampleRate = 44100)
        {
            int samples = Mathf.Max(1, Mathf.RoundToInt(seconds * sampleRate));
            int attack = Mathf.Max(1, Mathf.RoundToInt(0.004f * sampleRate)); // 鳴り始めの「プチッ」という音が出ないようにする
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
