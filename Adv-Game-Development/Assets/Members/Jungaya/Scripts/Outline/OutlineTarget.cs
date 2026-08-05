using System.Collections.Generic;
using UnityEngine;

namespace Toufuku.Rescue.Outline
{
    /// <summary>
    /// アウトラインを出したいオブジェクトに付ける。Renderer Feature が読む登録エントリ。
    ///
    /// OnEnable/OnDisable で静的レジストリに自己登録する。色はマテリアル固定にせず
    /// <see cref="SetColor"/> で客ごと（お守り5色）に差し替えられる。
    /// パターンIDはマスクRTのA成分へ運び、Compose の拡張点へ渡す。
    /// </summary>
    [DisallowMultipleComponent]
    public class OutlineTarget : MonoBehaviour
    {
        static readonly List<OutlineTarget> s_Active = new List<OutlineTarget>(32);

        [Tooltip("輪郭色。お守り5色など。実行中は SetColor で差し替え可。")]
        [SerializeField] Color color = Color.white;

        [Tooltip("線種。現状 Solid のみ実装。A成分に載せて Compose へ運ぶ。")]
        [SerializeField] OutlinePattern pattern = OutlinePattern.Solid;

        [Tooltip("描画対象の Renderer。未設定なら自分＋子から自動取得。")]
        [SerializeField] Renderer targetRenderer;

        /// <summary>いま有効な OutlineTarget 一覧（Feature が毎フレーム読む）。</summary>
        public static IReadOnlyList<OutlineTarget> ActiveTargets => s_Active;

        public Color Color => color;
        public OutlinePattern Pattern => pattern;
        public Renderer TargetRenderer => targetRenderer;

        void Awake()
        {
            if (targetRenderer == null)
                targetRenderer = GetComponentInChildren<Renderer>();
        }

        void OnEnable()
        {
            if (targetRenderer == null)
                targetRenderer = GetComponentInChildren<Renderer>();

            if (!s_Active.Contains(this))
                s_Active.Add(this);
        }

        void OnDisable()
        {
            s_Active.Remove(this);
        }

        /// <summary>輪郭色を差し替える（MockCrowdDirector.omamoriColors をそのまま流し込める）。</summary>
        public void SetColor(Color next)
        {
            color = next;
        }

        /// <summary>線種を差し替える（色覚対応の拡張用。現状 Compose は Solid のみ）。</summary>
        public void SetPattern(OutlinePattern next)
        {
            pattern = next;
        }

        /// <summary>描画対象 Renderer を明示設定する。</summary>
        public void SetRenderer(Renderer renderer)
        {
            targetRenderer = renderer;
        }
    }
}
