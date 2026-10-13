using UnityEngine;

namespace Toufuku.Scenery
{
    /*
        参道の篝火3つで進行を見せる（企画書 v8 8章「進行の可視化」：月が変わるごとに1つ灯る）
          ・灯っている数 = 今の月（1〜3）。セッション前は0
          ・タイマーUIは出さないので、子どもは「あと1つ」で残りが分かる
          ・炎の見た目はデザイン班のモデルに差しかえる前提で、ここは flame（オンオフ）と light（明るさ）だけ持つ
    */
    [ExecuteAlways]
    public class BonfireProgress : MonoBehaviour
    {
        [System.Serializable]
        public class Bonfire
        {
            [Tooltip("灯ったときだけ出す炎（モデル・パーティクル）。")]
            public GameObject flame;
            public Light light;
        }

        [Tooltip("手前から順に。1ヶ月目で [0] が灯る。")]
        [SerializeField] Bonfire[] bonfires = new Bonfire[3];
        [SerializeField] DayCycleController dayCycle;
        [SerializeField, Min(0f)] float baseIntensity = 6f;
        [Tooltip("ゆらぎの強さ（明るさの割合）。")]
        [SerializeField, Range(0f, 0.5f)] float flickerAmount = 0.15f;
        [SerializeField, Min(0f)] float flickerSpeed = 7f;
        [Tooltip("エディタやセッションがないときに灯しておく数。")]
        [SerializeField, Range(0, 3)] int previewLitCount = 3;

        public int LitCount { get; private set; }

        void Update()
        {
            LitCount = CurrentLitCount();
            float scale = dayCycle != null ? dayCycle.PracticalLightScale : 1f;
            float time = Application.isPlaying ? Time.time : 0f;

            for (int i = 0; i < bonfires.Length; i++)
            {
                var b = bonfires[i];
                if (b == null) continue;
                bool lit = i < LitCount;
                if (b.flame != null && b.flame.activeSelf != lit) b.flame.SetActive(lit);
                if (b.light == null) continue;
                b.light.enabled = lit;
                if (!lit) continue;
                // 篝火ごとに位相をずらして、そろってゆれないようにする
                float noise = Mathf.PerlinNoise(time * flickerSpeed, i * 17.3f) * 2f - 1f;
                b.light.intensity = baseIntensity * scale * (1f + flickerAmount * noise);
            }
        }

        int CurrentLitCount()
        {
            var session = Application.isPlaying ? GameSession.Instance : null;
            if (session == null) return previewLitCount;
            bool started = session.IsPlaying || session.IsResolving || session.IsFinished;
            return LitCountFor(started, session.CurrentMonth, bonfires.Length);
        }

        // 灯す数。始まる前は0、始まったら今の月の数（篝火の数まで）
        public static int LitCountFor(bool started, int month, int bonfireCount)
        {
            return started ? Mathf.Clamp(month, 0, bonfireCount) : 0;
        }
    }
}
