using UnityEngine;
using Toufuku.GameInput;

namespace Toufuku.Playtest
{
    /// <summary>
    /// 外部動画とログを同じ時間軸に乗せる道具 — Issue #50
    ///
    /// T0-CD は「外部動画の振りピークを意図の正本にする」ので、動画のコマとログの行を
    /// 突き合わせられないと何も数えられない。そのために 2 つ出す:
    ///
    /// ・<b>経過秒の表示</b>: 画面の隅に大きく出す。カメラの画角に画面を入れておけば、
    ///   動画のコマ送りでそのままログの「経過秒」列が読める。
    /// ・<b>同期マーク</b>: <see cref="Flash"/> で画面全体が一瞬光り、同時に短い音が鳴る。
    ///   ログには同じ時刻の「同期」行が残る。画面が映っていない画角でも、光った壁や音で合わせられる。
    ///
    /// 参加者ごとの始めと終わりに 1 回ずつ打つ（撮り始めと撮り終わりのズレを吸収するため）。
    /// </summary>
    public class VideoSyncFlash : MonoBehaviour
    {
        [Header("同期マーク")]
        [SerializeField, Min(0.02f)] float flashSeconds = 0.20f;
        [SerializeField] Color flashColor = Color.white;
        [SerializeField] AudioSource seSource;
        [SerializeField] AudioClip beep;
        [Tooltip("beep が未設定のとき、短い高音（1760Hz・0.12 秒）を合成して鳴らす")]
        [SerializeField] bool synthesizeBeep = true;

        [Header("経過秒の表示（動画に映す）")]
        [SerializeField] bool showTimecode = true;
        [SerializeField, Min(10)] int timecodeFontSize = 30;

        /// <summary>表示する経過秒。進行側（<see cref="CooldownTestDirector"/>）が毎フレーム入れる。</summary>
        public double ElapsedSeconds { get; set; }

        /// <summary>これまでに打った同期マークの数。</summary>
        public int MarkCount { get; private set; }

        double _flashEnd = double.NegativeInfinity;
        AudioClip _synthesized;
        GUIStyle _timecodeStyle;
        Texture2D _flashTexture;

        static double Now => Time.realtimeSinceStartupAsDouble;

        public bool IsFlashing => Now < _flashEnd;

        /// <summary>同期マークを打つ。</summary>
        public void Flash()
        {
            MarkCount++;
            _flashEnd = Now + flashSeconds;

            if (seSource == null) return;
            AudioClip clip = beep;
            if (clip == null && synthesizeBeep)
            {
                if (_synthesized == null)
                    _synthesized = ProceduralTone.Create("VideoSyncBeep", 1760f, 0.12f, 0.5f);
                clip = _synthesized;
            }
            if (clip != null) seSource.PlayOneShot(clip);
        }

        void OnDestroy()
        {
            if (_synthesized != null) Destroy(_synthesized);
            if (_flashTexture != null) Destroy(_flashTexture);
        }

        void OnGUI()
        {
            if (IsFlashing)
            {
                if (_flashTexture == null)
                {
                    _flashTexture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                    _flashTexture.SetPixel(0, 0, Color.white);
                    _flashTexture.Apply();
                }
                Color before = GUI.color;
                GUI.color = flashColor;
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _flashTexture);
                GUI.color = before;
            }

            if (!showTimecode) return;

            if (_timecodeStyle == null)
            {
                _timecodeStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = timecodeFontSize,
                    alignment = TextAnchor.UpperRight,
                    fontStyle = FontStyle.Bold
                };
            }

            var rect = new Rect(Screen.width - 260f, 8f, 250f, timecodeFontSize + 10f);
            GUI.Label(rect, $"T {ElapsedSeconds:0.000}", _timecodeStyle);
        }
    }
}
