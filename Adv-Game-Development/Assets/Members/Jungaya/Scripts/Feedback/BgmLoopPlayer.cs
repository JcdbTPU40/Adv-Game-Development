using UnityEngine;

namespace Toufuku.Feedback
{
    /*
        BGM を流すクラス（#64。MVP では1つの環境につき1ループだけ）

        1本のループ曲を、始まったらずっと鳴らしつづける。曲の切りかえや、季節・月で変えるのは MVP ではやらない
    */
    [RequireComponent(typeof(AudioSource))]
    public class BgmLoopPlayer : MonoBehaviour
    {
        [Tooltip("この環境のループ素材（1 本だけ）。未設定なら何も鳴らさない")]
        [SerializeField] AudioClip loop;
        [SerializeField, Range(0f, 1f)] float volume = 0.5f;
        [SerializeField] bool playOnStart = true;

        AudioSource _source;

        public bool IsPlaying => _source != null && _source.isPlaying;

        void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = true;
            _source.spatialBlend = 0f;
        }

        void Start()
        {
            if (!playOnStart) return;
            if (loop == null)
            {
                Debug.Log("[BGM] ループ素材が未設定のため BGM は鳴りません（#64 素材待ち）", this);
                return;
            }
            Play();
        }

        public void Play()
        {
            if (loop == null) return;
            _source.clip = loop;
            _source.volume = volume;
            if (!_source.isPlaying) _source.Play();
        }

        public void Stop()
        {
            if (_source != null) _source.Stop();
        }
    }
}

