using UnityEngine;

namespace Toufuku.Aim
{
    /// <summary>
    /// 照準位置の計算 — Issue #60（仕様書 v8 4章）
    ///
    /// 狙いは向きだけで決める。ヨー＝左右、ピッチ＝地面上の距離（既定 3〜18m）。
    /// 振りの強さはここに一切入らないので、強く振っても着弾点はずれない。
    /// MonoBehaviour に依存しない計算だけを置き、EditMode テストで確かめる。
    /// </summary>
    public static class AimSolver
    {
        public const float DefaultNearDistance = 3f;
        public const float DefaultFarDistance = 18f;

        /// <summary>
        /// ピッチ角（度）を地面上の距離へ正規化する。範囲外のピッチは近端／遠端に張り付く。
        /// pitchAtNear と pitchAtFar はどちらが大きくてもよい（センサーの向きに合わせて入れ替えられる）。
        /// </summary>
        public static float PitchToDistance(float pitch, float pitchAtNear, float pitchAtFar, float nearDistance, float farDistance)
        {
            if (Mathf.Approximately(pitchAtNear, pitchAtFar)) return nearDistance;
            float t = Mathf.InverseLerp(pitchAtNear, pitchAtFar, pitch);
            return Mathf.Lerp(nearDistance, farDistance, t);
        }

        /// <summary>ワールドのヨー角（度、+Z が 0、時計回りが正）から水平方向の単位ベクトルを返す。</summary>
        public static Vector3 DirectionFromYaw(float worldYawDegrees)
        {
            return Quaternion.Euler(0f, worldYawDegrees, 0f) * Vector3.forward;
        }

        /// <summary>水平ベクトルのヨー角（度）。長さがほぼ 0 なら fallback を返す。</summary>
        public static float YawOf(Vector3 direction, float fallback = 0f)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-8f) return fallback;
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        /// <summary>基準点から、ワールドのヨー角の向きへ distance 進んだ地面（高さ groundY）上の点。</summary>
        public static Vector3 GroundPoint(Vector3 origin, float worldYawDegrees, float distance, float groundY)
        {
            Vector3 p = origin + DirectionFromYaw(worldYawDegrees) * distance;
            p.y = groundY;
            return p;
        }

        /// <summary>地面上の点を、基準点から見たヨー角（度）と水平距離に分解する。</summary>
        public static void ToPolar(Vector3 origin, Vector3 point, out float worldYawDegrees, out float distance)
        {
            Vector3 d = point - origin;
            d.y = 0f;
            distance = d.magnitude;
            worldYawDegrees = YawOf(d);
        }

        /// <summary>ビューポート座標（0〜1）の点を、縁から margin だけ内側へ収める。</summary>
        public static Vector2 ClampToViewport(Vector2 viewport, float margin)
        {
            margin = Mathf.Clamp(margin, 0f, 0.49f);
            return new Vector2(
                Mathf.Clamp(viewport.x, margin, 1f - margin),
                Mathf.Clamp(viewport.y, margin, 1f - margin));
        }

        /// <summary>ビューポート座標（z = カメラからの奥行き）が、カメラ前方かつ縁から margin 以上内側にあるか。</summary>
        public static bool IsInsideViewport(Vector3 viewport, float margin)
        {
            return viewport.z > 0f
                && viewport.x >= margin && viewport.x <= 1f - margin
                && viewport.y >= margin && viewport.y <= 1f - margin;
        }
    }
}
