using UnityEngine;

namespace Toufuku.Rescue.Mock
{
    /*
        視認性モック（#44）の「輪郭が光る」見た目を担当するクラス。本番のアウトラインに入れかえるときは、このクラス1つだけさわればいい

        ---- 作り方の案をくらべた（#44 で2つの案を考えて、Aにした） ----
          案A インバートハル: 同じメッシュを法線の向きに少しふくらませて、裏側だけ（Cull Front）描いてふちどりにする
               ○ 「輪郭」そのものを検証できる。太さを変えてためせる
               ○ 客がごちゃっと集まったときに、となりの客と輪郭が分かれて見えるかをちゃんと測れる
               ○ 本番（#45 のステンシルアウトライン）と見え方が近いので、検証の結果をそのまま使える
               △ シェーダーを1本自分で作らないといけない。ドローコールが2倍（15人で30。これくらいなら問題ない）

          案B Emission で光らせる: URP/Lit の _EmissionColor に色を入れて Bloom で光らせる
               ○ シェーダーがいらない。描く回数も増えない
               ✗ 面全体が光るだけで「輪郭」の検証にならない
               ✗ 集まったときに、となりの客と光がまざってしまう
               ✗ Bloom の設定で結果が変わるので、検証で気にすることが増える

          → 案Aにした。理由は「#44 のお題は『輪郭』が光ったときに見分けられるかなので、面が光るのでは答えにならないし、
            #45 の本番と見え方がちがって結果を使えないから」
            でも案Bもすてずに OutlineMode として残してあって、プレイ中にキーで
            切りかえてくらべられるようにしてある（このくらべること自体が #44 の成果になる）

        本番（#45）に入れかえるときは、このクラスの Apply 系のメソッドの中身を
        本番のアウトラインを呼ぶ処理にかえるだけでいい（外から使うメソッドは変えない）

        ※ 検証用の使い捨て。Mock/ フォルダごと消せる
    */
    [DisallowMultipleComponent]
    public class MockCustomerOutline : MonoBehaviour
    {
        // 輪郭の表し方
        public enum OutlineMode
        {
            // 案A: インバートハル（ふつうはこっち）
            InvertedHull,
            // 案B: 本体の Emission を光らせる（くらべる用）
            Emission
        }

        // 輪郭の太さの決め方。遠くの客の輪郭がつぶれるかどうかを調べる用
        public enum WidthMode
        {
            // ワールドで固定。遠くの客ほど画面の上では細くなる
            World,
            // カメラからの距離に比例させる。画面の上での太さがだいたい同じになる
            ScreenConstant
        }

        [Header("表現方式")]
        [Tooltip("輪郭の描き方。実行中に切り替えて比較できる。")]
        [SerializeField] private OutlineMode mode = OutlineMode.InvertedHull;

        [Tooltip("太さの決め方。ScreenConstant は距離に比例させて画面上の太さを一定に保つ。")]
        [SerializeField] private WidthMode widthMode = WidthMode.ScreenConstant;

        /*
            ---- 太さの初期値について（実際に測って調整した） ----
            最初は outlineWidth=0.035 / referenceDistance=20 / widthClamp=(0.015, 0.35) だったけど、
            TestGame の実際のならびだと輪郭が細すぎて見えなかった。原因は referenceDistance の合わせ方がずれていたから:

              カメラが (0, 5.3, 39) にあって、客の定位置の帯は z=12〜33。実際の距離は 7〜27m くらいで、
              いちばん客が多い「近い」帯（z 27〜33）は 7.4〜12.8m しかない
              ScreenConstant は 太さ = outlineWidth × (距離 / referenceDistance) なので、
              基準の 20m に対して近い帯は 0.37〜0.64 倍まで下がって、さらに下限の 0.015 で止められる
              → 1080p にすると 1.3px くらい。ほとんど見えない太さになっていた

            なので基準の距離を実際の代表的な距離（近い帯の真ん中の 12m くらい）に合わせて、太さも上げた
            今の値は 1080p で、どの帯もだいたい 5px（ScreenConstant なので距離に関係なく同じ）
            プレイ中に [ / ] キーで増やしたり減らしたりできるので、測りながら決めること
        */
        [Header("インバートハル（案A）")]
        [Tooltip("押し出し幅（ワールド単位）。ScreenConstant のときは referenceDistance での幅になる。")]
        [SerializeField] private float outlineWidth = 0.09f;

