using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Toufuku.Rescue.Outline
{
    /*
        #45 の重さを調べるための HUD。ProfilerRecorder と FrameTimingManager で fps・CPU/GPU・DrawCalls などを出す

        キー:
          1/2/3: 人数を 8/12/16 にする
          O: アウトラインの ON/OFF（Feature だけの重さの差を見る）
          F: 雨の ON/OFF
          P: 計測の開始と停止（決めたフレーム数の平均）
          S: 人数ごとにまとめて測る（12条件を4周）
          C: CSV に書き出す
          G: グレースケールの ON/OFF（#59。色がわからなくても、もようで見分けられるかを見る）
          T: もようの ON/OFF（#59。OFF で全員実線＝色だけ）
          H: 照準が乗った客を次の人にする（#59。一段明るくなるのを見る）
          R: 欲張り客に1発目を当てたことにする／もとにもどす（#59。輪が2本→1本）
          F の雨は、まわり（ライト・環境光・背景）だけを暗くする。輪郭の明るさが変わらないことを見る（#59）

        ビルド版のコマンドライン:
          -outlineSweep[=frames,passes]: 起動したら自動でまとめて測り始める
          -outlineQuit: まとめて測り終わったら自動で終わる
    */
    public class OutlinePerfHud : MonoBehaviour
    {
        const int FrameBufferSize = 180;
        const int SampleFramesDefault = 120;

        [SerializeField] OutlinePerfDirector director;
        [SerializeField] int sampleFrames = SampleFramesDefault;

        [Header("計測条件（#45 実機計測）")]
        [Tooltip("ON なら vSync を切って fps 上限を外す。60fps 目標に対する余裕を見るため計測時は必須。"
               + "Editor では終了時に元の値へ戻す。")]
        [SerializeField] bool uncapFrameRate = true;

        int _savedVSyncCount = -1;
        int _savedTargetFrameRate;
        bool _quitAfterSweep;

        readonly float[] _frameTimes = new float[FrameBufferSize];
        int _frameWrite;
        int _frameCount;

        ProfilerRecorder _drawCalls;
        ProfilerRecorder _setPassCalls;
        ProfilerRecorder _batches;

        bool _sampling;
        int _sampleRemaining;
        readonly List<SampleRow> _samples = new List<SampleRow>(64);

        // 最近のデータを足していったもの
        double _accFps;
        double _accCpu;
        double _accGpu;
        double _accDraw;
        double _accSetPass;
        double _accBatches;
        int _accN;
        readonly List<float> _sampleFrameMs = new List<float>(256);

        FrameTiming[] _timings = new FrameTiming[1];

        // #59: グレースケール表示と、雨のときのまわりの暗さ
        const float RainLightScale = 0.4f;
        bool _grayscale;
        Volume _grayVolume;
        VolumeProfile _grayProfile;
        bool? _savedPostProcessing;
        bool _rainLookApplied;
        Light _sun;
        float _sunIntensity;
        Color _ambient;
        Color _background;

        struct SampleRow
        {
            public int Count;
            public bool OutlineOn;
            public bool Rain;
            public int Pass; // 何周目か。手で測ったときは -1
            public float AvgFps;
            public float Low1Fps;
            public float CpuMs;
            public float GpuMs;
            public float DrawCalls;
            public float SetPassCalls;
            public float Batches;
        }

        void Awake()
        {
            if (!uncapFrameRate) return;
            // vSync が効いていると fps が画面のリフレッシュレートにくっついてしまって、60fps に対してどれくらい余裕があるかわからない
            _savedVSyncCount = QualitySettings.vSyncCount;
            _savedTargetFrameRate = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
        }

        void OnDestroy()
        {
            if (_grayProfile != null) Destroy(_grayProfile);

            // エディタで動かすときは QualitySettings がプロジェクトのファイルなので、必ずもとにもどす
            if (_savedVSyncCount < 0) return;
            QualitySettings.vSyncCount = _savedVSyncCount;
            Application.targetFrameRate = _savedTargetFrameRate;
        }

        void Start()
        {
            Debug.Log($"[OutlinePerf] persistentDataPath = {Application.persistentDataPath}");
            ParseCommandLine();
        }

        /*
            ビルド版でキーを押さずにまとめて測るための、コマンドラインの読み取り
            例: OutlinePerf.exe -outlineSweep=60,4 -outlineQuit
        */
        void ParseCommandLine()
        {
            string[] args;
            try { args = System.Environment.GetCommandLineArgs(); }
            catch { return; }
            if (args == null) return;

            int frames = 60;
            int passes = 4;
            bool sweep = false;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "-outlineQuit") { _quitAfterSweep = true; continue; }
                if (!a.StartsWith("-outlineSweep")) continue;

                sweep = true;
                int eq = a.IndexOf('=');
                if (eq < 0 || eq + 1 >= a.Length) continue;

                string[] parts = a.Substring(eq + 1).Split(',');
                if (parts.Length > 0) int.TryParse(parts[0], out frames);
                if (parts.Length > 1) int.TryParse(parts[1], out passes);
            }

            if (!sweep) return;
            Debug.Log($"[OutlinePerf] コマンドラインからスイープ開始（{frames}f × {passes}巡, quit={_quitAfterSweep}）");
            RunAxisSweep(frames, passes);
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
            SyncRainLook();
            TickAxisSweep();

            if (_sampling)
            {
                AccumulateSample(dt);
                _sampleRemaining--;
                if (_sampleRemaining <= 0)
                    StopSampling(store: true);
            }
        }

        // 測っている最中かどうか（自動でまとめて測る用）
        public bool IsSampling => _sampling;

        // たまっているデータの数
        public int StoredSampleCount => _samples.Count;

        // コードから計測を始める（Pキーと同じ）
        public void BeginSample(int frames = -1)
        {
            if (frames > 0) sampleFrames = frames;
            if (_sampling) StopSampling(store: false);
            StartSampling();
        }

        // データの記録を消す
        public void ClearSamples() => _samples.Clear();

        // いちばん新しいデータを1行の文字にして返す（なければ空の文字）
        public string FormatLastSample()
        {
            if (_samples.Count == 0) return string.Empty;
            var r = _samples[_samples.Count - 1];
            return $"count={r.Count} outline={(r.OutlineOn ? "ON" : "OFF")} rain={(r.Rain ? "ON" : "OFF")} " +
                   $"avgFps={r.AvgFps:F1} 1%low={r.Low1Fps:F1} cpu={r.CpuMs:F2}ms gpu={r.GpuMs:F2}ms " +
                   $"draws={r.DrawCalls:F0} setPass={r.SetPassCalls:F0} batches={r.Batches:F0}";
        }

        /*
            人数×アウトライン×天気の12条件を自動で測る（#45 H）
            順番をシャッフルしてから何周かして、ONとOFFの差とノイズのはばで人数による差を判断する
            framesPerSample: 1条件で平均をとるフレーム数（60以上がおすすめ）
            passCount: 何周するか（4以上がおすすめ）
        */
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
                    if (_quitAfterSweep) Application.Quit();
                    break;
            }
        }

        /*
            ONとOFFの差を人数×天気ごとにまとめて、周ごとのばらつき（標準偏差）でノイズのはばを出す
            判断は「差がノイズよりはっきり大きいか」と「差が人数で変わらないか」の2つで見る
        */
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

                // 周ごとに ON と OFF をペアにする（シャッフルした順番でも Pass の番号で合わせる）
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

            // 判断: 晴れのときの GPU の差が人数でどう変わるか＋ノイズのはばとくらべる
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
                // ノイズのはばの目安: 両方の標準偏差を合わせたもの（ざっくり）
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

            // 差の CSV も persistentDataPath に書く
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
                    _samplePassTag = -1; // 手で測った
                    StartSampling();
                }
            }

            if (Input.GetKeyDown(KeyCode.S))
                RunAxisSweep();

            if (Input.GetKeyDown(KeyCode.C))
                ExportCsv();

            if (Input.GetKeyDown(KeyCode.G))
                SetGrayscale(!_grayscale);

            if (Input.GetKeyDown(KeyCode.T))
                director?.SetPatternsEnabled(!director.PatternsEnabled);

            if (Input.GetKeyDown(KeyCode.H))
                director?.CycleHighlight();

            if (Input.GetKeyDown(KeyCode.R))
                director?.AdvanceGreedy();
        }

        /*
            #59: グレースケールの ON/OFF。色がわからなくても、もようだけでお守りを見分けられるかを見る（完了条件）
            URP の Color Adjustments（彩度 -100）をのせた Volume を、はじめて使うときに作る
            ポストプロセスは輪郭の合成（AfterRenderingOpaques）より後なので、輪郭もいっしょに灰色になる
        */
        void SetGrayscale(bool on)
        {
            _grayscale = on;
            if (on && _grayVolume == null)
            {
                var go = new GameObject("GrayscaleVolume (#59)");
                _grayVolume = go.AddComponent<Volume>();
                _grayVolume.isGlobal = true;
                _grayVolume.priority = 100f;
                _grayProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                var adjustments = _grayProfile.Add<ColorAdjustments>(true);
                adjustments.saturation.Override(-100f);
                _grayVolume.sharedProfile = _grayProfile;
            }
            if (_grayVolume != null) _grayVolume.enabled = on;

            var cam = Camera.main;
            if (cam != null)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                if (_savedPostProcessing == null) _savedPostProcessing = data.renderPostProcessing;
                data.renderPostProcessing = on || _savedPostProcessing.Value;
            }
        }

        /*
            #59: 雨のときは、まわり（ライト・環境光・背景）だけを暗くする。天候オーバーレイのかわり
            輪郭は発光の合成なので、まわりが暗くなっても輪郭の明るさは変わらない（v8 6章「天候中も輪郭の明度を維持する」）
            スイープで雨を切りかえたときも同じ見た目になるように、キーではなく毎フレーム RainAmount に合わせる
        */
        void SyncRainLook()
        {
            bool raining = OutlineWeather.IsRaining;
            if (raining == _rainLookApplied) return;
            _rainLookApplied = raining;

            if (_sun == null) _sun = FindFirstObjectByType<Light>();
            var cam = Camera.main;
            if (raining)
            {
                if (_sun != null)
                {
                    _sunIntensity = _sun.intensity;
                    _sun.intensity *= RainLightScale;
                }
                _ambient = RenderSettings.ambientLight;
                RenderSettings.ambientLight = _ambient * RainLightScale;
                if (cam != null)
                {
                    _background = cam.backgroundColor;
                    cam.backgroundColor = _background * RainLightScale;
                }
            }
            else
            {
                if (_sun != null) _sun.intensity = _sunIntensity;
                RenderSettings.ambientLight = _ambient;
                if (cam != null) cam.backgroundColor = _background;
            }
        }

        void ToggleOutline()
        {
            var feature = OutlineRendererFeature.Instance;
            if (feature != null)
            {
                feature.OutlineEnabled = !feature.OutlineEnabled;
                return;
            }

            // Feature の参照がないときは RendererData からさがす
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset asset)
            {
                /*
                    defaultRenderer をリフレクションで取るのはバージョンによってちがうので、
                    計測シーンで Feature が登録されていれば Instance が入る、というやり方にしている
                */
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
            // エディタのドメインリロードの直後や、ツールを呼んだときの一時的なカクつきは計測に入れない
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

        /*
            1% low = いちばん悪い 1% のフレームの平均の fps
            データが少ないときは、少なくともいちばん悪い1フレームは使う
        */
        static float ComputeOnePercentLow(List<float> frameMs)
        {
            if (frameMs == null || frameMs.Count == 0) return 0f;
            var sorted = new List<float>(frameMs);
            sorted.Sort(); // 小さい順＝速い順。最後がいちばん遅い
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

            float band = 0f;
            float gap = 0f;
            float scale = 1f;
            int radius1 = 0;
            int radius2 = 0;
            var feature = OutlineRendererFeature.Instance;
            if (feature != null)
            {
                band = feature.CurrentSettings.bandWidthPx;
                gap = feature.CurrentSettings.ringGapPx;
                scale = feature.CurrentSettings.maskResolutionScale;
                radius1 = feature.SearchRadiusMaskTexels(1);
                radius2 = feature.SearchRadiusMaskTexels(2);
            }

            var rect = new Rect(12, 12, 540, 345);
            GUI.Box(rect, "Outline Perf (#45 / #59)");
            GUILayout.BeginArea(new Rect(20, 36, 520, 315));
            GUILayout.Label($"Count: {director?.AliveCount ?? 0} / target {director?.TargetCount ?? 0}");
            GUILayout.Label($"Outline: {(IsOutlineOn() ? "ON" : "OFF")}   Rain: {(OutlineWeather.IsRaining ? "ON" : "OFF")} ({OutlineWeather.RainAmount:F2})");
            GUILayout.Label($"Mask scale: {scale:F2}   輪のはば: {band:F1}px  すきま: {gap:F1}px   さがす半径(mask px): 1本 {radius1} / 2本 {radius2}");
            string aimLabel = director != null && director.HighlightIndex >= 0 ? director.HighlightIndex.ToString() : "-";
            GUILayout.Label($"Pattern: {(director != null && director.PatternsEnabled ? "ON" : "OFF(色だけ)")}   Gray: {(_grayscale ? "ON" : "OFF")}   " +
                            $"照準: {aimLabel}   欲張り R: {(director != null ? director.GreedyRemaining : 0)}（{(director != null ? director.GreedyCount : 0)}人）");
            GUILayout.Label($"FPS avg: {avgFps:F1}   1% low: {low1:F1}");
            GUILayout.Label($"CPU main: {cpu:F2} ms   GPU: {gpu:F2} ms");
            GUILayout.Label($"DrawCalls: {draws}   SetPass: {setPass}   Batches: {batches}");
            GUILayout.Label(_sampling
                ? $"Sampling... {_sampleRemaining} frames left"
                : $"Samples stored: {_samples.Count}" + (_axisSweepActive ? " (sweeping)" : ""));
            GUILayout.Label("1/2/3=count  O=outline  F=rain  P=sample  S=sweep  C=csv");
            GUILayout.Label("G=grayscale  T=pattern  H=aim  R=greedy hit");
            GUILayout.Label($"vSync={QualitySettings.vSyncCount}  targetFps={Application.targetFrameRate}");
            GUILayout.EndArea();
        }
    }
}
