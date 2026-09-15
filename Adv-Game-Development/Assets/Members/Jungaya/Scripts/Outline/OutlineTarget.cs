using System.Collections.Generic;
using UnityEngine;

namespace Toufuku.Rescue.Outline
{
    /*
        アウトラインを出したいオブジェクトに付けるクラス。Renderer Feature が読む登録のデータ（#45 / #59）

        OnEnable/OnDisable で static のリストに自分を登録する。色ともようは客ごとに変えられる
          ・今ほしい色ともよう（Current）: いつも出る輪。欲張り客で2本のときは外側
          ・次にほしい色ともよう（Next）: 欲張り客で R=2 のあいだだけ、内側にもう1本出す（企画書 v8 8章「輪郭が2重（内側に2色目）」）
            1色目が当たって R=1 になったら、Next を Current に入れて ClearNext する（外側の輪が消えて、内側の色だけになる）
          ・照準が乗っている（Highlighted）: 輪郭を一段明るくする（v8 3章）。ロック表示ではないので、形や太さは変えない

        お守りの種類から色ともようをまとめて入れるときは SetRequest を使う（色は OmamoriPalette、もようは OutlineStyle）
    */
    [DisallowMultipleComponent]
    public class OutlineTarget : MonoBehaviour
    {
        static readonly List<OutlineTarget> s_Active = new List<OutlineTarget>(32);

        [Tooltip("今ほしいお守りの輪郭色。実行中は SetCurrent / SetRequest で差し替える。")]
        [SerializeField] Color color = Color.white;

        [Tooltip("今ほしいお守りの線のもよう（v8 6章の表）。")]
        [SerializeField] OutlinePattern pattern = OutlinePattern.Solid;

        [Tooltip("ON なら内側に、次にほしいお守りの輪をもう1本出す（欲張り客で R=2 のあいだ）。")]
        [SerializeField] bool hasNext;

        [Tooltip("次にほしいお守りの輪郭色（hasNext のときだけ使う）。")]
        [SerializeField] Color nextColor = Color.white;

        [Tooltip("次にほしいお守りの線のもよう（hasNext のときだけ使う）。")]
        [SerializeField] OutlinePattern nextPattern = OutlinePattern.Solid;

        [Tooltip("描画対象の Renderer。未設定なら自分＋子から自動取得。")]
        [SerializeField] Renderer targetRenderer;

        // 今有効な OutlineTarget の一覧（Feature が毎フレーム読む）
        public static IReadOnlyList<OutlineTarget> ActiveTargets => s_Active;

        public Color Color => color;
        public OutlinePattern Pattern => pattern;
        public bool HasNext => hasNext;
        public Color NextColor => nextColor;
        public OutlinePattern NextPattern => nextPattern;
        // 輪の数（1 か 2）
        public int RingCount => hasNext ? 2 : 1;
        // 照準が乗っているか（OutlineAimHighlighter が入れる）
        public bool Highlighted { get; private set; }
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
            Highlighted = false;
        }

        // 今ほしい色ともようを入れる
        public void SetCurrent(Color nextCurrentColor, OutlinePattern nextCurrentPattern)
        {
            color = nextCurrentColor;
            pattern = nextCurrentPattern;
        }

        // 内側の輪（次にほしい色ともよう）を出す
        public void SetNext(Color nextRingColor, OutlinePattern nextRingPattern)
        {
            hasNext = true;
            nextColor = nextRingColor;
            nextPattern = nextRingPattern;
        }

        // 内側の輪を消す（1本にもどす）
        public void ClearNext()
        {
            hasNext = false;
        }

        /*
            お守りの種類から、色（OmamoriPalette）ともよう（v8 6章の表）をまとめて入れる。輪は1本
            palette が null のときは Color.magenta にして設定ミスを目立たせる（OmamoriPalette と同じ考え方）
        */
        public void SetRequest(OmamoriPalette palette, OmamoriType current)
        {
            SetCurrent(ColorOf(palette, current), OutlineStyle.PatternFor(current));
            ClearNext();
        }

        /*
            欲張り客用。remaining（残りの必要な発数 R）が2以上のあいだは内側に next の輪を出す
            R=1 になったら、呼ぶ側は current に2色目を入れて SetRequest(palette, current) を呼ぶ
        */
        public void SetRequest(OmamoriPalette palette, OmamoriType current, OmamoriType next, int remaining)
        {
            SetCurrent(ColorOf(palette, current), OutlineStyle.PatternFor(current));
            if (OutlineStyle.RingCount(remaining, true) >= 2)
                SetNext(ColorOf(palette, next), OutlineStyle.PatternFor(next));
            else
                ClearNext();
        }

        // 照準が乗っているかを入れる（乗っているあいだだけ一段明るい）
        public void SetHighlighted(bool on)
        {
            Highlighted = on;
        }

        // 輪郭の色だけを変える（黒客・救済成功の白など、もようを変えないとき）
        public void SetColor(Color next)
        {
            color = next;
        }

        // 線のもようだけを変える
        public void SetPattern(OutlinePattern next)
        {
            pattern = next;
        }

        // 描く Renderer をはっきり決める
        public void SetRenderer(Renderer renderer)
        {
            targetRenderer = renderer;
        }

        static Color ColorOf(OmamoriPalette palette, OmamoriType type)
            => palette != null ? palette.GetColor(type) : Color.magenta;
    }
}
