using UnityEngine;
using UnityEngine.UI;

namespace Toufuku.Menu
{
    /*
        参道マップの1マス（Node_〜 に付ける。小さな鳥居）

        ・stage にこのマスのステージを書く。マスの位置は、ふだんどおり Scene ビューで動かす
        ・解放されているか・クリア済みかで色を変えるのは SetState（MainMenu が参道マップを開くたびに呼ぶ）
        ・輪と光の動きは、このマスの Animator（Menu_StageNode）
    */
    public class MenuStageNode : MonoBehaviour
    {
        public StageInfo stage = new StageInfo();

        [Header("部品")]
        [Tooltip("笠木（いちばん上の横木）")]
        public Graphic top;
        [Tooltip("貫と柱")]
        public Graphic[] wood = new Graphic[0];
        public Graphic glow;
        public Text label;
        [Tooltip("クリア済みの印")]
        public GameObject stamp;
        [Tooltip("前のマスからここまでの、金の点（解放されると出す）。最初のマスは空でよい")]
        public GameObject pathFromPrevious;

        [Header("色（解放されている / まだ）")]
        public Color topUnlocked = new Color32(0x1d, 0x0d, 0x0d, 0xff);
        public Color topLocked = new Color32(0x2a, 0x22, 0x30, 0xff);
        public Color woodUnlocked = new Color32(0xc0, 0x36, 0x2c, 0xff);
        public Color woodLocked = new Color32(0x4a, 0x3e, 0x52, 0xff);
        public Color labelUnlocked = new Color32(0xff, 0xf3, 0xd6, 0xff);
        public Color labelLocked = new Color32(0x8a, 0x7e, 0x94, 0xff);

        public void SetState(bool unlocked, bool cleared)
        {
            if (top != null) top.color = unlocked ? topUnlocked : topLocked;
            foreach (Graphic g in wood)
                if (g != null) g.color = unlocked ? woodUnlocked : woodLocked;
            if (glow != null) glow.enabled = unlocked;
            if (label != null) label.color = unlocked ? labelUnlocked : labelLocked;
            if (stamp != null) stamp.SetActive(cleared);
            if (pathFromPrevious != null) pathFromPrevious.SetActive(unlocked);
        }
    }
}
