using UnityEngine;

namespace Toufuku.Aim
{
    /*
        照準の位置を計算するクラス（#60 / 企画書 v8 4章）

        ねらう場所は向きだけで決める。ヨーが左右、ピッチが地面の上の距離（ふつうは 3〜18m）
        振りの強さはここでは使わないので、強く振っても落ちる場所はずれない
        MonoBehaviour を使わない計算だけをここに置いて、EditMode テストで確かめている
    */
    public static class AimSolver
    {
        public const float DefaultNearDistance = 3f;
        public const float DefaultFarDistance = 18f;

        /*
            ピッチ角（度）を地面の上の距離に変える。範囲の外のピッチは、近いほうか遠いほうの端にくっつける
            pitchAtNear と pitchAtFar はどっちが大きくてもいい（センサーの向きに合わせて入れかえられるように）
        */
        public static float PitchToDistance(float pitch, float pitchAtNear, float pitchAtFar, float nearDistance, float farDistance)
        {
            if (Mathf.Approximately(pitchAtNear, pitchAtFar)) return nearDistance;
            float t = Mathf.InverseLerp(pitchAtNear, pitchAtFar, pitch);
            return Mathf.Lerp(nearDistance, farDistance, t);
        }

        // ワールドのヨー角（度）から、水平方向の長さ1のベクトルを返す。+Z が 0 度で、時計回りがプラス
        public static Vector3 DirectionFromYaw(float worldYawDegrees)
        {
            return Quaternion.Euler(0f, worldYawDegrees, 0f) * Vector3.forward;
        }

        // 水平なベクトルのヨー角（度）を返す。長さがほぼ 0 のときは fallback を返す
        public static float YawOf(Vector3 direction, float fallback = 0f)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-8f) return fallback;
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        // 基準点から、ヨー角の向きに distance だけ進んだ地面（高さ groundY）の上の点を返す
        public static Vector3 GroundPoint(Vector3 origin, float worldYawDegrees, float distance, float groundY)
        {
            Vector3 p = origin + DirectionFromYaw(worldYawDegrees) * distance;
            p.y = groundY;
            return p;
        }

        // 地面の上の点を、基準点から見たヨー角（度）と水平距離に分ける
        public static void ToPolar(Vector3 origin, Vector3 point, out float worldYawDegrees, out float distance)
        {
            Vector3 d = point - origin;
            d.y = 0f;
            distance = d.magnitude;
            worldYawDegrees = YawOf(d);
        }

        // ビューポート座標（0〜1）の点を、ふちから margin だけ内側に入れる
        public static Vector2 ClampToViewport(Vector2 viewport, float margin)
        {
            margin = Mathf.Clamp(margin, 0f, 0.49f);
            return new Vector2(
                Mathf.Clamp(viewport.x, margin, 1f - margin),
                Mathf.Clamp(viewport.y, margin, 1f - margin));
        }

        // ビューポート座標の点が、カメラの前にあって、ふちから margin 以上内側にあるかを調べる（z はカメラからの奥行き）
        public static bool IsInsideViewport(Vector3 viewport, float margin)
        {
            return viewport.z > 0f
                && viewport.x >= margin && viewport.x <= 1f - margin
                && viewport.y >= margin && viewport.y <= 1f - margin;
        }
    }
}
