using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Toufuku.Rescue
{
    /*
        「渋る」リアクション（#14）

        仕様（企画書 6章 / Issue #14）:
          相性が合わない（まちがえた）お守りが当たったとき、客が一瞬「しぶい顔」をしていやがる見た目のフィードバック
          ＝ プレイヤーに「今のは効いてない（コンボも切れた）」とパッと伝えるための演出

        担当するところ:
          ・このコンポーネントは「渋る見た目」だけを担当する
          ・ゲージがちょっと減るのと相性の判定は CustomerRescue（#13）、コンボが切れるのは ScoreManager（#15）の担当
          ・相性✗で当たったお知らせは CustomerRescue.onBadHit から受け取る（プレイ中に自動で受け取るようにするので、
            インスペクターでつながなくていい）

        演出（コードで動かしていて、これだけで完結している）:
          1) 回転のゆれ: 首をかしげる・いやがるみたいに、小さく左右にゆれる
             ※ 位置じゃなくて回転でゆらす。Customer_Move が毎フレーム position を書きかえるので、
               位置でゆらすとぶつかる。回転はだれもさわらないので安全
          2) 色のフラッシュ: くすんだ色に一瞬そめてから元にもどす（SpriteRenderer と 3D の Renderer の両方に対応）

        外とつなぐところ:
          onReluctance（UnityEvent）: 「しぶい…」の効果音やポップアップの文字などを、インスペクターであとから足す用
    */
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

        // 色のプロパティ名（Built-in: _Color / URP: _BaseColor）。両方に書けば、どっちの描画パイプラインでも動く
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
            // 相性✗で当たったお知らせを自動で受け取る（インスペクターでつながなくていい）
            if (_rescue != null && _rescue.onBadHit != null)
                _rescue.onBadHit.AddListener(Play);
        }

        private void OnDisable()
        {
            if (_rescue != null && _rescue.onBadHit != null)
                _rescue.onBadHit.RemoveListener(Play);
        }

        /*
            「渋る」リアクションを再生する。ふつうは CustomerRescue.onBadHit から自動で呼ばれるけど、
            外から直接動かしたいときにも使える
        */
        public void Play()
        {
            // ゆれを再生する（前のゆれが残っていたら入れかえる）
            if (_wobbleCo != null) StopCoroutine(_wobbleCo);
            _wobbleCo = StartCoroutine(WobbleRoutine());

            // 色のフラッシュ
            if (useColorFlash && HasColorTarget)
            {
                if (_colorCo != null) StopCoroutine(_colorCo);
                _colorCo = StartCoroutine(ColorFlashRoutine());
            }

            onReluctance?.Invoke();
        }

        /*
            渋る演出の「もどる色」になる基準の色を変える（#16）
            代表の色を入れたときに CustomerProfileApplier から呼ばれて、
            フラッシュのあとにマテリアルの元の色じゃなくて、客のタイプの代表の色にもどるようにする
        */
        public void SetBaseColor(Color c)
        {
            _baseColor = c;
            // フラッシュ中じゃなければ、すぐに基準の色にそろえておく
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
                // だんだん小さくなりながら左右に行ったり来たりする（最初がいちばん大きくて、最後に止まる）
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
                // MaterialPropertyBlock で書く＝マテリアルをコピーしないので、ほかの客にもえいきょうしない
                targetRenderer.GetPropertyBlock(_mpb);
                _mpb.SetColor(ColorId, c);
                _mpb.SetColor(BaseColorId, c);
                targetRenderer.SetPropertyBlock(_mpb);
            }
        }
    }
}
