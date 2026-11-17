using System.Collections.Generic;
using UnityEngine;

namespace Toufuku.Menu
{
    // メインメニューの画面の名前。シーンに番号で残るので、順番は変えない（足すときは最後に足す）
    public enum MenuPageId { Intro, SwingPrompt, Omamori, Logo, Top, StageMap, Settings, QuitConfirm }

    /*
        メインメニューの1画面（MainMenuCanvas の下の Page_〜 に付ける）

        ・MainMenu が、今の画面だけを見せて、ほかはフェードで消す（CanvasGroup の alpha と、GameObject の ON/OFF）
          シーンではどの画面を ON にしておいてもよい（起動したときに MainMenu がそろえる）
        ・items は、選べる項目を左（上）から順に。大幣を傾けると、この順でとなりへ動く
        ・animator を入れておくと、画面を開くたびに頭から再生しなおす
    */
    [RequireComponent(typeof(CanvasGroup))]
    public class MenuPage : MonoBehaviour
    {
        public MenuPageId id;
        [Tooltip("ON なら演出の画面（項目を選ばない。決める・クリックで先へ進む）")]
        public bool cinematic;

        [Header("もどる（Esc）")]
        public bool canGoBack;
        public MenuPageId back;

        [Header("項目（傾けて動く順）")]
        public List<MenuEntry> items = new List<MenuEntry>();
        [Tooltip("開いたときに選んでおく項目（items の番号）")]
        public int defaultIndex;

        [Header("背景の見え方（この画面のあいだ）")]
        [Tooltip("寄り（1 で全景、大きいほど鳥居に寄る）")]
        public float zoom = 1f;
        [Tooltip("背景を上下にずらす（ピクセル。マイナスで下を見る）")]
        public float offsetY;
        [Tooltip("背景を暗くする（0 でそのまま、1 でまっくら）")]
        [Range(0f, 1f)] public float dim;

        [Header("動き（なくてもよい）")]
        [Tooltip("画面を開くたびに頭から再生しなおす Animator")]
        public Animator animator;

        CanvasGroup _group;
        public CanvasGroup Group => _group != null ? _group : (_group = GetComponent<CanvasGroup>());

        // 動きを頭から
        public void Restart()
        {
            if (animator == null || !animator.isActiveAndEnabled) return;
            animator.Rebind();
            animator.Update(0f);
        }
    }
}
