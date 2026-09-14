using NUnit.Framework;
using UnityEngine;

namespace Toufuku.Aim.Tests
{
    /// <summary>#60 命中精度の 3 段階（中心 40% 以内 / 40〜70% / 70〜100%）。</summary>
    public class HitAccuracyTests
    {
        [TestCase(0f, HitZone.Center)]
        [TestCase(0.39f, HitZone.Center)]
        [TestCase(0.40f, HitZone.Center)]
        [TestCase(0.41f, HitZone.Inner)]
        [TestCase(0.70f, HitZone.Inner)]
        [TestCase(0.71f, HitZone.Outer)]
        [TestCase(1.00f, HitZone.Outer)]
        [TestCase(1.01f, HitZone.Miss)]
        public void 割合から命中ゾーンを決める(float normalized, HitZone expected)
        {
            Assert.AreEqual(expected, HitAccuracy.ZoneOf(normalized));
        }

        [Test]
        public void ちょうど40パーセントと70パーセントは内側に入る()
        {
            Assert.AreEqual(HitZone.Center, HitAccuracy.ZoneOf(HitAccuracy.NormalizedDistance(Vector3.zero, new Vector3(0.36f, 0f, 0f), 0.9f)));
            Assert.AreEqual(HitZone.Inner, HitAccuracy.ZoneOf(HitAccuracy.NormalizedDistance(Vector3.zero, new Vector3(0f, 0f, 0.63f), 0.9f)));
        }

        [Test]
        public void 距離は水平成分だけで測り高さは無視する()
        {
            float n = HitAccuracy.NormalizedDistance(new Vector3(0f, 1f, 0f), new Vector3(0.45f, 0f, 0f), 0.9f);
            Assert.AreEqual(0.5f, n, 1e-5f);
        }

        [Test]
        public void 半径が0以下なら必ず外れる()
        {
            Assert.AreEqual(HitZone.Miss, HitAccuracy.ZoneOf(HitAccuracy.NormalizedDistance(Vector3.zero, Vector3.zero, 0f)));
        }

        [Test]
        public void NaNは外れ扱い()
        {
            Assert.AreEqual(HitZone.Miss, HitAccuracy.ZoneOf(float.NaN));
        }
    }
}
