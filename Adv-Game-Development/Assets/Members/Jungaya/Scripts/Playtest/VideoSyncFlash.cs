using UnityEngine;
using Toufuku.GameInput;

namespace Toufuku.Playtest
{
    /*
        外で撮った動画とログを、同じ時間のものさしで見られるようにする道具（#50）

        T0-CD は「外の動画に映った振りを『振ろうとした』の正しい基準にする」ので、動画のコマとログの行を
        照らし合わせられないと何も数えられない。そのために2つ出す:

        ・経過秒の表示: 画面のすみに大きく出す。カメラに画面が映るようにしておけば、
          動画をコマ送りするだけでログの「経過秒」の列が読める
        ・同期マーク: Flash で画面全体が一瞬光って、同時に短い音が鳴る
          ログには同じ時刻に「同期」の行が残る。画面が映っていなくても、光ったかべや音で合わせられる

        参加者ごとの始めと終わりに1回ずつ打つ（撮り始めと撮り終わりのズレを吸収するため）
    */
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

        // 表示する経過秒。進める側（CooldownTestDirector）が毎フレーム入れる
        public double ElapsedSeconds { get; set; }

        // 今までに打った同期マークの数
        public int MarkCount { get; private set; }

        double _flashEnd = double.NegativeInfinity;
        AudioClip _synthesized;
        GUIStyle _timecodeStyle;
        Texture2D _flashTexture;

        static double Now => Time.realtimeSinceStartupAsDouble;

        public bool IsFlashing => Now < _flashEnd;

        // 同期マークを打つ
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
