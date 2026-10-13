using System;
using System.Collections.Generic;
using UnityEngine;

namespace Toufuku.Scenery
{
    /*
        空・照明の日周表現（企画書 v8 8章「時間」：1日＝1ヶ月。日が昇り、沈み、月が昇り沈んで、再び日が昇ったら翌月）
          ・GameSession の時計から「今の月の中で何割すぎたか」を出して、朝→昼→夕→夜→（翌月の朝）の4プロファイルをつなぐ
          ・動かすのはライト・環境光・空・フォグだけ。ポストエフェクトは固定（TimeOfDayProfile のコメント）
          ・太陽と月が入れ替わるあいだは、片方が地平線まで沈んで暗くなってから、もう片方が反対側から昇る
            （向きを補間すると、夕方の空を太陽が横切ってしまうため）
          ・エディタでは previewTime01 で時刻を動かして見られる（ExecuteAlways）
    */
    [ExecuteAlways]
    public class DayCycleController : MonoBehaviour
    {
        public enum TimeSource
        {
            Session,   // GameSession の時計（本番）。セッションがなければ previewTime01
            FreeRun,   // freeRunSecondsPerDay で勝手に回す（環境シーンの確認用）
            Preview,   // previewTime01 で止める
        }

        [Serializable]
        public class Key
        {
            public TimeOfDayProfile profile;
            [Tooltip("1日（＝1ヶ月）の中の位置。0 が日の出。")]
            [Range(0f, 1f)] public float time01;
        }

        [SerializeField] Light keyLight;
        [Tooltip("空のもと。実行中は複製して使うので、アセットは書きかえない。")]
        [SerializeField] Material skyboxTemplate;
        [Tooltip("時刻の順にならべる。最後のキーの次は最初のキーへ戻る（翌月の朝）。")]
        [SerializeField] List<Key> keys = new List<Key>();

        [Header("時刻")]
        [SerializeField] TimeSource source = TimeSource.Session;
        [Tooltip("セッション開始の時刻をずらす（0 で日の出から始まる）。")]
        [SerializeField, Range(0f, 1f)] float sessionOffset01 = 0f;
        [SerializeField, Min(1f)] float freeRunSecondsPerDay = 60f;
        [SerializeField, Range(0f, 1f)] float previewTime01 = 0.25f;

        [Header("境内の灯り")]
        [Tooltip("夜ほど強く光らせる発光（灯籠の火袋など）。マテリアルの _EmissionColor を倍率つきで上書きする。")]
        [SerializeField] List<Renderer> nightGlowRenderers = new List<Renderer>();
        [SerializeField, ColorUsage(false, true)] Color nightGlowColor = new Color(1f, 0.55f, 0.2f) * 2f;

        static readonly int SkyTintId = Shader.PropertyToID("_SkyTint");
        static readonly int GroundColorId = Shader.PropertyToID("_GroundColor");
        static readonly int ExposureId = Shader.PropertyToID("_Exposure");
        static readonly int AtmosphereId = Shader.PropertyToID("_AtmosphereThickness");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        Material _skyInstance;
        MaterialPropertyBlock _mpb;

        // 今の1日の中の位置（0〜1）
        public float Time01 { get; private set; }
        // 篝火・灯籠の灯りにかける倍率（BonfireProgress が読む）
        public float PracticalLightScale { get; private set; } = 1f;

        public float PreviewTime01
        {
            get => previewTime01;
            set => previewTime01 = Mathf.Repeat(value, 1f);
        }

