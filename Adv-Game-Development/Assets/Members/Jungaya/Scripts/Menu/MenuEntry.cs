using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Toufuku.Menu
{
    /*
        メインメニューの、選べる項目1つ（札・設定の行・参道マップのマスに付ける）

        ・onDecide: 決めたとき（振る・Enter・クリック）に呼ぶもの。インスペクタで MainMenu の Open〜 などを入れる
        ・選ばれているときの見た目は、ここで決める（出すもの・色・大きさ）
        ・クリックは、同じ GameObject の Button で受ける（MainMenu が起動時につなぐ。Button の onClick は空のままでよい）
        ・画面の MenuPage の items に入れておかないと、選べない
    */
    public class MenuEntry : MonoBehaviour
    {
        [Serializable]
        public class Tint
        {
            public Graphic graphic;
            public Color normal = Color.white;
            public Color selected = Color.white;
        }

        [Tooltip("決めたときに呼ぶ")]
        public UnityEvent onDecide = new UnityEvent();

        [Header("選ばれているときの見た目")]
        [Tooltip("選ばれているあいだだけ出すもの（光・カーソルなど）")]
        public GameObject[] showWhenSelected = new GameObject[0];
        [Tooltip("色を変えるもの（ふち・地・文字）")]
        public List<Tint> tints = new List<Tint>();
        public float normalScale = 1f;
        public float selectedScale = 1.06f;

        [Tooltip("設定画面で、選んでいるあいだ下に出す説明（ほかの画面では使わない）")]
        [TextArea(2, 4)] public string help;

        public RectTransform Rect => (RectTransform)transform;

        public void SetSelected(bool on)
        {
            foreach (GameObject go in showWhenSelected)
                if (go != null) go.SetActive(on);
            foreach (Tint t in tints)
                if (t.graphic != null) t.graphic.color = on ? t.selected : t.normal;
            transform.localScale = Vector3.one * (on ? selectedScale : normalScale);
        }

        public void Decide() => onDecide?.Invoke();
    }
}
