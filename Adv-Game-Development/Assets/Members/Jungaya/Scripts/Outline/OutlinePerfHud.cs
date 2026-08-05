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
            public int Pass; // スイープ巡番号。手動計測は -1
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
        /// 体数×Outline×天候の12条件を自動計測する（#45 H）。
        /// 順序をシャッフルしたうえで複数巡し、ON−OFF 差分とノイズ幅で体数軸を判定する。
        /// </summary>
        /// <param name="framesPerSample">1条件あたりの平均フレーム数（60以上推奨）。</param>
        /// <param name="passCount">巡回数（4以上推奨）。</param>
        public void RunAxisSweep(int framesPerSample = 60, int passCount = 4)
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
            _axisPassCount = Mathf.Max(1, passCount);
            _axisIndex = -1;
            _axisSettle = 0;
            _axisFramesPerSample = Mathf.Max(20, framesPerSample);
            _axisSweepActive = true;
            _axisPhase = AxisPhase.Shuffle;
            Debug.Log($"[OutlinePerf] ===== 体数軸スイープ開始（{_axisConditions.Count}条件 × {_axisPassCount}巡, {framesPerSample}f） =====");
        }

        enum AxisPhase { Idle, Shuffle, Apply, Settle, Sample, Summarize }

        bool _axisSweepActive;
        AxisPhase _axisPhase = AxisPhase.Idle;
        readonly List<(int count, bool outline, bool rain)> _axisConditions = new List<(int, bool, bool)>(12);
        int _axisPass;
        int _axisPassCount = 4;
        int _axisIndex;
        int _axisSettle;
        int _axisFramesPerSample = 60;
        int _samplePassTag = -1; // StopSampling が SampleRow.Pass に書く

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
                    Debug.Log($"[OutlinePerf] --- pass {_axisPass + 1}/{_axisPassCount} ---");
                    _axisIndex = -1;
                    _axisPhase = AxisPhase.Apply;
                    break;

                case AxisPhase.Apply:
                    _axisIndex++;
                    if (_axisIndex >= _axisConditions.Count)
                    {
                        _axisPass++;
                        if (_axisPass >= _axisPassCount)
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
                        _samplePassTag = _axisPass;
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
                    _samplePassTag = -1;
                    Debug.Log("[OutlinePerf] ===== 体数軸スイープ完了 =====");
                    break;
            }
        }

        /// <summary>
        /// ON−OFF の Δ を体数×天候ごとに集計し、巡間の標準偏差でノイズ幅を出す。
        /// 判定は「Δ がノイズより明確に大きいか」「Δ が体数に対して平坦か」の両面で行う。
        /// </summary>
        void LogBodyCountAxisSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[OutlinePerf] 体数軸サマリ（ON−OFF 差分 / 巡間 std）:");
            sb.AppendLine("  count,rain,Δgpu_mean,Δgpu_std,Δgpu_min,Δgpu_max,Δcpu_mean,Δfps_mean,Δdraws_mean,n_pairs");

            var deltaCsv = new StringBuilder();
            deltaCsv.AppendLine("count,rain,delta_gpu_mean,delta_gpu_std,delta_gpu_min,delta_gpu_max,delta_cpu_mean,delta_fps_mean,delta_draws_mean,n_pairs");

            int[] counts = { 8, 12, 16 };
            bool[] rains = { false, true };
            var sunnyDeltas = new List<float>();
            var allDeltaMeans = new List<(int count, bool rain, float mean, float std)>();

            for (int ci = 0; ci < counts.Length; ci++)
            for (int ri = 0; ri < rains.Length; ri++)
            {
                int count = counts[ci];
                bool rain = rains[ri];
                var pairDeltasGpu = new List<float>();
                var pairDeltasCpu = new List<float>();
                var pairDeltasFps = new List<float>();
                var pairDeltasDraws = new List<float>();

                // 巡ごとに ON/OFF をペアにする（シャッフル順でも Pass タグで対応）。
                int maxPass = -1;
                for (int i = 0; i < _samples.Count; i++)
                    if (_samples[i].Pass > maxPass) maxPass = _samples[i].Pass;

                for (int pass = 0; pass <= maxPass; pass++)
                {
                    if (!TryFindSample(count, true, rain, pass, out var on)) continue;
                    if (!TryFindSample(count, false, rain, pass, out var off)) continue;
                    pairDeltasGpu.Add(on.GpuMs - off.GpuMs);
                    pairDeltasCpu.Add(on.CpuMs - off.CpuMs);
                    pairDeltasFps.Add(on.AvgFps - off.AvgFps);
                    pairDeltasDraws.Add(on.DrawCalls - off.DrawCalls);
                }

                if (pairDeltasGpu.Count == 0)
                {
                    sb.AppendLine($"  {count},{(rain ? "rain" : "sun")},NO_PAIRS");
                    continue;
                }

                MeanStdMinMax(pairDeltasGpu, out float gMean, out float gStd, out float gMin, out float gMax);
                MeanStdMinMax(pairDeltasCpu, out float cMean, out _, out _, out _);
                MeanStdMinMax(pairDeltasFps, out float fMean, out _, out _, out _);
                MeanStdMinMax(pairDeltasDraws, out float dMean, out _, out _, out _);

                string rainLabel = rain ? "rain" : "sun";
                sb.AppendLine($"  {count},{rainLabel},{gMean:F3},{gStd:F3},{gMin:F3},{gMax:F3},{cMean:F3},{fMean:F2},{dMean:F1},{pairDeltasGpu.Count}");
                deltaCsv.AppendLine($"{count},{(rain ? 1 : 0)},{gMean:F3},{gStd:F3},{gMin:F3},{gMax:F3},{cMean:F3},{fMean:F2},{dMean:F1},{pairDeltasGpu.Count}");

                allDeltaMeans.Add((count, rain, gMean, gStd));
                if (!rain) sunnyDeltas.Add(gMean);
            }

            // 判定: 晴天の ΔGPU が体数でどう動くか + ノイズ幅との比較
            sb.AppendLine();
            if (sunnyDeltas.Count >= 2)
            {
                float d8 = 0, d16 = 0;
                float s8 = 0, s16 = 0;
                for (int i = 0; i < allDeltaMeans.Count; i++)
                {
                    var a = allDeltaMeans[i];
                    if (a.rain) continue;
                    if (a.count == 8) { d8 = a.mean; s8 = a.std; }
                    if (a.count == 16) { d16 = a.mean; s16 = a.std; }
                }
                float bodyDelta = d16 - d8;
                // ノイズ幅の目安: 両側 std の合成（粗い）
                float noise = Mathf.Sqrt(s8 * s8 + s16 * s16);
                sb.AppendLine($"  晴天 ΔGPU: 8体={d8:F3}±{s8:F3} / 16体={d16:F3}±{s16:F3} / 体数差={bodyDelta:F3} / ノイズ幅≈{noise:F3}");

                bool deltaAboveNoise = Mathf.Abs(d8) > 2f * s8 && Mathf.Abs(d16) > 2f * s16;
                bool bodyFlat = Mathf.Abs(bodyDelta) <= Mathf.Max(0.05f, 2f * noise);

                if (!deltaAboveNoise)
                {
                    sb.AppendLine("  判定: Δ がノイズ幅に埋もれている → Editor では体数軸を判定不能。実機で取り直すこと。");
                    sb.AppendLine("  VERDICT=NOISE_LIMITED");
                }
                else if (bodyFlat)
                {
                    sb.AppendLine("  判定: Δ GPU はノイズより大きく、体数に対して平坦 → 体数を減らしても軽くならない（ダイレート支配）。");
                    sb.AppendLine("  VERDICT=FLAT_DOMINATED_BY_DILATE");
                }
                else
                {
                    sb.AppendLine("  判定: Δ GPU が体数に対して有意に変化 → 体数依存のコストが見える。");
                    sb.AppendLine("  VERDICT=SCALES_WITH_COUNT");
                }
            }
            else
            {
                sb.AppendLine("  判定: ペア不足 → 判定不能");
                sb.AppendLine("  VERDICT=INSUFFICIENT_DATA");
            }

            Debug.Log(sb.ToString());

            // Δ CSV も persistentDataPath に書く
            string dir = Application.persistentDataPath;
            string path = Path.Combine(dir, $"outline_perf_delta_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllText(path, deltaCsv.ToString(), Encoding.UTF8);
            Debug.Log($"[OutlinePerf] Δ CSV: {path}\n{deltaCsv}");
        }

        bool TryFindSample(int count, bool outline, bool rain, int pass, out SampleRow row)
        {
            for (int i = 0; i < _samples.Count; i++)
            {
                var r = _samples[i];
                if (r.Count == count && r.OutlineOn == outline && r.Rain == rain && r.Pass == pass)
                {
                    row = r;
                    return true;
                }
            }
            row = default;
            return false;
        }

        static void MeanStdMinMax(List<float> values, out float mean, out float std, out float min, out float max)
        {
            mean = 0f;
            std = 0f;
            min = 0f;
            max = 0f;
            if (values == null || values.Count == 0) return;
            min = values[0];
            max = values[0];
            double sum = 0;
            for (int i = 0; i < values.Count; i++)
            {
                float v = values[i];
                sum += v;
                if (v < min) min = v;
                if (v > max) max = v;
            }
            mean = (float)(sum / values.Count);
            if (values.Count < 2) { std = 0f; return; }
            double var = 0;
            for (int i = 0; i < values.Count; i++)
            {
                double d = values[i] - mean;
                var += d * d;
            }
            std = (float)Mathf.Sqrt((float)(var / (values.Count - 1)));
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
                else
                {
                    _samplePassTag = -1; // 手動計測
                    StartSampling();
                }
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
                Pass = _samplePassTag,
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
                $"[OutlinePerf] 計測完了 count={row.Count} outline={(row.OutlineOn ? "ON" : "OFF")} rain={(row.Rain ? "ON" : "OFF")} pass={row.Pass} " +
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
            sb.AppendLine("count,outline,rain,pass,avg_fps,low1_fps,cpu_ms,gpu_ms,draw_calls,setpass_calls,batches");
            for (int i = 0; i < _samples.Count; i++)
            {
                var r = _samples[i];
                sb.AppendLine(
                    $"{r.Count},{(r.OutlineOn ? 1 : 0)},{(r.Rain ? 1 : 0)},{r.Pass},{r.AvgFps:F2},{r.Low1Fps:F2}," +
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

            float thickness = 3f;
            float scale = 1f;
            var feature = OutlineRendererFeature.Instance;
            if (feature != null)
            {
                thickness = feature.CurrentSettings.thicknessPx;
                scale = feature.CurrentSettings.maskResolutionScale;
            }
            float radiusMask = OutlineRendererFeature.EffectiveRadiusMaskTexels(thickness, scale);
            float thicknessScreen = OutlineRendererFeature.EffectiveThicknessScreenPx(thickness, scale);

            var rect = new Rect(12, 12, 480, 250);
            GUI.Box(rect, "Outline Perf (#45)");
            GUILayout.BeginArea(new Rect(20, 36, 460, 220));
            GUILayout.Label($"Count: {director?.AliveCount ?? 0} / target {director?.TargetCount ?? 0}");
            GUILayout.Label($"Outline: {(IsOutlineOn() ? "ON" : "OFF")}   Rain: {(OutlineWeather.IsRaining ? "ON" : "OFF")} ({OutlineWeather.RainAmount:F2})");
            GUILayout.Label($"Mask scale: {scale:F2}   radius(mask px): {radiusMask:F0}   実効太さ(screen px): {thicknessScreen:F1}");
            GUILayout.Label($"FPS avg: {avgFps:F1}   1% low: {low1:F1}");
            GUILayout.Label($"CPU main: {cpu:F2} ms   GPU: {gpu:F2} ms");
            GUILayout.Label($"DrawCalls: {draws}   SetPass: {setPass}   Batches: {batches}");
            GUILayout.Label(_sampling
                ? $"Sampling... {_sampleRemaining} frames left"
                : $"Samples stored: {_samples.Count}" + (_axisSweepActive ? " (sweeping)" : ""));
            GUILayout.Label("1/2/3=count  O=outline  F=rain  P=sample  C=csv");
            GUILayout.EndArea();
        }
    }
}
