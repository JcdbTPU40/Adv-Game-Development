using UnityEngine;

namespace Toufuku.Rescue.Mock
{
    /// <summary>
    /// 視認性モック(#44)の「鳥居 → 定位置」の歩行。補充テンポ確認用の最小実装。
    ///
    /// ・位置を補間するだけ。回転は触らない（本番の演出コンポーネントと競合させないため）。
    /// ・所要時間とイージングは <see cref="MockCrowdDirector"/> から渡され、Inspector で調整できる。
    ///
    /// ※ 検証用の使い捨て。Mock/ ごと削除できる。
    /// </summary>
    public class MockCustomerWalker : MonoBehaviour
    {
        private Vector3 _from;
        private Vector3 _to;
        private float _duration;
        private float _elapsed;
        private AnimationCurve _ease;
        private bool _walking;

        /// <summary>移動中か。Director はこれを見て「定位置に着いた体数」を数える。</summary>
        public bool IsWalking => _walking;

        /// <summary>この客の最終的な定位置。</summary>
        public Vector3 Destination => _to;

        private void Update()
        {
            if (!_walking) return;

            _elapsed += Time.deltaTime;
            float t = _duration > 0.0001f ? Mathf.Clamp01(_elapsed / _duration) : 1f;
            float eased = _ease != null ? _ease.Evaluate(t) : t;

            transform.position = Vector3.LerpUnclamped(_from, _to, eased);

            if (t >= 1f)
            {
                transform.position = _to;
                _walking = false;
            }
        }

        /// <summary>鳥居位置から定位置へ歩き始める。</summary>
        public void Begin(Vector3 from, Vector3 to, float duration, AnimationCurve ease)
        {
            _from = from;
            _to = to;
            _duration = Mathf.Max(0f, duration);
            _ease = ease;
            _elapsed = 0f;
            _walking = _duration > 0.0001f;

            transform.position = _walking ? from : to;
        }

        /// <summary>歩行を省いて定位置に即着地する（開始時の一括配置用）。</summary>
        public void SnapTo(Vector3 position)
        {
            _from = position;
            _to = position;
            _elapsed = 0f;
            _duration = 0f;
            _walking = false;
            transform.position = position;
        }
    }
}
