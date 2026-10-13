using UnityEngine;

namespace Toufuku.Scenery
{
    /*
        ボーンに付いていないパーツ（スキンなしのメッシュ）を、実行のはじめにボーンの子にして いっしょに動かす
        デザイン班の客モデル Mob_1_01 の耳（Ears）がこれ。モデル側でスキンを付けてもらえたら、このコンポーネントは外してよい
        プレハブの中の FBX は親を付けかえられないので、実行してから付けかえる
    */
    public class BoneAttachment : MonoBehaviour
    {
        [System.Serializable]
        public class Pair
        {
            public Transform part;
            public Transform bone;
        }

        [SerializeField] Pair[] attachments = new Pair[0];

        void Awake()
        {
            foreach (var a in attachments)
            {
                if (a == null || a.part == null || a.bone == null) continue;
                // 今の見た目の位置のまま、ボーンの子にする
                a.part.SetParent(a.bone, true);
            }
        }
    }
}
