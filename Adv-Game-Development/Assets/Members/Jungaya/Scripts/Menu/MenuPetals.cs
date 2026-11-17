using UnityEngine;

namespace Toufuku.Menu
{
    /*
        舞う花びら（この下にある絵を、ゆれながら落とす。下まで行ったら上へもどす）

        ・この GameObject は画面いっぱいに広げておく（落ちる範囲になる）
        ・花びらを足したり消したり、色や大きさを変えたりしてよい（起動したときに数えなおす）
    */
    public class MenuPetals : MonoBehaviour
    {
        [Tooltip("落ちる速さ（ピクセル/秒。この間でばらつく）")]
        public float fallMin = 40f;
        public float fallMax = 110f;
        [Tooltip("左右のゆれの大きさ（ピクセル/秒）")]
        public float sway = 40f;
        [Tooltip("右へ流れる速さ（ピクセル/秒）")]
        public float drift = 25f;
        [Tooltip("回る速さの上限（度/秒）")]
        public float spinMax = 120f;
        public int seed = 7;

        RectTransform[] _petals;
        Vector2[] _pos;
        float[] _fall, _phase, _spin;
        System.Random _rnd;

        void Awake()
        {
            _rnd = new System.Random(seed);
            int n = transform.childCount;
            _petals = new RectTransform[n];
            _pos = new Vector2[n];
            _fall = new float[n];
            _phase = new float[n];
            _spin = new float[n];
            for (int i = 0; i < n; i++)
            {
                _petals[i] = (RectTransform)transform.GetChild(i);
                _pos[i] = _petals[i].anchoredPosition;
                _fall[i] = Rand(fallMin, fallMax);
                _phase[i] = Rand(0f, 6.3f);
                _spin[i] = Rand(-spinMax, spinMax);
            }
        }

        void Update()
        {
            float time = Time.time, dt = Time.deltaTime;
            Vector2 half = ((RectTransform)transform).rect.size * 0.5f;
            for (int i = 0; i < _petals.Length; i++)
            {
                if (_petals[i] == null) continue;
                Vector2 p = _pos[i];
                p.y -= _fall[i] * dt;
                p.x += (Mathf.Sin(time * 1.3f + _phase[i]) * sway + drift) * dt;
                if (p.y < -half.y - 30f)
                {
                    p.y = half.y + 30f;
                    p.x = Rand(-half.x, half.x);
                }
                if (p.x > half.x + 30f) p.x = -half.x - 30f;
                _pos[i] = p;
                _petals[i].anchoredPosition = p;
                _petals[i].localRotation = Quaternion.Euler(0f, 0f, time * _spin[i] + _phase[i] * 57f);
            }
        }

        float Rand(float min, float max) => min + (float)_rnd.NextDouble() * (max - min);
    }
}
