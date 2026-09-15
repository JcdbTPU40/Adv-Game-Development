using System.Collections.Generic;
using UnityEngine;

namespace Toufuku.Rescue.Outline
{
    /*
        アウトラインを出したいオブジェクトに付けるクラス。Renderer Feature が読む登録のデータ

        OnEnable/OnDisable で static のリストに自分を登録する。色はマテリアルで固定にしないで、
        SetColor で客ごと（お守り5色）に変えられる
        もようの番号はマスクRT の A に入れて、Compose の拡張する場所に渡す
    */
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

        // 今有効な OutlineTarget の一覧（Feature が毎フレーム読む）
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

        // 輪郭の色を変える（MockCrowdDirector.omamoriColors をそのまま入れられる）
        public void SetColor(Color next)
        {
            color = next;
        }

        // 線の種類を変える（色覚対応で広げるとき用。今の Compose は Solid だけ）
        public void SetPattern(OutlinePattern next)
        {
            pattern = next;
        }

        // 描く Renderer をはっきり決める
        public void SetRenderer(Renderer renderer)
        {
            targetRenderer = renderer;
        }
    }
}