        [Tooltip("ScreenConstant の基準距離(m)。この距離で outlineWidth ちょうどになる。客の代表距離に合わせること。")]
        [SerializeField] private float referenceDistance = 12f;

        /*
            下限はあくまで「こわれないようにするための安全装置」。ここを上げすぎると、プレイ中に [ ] で細くしても
            効かなくなって（HUD の表示と実際の見た目がずれる）、合わせ方のミスに気づけなくなる
        */
        [Tooltip("押し出し幅の下限・上限（ワールド単位）。極端な近接/遠方での破綻よけ。下限は安全弁なので低めに置くこと。")]
        [SerializeField] private Vector2 widthClamp = new Vector2(0.02f, 0.6f);

        [Tooltip("輪郭に使うマテリアル（Toufuku/Mock/OutlineHull）。色はMPBで客ごとに差し替える。")]
        [SerializeField] private Material outlineMaterial;

        [Header("Emission（案B・比較用）")]
        [Tooltip("Emission の強さ。1未満だと Bloom に乗りにくい。")]
        [SerializeField] private float emissionIntensity = 3f;

        [Header("参照（未設定なら自動取得/自動生成）")]
        [Tooltip("客本体のレンダラ。未設定なら子から自動取得。")]
        [SerializeField] private Renderer bodyRenderer;

        [Tooltip("輪郭用の複製レンダラ。未設定なら実行時に子として自動生成する。")]
        [SerializeField] private MeshRenderer outlineRenderer;

        // 色のプロパティ名。Built-in（_Color）と URP（_BaseColor）の両方に書いて、どっちのパイプラインでも動くようにする
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private MaterialPropertyBlock _outlineMpb;
        private MaterialPropertyBlock _bodyMpb;
        private Color _outlineColor = Color.white;
        private Color _bodyColor = Color.gray;
        private Camera _camera;

        // 今の表し方
        public OutlineMode Mode => mode;

        // 今の太さのモード
        public WidthMode Width => widthMode;

        private void Awake()
        {
            if (bodyRenderer == null) bodyRenderer = GetComponentInChildren<MeshRenderer>();
            _outlineMpb = new MaterialPropertyBlock();
            _bodyMpb = new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
            // ScreenConstant のときだけ、毎フレーム太さを合わせなおす（歩いている間も距離が変わるから）
            if (mode == OutlineMode.InvertedHull && widthMode == WidthMode.ScreenConstant)
                ApplyOutline();
        }

        /*
            出てきたときに MockCrowdDirector から呼ばれる初期化
            outlineColor: この客の輪郭の色（お守り5色か黒）
            bodyColor: 本体の色。輪郭を目立たせたいので、ふつうはグレー
            material: 輪郭のマテリアル。null なら Inspector 側の設定を使う
        */
        public void Setup(Color outlineColor, Color bodyColor, Material material)
        {
            if (material != null) outlineMaterial = material;
            _outlineColor = outlineColor;
            _bodyColor = bodyColor;

            EnsureHull();
            ApplyBodyColor();
            ApplyMode();
        }

        // 表し方を切りかえる（デバッグ操作から呼ばれる）
        public void SetMode(OutlineMode next)
        {
            if (mode == next) return;
            mode = next;
            ApplyMode();
        }

        // 太さのモードを切りかえる（デバッグ操作から呼ばれる）
        public void SetWidthMode(WidthMode next)
        {
            if (widthMode == next) return;
            widthMode = next;
            ApplyOutline();
        }

