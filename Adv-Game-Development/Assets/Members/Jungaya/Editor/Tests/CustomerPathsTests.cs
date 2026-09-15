using NUnit.Framework;
using UnityEngine;

namespace Toufuku.Rescue.Tests
{
    /// <summary>
    /// 移動客の往復経路と退場経路（企画書 v8 10章「左右3mの往復経路」／6章「参道を歩いて退場」／付録B MOVE.SPEED）— Issue #62
    /// </summary>
    public class CustomerPathsTests
    {
        // ── 往復経路 ────────────────────────────────────────────

        [TestCase(0f, 0f)]
        [TestCase(1f, 1f)]
        [TestCase(1.5f, 1.5f)]   // 右端で折り返す
        [TestCase(3f, 0f)]       // 中心を通過
        [TestCase(4.5f, -1.5f)]  // 左端で折り返す
        [TestCase(6f, 0f)]       // 1往復で中心に戻る
        [TestCase(7f, 1f)]       // 2周目も同じ経路
        public void 幅3mなら中心から右へ歩き出し両端で折り返す(float travelled, float expected)
        {
            Assert.AreEqual(expected, PatrolPath.Offset(3f, travelled, startSign: 1), 0.0001f);
        }

        [Test]
        public void 左から歩き出すと右からの経路の鏡写しになる()
        {
            for (float s = 0f; s <= 12f; s += 0.25f)
                Assert.AreEqual(-PatrolPath.Offset(3f, s, 1), PatrolPath.Offset(3f, s, -1), 0.0001f, $"s={s}");
        }

        [Test]
        public void 往復は幅の半分より外へ出ない()
        {
            for (float s = 0f; s <= 30f; s += 0.05f)
                Assert.LessOrEqual(Mathf.Abs(PatrolPath.Offset(3f, s, 1)), 1.5f + 0.0001f);
        }

        [Test]
        public void 速度1mの毎秒なら6秒で1往復して中心に戻る()
        {
            const float speed = 1f;   // 付録B MOVE.SPEED
            float travelled = 0f;
            for (int frame = 0; frame < 360; frame++) travelled += speed * (1f / 60f);

            Assert.AreEqual(0f, PatrolPath.Offset(3f, travelled, 1), 0.01f);
        }

        [Test]
        public void 幅0なら動かない()
        {
            Assert.AreEqual(0f, PatrolPath.Offset(0f, 5f, 1));
        }

        [TestCase(8f, 7f)]      // 右端 8.5 に経路の端を合わせる
        [TestCase(-8.4f, -7f)]
        [TestCase(2f, 2f)]
        public void 経路が帯の左右範囲からはみ出さないよう中心を寄せる(float center, float expected)
        {
            Assert.AreEqual(expected, PatrolPath.ClampCenter(center, 3f, -8.5f, 8.5f), 0.0001f);
        }

        [Test]
        public void 帯が経路より狭ければ帯の中央()
        {
            Assert.AreEqual(1f, PatrolPath.ClampCenter(5f, 3f, 0f, 2f), 0.0001f);
        }

        [Test]
        public void レーン同士の距離は横の重なりを差し引いて測る()
        {
            var a = new Vector3(0f, 1f, 20f);

            Assert.AreEqual(0f, PatrolPath.LaneToLane(a, 3f, new Vector3(1f, 1f, 20f), 0f), 0.0001f, "同じ奥行きで経路の上に立つ");
            Assert.AreEqual(1f, PatrolPath.LaneToLane(a, 3f, new Vector3(2.5f, 1f, 20f), 0f), 0.0001f, "端から1m");
            Assert.AreEqual(2f, PatrolPath.LaneToLane(a, 3f, new Vector3(0f, 1f, 22f), 0f), 0.0001f, "奥行きだけ2m離れる");
            Assert.AreEqual(5f, PatrolPath.LaneToLane(a, 0f, new Vector3(3f, 1f, 24f), 0f), 0.0001f, "幅0同士は点の距離");
        }

        // ── 退場経路 ────────────────────────────────────────────

        static readonly Vector3[] Exits = { new Vector3(-9f, 1f, 37f), new Vector3(9f, 1f, 37f) };

        [Test]
        public void 左右位置が近い出口へ向かう()
        {
            Assert.AreEqual(0, ExitRoute.Choose(new Vector3(-2f, 1f, 22f), Exits));
            Assert.AreEqual(1, ExitRoute.Choose(new Vector3(3f, 1f, 30f), Exits));
        }

        [Test]
        public void 真ん中の客は添字の小さい出口()
        {
            Assert.AreEqual(0, ExitRoute.Choose(new Vector3(0f, 1f, 25f), Exits));
        }

        [Test]
        public void 出口が無ければマイナス1()
        {
            Assert.AreEqual(-1, ExitRoute.Choose(Vector3.zero, null));
            Assert.AreEqual(-1, ExitRoute.Choose(Vector3.zero, new Vector3[0]));
        }

        [Test]
        public void 奥の客ほど帰路が長い()
        {
            // ShootPos z=38.73 から 近5m／中10m／遠15m（同じ左側 x=-3）
            float near = ExitRoute.Length(new Vector3(-3f, 1f, 33.7f), Exits[0]);
            float middle = ExitRoute.Length(new Vector3(-3f, 1f, 28.7f), Exits[0]);
            float far = ExitRoute.Length(new Vector3(-3f, 1f, 23.7f), Exits[0]);

            Assert.Less(near, middle);
            Assert.Less(middle, far);
        }
    }
}
