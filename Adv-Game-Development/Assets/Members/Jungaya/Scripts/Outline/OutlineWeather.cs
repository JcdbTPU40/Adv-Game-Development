using UnityEngine;

namespace Toufuku.Rescue.Outline
{
    /// <summary>
    /// 雨天時のアウトライン減衰パラメータ（static）。
    ///
    /// 雨天時は発光を弱める仕様のため、Compose シェーダへグローバルプロパティで渡す。
    /// 数値はすべて仮。展示機・実機計測で調整する前提（Inspector の Tooltip にも明記）。
    ///
    /// 距離減衰の既定は MockCrowdDirector の帯配置（z=12〜33）に合わせてある。
    /// near で 1.0、far で farIntensity まで線形に落とす。
    /// </summary>
    public static class OutlineWeather
    {
        static readonly int RainAmountId = Shader.PropertyToID("_OutlineRainAmount");
        static readonly int RainIntensityScaleId = Shader.PropertyToID("_OutlineRainIntensityScale");
        static readonly int NearDistanceId = Shader.PropertyToID("_OutlineNearDistance");
        static readonly int FarDistanceId = Shader.PropertyToID("_OutlineFarDistance");
        static readonly int FarIntensityId = Shader.PropertyToID("_OutlineFarIntensity");

        /// <summary>雨量 0〜1。0=晴天、1=豪雨相当。</summary>
        public static float RainAmount { get; set; }

        /// <summary>雨天時の強度倍率（仮。既定 0.45）。</summary>
        public static float RainIntensityScale { get; set; } = 0.45f;

        /// <summary>距離減衰の近端距離(m)。ここまでは倍率 1.0（仮。既定 12）。</summary>
        public static float NearDistance { get; set; } = 12f;

        /// <summary>距離減衰の遠端距離(m)。ここで farIntensity になる（仮。既定 33）。</summary>
        public static float FarDistance { get; set; } = 33f;

        /// <summary>遠端での強度倍率（仮。既定 0.55）。</summary>
        public static float FarIntensity { get; set; } = 0.55f;

        /// <summary>雨天モードか（RainAmount &gt; 0）。</summary>
        public static bool IsRaining => RainAmount > 0.001f;

        /// <summary>晴天／雨天を切り替える（計測HUDの F キー用）。</summary>
        public static void ToggleRain()
        {
            RainAmount = IsRaining ? 0f : 1f;
        }

        /// <summary>Compose シェーダ用のグローバルを毎フレーム流し込む。</summary>
        public static void ApplyGlobals()
        {
            Shader.SetGlobalFloat(RainAmountId, Mathf.Clamp01(RainAmount));
            Shader.SetGlobalFloat(RainIntensityScaleId, RainIntensityScale);
            Shader.SetGlobalFloat(NearDistanceId, NearDistance);
            Shader.SetGlobalFloat(FarDistanceId, FarDistance);
            Shader.SetGlobalFloat(FarIntensityId, FarIntensity);
        }
    }
}