        void OnEnable()
        {
            if (Application.isPlaying && skyboxTemplate != null)
            {
                _skyInstance = new Material(skyboxTemplate) { name = skyboxTemplate.name + " (Runtime)" };
                RenderSettings.skybox = _skyInstance;
            }
            else if (skyboxTemplate != null)
            {
                RenderSettings.skybox = skyboxTemplate;
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            if (keyLight != null) RenderSettings.sun = keyLight;
            Apply();
        }

        void OnDisable()
        {
            if (_skyInstance != null)
            {
                if (RenderSettings.skybox == _skyInstance) RenderSettings.skybox = skyboxTemplate;
                Destroy(_skyInstance);
                _skyInstance = null;
            }
        }

        void OnValidate()
        {
            if (isActiveAndEnabled) Apply();
        }

        void Update()
        {
            Apply();
        }

        float CurrentTime01()
        {
            if (!Application.isPlaying) return previewTime01;

            switch (source)
            {
                case TimeSource.FreeRun:
                    return Mathf.Repeat(Time.time / freeRunSecondsPerDay, 1f);
                case TimeSource.Session:
                    var session = GameSession.Instance;
                    if (session == null) return previewTime01;
                    // セッション前（タイトルから入った直後など）は日の出で止めておく
                    float secondsPerDay = session.TotalSeconds / Mathf.Max(1, session.TotalMonths);
                    return Mathf.Repeat(session.ElapsedSeconds / Mathf.Max(0.01f, secondsPerDay) + sessionOffset01, 1f);
                default:
                    return previewTime01;
            }
        }

        public void Apply()
        {
            Time01 = CurrentTime01();
            if (!TryFindSegment(Time01, out TimeOfDayProfile a, out TimeOfDayProfile b, out float s)) return;

            ApplyLight(a, b, s);

            RenderSettings.ambientSkyColor = Color.Lerp(a.ambientSky, b.ambientSky, s);
            RenderSettings.ambientEquatorColor = Color.Lerp(a.ambientEquator, b.ambientEquator, s);
            RenderSettings.ambientGroundColor = Color.Lerp(a.ambientGround, b.ambientGround, s);

            RenderSettings.fogColor = Color.Lerp(a.fogColor, b.fogColor, s);
            RenderSettings.fogDensity = Mathf.Lerp(a.fogDensity, b.fogDensity, s);

            Material sky = _skyInstance != null ? _skyInstance : skyboxTemplate;
            if (sky != null)
            {
                sky.SetColor(SkyTintId, Color.Lerp(a.skyTint, b.skyTint, s));
                sky.SetColor(GroundColorId, Color.Lerp(a.skyGround, b.skyGround, s));
                sky.SetFloat(ExposureId, Mathf.Lerp(a.skyExposure, b.skyExposure, s));
                sky.SetFloat(AtmosphereId, Mathf.Lerp(a.atmosphereThickness, b.atmosphereThickness, s));
            }

            PracticalLightScale = Mathf.Lerp(a.practicalLightScale, b.practicalLightScale, s);
            ApplyNightGlow();
        }

        /*
            主光源。同じ天体どうしなら向きを球面補間する
            ちがう天体（夕→夜、夜→朝）なら、前半で a の天体が地平線まで沈みながら暗くなり、後半で b の天体が地平線から昇る
        */
        void ApplyLight(TimeOfDayProfile a, TimeOfDayProfile b, float s)
        {
            if (keyLight == null) return;

            float elevation, azimuth, fade;
            if (a.body == b.body)
            {
                elevation = Mathf.Lerp(a.elevation, b.elevation, s);
                azimuth = Mathf.LerpAngle(a.azimuth, b.azimuth, s);
                fade = 1f;
            }
            else if (s < 0.5f)
            {
                float h = s * 2f;
                elevation = Mathf.Lerp(a.elevation, -2f, h);
                azimuth = a.azimuth;
                fade = 1f - h;
            }
            else
            {
                float h = (s - 0.5f) * 2f;
                elevation = Mathf.Lerp(-2f, b.elevation, h);
                azimuth = b.azimuth;
                fade = h;
            }

            keyLight.transform.rotation = Quaternion.Euler(elevation, azimuth, 0f);
            keyLight.color = Color.Lerp(a.lightColor, b.lightColor, s);
            keyLight.intensity = Mathf.Lerp(a.lightIntensity, b.lightIntensity, s) * fade;
            keyLight.shadowStrength = Mathf.Lerp(a.shadowStrength, b.shadowStrength, s);
        }

        void ApplyNightGlow()
        {
            if (nightGlowRenderers.Count == 0) return;
            _mpb ??= new MaterialPropertyBlock();
            Color glow = nightGlowColor * PracticalLightScale;
            foreach (var r in nightGlowRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(EmissionColorId, glow);
                r.SetPropertyBlock(_mpb);
            }
        }

        bool TryFindSegment(float t, out TimeOfDayProfile a, out TimeOfDayProfile b, out float s)
        {
            a = b = null;
            s = 0f;
            int n = keys.Count;
            if (n == 0) return false;

            _keyTimes = _keyTimes != null && _keyTimes.Length == n ? _keyTimes : new float[n];
            for (int i = 0; i < n; i++) _keyTimes[i] = keys[i] != null ? keys[i].time01 : 0f;
            FindSegment(_keyTimes, t, out int prev, out int next, out s);

            Key ka = keys[prev], kb = keys[next];
            if (ka == null || kb == null || ka.profile == null || kb.profile == null) return false;
            a = ka.profile;
            b = kb.profile;
            // 0 と 1 で止まる時間を少し作る（各時刻帯の見た目を保つ）
            s = Mathf.SmoothStep(0f, 1f, s);
            return true;
        }

        float[] _keyTimes;

        /*
            t（0〜1）の前後のキーの番号と、そのあいだの割合 s（0〜1、直線）を出す
              ・keyTimes は小さい順にならんでいる前提
              ・最後のキーの次は最初のキー（翌日＝翌月の朝）。最初のキーより前の t は、最後のキーからの続き
        */
        public static void FindSegment(IReadOnlyList<float> keyTimes, float t, out int prev, out int next, out float s)
        {
            int n = keyTimes.Count;
            prev = n - 1;
            for (int i = 0; i < n; i++)
            {
                if (keyTimes[i] <= t) prev = i;
            }
            next = (prev + 1) % n;

            float start = keyTimes[prev];
            float end = keyTimes[next];
            if (end <= start) end += 1f;
            float tt = t < start ? t + 1f : t;
            s = end > start ? Mathf.Clamp01((tt - start) / (end - start)) : 0f;
        }
    }
}
