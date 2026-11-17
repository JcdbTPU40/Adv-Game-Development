using UnityEngine;
using UnityEngine.UI;

namespace Toufuku.Menu
{
    /*
        この下にある絵（Image など）を、ぜんぶ明滅させる（星・参道の金の点）

        ・数が多くて AnimationClip では扱いにくいので、数値で動かす
        ・randomPhase が ON なら、1つずつばらばらに（星）。OFF なら phaseStep ずつずらして、波のように（金の点）
        ・絵を足したり消したりしてよい（起動したときに、下にあるものを数えなおす）
    */
    public class MenuTwinkle : MonoBehaviour
    {
        [Range(0f, 1f)] public float minAlpha = 0.25f;
        [Range(0f, 1f)] public float maxAlpha = 0.8f;
        [Tooltip("明滅の速さ（この間でばらつく）")]
        public float speedMin = 0.6f;
        public float speedMax = 2.2f;
        [Tooltip("ON なら、1つずつばらばらのタイミングで")]
        public bool randomPhase = true;
        [Tooltip("randomPhase が OFF のとき、ならびの順にこれだけずらす（波のように見える）")]
        public float phaseStep = 0f;
        public int seed = 7;

        Graphic[] _graphics;
        float[] _phase, _speed;

        void Awake()
        {
            var list = new System.Collections.Generic.List<Graphic>();
            foreach (Graphic g in GetComponentsInChildren<Graphic>(true))
                if (g.gameObject != gameObject) list.Add(g);
            _graphics = list.ToArray();

            var rnd = new System.Random(seed);
            _phase = new float[_graphics.Length];
            _speed = new float[_graphics.Length];
            for (int i = 0; i < _graphics.Length; i++)
            {
                _phase[i] = randomPhase ? (float)rnd.NextDouble() * 6.3f : i * phaseStep;
                _speed[i] = Mathf.Lerp(speedMin, speedMax, (float)rnd.NextDouble());
            }
        }

        void Update()
        {
            float time = Time.time;
            for (int i = 0; i < _graphics.Length; i++)
            {
                Graphic g = _graphics[i];
                if (g == null) continue;
                Color c = g.color;
                c.a = Mathf.Lerp(minAlpha, maxAlpha, 0.5f + 0.5f * Mathf.Sin(time * _speed[i] + _phase[i]));
                g.color = c;
            }
        }
    }
}
