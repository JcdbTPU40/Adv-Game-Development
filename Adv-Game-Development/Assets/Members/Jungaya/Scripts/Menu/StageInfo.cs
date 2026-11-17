using System;
using UnityEngine;

namespace Toufuku.Menu
{
    /*
        参道マップの1マスのステージ（マスの MenuStageNode に書く。順番は MainMenu の stageNodes）

        ・MainMenu の unlockAllStages が ON なら、どれもはじめから選べる（ゲームショウ用）。
          OFF なら、前のステージをクリアすると次が解放される（いちばん前は、はじめから解放）
        ・ステージごとの中身（客の数・時間など）はまだ決まっていないので、今はどれも sceneName のシーンを開くだけ
    */
    [Serializable]
    public class StageInfo
    {
        [Tooltip("セーブに残す名前。あとから変えない（例: 1-1）")]
        public string id = "1-1";
        [Tooltip("マスの上に出す名前（例: 1-1、本殿）")]
        public string label = "1-1";
        [Tooltip("説明の板の見出し")]
        public string title = "朝の参道";
        [TextArea(2, 4)]
        public string description = "";
        [Tooltip("このステージで開くシーン（Build Settings に入っていること）")]
        public string sceneName = "GameScene";
    }
}
