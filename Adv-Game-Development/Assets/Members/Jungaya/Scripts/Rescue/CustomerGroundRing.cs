using System;
using UnityEngine;
using UnityEngine.Rendering;
using Toufuku.Aim;
using Toufuku.Playtest;

namespace Toufuku.Rescue
{
    /*
        客の足元の円（危険円＋二重円）（#55 / 企画書 v8 6章、13章 観戦要件、付録B UI.DANGER・PRIORITY.MARK）

        ・危険円: 危険度 D が 50 以上で出て、D が上がるほど濃く太くなる。85 から明るさがドクドクする
        ・二重円: 優先救済の相手1人に出る、細い2本の輪。D が 50 より小さくても出す（付録B PRIORITY.MARK: D のしきい値なし）
                  ゆっくり息をするように広がる（危険円と意味がまざらないように、動かすのは明るさじゃなくて半径）

        こういう見せ方にした理由:
          ・輪郭 = 種類、足元 = 危険度、頭の上 = 残りの発数、という分け方（v8 6章）をくずさないように、色は
            危険円 = 赤っぽい色、二重円 = 金っぽい色で固定する。お守りの5色とは別の色にして読みまちがえないようにする
          ・待っている列から見える必要がある（13章）ので、線のはばはカメラからの距離で太くして、
            遠くの客でも髪の毛みたいに細くならないようにする

        置き方: 客1人に1つ。GroundRingDirector をシーンに1つ置けば、
                今いる客にもあとから出てくる客にも自動で付く（プレハブに手で付けてもいい）
    */
    [DisallowMultipleComponent]
    public class CustomerGroundRing : MonoBehaviour
    {
        // 見た目の設定。シーンの1か所（GroundRingDirector）でまとめて調整できるように分けてある
        [Serializable]
        public class Style
        {
            [Header("大きさ（当たり判定の半径に対する倍率）")]
            [Tooltip("危険円の半径。1 で当たり判定の外周と同じ大きさ。")]
            [Min(0.05f)] public float dangerRadiusScale = 1.00f;
            [Tooltip("二重円の内側の輪の半径。")]
            [Min(0.05f)] public float priorityInnerRadiusScale = 1.10f;
            [Tooltip("二重円の外側の輪の半径。")]
            [Min(0.05f)] public float priorityOuterRadiusScale = 1.45f;
            [Tooltip("当たり判定（HitZoneTarget）が無い客で使う半径（m）。")]
            [Min(0.05f)] public float fallbackRadius = 0.9f;

            [Header("線の太さ（m。遠くの客では距離で割り増しする）")]
            [Min(0.005f)] public float dangerWidth = 0.07f;
            [Min(0.005f)] public float priorityWidth = 0.06f;
            [Tooltip("この距離までは指定どおりの太さ。これより遠いと距離に比例して太くする。")]
            [Min(1f)] public float widthReferenceDistance = 12f;
            [Tooltip("太さの割り増しの上限（倍）。")]
            [Min(1f)] public float maxWidthScale = 3.0f;

            [Header("色（輪郭5色・お守り色と混ざらない系統にする）")]
            [Tooltip("D=50 付近の危険円。")]
            public Color dangerNearColor = new Color(1f, 0.55f, 0.25f, 0.55f);
            [Tooltip("D=100 付近の危険円。")]
            public Color dangerFarColor = new Color(1f, 0.12f, 0.12f, 0.95f);
            [Tooltip("二重円。待機列からも読めるよう明るく不透明に。")]
            public Color priorityColor = new Color(1f, 0.92f, 0.45f, 1f);

            [Header("動き")]
            [Tooltip("危険円が脈打つ速さ（回/秒）。D=85 以上で明るさが脈打つ（付録B UI.DANGER）。")]
            [Min(0f)] public float dangerPulsePerSecond = 2.0f;
            [Tooltip("二重円が呼吸する速さ（回/秒）。0 なら動かさない。")]
            [Min(0f)] public float priorityBreathPerSecond = 0.6f;
            [Tooltip("二重円の呼吸で半径が動く幅（半径に対する割合）。")]
            [Range(0f, 0.3f)] public float priorityBreathAmount = 0.08f;

