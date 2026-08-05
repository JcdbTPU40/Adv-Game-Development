using UnityEngine;

namespace Toufuku.Rescue.Mock
{
    /// <summary>
    /// 視認性モック(#44)の「輪郭発光」表現。★本番アウトラインへの差し替え点はこのクラス1つ。
    ///
    /// ── 実装案の比較（#44 で 2 案を検討し A を採用）──────────────────────
    ///   案A インバートハル … 同じメッシュを法線方向に押し出し、裏面だけ(Cull Front)描いて縁取りにする。
    ///        ○ “輪郭”そのものを検証できる／太さを独立変数として振れる
    ///        ○ 密集時に隣の客と輪郭が分離して見えるかを正しく測れる
    ///        ○ 本番(#45 ステンシルアウトライン)と見え方が近く、検証結果を転用できる
    ///        △ 自作シェーダ1本が必要／ドローコール2倍（15体=30。この規模では無問題）
    ///
    ///   案B Emission/リム発光 … URP/Lit の _EmissionColor を塗って Bloom で光らせる。
    ///        ○ シェーダ不要・追加描画なし
    ///        ✗ 面全体が光るだけで“輪郭”の検証にならない
    ///        ✗ 密集時に隣の客と発光が溶け合う
    ///        ✗ Bloom 設定に結果が左右され、検証の変数が増える
    ///
    ///   → 案A を採用。理由は「#44 のお題が *輪郭* 発光の識別性であり、面発光では答えにならず、
    ///     #45 の本番実装と見え方が乖離して結果を転用できないため」。
    ///     ただし案B も捨てず <see cref="OutlineMode"/> として同居させ、実行中にキーで
    ///     切り替えて直接比較できるようにしてある（この比較自体が #44 の成果物になる）。
    ///
    /// 本番(#45)へ差し替えるときは、このクラスの Apply 系メソッドの中身を
    /// 本番アウトラインの呼び出しに置き換えるだけでよい（外から見た API は変えない）。
    ///
    /// ※ 検証用の使い捨て。Mock/ ごと削除できる。
    /// </summary>
    [DisallowMultipleComponent]
    public class MockCustomerOutline : MonoBehaviour
    {
        /// <summary>輪郭の表現方式。</summary>
        public enum OutlineMode
        {
            /// <summary>案A: インバートハル（既定）。</summary>
            InvertedHull,
            /// <summary>案B: 本体の Emission を光らせる（比較用）。</summary>
            Emission
        }

        /// <summary>輪郭の太さの決め方。遠くの客の輪郭が潰れるかどうかの検証用。</summary>
        public enum WidthMode
        {
            /// <summary>ワールド固定。遠い客ほど画面上では細くなる。</summary>
            World,
            /// <summary>カメラ距離に比例。画面上の太さがほぼ一定になる。</summary>
            ScreenConstant
        }

        [Header("表現方式")]
        [Tooltip("輪郭の描き方。実行中に切り替えて比較できる。")]
        [SerializeField] private OutlineMode mode = OutlineMode.InvertedHull;

        [Tooltip("太さの決め方。ScreenConstant は距離に比例させて画面上の太さを一定に保つ。")]
        [SerializeField] private WidthMode widthMode = WidthMode.ScreenConstant;

        // ── 太さの既定値について（実測で調整済み）────────────────────────
        // 初期値は outlineWidth=0.035 / referenceDistance=20 / widthClamp=(0.015, 0.35) だったが、
        // TestGame の実配置では輪郭が細すぎて見えなかった。原因は referenceDistance の較正ずれ:
        //
        //   カメラ (0, 5.3, 39) に対し、客の定位置バンドは z=12〜33。実距離は約 7〜27m で、
        //   最も客が多い「近」バンド(z 27〜33)は 7.4〜12.8m しかない。
        //   ScreenConstant は width = outlineWidth × (距離 / referenceDistance) なので、
        //   基準 20m に対し近バンドの係数は 0.37〜0.64 まで落ち、さらに下限 0.015 でクランプされる。
        //   → 1080p 換算で約 1.3px。ほぼ視認できない太さになっていた。
        //
        // そこで基準距離を実際の代表距離(近バンド中央 ≒ 12m)に合わせ、幅も引き上げた。
        // 現在の値は 1080p 換算で全バンドおよそ 5px 相当（ScreenConstant なので距離によらず一定）。
        // 実行中に [ / ] キーで増減できるので、実測しながら詰めること。
        [Header("インバートハル（案A）")]
        [Tooltip("押し出し幅（ワールド単位）。ScreenConstant のときは referenceDistance での幅になる。")]
        [SerializeField] private float outlineWidth = 0.09f;

