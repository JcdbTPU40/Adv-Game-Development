using UnityEngine;

namespace Toufuku.Scenery
{
    /*
        境内の飾りの狐火。ゲームの情報は持たない（当たり判定・得点・客とは無関係）
          ・子の ParticleSystem 1つが狐火1つ。置いた位置のまわりをゆっくり漂い、上下にゆれる
          ・明るさは DayCycleController.PracticalLightScale（灯籠・篝火と同じ倍率）に合わせて、夕方から灯り、夜にいちばん強くなる
          ・客を隠さないため、置き場所は README「差し替えのルール」と同じく |x| ≥ 9.5 か鳥居より奥（z < 7）
          ・色は輪郭5色とまぎれないように、彩度を落とした青白にする（学業の青 #2F7FD8 と並べても見分けがつく）
    */
    public class KitsunebiWisps : MonoBehaviour
    {
        [SerializeField] DayCycleController dayCycle;
        [Tooltip("空なら子の ParticleSystem をすべて使う。")]
        [SerializeField] ParticleSystem[] wisps;

        [Header("明るさ")]
        [Tooltip("PracticalLightScale がこの値以下なら、昼の明るさ（dayVisibility）にする。")]
        [SerializeField, Min(0f)] float dimScale = 0.3f;
        [Tooltip("PracticalLightScale がこの値以上なら、いちばん明るくする。")]
        [SerializeField, Min(0f)] float fullScale = 1.6f;
        [Tooltip("昼でもうっすら見せる割合。0 なら昼は消える。")]
        [SerializeField, Range(0f, 1f)] float dayVisibility = 0.1f;
        [SerializeField, Min(0f)] float emissionRate = 28f;
        [Tooltip("ちらつきの強さ（明るさの割合）。")]
        [SerializeField, Range(0f, 0.5f)] float flickerAmount = 0.25f;
        [SerializeField, Min(0f)] float flickerSpeed = 3f;

        [Header("ただよい")]
        [Tooltip("置いた位置から横に離れる距離（m）。|x| ≥ 9.5 を守れる範囲にする。")]
        [SerializeField, Min(0f)] float wanderRadius = 0.35f;
        [SerializeField, Min(0f)] float wanderSpeed = 0.25f;
        [SerializeField, Min(0f)] float bobHeight = 0.25f;
        [SerializeField, Min(0f)] float bobSpeed = 0.8f;

        Vector3[] _anchors;

        void Awake()
        {
            if (wisps == null || wisps.Length == 0) wisps = GetComponentsInChildren<ParticleSystem>();
            _anchors = new Vector3[wisps.Length];
            for (int i = 0; i < wisps.Length; i++)
                if (wisps[i] != null) _anchors[i] = wisps[i].transform.localPosition;
        }

        void Update()
        {
            float scale = dayCycle != null ? dayCycle.PracticalLightScale : fullScale;
            float visibility = VisibilityFor(scale, dimScale, fullScale, dayVisibility);
            float t = Time.time;

            for (int i = 0; i < wisps.Length; i++)
            {
                var ps = wisps[i];
                if (ps == null) continue;

                // 狐火ごとに位相をずらして、そろって動かないようにする
                float seed = i * 13.7f;
                float wx = Mathf.PerlinNoise(t * wanderSpeed, seed) * 2f - 1f;
                float wz = Mathf.PerlinNoise(seed, t * wanderSpeed) * 2f - 1f;
                float bob = Mathf.Sin(t * bobSpeed * Mathf.PI * 2f + seed) * bobHeight;
                ps.transform.localPosition = _anchors[i] + new Vector3(wx * wanderRadius, bob, wz * wanderRadius);

                float flicker = 1f + flickerAmount * (Mathf.PerlinNoise(t * flickerSpeed, seed + 5f) * 2f - 1f);
                float a = Mathf.Clamp01(visibility * flicker);

                var main = ps.main;
                Color c = main.startColor.color;
                c.a = a;
                main.startColor = c;

                var emission = ps.emission;
                emission.rateOverTime = visibility > 0f ? emissionRate : 0f;
            }
        }

        // 灯籠・篝火の倍率から、狐火の見え方（0〜1）を出す
        public static float VisibilityFor(float practicalScale, float dimScale, float fullScale, float dayVisibility)
        {
            float k = fullScale > dimScale ? Mathf.InverseLerp(dimScale, fullScale, practicalScale) : (practicalScale >= fullScale ? 1f : 0f);
            return Mathf.Lerp(dayVisibility, 1f, k);
        }
    }
}
