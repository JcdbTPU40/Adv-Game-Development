using System.Collections.Generic;
using UnityEngine;

namespace Toufuku.Rescue.Mock
{
    /// <summary>
    /// 視認性モック(#44)用の頭上ゲージ。既存 Test/CustomerGaugeHud の派生版（既存ファイルは無改変）。
    ///
    /// ── なぜ既存 CustomerGaugeHud をそのまま使わないか ────────────────────
    ///   既存版を 12〜15 体で使うと、次の3点が検証の邪魔になると判断した。
    ///     1) OnGUI の中で毎回 FindObjectsByType している。OnGUI は 1 フレームに
    ///        複数回（Layout/Repaint ほか）呼ばれるため、体数ぶんの全探索が何度も走る。
    ///        → Director が持つ登録リストを読み、描画は Repaint のときだけにした。
    ///     2) バー幅が固定ピクセル。遠近感が消え、密集すると隣のバーと重なって読めない。
    ///        → 距離スケールを ON/OFF できるようにした。どちらが読めるかが #44 の検証項目。
    ///     3) 描画順が探索順まかせ。手前の客のバーが奥の客のバーに隠れることがある。
    ///        → 奥→手前にソートしてから描く。
    ///   加えて既存版には黒客（黒ゲージ）の概念が無いため、#44 の検証ができない。
    ///
    /// ※ 検証用の使い捨て。Mock/ ごと削除できる。
    /// </summary>
    public class MockGaugeHud : MonoBehaviour
    {
        private struct DrawItem
        {
            public float Depth;      // カメラからの距離（奥→手前ソート用）
            public Rect Rect;
            public float Fill;       // 0〜1
            public bool Resolved;
            public bool Black;
            public Color Tint;
        }

        [Header("参照")]
        [Tooltip("客の一覧を持つ Director。未設定ならシーンから自動取得。")]
        [SerializeField] private MockCrowdDirector director;

        [Tooltip("描画に使うカメラ。未設定なら Camera.main。")]
        [SerializeField] private Camera targetCamera;

        [Header("バー配置")]
        [Tooltip("客の頭上どれだけ上に出すか（ワールド単位）。")]
        [SerializeField] private float worldHeightOffset = 2.0f;

        [Tooltip("基準距離でのバー幅(px)。")]
        [SerializeField] private float barWidth = 80f;

        [Tooltip("基準距離でのバー高さ(px)。")]
        [SerializeField] private float barHeight = 10f;

        [Header("距離スケール（#44 の検証項目）")]
        [Tooltip("ON なら遠い客のバーを小さくする。OFF は既存版と同じ固定ピクセル幅。")]
        [SerializeField] private bool scaleWithDistance = true;

        [Tooltip("この距離(m)でバーが barWidth ちょうどになる。")]
        [SerializeField] private float referenceDistance = 20f;

        [Tooltip("スケール倍率の下限・上限。遠すぎ／近すぎでの破綻よけ。")]
        [SerializeField] private Vector2 scaleClamp = new Vector2(0.45f, 1.8f);

        [Header("色（赤系で統一：満ちる＝悪い）")]
        [SerializeField] private Color emptyColor = new Color(1.0f, 0.75f, 0.4f);
        [SerializeField] private Color fullColor = new Color(0.95f, 0.15f, 0.15f);
        [SerializeField] private Color backColor = new Color(0f, 0f, 0f, 0.6f);

        [Header("黒客のゲージ（黒輪郭・黒ゲージ）")]
        [Tooltip("黒客のゲージ色（残量が少ないとき）。")]
        [SerializeField] private Color blackEmptyColor = new Color(0.45f, 0.45f, 0.48f);

        [Tooltip("黒客のゲージ色（満タンに近いとき）。")]
        [SerializeField] private Color blackFullColor = new Color(0.06f, 0.06f, 0.08f);

        [Tooltip("黒客のゲージ背景。黒ゲージが背景に埋もれないよう明るめにできる。")]
        [SerializeField] private Color blackBackColor = new Color(0.85f, 0.85f, 0.88f, 0.7f);

        [Header("0で光る演出（解消時）")]
        [SerializeField] private Color glowColor = new Color(1f, 0.95f, 0.6f);
        [SerializeField] private float glowPulseSpeed = 6f;

