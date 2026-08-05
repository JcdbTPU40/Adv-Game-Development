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
            TickAxisSweep();

            if (_sampling)
            {
                AccumulateSample(dt);
                _sampleRemaining--;
                if (_sampleRemaining <= 0)
                    StopSampling(store: true);
            }
        }

        /// <summary>計測中か（自動バッチ用）。</summary>
        public bool IsSampling => _sampling;

        /// <summary>蓄積済みサンプル数。</summary>
        public int StoredSampleCount => _samples.Count;

        /// <summary>コードから計測を開始する（P キー相当）。</summary>
        public void BeginSample(int frames = -1)
        {
            if (frames > 0) sampleFrames = frames;
            if (_sampling) StopSampling(store: false);
            StartSampling();
        }

        /// <summary>サンプル履歴をクリアする。</summary>
        public void ClearSamples() => _samples.Clear();

        /// <summary>直近サンプルを1行テキストで返す（無ければ空文字）。</summary>
        public string FormatLastSample()
        {
            if (_samples.Count == 0) return string.Empty;
            var r = _samples[_samples.Count - 1];
            return $"count={r.Count} outline={(r.OutlineOn ? "ON" : "OFF")} rain={(r.Rain ? "ON" : "OFF")} " +
                   $"avgFps={r.AvgFps:F1} 1%low={r.Low1Fps:F1} cpu={r.CpuMs:F2}ms gpu={r.GpuMs:F2}ms " +
                   $"draws={r.DrawCalls:F0} setPass={r.SetPassCalls:F0} batches={r.Batches:F0}";
        }

        /// <summary>
        /// 体数×Outline×天候の12条件を自動計測する（#45 D）。
        /// 順序をシャッフルしたうえで2巡し、ドリフトを見えるようにする。
        /// コルーチンではなく Update ステートマシンで回す（PlayMode 中の MCP 呼び出しでも落ちにくい）。
        /// </summary>
        public void RunAxisSweep(int framesPerSample = 60)
        {
            if (_axisSweepActive) return;

            ClearSamples();
            _axisConditions.Clear();
            int[] counts = { 8, 12, 16 };
            bool[] outlineStates = { true, false };
            bool[] rainStates = { false, true };
            for (int ci = 0; ci < counts.Length; ci++)
            for (int oi = 0; oi < outlineStates.Length; oi++)
            for (int ri = 0; ri < rainStates.Length; ri++)
                _axisConditions.Add((counts[ci], outlineStates[oi], rainStates[ri]));

            _axisPass = 0;
            _axisIndex = -1;
            _axisSettle = 0;
            _axisFramesPerSample = Mathf.Max(20, framesPerSample);
            _axisSweepActive = true;
            _axisPhase = AxisPhase.Shuffle;
            Debug.Log($"[OutlinePerf] ===== 体数軸スイープ開始（{_axisConditions.Count}条件 × 2巡） =====");
        }

        enum AxisPhase { Idle, Shuffle, Apply, Settle, Sample, Summarize }

        bool _axisSweepActive;
        AxisPhase _axisPhase = AxisPhase.Idle;
        readonly List<(int count, bool outline, bool rain)> _axisConditions = new List<(int, bool, bool)>(12);
        int _axisPass;
        int _axisIndex;
        int _axisSettle;
        int _axisFramesPerSample = 60;

        void TickAxisSweep()
        {
            if (!_axisSweepActive) return;

            switch (_axisPhase)
            {
                case AxisPhase.Shuffle:
                    for (int i = _axisConditions.Count - 1; i > 0; i--)
                    {
                        int j = Random.Range(0, i + 1);
                        (_axisConditions[i], _axisConditions[j]) = (_axisConditions[j], _axisConditions[i]);
                    }
                    Debug.Log($"[OutlinePerf] --- pass {_axisPass + 1}/2 ---");
                    _axisIndex = -1;
                    _axisPhase = AxisPhase.Apply;
                    break;

                case AxisPhase.Apply:
                    _axisIndex++;
                    if (_axisIndex >= _axisConditions.Count)
                    {
                        _axisPass++;
                        if (_axisPass >= 2)
                        {
                            _axisPhase = AxisPhase.Summarize;
                            break;
                        }
                        _axisPhase = AxisPhase.Shuffle;
                        break;
                    }

                    var c = _axisConditions[_axisIndex];
                    director?.SetTargetCount(c.count);
                    var feature = OutlineRendererFeature.Instance;
                    if (feature != null) feature.OutlineEnabled = c.outline;
                    OutlineWeather.RainAmount = c.rain ? 1f : 0f;
                    _axisSettle = 3;
                    _axisPhase = AxisPhase.Settle;
                    break;

                case AxisPhase.Settle:
                    _axisSettle--;
                    if (_axisSettle <= 0)
                    {
                        BeginSample(_axisFramesPerSample);
                        _axisPhase = AxisPhase.Sample;
                    }
                    break;

                case AxisPhase.Sample:
                    if (!_sampling)
                    {
                        Debug.Log($"[OutlinePerf][pass{_axisPass + 1}] {FormatLastSample()}");
                        _axisPhase = AxisPhase.Apply;
                    }
                    break;

                case AxisPhase.Summarize:
                    LogBodyCountAxisSummary();
                    ExportCsv();
                    _axisSweepActive = false;
                    _axisPhase = AxisPhase.Idle;
                    Debug.Log("[OutlinePerf] ===== 体数軸スイープ完了 =====");
                    break;
            }
        }

        void LogBodyCountAxisSummary()
        {
            float SumGpu(int count, bool outline, bool rain, out int n)
            {
                double sum = 0;
                n = 0;
                for (int i = 0; i < _samples.Count; i++)
                {
                    var r = _samples[i];
                    if (r.Count != count || r.OutlineOn != outline || r.Rain != rain) continue;
                    sum += r.GpuMs;
                    n++;
                }
                return n > 0 ? (float)(sum / n) : 0f;
            }

            var sb = new StringBuilder();
            sb.AppendLine("[OutlinePerf] 体数軸サマリ（Outline ON / 晴天 の GPU ms 平均）:");
            float g8 = SumGpu(8, true, false, out int n8);
            float g12 = SumGpu(12, true, false, out int n12);
            float g16 = SumGpu(16, true, false, out int n16);
            sb.AppendLine($"  8体:  GPU={g8:F3}ms (n={n8})");
            sb.AppendLine($"  12体: GPU={g12:F3}ms (n={n12})");
            sb.AppendLine($"  16体: GPU={g16:F3}ms (n={n16})");
            float delta = g16 - g8;
            sb.AppendLine($"  Δ(16-8)={delta:F3}ms");
            bool significant = Mathf.Abs(delta) >= 0.1f && (g8 <= 1e-4f || Mathf.Abs(delta / Mathf.Max(g8, 1e-4f)) >= 0.05f);
            sb.AppendLine(significant
                ? "  判定: 8体と16体で GPU ms に差あり（体数依存のコストが見える）"
                : "  判定: 8体と16体で GPU ms に有意な差なし → 支配的なのはフルスクリーンのダイレート");

            float sunny = SumGpu(16, true, false, out int ns);
            float rainy = SumGpu(16, true, true, out int nr);
            sb.AppendLine($"  雨天差(16/ON): 晴天 GPU={sunny:F3}ms (n={ns}) / 雨天 GPU={rainy:F3}ms (n={nr}) / Δ={rainy - sunny:F3}ms");
            Debug.Log(sb.ToString());
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
