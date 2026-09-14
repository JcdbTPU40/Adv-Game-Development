using System;
using UnityEngine;
using UnityEngine.Rendering;
using Toufuku.Aim;
using Toufuku.Playtest;

namespace Toufuku.Rescue
{
    /// <summary>
    /// 客の足元の円（危険円＋二重円）— Issue #55（仕様書 v8 6章／13章 観戦要件／付録B UI.DANGER・PRIORITY.MARK）
    ///
    /// ・危険円 … 危険度 D が 50 以上で出て、D が上がるほど濃く・太くなる。85 から明るさが脈打つ。
    /// ・二重円 … 優先救済の対象 1 人に出る細い 2 本の輪。<b>D&lt;50 でも出す</b>（付録B PRIORITY.MARK：D 閾値なし）。
    ///            ゆっくり呼吸するように広がる（危険円と意味が混ざらないよう、動かすのは明るさではなく半径）。
    ///
    /// 見せ方の根拠:
    ///   ・輪郭＝種類／足元＝危険度／頭上＝残り発数 のチャネル分離（v8 6章）を崩さないよう、色は
    ///     危険円＝赤系、二重円＝金系に固定する。お守りの 5 色とは別系統の色にして読み違えを防ぐ。
    ///   ・待機列から見える必要がある（13章）ので、線幅はカメラからの距離で割り増しして、
    ///     遠くの客でも髪の毛のように細くならないようにする。
    ///
    /// 置き方: 客 1 人に 1 つ。<see cref="GroundRingDirector"/> をシーンに 1 つ置けば、
    ///         いま居る客と後から湧く客の両方へ自動で付く（プレハブへ手で付けてもよい）。
    /// </summary>
    [DisallowMultipleComponent]
    public class CustomerGroundRing : MonoBehaviour
    {
        /// <summary>見た目の設定。シーンで 1 か所（GroundRingDirector）にまとめて調整できるよう切り出す。</summary>
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

        /// <summary>この客の生成ID（優先対象の照合に使う ID と同じもの）。</summary>
        public int CustomerId => _id;
        /// <summary>いま危険円が見えているか（確認・テスト用）。</summary>
        public bool DangerRingVisible => _danger != null && _danger.enabled;
        /// <summary>いま二重円が見えているか（確認・テスト用）。</summary>
        public bool PriorityRingVisible => _priorityOuter != null && _priorityOuter.enabled;

        void Awake()
        {
            _state = GetComponent<CustomerState>();
            _zone = GetComponent<HitZoneTarget>();
            BuildRings();
        }

        void OnEnable()
        {
            // ID はスポーン側が振る。振られていなければここで確定させる（優先対象の照合に必ず ID が要る）。
            _id = CustomerSpawnId.Of(gameObject);
        }

        void LateUpdate()
        {
            if (_state == null || _root == null) return;

            // 足元へ置き直す（客が歩いても円は地面に水平のまま付いていく）。
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

            // D≥85 は明るさを脈打たせる（付録B UI.DANGER）。半径は動かさない。
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

            // 呼吸（半径）。危険円の脈打ち（明るさ）と動かす軸を分けて、2 つの意味が混ざらないようにする。
            float breath = style.priorityBreathPerSecond > 0f
                ? 0.5f + 0.5f * Mathf.Sin(Time.time * style.priorityBreathPerSecond * Mathf.PI * 2f)
                : 1f;
            float grow = 1f + style.priorityBreathAmount * (breath - 0.5f) * 2f;

            float r = Radius();
            _innerRadius = SetRing(_priorityInner, _innerRadius, r * style.priorityInnerRadiusScale, style.priorityWidth, style.priorityColor);
            _outerRadius = SetRing(_priorityOuter, _outerRadius, r * style.priorityOuterRadiusScale * grow, style.priorityWidth, style.priorityColor);
        }

        /// <summary>見た目の設定を差し替える（GroundRingDirector がシーン全体へ配る）。</summary>
        public void SetStyle(Style next)
        {
            if (next == null) return;
            style = next;

            // 分割数が変わっているかもしれないので、次の更新で張り直させる。
            _dangerRadius = -1f;
            _innerRadius = -1f;
            _outerRadius = -1f;
        }

        /// <summary>この客に足元の円を付ける（すでに付いていればそれを返す）。</summary>
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
            // 判定の中心は胴の高さにあることがあるので、水平位置だけ借りて足元の高さへ落とす。
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
            lr.alignment = LineAlignment.TransformZ;   // 地面に寝かせる（カメラへ向けない）
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

        /// <summary>
        /// 輪を更新して、張り直した半径を返す。
        /// 半径は点の座標そのもので与える（transform を拡大すると線幅まで一緒に伸びてしまうため）。
        /// </summary>
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

        /// <summary>半径 r の円をローカル XY 平面に張る（この子は X+90 度回してあるので地面に寝る）。</summary>
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

        /// <summary>遠くの客でも線が消えないように、カメラからの距離で太さを割り増しする（13章 観戦要件）。</summary>
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
