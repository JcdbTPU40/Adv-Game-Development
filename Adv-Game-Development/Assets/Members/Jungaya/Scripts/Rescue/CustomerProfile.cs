using UnityEngine;

namespace Toufuku.Rescue
{
    /*
        客のタイプ1つぶんの「決まりのデータ」（#16）
        「客のタイプ → なやみ → 正解のお守り」のつながりを1つのアセットにまとめた入れ物

        企画の方針:
          ・v2 の「見た目でわかってもらう」やり方は企画書 v3 §6 でやめた。今は【客の輪郭が光る】ことで
            ほしいお守りをはっきり見せる
          ・このアセットは、輪郭・弾・ボタンでいっしょに使う代表の色と、見た目のアセットの
            入れ物を持つ（データの形は v2 のをそのまま使っている）
          ・今は絵がないので「仮の画像でOK」。デザインの人があとから
            placeholderSprite / viewPrefab に本番の絵を
            入れるだけですむように、先に入れ物だけ用意しておく

        役割分担:
          ・このアセットは「データ（つながり＋見た目）」だけを持つ。動きは持たない
          ・相性◯/✗の判定は OmamoriAffinityTable（#12）の担当
            ここの correctOmamori は「1対1の正解（予備）」の意味
          ・ぜんぶのタイプをまとめて取るのは CustomerProfileCatalog

        使い方:
          Project で右クリック → Create → Toufuku → 客タイプ定義(CustomerProfile)
          客のタイプ1つにつき1つアセットを作って、Catalog に登録する
    */
    [CreateAssetMenu(
        fileName = "CustomerProfile",
        menuName = "Toufuku/客タイプ定義 (CustomerProfile)",
        order = 1)]
    public class CustomerProfile : ScriptableObject
    {
        [Header("紐付け（客タイプ → 悩み → 正解お守り）")]
        [Tooltip("この定義が表す客タイプ。＝抱えている悩みの種類。")]
        [SerializeField] private CustomerType customerType = CustomerType.Kenkou;

        [Tooltip("この客に効く“正解お守り”。相性テーブル未設定時のフォールバック判定にも使う。")]
        [SerializeField] private OmamoriType correctOmamori = OmamoriType.Kenkou;

        [Header("色の正（お守り5色パレット v3 §3）")]
        [Tooltip("お守り5色パレット（唯一の正）。設定時は correctOmamori に対応する色が代表色になり、下の representativeColor は無視される。")]
        [SerializeField] private OmamoriPalette palette;

        [Header("見た目の受け皿（プレースホルダーOK／あとで差し替え）")]
        [Tooltip("客の見た目プレースホルダー画像(2D)。デザイン陣が本番アートに差し替える。")]
        [SerializeField] private Sprite placeholderSprite;

        [Tooltip("客の見た目プレハブ(3D/演出付き)。3Dや専用アニメで出す場合はこちらを差し替える。")]
        [SerializeField] private GameObject viewPrefab;

        [Header("フォールバック用（palette 未設定時のみ）")]
        [Tooltip("正は OmamoriPalette。palette 設定時この値は無視される。")]
        [SerializeField] private Color representativeColor = Color.white;

        [Header("エディタ/デバッグ用（※ゲーム内ラベルには使わない）")]
        [Tooltip("Inspector/デバッグ表示用の名前。日本語可。ゲーム内ラベルには使わない（明示は輪郭発光で行う。v3 §6）。")]
        [SerializeField] private string displayName = "";

        [Tooltip("デザイン/企画への申し送りメモ。仕様には影響しない。")]
        [TextArea(2, 4)]
        [SerializeField] private string designerNote = "";

        // ---- 読むだけのプロパティ（外からはここを使う） ----

        // この決まりが表す客のタイプ（＝なやみ）
        public CustomerType CustomerType => customerType;

        // この客に効く正解のお守り（1対1の予備）
        public OmamoriType CorrectOmamori => correctOmamori;

        // 見た目の仮の画像（2D）。入っていなければ null
        public Sprite PlaceholderSprite => placeholderSprite;

        // 見た目のプレハブ（3D や演出付き）。入っていなければ null
        public GameObject ViewPrefab => viewPrefab;

        /*
            この客がほしいお守りの代表の色。輪郭・弾・ボタンの色と完全にそろえる（v3 §3）
            この客の代表の色は「ほしいお守りの色」と必ず同じになるので、
            palette が入っていれば OmamoriPalette から correctOmamori で取る
            palette が入っていないときだけ、予備の representativeColor を返す
        */
        public Color RepresentativeColor =>
            palette != null ? palette.GetColor(correctOmamori) : representativeColor;

        // エディタやデバッグで表示する名前（入っていなければ客のタイプの名前を返す）
        public string DisplayName =>
            string.IsNullOrEmpty(displayName) ? customerType.ToString() : displayName;

        // デザインや企画の人向けのメモ
        public string DesignerNote => designerNote;

#if UNITY_EDITOR
        /*
            表示する名前が空のとき、アセットの名前から入れておく（Inspector で見やすくするため）
            仕様には関係ない
        */
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(displayName))
                displayName = name;

            /*
                palette が入っているのに、予備の representativeColor が正しい色と大きくちがうときは
                警告を出す（palette を外した瞬間に古い色にもどってしまうのに気づけるように）
                「大きく」のしきい値は RGB の差の合計が 0.3（見てはっきり別の色になるくらい）
            */
            if (palette != null)
            {
                Color c = palette.GetColor(correctOmamori);
                float diff = Mathf.Abs(c.r - representativeColor.r)
                           + Mathf.Abs(c.g - representativeColor.g)
                           + Mathf.Abs(c.b - representativeColor.b);
                if (diff > 0.3f)
                    Debug.LogWarning(
                        $"[CustomerProfile] {name}: representativeColor（フォールバック）が OmamoriPalette の正" +
                        $"（{correctOmamori} の色）と大きく食い違っています（RGB差合計 {diff:0.00}）。" +
                        "フォールバック値をパレットと同値に更新してください。", this);
            }
        }
#endif
    }
}