            [Header("地面との重なり")]
            [Tooltip("地面へのめり込みを避けるための持ち上げ（m）。")]
            [Min(0f)] public float groundOffset = 0.02f;
            [Tooltip("円の分割数。多いほど滑らかで重い。")]
            [Range(12, 128)] public int segments = 48;
        }

        [SerializeField] Style style = new Style();

        [Header("確認用")]
        [Tooltip("ON なら二重円の ON/OFF をログに出す（検証シーン用）。")]
        [SerializeField] bool logChanges = false;

        static Material s_material;

        CustomerState _state;
        HitZoneTarget _zone;
        Transform _root;
        LineRenderer _danger;
        LineRenderer _priorityInner;
        LineRenderer _priorityOuter;
        float _dangerRadius = -1f;
        float _innerRadius = -1f;
        float _outerRadius = -1f;
        Camera _camera;
        int _id;
        bool _lastPriority;

        // この客の生成ID（優先の相手と照らし合わせるときに使うIDと同じ）
        public int CustomerId => _id;
        // 今危険円が見えているかどうか（確認・テスト用）
        public bool DangerRingVisible => _danger != null && _danger.enabled;
        // 今二重円が見えているかどうか（確認・テスト用）
        public bool PriorityRingVisible => _priorityOuter != null && _priorityOuter.enabled;

        void Awake()
        {
            _state = GetComponent<CustomerState>();
            _zone = GetComponent<HitZoneTarget>();
            BuildRings();
        }

        void OnEnable()
        {
            // IDは出す側が付ける。付いていなければここで決める（優先の相手と照らし合わせるのに必ずIDがいる）
            _id = CustomerSpawnId.Of(gameObject);
        }

        void LateUpdate()
        {
            if (_state == null || _root == null) return;

            // 足元に置きなおす（客が歩いても、円は地面に水平なままついていく）
            _root.position = FeetPosition();
            _root.rotation = Quaternion.identity;

            bool rescueTarget = _state.IsRescueTarget;
            float danger = _state.Danger;

            UpdateDangerRing(danger, rescueTarget);
            UpdatePriorityRing(danger, rescueTarget);
        }

        void UpdateDangerRing(float danger, bool rescueTarget)
        {
            bool show = DangerRingDisplay.ShowsDangerRing(danger, rescueTarget);
            _danger.enabled = show;
            if (!show) return;

            float amount = DangerRingDisplay.DangerAmount01(danger);
            Color color = Color.Lerp(style.dangerNearColor, style.dangerFarColor, amount);

            // D が 85 以上なら明るさをドクドクさせる（付録B UI.DANGER）。半径は動かさない
            if (DangerRingDisplay.Pulses(danger))
            {
                float pulse = DangerRingDisplay.PulseAmount01(danger, Time.time, style.dangerPulsePerSecond);
                color.a *= Mathf.Lerp(0.45f, 1f, pulse);
            }

            _dangerRadius = SetRing(_danger, _dangerRadius, Radius() * style.dangerRadiusScale,
                style.dangerWidth * Mathf.Lerp(1f, 1.6f, amount), color);
        }

        void UpdatePriorityRing(float danger, bool rescueTarget)
        {
            bool show = DangerRingDisplay.ShowsPriorityRing(PriorityRescue.IsPriority(_id), rescueTarget);

            _priorityInner.enabled = show;
            _priorityOuter.enabled = show;

            if (show != _lastPriority)
            {
                _lastPriority = show;
                if (logChanges)
                    Debug.Log($"[GroundRing] 二重円 {(show ? "ON" : "OFF")} : ID={_id} D={danger:0.0} ({name})", this);
            }

            if (!show) return;

            // 息をするように半径を変える。危険円のドクドク（明るさ）と動かすものを分けて、2つの意味がまざらないようにする
            float breath = style.priorityBreathPerSecond > 0f
                ? 0.5f + 0.5f * Mathf.Sin(Time.time * style.priorityBreathPerSecond * Mathf.PI * 2f)
                : 1f;
            float grow = 1f + style.priorityBreathAmount * (breath - 0.5f) * 2f;

            float r = Radius();
            _innerRadius = SetRing(_priorityInner, _innerRadius, r * style.priorityInnerRadiusScale, style.priorityWidth, style.priorityColor);
            _outerRadius = SetRing(_priorityOuter, _outerRadius, r * style.priorityOuterRadiusScale * grow, style.priorityWidth, style.priorityColor);
        }

