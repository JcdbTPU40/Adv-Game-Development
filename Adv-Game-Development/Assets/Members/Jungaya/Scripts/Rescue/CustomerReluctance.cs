using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Toufuku.Rescue
{
    /// <summary>
    /// 「渋る」リアクション — Issue #14
    ///
    /// 仕様（企画書 6章 / Issue #14）:
    ///   相性外（誤投擲）のお守りが当たったとき、客が一瞬「渋い顔」をして嫌がる見た目フィードバック。
    ///   ＝ プレイヤーに「今のは効いていない（コンボも途切れた）」と直感的に伝える演出。
    ///
    /// 担当範囲:
    ///   ・このコンポーネントは “渋る見た目” だけを担当する。
    ///   ・ゲージ微減・相性判定は CustomerRescue(#13)、コンボ途切れは ScoreManager(#15) が担当。
    ///   ・相性✗ヒットの通知は CustomerRescue.onBadHit から受け取る（実行時に自動購読するので
    ///     インスペクタでの配線は不要）。
    ///
    /// 演出（コード駆動・自己完結）:
    ///   1) 回転ワブル … 首をかしげる/嫌がるように小さく左右に揺れる。
    ///      ※ 位置ではなく回転で揺らす。Customer_Move が毎フレーム position を書き換えるため、
    ///        位置揺れだと競合する。回転は誰も触らないので安全。
    ///   2) 色フラッシュ … くすんだ色に一瞬染めてから元へ戻す（SpriteRenderer / 3D Renderer 両対応）。
    ///
    /// 外部接続:
    ///   onReluctance(UnityEvent) … 「渋い…」SE やポップアップ文字などをインスペクタで後付けする用。
    /// </summary>
    [RequireComponent(typeof(CustomerRescue))]
    public class CustomerReluctance : MonoBehaviour
    {
        [Header("回転ワブル（嫌がる/首をかしげる）")]
        [Tooltip("揺れの継続時間（秒）。")]
        [SerializeField] private float wobbleDuration = 0.22f;
        [Tooltip("揺れの最大角度（度）。左右にこの角度まで振れる。")]
        [SerializeField] private float wobbleAngle = 14f;
        [Tooltip("継続時間中に左右へ往復する回数。大きいほど細かく震える。")]
        [SerializeField] private float wobbleOscillations = 3f;
        [Tooltip("揺らす軸（ローカル）。既定は前後の傾き(Z)。横振りにしたいなら Y。")]
        [SerializeField] private Vector3 wobbleAxis = Vector3.forward;
        [Tooltip("揺らす対象。未設定ならこの GameObject 自身（root）を回す。")]
        [SerializeField] private Transform wobbleTarget;

        [Header("色フラッシュ（渋い色に一瞬染まる）")]
        [Tooltip("色フラッシュを行うか。")]
        [SerializeField] private bool useColorFlash = true;
        [Tooltip("一瞬染める「渋い」色。くすんだ青系がおすすめ。")]
        [SerializeField] private Color reluctantColor = new Color(0.45f, 0.52f, 0.62f, 1f);
        [Tooltip("色が元に戻るまでの時間（秒）。")]
        [SerializeField] private float colorFlashDuration = 0.25f;
        [Tooltip("色を変えるレンダラ。未設定なら子も含めて自動取得（SpriteRenderer 優先）。")]
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private SpriteRenderer targetSprite;

        [Header("外部フック（SE/ポップアップ用）")]
        [Tooltip("渋った瞬間に発火。「渋い…」SE・ポップアップ文字などをここに繋ぐ。")]
        public UnityEvent onReluctance;

        // 色プロパティ名（Built-in: _Color / URP: _BaseColor）。両方に書けば描画パイプライン非依存。
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private CustomerRescue _rescue;
        private MaterialPropertyBlock _mpb;

        private Quaternion _baseLocalRot;
        private Color _baseColor = Color.white;

        private Coroutine _wobbleCo;
        private Coroutine _colorCo;

        private void Awake()
        {
            _rescue = GetComponent<CustomerRescue>();

            if (wobbleTarget == null) wobbleTarget = transform;
            _baseLocalRot = wobbleTarget.localRotation;

            ResolveRenderer();
            CacheBaseColor();
        }

        private void OnEnable()
        {
            // 相性✗ヒット通知に自動購読（インスペクタ配線不要）。
            if (_rescue != null && _rescue.onBadHit != null)
                _rescue.onBadHit.AddListener(Play);
        }

        private void OnDisable()
        {
            if (_rescue != null && _rescue.onBadHit != null)
                _rescue.onBadHit.RemoveListener(Play);
        }

        /// <summary>
        /// 「渋る」リアクションを再生する。通常は CustomerRescue.onBadHit から自動で呼ばれるが、
        /// 外部から直接トリガしたい場合にも使える。
        /// </summary>
        public void Play()
        {
            // 揺れの再生（前の揺れが残っていれば差し替え）。
            if (_wobbleCo != null) StopCoroutine(_wobbleCo);
            _wobbleCo = StartCoroutine(WobbleRoutine());

            // 色フラッシュ。
            if (useColorFlash && HasColorTarget)
            {
                if (_colorCo != null) StopCoroutine(_colorCo);
                _colorCo = StartCoroutine(ColorFlashRoutine());
            }

            onReluctance?.Invoke();
        }

        /// <summary>
        /// 渋り演出の“戻り先”となる基準色を更新する（#16）。
        /// 代表カラー適用時に <see cref="CustomerProfileApplier"/> から呼ばれ、
        /// フラッシュ後に元マテリアル色ではなく客タイプの代表カラーへ戻るようにする。
        /// </summary>
        public void SetBaseColor(Color c)
        {
            _baseColor = c;
            // フラッシュ中でなければ即座に基準色へそろえておく。
            if (_colorCo == null && HasColorTarget)
                ApplyColor(_baseColor);
        }

        private IEnumerator WobbleRoutine()
        {
            Vector3 axis = wobbleAxis.sqrMagnitude > 0.0001f ? wobbleAxis.normalized : Vector3.forward;
            float t = 0f;

            while (t < wobbleDuration)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / wobbleDuration);
                // 減衰しながら左右に往復（最初が一番大きく、終わりに収まる）。
                float damp = 1f - p;
                float angle = Mathf.Sin(p * Mathf.PI * 2f * wobbleOscillations) * wobbleAngle * damp;
                wobbleTarget.localRotation = _baseLocalRot * Quaternion.AngleAxis(angle, axis);
                yield return null;
            }

            wobbleTarget.localRotation = _baseLocalRot;
            _wobbleCo = null;
        }

        private IEnumerator ColorFlashRoutine()
        {
            float t = 0f;
            ApplyColor(reluctantColor);

            while (t < colorFlashDuration)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / colorFlashDuration);
                ApplyColor(Color.Lerp(reluctantColor, _baseColor, p));
                yield return null;
            }

            ApplyColor(_baseColor);
            _colorCo = null;
        }

        private bool HasColorTarget => targetSprite != null || targetRenderer != null;

        private void ResolveRenderer()
        {
            if (targetSprite == null)
                targetSprite = GetComponentInChildren<SpriteRenderer>();

            if (targetSprite == null && targetRenderer == null)
                targetRenderer = GetComponentInChildren<Renderer>();
        }

        private void CacheBaseColor()
        {
            if (targetSprite != null)
            {
                _baseColor = targetSprite.color;
            }
            else if (targetRenderer != null)
            {
                _mpb = new MaterialPropertyBlock();
                var mat = targetRenderer.sharedMaterial;
                if (mat != null)
                {
                    if (mat.HasProperty(BaseColorId)) _baseColor = mat.GetColor(BaseColorId);
                    else if (mat.HasProperty(ColorId)) _baseColor = mat.GetColor(ColorId);
                }
            }
        }

        private void ApplyColor(Color c)
        {
            if (targetSprite != null)
            {
                targetSprite.color = c;
            }
            else if (targetRenderer != null)
            {
                // MaterialPropertyBlock で書く＝マテリアルを複製せず、他インスタンスにも影響しない。
                targetRenderer.GetPropertyBlock(_mpb);
                _mpb.SetColor(ColorId, c);
                _mpb.SetColor(BaseColorId, c);
                targetRenderer.SetPropertyBlock(_mpb);
            }
        }
    }
}
