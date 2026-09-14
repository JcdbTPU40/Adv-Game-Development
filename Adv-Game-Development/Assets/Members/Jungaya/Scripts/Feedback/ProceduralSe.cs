using System;
using UnityEngine;

namespace Toufuku.Feedback
{
    /// <summary>
    /// 素材が届くまでの仮SE を実行時に合成する — Issue #64
    ///
    /// どれも立ち上がり 2〜3ms で鳴り始める（発音タイミングの確認に使うため、頭に無音を置かない）。
    /// 正式素材を GameFeedbackDirector に設定すれば使われなくなる。
    /// </summary>
    public static class ProceduralSe
    {
        const int SampleRate = 44100;

        /// <summary>命中音の根音（C5）。音階はここから AudioSource.pitch で上げる。</summary>
        public const float HitRootFrequency = 523.25f;

        /// <summary>発射: 低域へ抜けるノイズの「シュッ」。</summary>
        public static AudioClip Throw()
        {
            const float seconds = 0.14f;
            var rng = new System.Random(64);
            float lp = 0f;
            return Render("SE_Throw_Placeholder", seconds, t =>
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * Mathf.Lerp(0.45f, 0.08f, t / seconds);
                return lp * 1.8f * Envelope(t, seconds, 0.002f, 1.5f);
            });
        }

        /// <summary>命中: 短い「ポン」。</summary>
        public static AudioClip Hit()
        {
            const float seconds = 0.22f;
            return Render("SE_Hit_Placeholder", seconds, t =>
                (Sine(HitRootFrequency, t) * 0.7f + Sine(HitRootFrequency * 2f, t) * 0.2f) * Envelope(t, seconds, 0.002f, 3f));
        }

        /// <summary>救済: 上がる 2 音の「ピロン」。</summary>
        public static AudioClip Rescue()
        {
            const float seconds = 0.5f;
            return Render("SE_Rescue_Placeholder", seconds, t =>
            {
                float a = Sine(1046.5f, t) * Envelope(t, 0.25f, 0.002f, 2f);
                float b = t >= 0.09f ? (Sine(1568f, t) + Sine(3136f, t) * 0.25f) * Envelope(t - 0.09f, seconds - 0.09f, 0.002f, 2f) : 0f;
                return (a + b) * 0.5f;
            });
        }

        /// <summary>失敗（黒客化）: 下がる濁った低音。</summary>
        public static AudioClip Fail()
        {
            const float seconds = 0.45f;
            double phase = 0.0;
            return Render("SE_Fail_Placeholder", seconds, t =>
            {
                float hz = Mathf.Lerp(330f, 120f, t / seconds);
                phase += hz / SampleRate;
                float s = Mathf.Sin((float)(phase * 2.0 * Math.PI));
                return (Mathf.Sign(s) * 0.35f + s * 0.4f) * Envelope(t, seconds, 0.003f, 1.2f) * 0.7f;
            });
        }

        /// <summary>黒客ヒット: 鈍い「ドッ」。</summary>
        public static AudioClip BlackHit()
        {
            const float seconds = 0.16f;
            var rng = new System.Random(4);
            float lp = 0f;
            return Render("SE_BlackHit_Placeholder", seconds, t =>
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.1f;
                return (Sine(95f, t) * 0.8f + lp * 1.2f) * Envelope(t, seconds, 0.002f, 2.5f);
            });
        }

        /// <summary>カウントダウン: 短い「ピッ」。accent なら高い音（残り数秒の強調）。</summary>
        public static AudioClip CountdownTick(bool accent)
        {
            const float seconds = 0.08f;
            float hz = accent ? 1760f : 1174.7f;
            return Render(accent ? "SE_CountdownAccent_Placeholder" : "SE_Countdown_Placeholder", seconds, t =>
                Sine(hz, t) * Envelope(t, seconds, 0.002f, 1.5f) * 0.6f);
        }

        /// <summary>鈴: 高い倍音を 3 回細かく鳴らす「シャラン」。</summary>
        public static AudioClip Bell()
        {
            const float seconds = 0.7f;
            float[] strikes = { 0f, 0.06f, 0.13f };
            float[] partials = { 3150f, 4730f, 6020f };
            return Render("SE_Bell_Placeholder", seconds, t =>
            {
                float sum = 0f;
                for (int s = 0; s < strikes.Length; s++)
                {
                    float local = t - strikes[s];
                    if (local < 0f) continue;
                    float env = Envelope(local, seconds - strikes[s], 0.001f, 4f) * (1f - s * 0.2f);
                    for (int p = 0; p < partials.Length; p++)
                        sum += Sine(partials[p] * (1f + s * 0.004f), local) * env / (p + 1);
                }
                return sum * 0.35f;
            });
        }

        static AudioClip Render(string name, float seconds, Func<float, float> sample)
        {
            int samples = Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
                data[i] = Mathf.Clamp(sample((float)i / SampleRate), -1f, 1f);

            AudioClip clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>立ち上がり attack 秒・seconds 秒で 0 へ減衰する包絡。</summary>
        static float Envelope(float t, float seconds, float attack, float curve)
        {
            if (t < 0f || seconds <= 0f) return 0f;
            float a = Mathf.Min(1f, t / Mathf.Max(1e-5f, attack));
            float d = Mathf.Clamp01(1f - t / seconds);
            return a * Mathf.Pow(d, curve);
        }

        static float Sine(float hz, float t) => Mathf.Sin(2f * Mathf.PI * hz * t);
    }
}

