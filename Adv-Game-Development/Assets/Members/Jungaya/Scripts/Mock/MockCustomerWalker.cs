using UnityEngine;

namespace Toufuku.Rescue.Mock
{
    /*
        視認性モック（#44）で「鳥居 → 定位置」まで歩かせるクラス。補充のテンポを見るための、いちばんシンプルなもの

        ・位置をなめらかにつなぐだけ。回転はさわらない（本番の演出のコンポーネントとぶつからないように）
        ・かかる時間と動きのカーブは MockCrowdDirector から渡されて、Inspector で調整できる

        ※ 検証用の使い捨て。Mock/ フォルダごと消せる
    */
    public class MockCustomerWalker : MonoBehaviour
    {
        private Vector3 _from;
        private Vector3 _to;
        private float _duration;
        private float _elapsed;
        private AnimationCurve _ease;
        private bool _walking;
        // #93: とちゅうで立ち止まる場所（歩く時間の割合 0〜1）と長さ（秒）。長さが 0 なら止まらない
        private float _pauseAt;
        private float _pauseSeconds;
        private float _pauseElapsed;

        // 歩いている途中かどうか（立ち止まっている間もふくむ）。Director はこれを見て「定位置に着いた人数」を数える
        public bool IsWalking => _walking;

        // #93: 参道のとちゅうで立ち止まって見回している間か
        public bool IsPaused => _walking && _pauseSeconds > 0f && _pauseElapsed < _pauseSeconds
                                && _duration > 0.0001f && _elapsed >= _duration * _pauseAt;

        // この客が最後に着く定位置
        public Vector3 Destination => _to;

        // 歩いて定位置に着いたときに呼ばれる（SnapTo では呼ばれない）。#62 の移動客はここから往復を始める
        public event System.Action Arrived;

        private void Update()
        {
            if (!_walking) return;

            // #93: 立ち止まる場所に着いたら、決めた秒数だけその場で待つ（位置は止まる場所のまま）
            if (IsPaused)
            {
                _pauseElapsed += Time.deltaTime;
                return;
            }

            _elapsed += Time.deltaTime;
            // 止まる場所を1フレームでとびこえないように、止まる前はそこまでにおさえる
            if (_pauseSeconds > 0f && _pauseElapsed < _pauseSeconds)
                _elapsed = Mathf.Min(_elapsed, _duration * _pauseAt);
            float t = _duration > 0.0001f ? Mathf.Clamp01(_elapsed / _duration) : 1f;
            float eased = _ease != null ? _ease.Evaluate(t) : t;

            transform.position = Vector3.LerpUnclamped(_from, _to, eased);

            if (t >= 1f)
            {
                transform.position = _to;
                _walking = false;
                Arrived?.Invoke();
            }
        }

        /*
            鳥居の位置から定位置に歩き始める
            pauseAt / pauseSeconds: #93 とちゅうで立ち止まって見回す場所（歩く時間の割合 0〜1）と長さ（秒）。0 秒なら止まらない
              集団客は同じ値を全員に渡して、そろって止まる
        */
        public void Begin(Vector3 from, Vector3 to, float duration, AnimationCurve ease, float pauseAt = 0f, float pauseSeconds = 0f)
        {
            _from = from;
            _to = to;
            _duration = Mathf.Max(0f, duration);
            _ease = ease;
            _elapsed = 0f;
            _pauseAt = Mathf.Clamp01(pauseAt);
            _pauseSeconds = Mathf.Max(0f, pauseSeconds);
            _pauseElapsed = 0f;
            _walking = _duration > 0.0001f;

            transform.position = _walking ? from : to;
        }

        // 歩くのをとばして、すぐ定位置に置く（始めにまとめてならべる用）
        public void SnapTo(Vector3 position)
        {
            _from = position;
            _to = position;
            _elapsed = 0f;
            _duration = 0f;
            _pauseSeconds = 0f;
            _walking = false;
            transform.position = position;
        }
    }
}