        [Header("追加の識別手がかり（既定OFF：輪郭だけで判別できるかを測るため）")]
        [Tooltip("ON にするとバーの外枠を客の割り当て色で塗る。輪郭以外の手がかりを足したい比較用。")]
        [SerializeField] private bool tintBorderByCustomerColor;

        [Tooltip("外枠の太さ(px)。")]
        [SerializeField] private float borderThickness = 2f;

        private readonly List<DrawItem> _items = new List<DrawItem>();
        private Texture2D _tex;

        private static readonly System.Comparison<DrawItem> FarToNear =
            (a, b) => b.Depth.CompareTo(a.Depth);

        /// <summary>距離スケールが有効か（デバッグ表示用）。</summary>
        public bool ScaleWithDistance => scaleWithDistance;

        /// <summary>距離スケールを切り替える（デバッグ操作から呼ばれる）。</summary>
        public void ToggleScaleWithDistance() => scaleWithDistance = !scaleWithDistance;

        private void Awake()
        {
            _tex = Texture2D.whiteTexture;
            if (director == null) director = FindFirstObjectByType<MockCrowdDirector>();
        }

        private void OnGUI()
        {
            // Layout など Repaint 以外のイベントでは描かない（OnGUI は1フレームに複数回来る）。
            if (Event.current.type != EventType.Repaint) return;
            if (director == null) return;

            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null) return;

            _items.Clear();
            CollectItems(cam);

            // 奥→手前の順に描く＝手前の客のバーが必ず上に来る。
            _items.Sort(FarToNear);

            for (int i = 0; i < _items.Count; i++)
                DrawItemAt(_items[i]);

            GUI.color = Color.white;
        }

        private void CollectItems(Camera cam)
        {
            IReadOnlyList<MockCrowdDirector.Member> members = director.Members;

            for (int i = 0; i < members.Count; i++)
            {
                MockCrowdDirector.Member m = members[i];
                if (m == null || m.Go == null || m.Mood == null) continue;
                if (m.Mood.IsAngry) continue;   // 怒り（失敗）はバーを描かない

                Vector3 worldPos = m.Tr.position + Vector3.up * worldHeightOffset;
                Vector3 sp = cam.WorldToScreenPoint(worldPos);
                if (sp.z <= 0f) continue;       // カメラ後方

                float scale = 1f;
                if (scaleWithDistance && referenceDistance > 0.001f)
                {
                    float dist = Vector3.Distance(cam.transform.position, m.Tr.position);
                    scale = Mathf.Clamp(referenceDistance / Mathf.Max(0.001f, dist),
                                        scaleClamp.x, scaleClamp.y);
                }

                float w = barWidth * scale;
                float h = barHeight * scale;

                // スクリーン座標 → GUI座標（Yを反転）
                float x = sp.x - w * 0.5f;
                float y = (Screen.height - sp.y) - h * 0.5f;

                bool black = m.Tag != null && m.Tag.IsBlack;

                _items.Add(new DrawItem
                {
                    Depth = sp.z,
                    Rect = new Rect(x, y, w, h),
                    Fill = Mathf.Clamp01(m.Mood.GaugeNormalized),
                    Resolved = m.Mood.IsResolved,
                    Black = black,
                    Tint = m.Tag != null ? m.Tag.AssignedColor : Color.white,
                });
            }
        }

        private void DrawItemAt(DrawItem item)
        {
            Rect r = item.Rect;

            // 外枠（既定OFF。輪郭以外の識別手がかりを足したい比較用）
            if (tintBorderByCustomerColor)
            {
                GUI.color = item.Tint;
                float b = borderThickness;
                GUI.DrawTexture(new Rect(r.x - b, r.y - b, r.width + b * 2f, r.height + b * 2f), _tex);
            }

            // 背景
            GUI.color = item.Black ? blackBackColor : backColor;
            GUI.DrawTexture(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), _tex);

            if (item.Resolved)
            {
                // 解消＝0で光る演出。
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * glowPulseSpeed * Mathf.PI * 2f);
                Color g = glowColor;
                g.a = Mathf.Lerp(0.4f, 1f, pulse);
                GUI.color = g;
                GUI.DrawTexture(r, _tex);
                return;
            }

            // 通常：満ち具合を表示。黒客だけは黒系のグラデにする。
            Color from = item.Black ? blackEmptyColor : emptyColor;
            Color to = item.Black ? blackFullColor : fullColor;

            GUI.color = Color.Lerp(from, to, item.Fill);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * item.Fill, r.height), _tex);
        }
    }
}