        [Tooltip("ScreenConstant の基準距離(m)。この距離で outlineWidth ちょうどになる。客の代表距離に合わせること。")]
        [SerializeField] private float referenceDistance = 12f;

        // 下限はあくまで「破綻よけの安全弁」。ここを上げすぎると、実行中に [ ] で細くしても
        // 効かなくなり（HUD の表示と実際の見た目がずれる）、較正ミスに気づけなくなる。
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

        // 色プロパティ名。Built-in(_Color) / URP(_BaseColor) の両方に書いてパイプライン非依存にする。
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

        /// <summary>現在の表現方式。</summary>
        public OutlineMode Mode => mode;

        /// <summary>現在の太さモード。</summary>
        public WidthMode Width => widthMode;

        private void Awake()
        {
            if (bodyRenderer == null) bodyRenderer = GetComponentInChildren<MeshRenderer>();
            _outlineMpb = new MaterialPropertyBlock();
            _bodyMpb = new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
            // ScreenConstant のときだけ毎フレーム太さを追従させる（歩行中も距離が変わるため）。
            if (mode == OutlineMode.InvertedHull && widthMode == WidthMode.ScreenConstant)
                ApplyOutline();
        }

        /// <summary>
        /// スポーン時に <see cref="MockCrowdDirector"/> から呼ばれる初期化。
        /// </summary>
        /// <param name="outlineColor">この客の輪郭色（お守り5色 or 黒）。</param>
        /// <param name="bodyColor">本体の色。輪郭を主役にするため通常はニュートラルな灰色。</param>
        /// <param name="material">輪郭マテリアル。null なら Inspector 側の設定を使う。</param>
        public void Setup(Color outlineColor, Color bodyColor, Material material)
        {
            if (material != null) outlineMaterial = material;
            _outlineColor = outlineColor;
            _bodyColor = bodyColor;

            EnsureHull();
            ApplyBodyColor();
            ApplyMode();
        }

        /// <summary>表現方式を切り替える（デバッグ操作から呼ばれる）。</summary>
        public void SetMode(OutlineMode next)
        {
            if (mode == next) return;
            mode = next;
            ApplyMode();
        }

        /// <summary>太さモードを切り替える（デバッグ操作から呼ばれる）。</summary>
        public void SetWidthMode(WidthMode next)
        {
            if (widthMode == next) return;
            widthMode = next;
            ApplyOutline();
        }

        /// <summary>太さの基準値を変える（Inspector 実測調整の反映用）。</summary>
        public void SetOutlineWidth(float world)
        {
            outlineWidth = world;
            ApplyOutline();
        }

        /// <summary>現在の太さの基準値（ワールド単位）。実行中の調整結果を読むため。</summary>
        public float OutlineWidth => outlineWidth;

        // ── 内部 ────────────────────────────────────────────────

        /// <summary>
        /// 輪郭用の複製レンダラ（インバートハル）を用意する。
        /// 本体と同じメッシュを共有するだけなのでメモリコストは無い。
        /// </summary>
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
            // 輪郭は影を落とさない/受けない（本体の影と二重にならないように）。
            outlineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            outlineRenderer.receiveShadows = false;
            outlineRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            outlineRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        /// <summary>現在のモードに応じて、ハルと Emission の有効・無効を切り替える。</summary>
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

        /// <summary>ハルの色と押し出し幅を MPB で書き込む。</summary>
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
                    // 距離に比例させる＝画面上の見かけの太さが一定になる。
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

        /// <summary>本体の Emission（案B）を MPB で書き込む。</summary>
        private void ApplyEmission(Color emission)
        {
            if (bodyRenderer == null) return;
            if (_bodyMpb == null) _bodyMpb = new MaterialPropertyBlock();

            bodyRenderer.GetPropertyBlock(_bodyMpb);
            _bodyMpb.SetColor(EmissionColorId, emission);
            bodyRenderer.SetPropertyBlock(_bodyMpb);
        }

        /// <summary>本体のベース色（ニュートラル灰）を MPB で書き込む。</summary>
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
