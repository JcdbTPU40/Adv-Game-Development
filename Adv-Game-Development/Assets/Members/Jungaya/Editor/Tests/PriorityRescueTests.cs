using NUnit.Framework;

namespace Toufuku.Rescue.Tests
{
    /// <summary>
    /// 優先救済 +50 の成立条件 — Issue #55（仕様書 v8 7章 得点表／付録B B-2）
    ///
    /// 「通常弾の SwingAccepted 時に保存した優先対象IDと、その弾が<b>救済完了させた</b>参拝客IDが一致」
    /// のときだけ加点する。飛翔中に二重円が移っても保存値は変わらないので、ここは ID の照合だけで済む。
    /// </summary>
    public class PriorityRescueTests
    {
        [Test]
        public void 保存した対象を救済完了させたら加点()
        {
            Assert.IsTrue(PriorityRescue.IsBonusHit(savedTargetId: 7, rescuedCustomerId: 7, rescued: true));
        }

        [Test]
        public void 別の客を救済しても加点しない()
        {
            Assert.IsFalse(PriorityRescue.IsBonusHit(savedTargetId: 7, rescuedCustomerId: 3, rescued: true));
        }

        [Test]
        public void 救済完了していない途中命中では加点しない()
        {
            // 欲張り客の1発目・ボス客の1〜2発目（付録B B-2「部分点なし」）。
            Assert.IsFalse(PriorityRescue.IsBonusHit(savedTargetId: 7, rescuedCustomerId: 7, rescued: false));
        }

        [Test]
        public void 発射時に二重円が居なければ加点しない()
        {
            Assert.IsFalse(PriorityRescue.IsBonusHit(PriorityRescue.NoTarget, rescuedCustomerId: 7, rescued: true));
        }

        [Test]
        public void IDが振られていない客は加点しない()
        {
            Assert.IsFalse(PriorityRescue.IsBonusHit(savedTargetId: 7, rescuedCustomerId: PriorityRescue.NoTarget, rescued: true));
        }

        [Test]
        public void 飛翔中に二重円が移っても発射時の対象なら加点される()
        {
            // 「発射から着弾までの 0.65 秒間に対象が移動しても、表示どおり狙った +50 が入る」（#55 完了条件）。
            // 弾は発射時の ID（=2）を持ち続ける。着弾時点の二重円が別の客（=5）へ移っていても、
            // 判定に使うのは弾の保存値なので結果は変わらない。
            const int savedAtSwing = 2;
            const int priorityNow = 5;

            Assert.IsTrue(PriorityRescue.IsBonusHit(savedAtSwing, rescuedCustomerId: 2, rescued: true));
            Assert.AreNotEqual(savedAtSwing, priorityNow, "着弾時の二重円は別人でよい（判定に使わない）");
        }
    }
}
