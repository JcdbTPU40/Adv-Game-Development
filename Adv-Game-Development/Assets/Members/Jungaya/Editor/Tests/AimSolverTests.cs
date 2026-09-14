using NUnit.Framework;
using UnityEngine;

namespace Toufuku.Aim.Tests
{
    /// <summary>#60 ヨー／ピッチ → 照準位置、画面外の押し戻し。</summary>
    public class AimSolverTests
    {
        const float Eps = 1e-4f;

        static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.AreEqual(expected.x, actual.x, Eps, "x");
            Assert.AreEqual(expected.y, actual.y, Eps, "y");
            Assert.AreEqual(expected.z, actual.z, Eps, "z");
        }

        // ---- ピッチ → 距離 ----

        [Test]
        public void ピッチの近端で3m_遠端で18m()
        {
            Assert.AreEqual(3f, AimSolver.PitchToDistance(-30f, -30f, 20f, 3f, 18f), Eps);
            Assert.AreEqual(18f, AimSolver.PitchToDistance(20f, -30f, 20f, 3f, 18f), Eps);
            Assert.AreEqual(10.5f, AimSolver.PitchToDistance(-5f, -30f, 20f, 3f, 18f), Eps);
        }

        [Test]
        public void 範囲外のピッチは3から18mに張り付く()
        {
            Assert.AreEqual(3f, AimSolver.PitchToDistance(-90f, -30f, 20f, 3f, 18f), Eps);
            Assert.AreEqual(18f, AimSolver.PitchToDistance(90f, -30f, 20f, 3f, 18f), Eps);
        }

        [Test]
        public void ピッチの向きを逆に設定しても補間できる()
        {
            Assert.AreEqual(18f, AimSolver.PitchToDistance(-30f, 20f, -30f, 3f, 18f), Eps);
            Assert.AreEqual(3f, AimSolver.PitchToDistance(20f, 20f, -30f, 3f, 18f), Eps);
        }

        [Test]
        public void 近端と遠端のピッチが同じなら近端の距離()
        {
            Assert.AreEqual(3f, AimSolver.PitchToDistance(10f, 0f, 0f, 3f, 18f), Eps);
        }

        // ---- ヨー → 左右 ----

        [Test]
        public void ヨー0度は前方_90度は右_地面の高さに置く()
        {
            Vector3 origin = new Vector3(1f, 1.5f, 2f);
            AssertVector(new Vector3(1f, 0f, 12f), AimSolver.GroundPoint(origin, 0f, 10f, 0f));
            AssertVector(new Vector3(11f, 0f, 2f), AimSolver.GroundPoint(origin, 90f, 10f, 0f));
            AssertVector(new Vector3(1f, 0f, -8f), AimSolver.GroundPoint(origin, 180f, 10f, 0f));
        }

        [Test]
        public void 地面上の点からヨーと距離へ戻せる()
        {
            Vector3 origin = new Vector3(0f, 1.5f, 38.7f);
            Vector3 p = AimSolver.GroundPoint(origin, 180f - 25f, 12f, 0f);
            AimSolver.ToPolar(origin, p, out float yaw, out float distance);
            Assert.AreEqual(12f, distance, Eps);
            Assert.AreEqual(0f, Mathf.DeltaAngle(155f, yaw), Eps);
        }

        [Test]
        public void 同じヨーとピッチからは必ず同じ照準位置になる()
        {
            Vector3 origin = new Vector3(0f, 1.5f, 38.7f);
            float d = AimSolver.PitchToDistance(3.3f, -30f, 20f, 3f, 18f);
            Vector3 a = AimSolver.GroundPoint(origin, 171.2f, d, 0f);
            Vector3 b = AimSolver.GroundPoint(origin, 171.2f, AimSolver.PitchToDistance(3.3f, -30f, 20f, 3f, 18f), 0f);
            Assert.AreEqual(a, b);
        }

        // ---- 画面外の押し戻し ----

        [Test]
        public void ビューポートの外は縁の内側へ押し戻す()
        {
            Vector2 c = AimSolver.ClampToViewport(new Vector2(1.2f, -0.1f), 0.05f);
            Assert.AreEqual(0.95f, c.x, Eps);
            Assert.AreEqual(0.05f, c.y, Eps);
        }

        [Test]
        public void ビューポートの内側はそのまま()
        {
            Vector2 c = AimSolver.ClampToViewport(new Vector2(0.3f, 0.7f), 0.05f);
            Assert.AreEqual(0.3f, c.x, Eps);
            Assert.AreEqual(0.7f, c.y, Eps);
        }

        [Test]
        public void カメラの後ろは画面内とみなさない()
        {
            Assert.IsFalse(AimSolver.IsInsideViewport(new Vector3(0.5f, 0.5f, -1f), 0.05f));
            Assert.IsTrue(AimSolver.IsInsideViewport(new Vector3(0.5f, 0.5f, 10f), 0.05f));
            Assert.IsFalse(AimSolver.IsInsideViewport(new Vector3(0.97f, 0.5f, 10f), 0.05f));
        }
    }
}
