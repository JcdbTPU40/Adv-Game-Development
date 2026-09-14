using UnityEngine;

namespace Toufuku.Feedback
{
    /// <summary>
    /// BGM — Issue #64（MVP 範囲: 1 環境につき 1 ループだけ）
    ///
    /// 1 本のループ素材を開始と同時に鳴らし続ける。曲の切り替え・季節や月での変化は MVP 範囲外。
    /// </summary>
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

