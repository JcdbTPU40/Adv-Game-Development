using UnityEngine;

namespace Toufuku.Rescue.Outline
{
    /*
        雨のときにアウトラインを弱くするための数値（static）

        雨のときは光を弱くする仕様なので、Compose シェーダーにグローバルなプロパティで渡す
        数値はぜんぶ仮。展示用のPCや実機で測って調整するつもり（Inspector の Tooltip にも書いてある）

        距離で弱くするときのふつうの値は、MockCrowdDirector の帯のならび（z=12〜33）に合わせてある
        near で 1.0、far で farIntensity まで、まっすぐ下げていく
    */
    public static class OutlineWeather
    {
        static readonly int RainAmountId = Shader.PropertyToID("_OutlineRainAmount");
        static readonly int RainIntensityScaleId = Shader.PropertyToID("_OutlineRainIntensityScale");
        static readonly int NearDistanceId = Shader.PropertyToID("_OutlineNearDistance");
        static readonly int FarDistanceId = Shader.PropertyToID("_OutlineFarDistance");
        static readonly int FarIntensityId = Shader.PropertyToID("_OutlineFarIntensity");

        // 雨の量 0〜1。0 が晴れ、1 が大雨くらい
        public static float RainAmount { get; set; }

        // 雨のときの強さの倍率（仮。ふつうは 0.45）
        public static float RainIntensityScale { get; set; } = 0.45f;

        // 距離で弱くするときの近いほうの距離（m）。ここまでは 1.0 倍（仮。ふつうは 12）
        public static float NearDistance { get; set; } = 12f;

        // 距離で弱くするときの遠いほうの距離（m）。ここで farIntensity になる（仮。ふつうは 33）
        public static float FarDistance { get; set; } = 33f;

        // いちばん遠いところでの強さの倍率（仮。ふつうは 0.55）
        public static float FarIntensity { get; set; } = 0.55f;

        // 雨モードかどうか（RainAmount が 0 より大きい）
        public static bool IsRaining => RainAmount > 0.001f;

        // 晴れと雨を切りかえる（計測 HUD の F キー用）
        public static void ToggleRain()
        {
            RainAmount = IsRaining ? 0f : 1f;
        }

        // Compose シェーダー用のグローバルな値を毎フレーム入れる
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
