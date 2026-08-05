using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Toufuku.Rescue.Outline
{
    /// <summary>
    /// #45 負荷検証 HUD。ProfilerRecorder / FrameTimingManager で fps・CPU/GPU・DrawCalls 等を出す。
    ///
    /// キー:
    ///   1/2/3 … 体数 8/12/16
    ///   O … アウトライン ON/OFF（Feature の純コスト差分）
    ///   F … 雨天 ON/OFF
    ///   P … 計測開始/停止（一定フレーム平均）
    ///   C … CSV 書き出し
    /// </summary>
    public class OutlinePerfHud : MonoBehaviour
    {
        const int FrameBufferSize = 180;
        const int SampleFramesDefault = 120;

        [SerializeField] OutlinePerfDirector director;
        [SerializeField] int sampleFrames = SampleFramesDefault;

        readonly float[] _frameTimes = new float[FrameBufferSize];
        int _frameWrite;
        int _frameCount;

        ProfilerRecorder _drawCalls;
        ProfilerRecorder _setPassCalls;
        ProfilerRecorder _batches;

        bool _sampling;
        int _sampleRemaining;
        readonly List<SampleRow> _samples = new List<SampleRow>(64);

        // 直近サンプルの累積
        double _accFps;
        double _accCpu;
        double _accGpu;
        double _accDraw;
        double _accSetPass;
        double _accBatches;
        int _accN;
        readonly List<float> _sampleFrameMs = new List<float>(256);

        FrameTiming[] _timings = new FrameTiming[1];

        struct SampleRow
        {
            public int Count;
            public bool OutlineOn;
            public bool Rain;
            public float AvgFps;
            public float Low1Fps;
            public float CpuMs;
            public float GpuMs;
            public float DrawCalls;
            public float SetPassCalls;
            public float Batches;
        }

        void OnEnable()
        {
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _setPassCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            FrameTimingManager.CaptureFrameTimings();
        }

        void OnDisable()
        {
            _drawCalls.Dispose();
            _setPassCalls.Dispose();
            _batches.Dispose();
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _frameTimes[_frameWrite] = dt;
            _frameWrite = (_frameWrite + 1) % FrameBufferSize;
            if (_frameCount < FrameBufferSize) _frameCount++;

            FrameTimingManager.CaptureFrameTimings();

            HandleKeys();

            if (_sampling)
            {
                AccumulateSample(dt);
                _sampleRemaining--;
                if (_sampleRemaining <= 0)
                    StopSampling(store: true);
            }
        }

        void HandleKeys()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) director?.SetTargetCount(8);
            if (Input.GetKeyDown(KeyCode.Alpha2)) director?.SetTargetCount(12);
            if (Input.GetKeyDown(KeyCode.Alpha3)) director?.SetTargetCount(16);

            if (Input.GetKeyDown(KeyCode.O))
                ToggleOutline();

            if (Input.GetKeyDown(KeyCode.F))
                OutlineWeather.ToggleRain();

            if (Input.GetKeyDown(KeyCode.P))
            {
                if (_sampling) StopSampling(store: true);
                else StartSampling();
            }

            if (Input.GetKeyDown(KeyCode.C))
                ExportCsv();
        }

        void ToggleOutline()
        {
            var feature = OutlineRendererFeature.Instance;
            if (feature != null)
            {
                feature.OutlineEnabled = !feature.OutlineEnabled;
                return;
            }

            // Feature 参照が無い場合は RendererData から探す。
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset asset)
            {
                // 反射で defaultRenderer を取得するのはバージョン差があるため、
                // 計測シーン側で Feature が登録されていれば Instance が立つ。
                Debug.LogWarning("[OutlinePerf] OutlineRendererFeature.Instance が null です。PC_Renderer に Feature を追加してください。");
            }
        }

        void StartSampling()
        {
            _sampling = true;
            _sampleRemaining = Mathf.Max(30, sampleFrames);
            _accFps = _accCpu = _accGpu = _accDraw = _accSetPass = _accBatches = 0;
            _accN = 0;
            _sampleFrameMs.Clear();
            Debug.Log($"[OutlinePerf] 計測開始（{_sampleRemaining} frames） outline={IsOutlineOn()} rain={OutlineWeather.IsRaining} count={director?.AliveCount}");
        }

        void StopSampling(bool store)
        {
            if (!_sampling) return;
            _sampling = false;

            if (!store || _accN == 0) return;

            float avgFps = (float)(_accFps / _accN);
            float low1 = ComputeOnePercentLow(_sampleFrameMs);

            var row = new SampleRow
            {
                Count = director != null ? director.AliveCount : 0,
                OutlineOn = IsOutlineOn(),
                Rain = OutlineWeather.IsRaining,
                AvgFps = avgFps,
                Low1Fps = low1,
                CpuMs = (float)(_accCpu / _accN),
                GpuMs = (float)(_accGpu / _accN),
                DrawCalls = (float)(_accDraw / _accN),
                SetPassCalls = (float)(_accSetPass / _accN),
                Batches = (float)(_accBatches / _accN),
            };
            _samples.Add(row);

            Debug.Log(
                $"[OutlinePerf] 計測完了 count={row.Count} outline={(row.OutlineOn ? "ON" : "OFF")} rain={(row.Rain ? "ON" : "OFF")} " +
                $"avgFps={row.AvgFps:F1} 1%low={row.Low1Fps:F1} cpu={row.CpuMs:F2}ms gpu={row.GpuMs:F2}ms " +
                $"draws={row.DrawCalls:F0} setPass={row.SetPassCalls:F0} batches={row.Batches:F0}");
        }

        void AccumulateSample(float dt)
        {
            float ms = dt * 1000f;
            // Editor のドメインリロード直後やツール呼び出しによる一時ヒッチは計測から除外。
            if (ms > 100f) return;

            _sampleFrameMs.Add(ms);
            float fps = dt > 1e-6f ? 1f / dt : 0f;
            _accFps += fps;

            uint count = FrameTimingManager.GetLatestTimings(1, _timings);
            if (count > 0)
            {
                _accCpu += _timings[0].cpuMainThreadFrameTime;
                _accGpu += _timings[0].gpuFrameTime;
            }

            if (_drawCalls.Valid) _accDraw += _drawCalls.LastValue;
            if (_setPassCalls.Valid) _accSetPass += _setPassCalls.LastValue;
            if (_batches.Valid) _accBatches += _batches.LastValue;
            _accN++;
        }

        /// <summary>
        /// 1% low = 最悪 1% フレームの平均 fps。
        /// サンプルが少ないときは少なくとも最悪1フレームを使う。
        /// </summary>
        static float ComputeOnePercentLow(List<float> frameMs)
        {
            if (frameMs == null || frameMs.Count == 0) return 0f;
            var sorted = new List<float>(frameMs);
            sorted.Sort(); // 昇順＝速い→遅い。末尾が最悪。
            int worstCount = Mathf.Max(1, Mathf.CeilToInt(sorted.Count * 0.01f));
            double sumMs = 0;
            for (int i = sorted.Count - worstCount; i < sorted.Count; i++)
                sumMs += sorted[i];
            float avgMs = (float)(sumMs / worstCount);
            return avgMs > 1e-4f ? 1000f / avgMs : 0f;
        }

        bool IsOutlineOn()
        {
            var feature = OutlineRendererFeature.Instance;
            return feature != null && feature.OutlineEnabled;
        }

        void ExportCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("count,outline,rain,avg_fps,low1_fps,cpu_ms,gpu_ms,draw_calls,setpass_calls,batches");
            for (int i = 0; i < _samples.Count; i++)
            {
                var r = _samples[i];
                sb.AppendLine(
                    $"{r.Count},{(r.OutlineOn ? 1 : 0)},{(r.Rain ? 1 : 0)},{r.AvgFps:F2},{r.Low1Fps:F2}," +
                    $"{r.CpuMs:F3},{r.GpuMs:F3},{r.DrawCalls:F1},{r.SetPassCalls:F1},{r.Batches:F1}");
            }

            string dir = Application.persistentDataPath;
            string path = Path.Combine(dir, $"outline_perf_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[OutlinePerf] CSV 書き出し: {path}\n{sb}");
        }

        void OnGUI()
        {
            float avgFps = 0f;
            float low1 = 0f;
            if (_frameCount > 0)
            {
                double sum = 0;
                var msList = new List<float>(_frameCount);
                for (int i = 0; i < _frameCount; i++)
                {
                    float t = _frameTimes[i];
                    sum += t;
                    msList.Add(t * 1000f);
                }
                avgFps = (float)(_frameCount / sum);
                low1 = ComputeOnePercentLow(msList);
            }

            uint timingCount = FrameTimingManager.GetLatestTimings(1, _timings);
            float cpu = timingCount > 0 ? (float)_timings[0].cpuMainThreadFrameTime : 0f;
            float gpu = timingCount > 0 ? (float)_timings[0].gpuFrameTime : 0f;

            long draws = _drawCalls.Valid ? _drawCalls.LastValue : 0;
            long setPass = _setPassCalls.Valid ? _setPassCalls.LastValue : 0;
            long batches = _batches.Valid ? _batches.LastValue : 0;

            var rect = new Rect(12, 12, 460, 220);
            GUI.Box(rect, "Outline Perf (#45)");
            GUILayout.BeginArea(new Rect(20, 36, 440, 190));
            GUILayout.Label($"Count: {director?.AliveCount ?? 0} / target {director?.TargetCount ?? 0}");
            GUILayout.Label($"Outline: {(IsOutlineOn() ? "ON" : "OFF")}   Rain: {(OutlineWeather.IsRaining ? "ON" : "OFF")} ({OutlineWeather.RainAmount:F2})");
            GUILayout.Label($"FPS avg: {avgFps:F1}   1% low: {low1:F1}");
            GUILayout.Label($"CPU main: {cpu:F2} ms   GPU: {gpu:F2} ms");
            GUILayout.Label($"DrawCalls: {draws}   SetPass: {setPass}   Batches: {batches}");
            GUILayout.Label(_sampling
                ? $"Sampling... {_sampleRemaining} frames left"
                : $"Samples stored: {_samples.Count}");
            GUILayout.Label("1/2/3=count  O=outline  F=rain  P=sample  C=csv");
            GUILayout.EndArea();
        }
    }
}