        // 見た目の設定を入れかえる（GroundRingDirector がシーン全体に配る）
        public void SetStyle(Style next)
        {
            if (next == null) return;
            style = next;

            // 分ける数が変わっているかもしれないので、次の更新で作りなおさせる
            _dangerRadius = -1f;
            _innerRadius = -1f;
            _outerRadius = -1f;
        }

        // この客に足元の円を付ける（もう付いていたらそれを返す）
        public static CustomerGroundRing EnsureOn(GameObject customer, Style style = null)
        {
            if (customer == null) return null;

            CustomerGroundRing ring = customer.GetComponent<CustomerGroundRing>();
            if (ring == null) ring = customer.AddComponent<CustomerGroundRing>();
            if (style != null) ring.SetStyle(style);
            return ring;
        }

        float Radius()
        {
            return _zone != null ? _zone.Radius : style.fallbackRadius;
        }

        Vector3 FeetPosition()
        {
            // 判定の中心は胴の高さにあることがあるので、水平の位置だけ使って足元の高さに下ろす
            Vector3 center = _zone != null ? _zone.Center : transform.position;
            return new Vector3(center.x, transform.position.y + style.groundOffset, center.z);
        }

        void BuildRings()
        {
            var root = new GameObject("GroundRings");
            root.transform.SetParent(transform, false);
            _root = root.transform;

            _danger = CreateRing("DangerRing");
            _priorityInner = CreateRing("PriorityRingInner");
            _priorityOuter = CreateRing("PriorityRingOuter");
        }

        LineRenderer CreateRing(string ringName)
        {
            var go = new GameObject(ringName);
            go.transform.SetParent(_root, false);

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.alignment = LineAlignment.TransformZ;   // 地面に寝かせる（カメラのほうに向けない）
            lr.textureMode = LineTextureMode.Stretch;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCornerVertices = 0;
            lr.numCapVertices = 0;
            lr.enabled = false;

            if (s_material == null) s_material = AimRenderUtil.CreateTransparentMaterial("GroundRing");
            if (s_material != null) lr.sharedMaterial = s_material;

            lr.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            return lr;
        }

        /*
            輪を更新して、作りなおした半径を返す
            半径は点の座標そのもので決める（transform を大きくすると線のはばまでいっしょにのびてしまうから）
        */
        float SetRing(LineRenderer lr, float builtRadius, float radius, float width, Color color)
        {
            float r = Mathf.Max(0.0001f, radius);
            if (Mathf.Abs(builtRadius - r) > 0.0001f || lr.positionCount != Mathf.Clamp(style.segments, 12, 128))
                BuildCircle(lr, r);

            lr.widthMultiplier = width * WidthScale();
            lr.startColor = color;
            lr.endColor = color;
            return r;
        }

        // 半径 r の円を、ローカルの XY 平面に作る（この子オブジェクトは X に 90 度回してあるので地面に寝る）
        void BuildCircle(LineRenderer lr, float r)
        {
            int segments = Mathf.Clamp(style.segments, 12, 128);
            lr.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
            }
        }

        // 遠くの客でも線が消えないように、カメラからの距離で太さを増やす（13章 観戦要件）
        float WidthScale()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return 1f;

            float distance = Vector3.Distance(_camera.transform.position, _root.position);
            float scale = distance / Mathf.Max(1f, style.widthReferenceDistance);
            return Mathf.Clamp(scale, 1f, Mathf.Max(1f, style.maxWidthScale));
        }
    }
}
