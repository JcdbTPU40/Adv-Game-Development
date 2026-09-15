using System.Collections.Generic;
using UnityEngine;

namespace Toufuku.Rescue.Mock
{
    /*
        視認性モック（#44）用の、頭の上のゲージ。もともとある Test/CustomerGaugeHud をもとに作ったもの（もとのファイルはさわっていない）

        ---- なんでもとの CustomerGaugeHud をそのまま使わないのか ----
          もとのほうを 12〜15人で使うと、次の3つが検証のじゃまになると思った
            1) OnGUI の中で毎回 FindObjectsByType している。OnGUI は1フレームに
               何回か（Layout や Repaint など）呼ばれるので、人数ぶんの全部さがしが何回も動く
               → Director が持っているリストを読むようにして、描くのは Repaint のときだけにした
            2) バーのはばがピクセルで固定。遠い近いがわからなくなるし、集まるととなりのバーと重なって読めない
               → 距離に合わせて大きさを変えるのをオンオフできるようにした。どっちが読みやすいかが #44 で調べること
            3) 描く順番がさがした順番まかせ。手前の客のバーが、奥の客のバーにかくれることがある
               → 奥から手前の順にならべてから描く
          それと、もとのほうには黒客（黒いゲージ）がないので、#44 の検証ができない

        ※ 検証用の使い捨て。Mock/ フォルダごと消せる
    */
    public class MockGaugeHud : MonoBehaviour
    {
        private struct DrawItem
        {
            public float Depth;      // カメラからの距離（奥から手前にならべる用）
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

        // 距離に合わせて大きさを変えるのが ON かどうか（デバッグ表示用）
        public bool ScaleWithDistance => scaleWithDistance;

        // 距離に合わせて大きさを変えるのを切りかえる（デバッグ操作から呼ばれる）
        public void ToggleScaleWithDistance() => scaleWithDistance = !scaleWithDistance;

        private void Awake()
        {
            _tex = Texture2D.whiteTexture;
            if (director == null) director = FindFirstObjectByType<MockCrowdDirector>();
        }

        private void OnGUI()
        {
            // Layout とか Repaint じゃないイベントのときは描かない（OnGUI は1フレームに何回も来るから）
            if (Event.current.type != EventType.Repaint) return;
            if (director == null) return;

            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null) return;

            _items.Clear();
            CollectItems(cam);

            // 奥から手前の順に描く＝手前の客のバーが必ず上に来る
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
                if (m == null || m.Go == null || m.State == null) continue;
                if (m.State.IsBlack) continue;  // 黒客はバーを描かない（頭の上のゲージは消す。v8 6章）

                Vector3 worldPos = m.Tr.position + Vector3.up * worldHeightOffset;
                Vector3 sp = cam.WorldToScreenPoint(worldPos);
                if (sp.z <= 0f) continue;       // カメラのうしろ

                float scale = 1f;
                if (scaleWithDistance && referenceDistance > 0.001f)
                {
                    float dist = Vector3.Distance(cam.transform.position, m.Tr.position);
                    scale = Mathf.Clamp(referenceDistance / Mathf.Max(0.001f, dist),
                                        scaleClamp.x, scaleClamp.y);
                }

                float w = barWidth * scale;
                float h = barHeight * scale;

                // スクリーン座標を GUI の座標に直す（Y を反対にする）
                float x = sp.x - w * 0.5f;
                float y = (Screen.height - sp.y) - h * 0.5f;

                bool black = m.Tag != null && m.Tag.IsBlack;

                _items.Add(new DrawItem
                {
                    Depth = sp.z,
                    Rect = new Rect(x, y, w, h),
                    Fill = Mathf.Clamp01(m.State.DangerNormalized),
                    Resolved = m.State.IsRescued,
                    Black = black,
                    Tint = m.Tag != null ? m.Tag.AssignedColor : Color.white,
                });
            }
        }

        private void DrawItemAt(DrawItem item)
        {
            Rect r = item.Rect;

            // 外わく（ふつうは OFF。輪郭以外にも見分けるヒントを足したいときにくらべる用）
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
                // 解消して 0 になったら光る演出
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * glowPulseSpeed * Mathf.PI * 2f);
                Color g = glowColor;
                g.a = Mathf.Lerp(0.4f, 1f, pulse);
                GUI.color = g;
                GUI.DrawTexture(r, _tex);
                return;
            }

            // ふつう: どれくらいたまっているかを表示する。黒客だけは黒っぽいグラデーションにする
            Color from = item.Black ? blackEmptyColor : emptyColor;
            Color to = item.Black ? blackFullColor : fullColor;

            GUI.color = Color.Lerp(from, to, item.Fill);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * item.Fill, r.height), _tex);
        }
    }
}