        // 太さの基準の値を変える（Inspector で測りながら調整したのを反映する用）
        public void SetOutlineWidth(float world)
        {
            outlineWidth = world;
            ApplyOutline();
        }

        // 今の太さの基準の値（ワールド単位）。プレイ中に調整した結果を読むため
        public float OutlineWidth => outlineWidth;

        // ---- 中の処理 ----

        /*
            輪郭用のコピーのレンダラー（インバートハル）を用意する
            本体と同じメッシュを使いまわすだけなので、メモリは増えない
        */
        private void EnsureHull()
        {
            if (outlineRenderer != null) return;

            MeshFilter bodyFilter = GetComponentInChildren<MeshFilter>();
            if (bodyFilter == null || bodyFilter.sharedMesh == null)
            {
                Debug.LogWarning("[MockOutline] 本体の MeshFilter が見つからないため輪郭を生成できません。", this);
                return;
            }

            var hull = new GameObject("OutlineHull");
            hull.transform.SetParent(bodyFilter.transform, false);

            hull.AddComponent<MeshFilter>().sharedMesh = bodyFilter.sharedMesh;

            outlineRenderer = hull.AddComponent<MeshRenderer>();
            outlineRenderer.sharedMaterial = outlineMaterial;
            // 輪郭は影を落とさないし受けない（本体の影と2重にならないように）
            outlineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            outlineRenderer.receiveShadows = false;
            outlineRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            outlineRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        // 今のモードに合わせて、ハルと Emission のオンオフを切りかえる
        private void ApplyMode()
        {
            bool useHull = mode == OutlineMode.InvertedHull;

            if (outlineRenderer != null)
                outlineRenderer.enabled = useHull;

            if (useHull)
            {
                ApplyOutline();
                ApplyEmission(Color.black);   // 案Aのときは本体を光らせない
            }
            else
            {
                ApplyEmission(_outlineColor * emissionIntensity);
            }
        }

        // ハルの色とふくらませる幅を MPB で書きこむ
        private void ApplyOutline()
        {
            if (outlineRenderer == null) return;
            if (_outlineMpb == null) _outlineMpb = new MaterialPropertyBlock();

            float width = outlineWidth;

            if (widthMode == WidthMode.ScreenConstant && referenceDistance > 0.001f)
            {
                if (_camera == null) _camera = Camera.main;
                if (_camera != null)
                {
                    // 距離に比例させる＝画面で見たときの太さが同じになる
                    float dist = Vector3.Distance(_camera.transform.position, transform.position);
                    width = outlineWidth * (dist / referenceDistance);
                }
            }

            width = Mathf.Clamp(width, widthClamp.x, widthClamp.y);

            outlineRenderer.GetPropertyBlock(_outlineMpb);
            _outlineMpb.SetColor(OutlineColorId, _outlineColor);
            _outlineMpb.SetFloat(OutlineWidthId, width);
            outlineRenderer.SetPropertyBlock(_outlineMpb);
        }

        // 本体の Emission（案B）を MPB で書きこむ
        private void ApplyEmission(Color emission)
        {
            if (bodyRenderer == null) return;
            if (_bodyMpb == null) _bodyMpb = new MaterialPropertyBlock();

            bodyRenderer.GetPropertyBlock(_bodyMpb);
            _bodyMpb.SetColor(EmissionColorId, emission);
            bodyRenderer.SetPropertyBlock(_bodyMpb);
        }

        // 本体のもとの色（グレー）を MPB で書きこむ
        private void ApplyBodyColor()
        {
            if (bodyRenderer == null) return;
            if (_bodyMpb == null) _bodyMpb = new MaterialPropertyBlock();

            bodyRenderer.GetPropertyBlock(_bodyMpb);
            _bodyMpb.SetColor(ColorId, _bodyColor);
            _bodyMpb.SetColor(BaseColorId, _bodyColor);
            bodyRenderer.SetPropertyBlock(_bodyMpb);
        }
    }
}
