using UnityEngine;

namespace Toufuku.Scenery
{
    /*
        時刻帯ひとつぶんのライティング（企画書 v8 9章「ライティング設計」：朝／昼／夕／夜の4基礎プロファイル）
          ・ここに入れるのは「ライト・環境光・空・フォグ」だけ。ポストエフェクト（色補正・露出）は入れない
            輪郭は AfterRenderingOpaques で合成されるので、ポストで暗くすると夜に輪郭の明度まで落ちる（13章・9章「必須情報の明度を変えない」）
          ・天候（雨・風・雪）はここに焼かず、あとからオーバーレイで重ねる（別Issue）
    */
    [CreateAssetMenu(menuName = "Toufuku/Scenery/Time Of Day Profile", fileName = "TimeOfDay_")]
    public class TimeOfDayProfile : ScriptableObject
    {
        public enum CelestialBody { Sun, Moon }

        [Header("主光源（太陽か月）")]
        [Tooltip("この時刻帯の主光源。太陽と月が切り替わるあいだは、片方が沈んでからもう片方が昇る。")]
        public CelestialBody body = CelestialBody.Sun;
        [Tooltip("高さ（度）。0 で地平線、90 で真上。")]
        [Range(-10f, 90f)] public float elevation = 45f;
        [Tooltip("向き（度）。180 でカメラの後ろから奥（-Z）へ照らす＝参拝客の正面に光が当たる。")]
        [Range(0f, 360f)] public float azimuth = 180f;
        [ColorUsage(false)] public Color lightColor = Color.white;
        [Min(0f)] public float lightIntensity = 1f;
        [Range(0f, 1f)] public float shadowStrength = 0.8f;

        [Header("環境光（Trilight）")]
        [ColorUsage(false, true)] public Color ambientSky = new Color(0.55f, 0.62f, 0.75f);
        [ColorUsage(false, true)] public Color ambientEquator = new Color(0.45f, 0.45f, 0.42f);
        [ColorUsage(false, true)] public Color ambientGround = new Color(0.25f, 0.23f, 0.2f);

        [Header("空（Skybox/Procedural）")]
        [ColorUsage(false)] public Color skyTint = new Color(0.5f, 0.5f, 0.5f);
        [ColorUsage(false)] public Color skyGround = new Color(0.37f, 0.35f, 0.34f);
        [Range(0f, 8f)] public float skyExposure = 1.3f;
        [Range(0f, 5f)] public float atmosphereThickness = 1f;

        [Header("フォグ（奥行き。遠い帯ほど少しかすむ）")]
        [ColorUsage(false)] public Color fogColor = new Color(0.7f, 0.75f, 0.8f);
        [Range(0f, 0.05f)] public float fogDensity = 0.008f;

        [Header("篝火・灯籠など境内の灯り")]
        [Tooltip("篝火のライトと灯籠の発光にかける倍率。昼は控えめ、夜は強く。")]
        [Min(0f)] public float practicalLightScale = 1f;
    }
}
