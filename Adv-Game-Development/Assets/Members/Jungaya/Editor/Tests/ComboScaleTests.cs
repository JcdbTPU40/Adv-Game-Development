using NUnit.Framework;

namespace Toufuku.Feedback.Tests
{
    /// <summary>#64 福の連なりで命中音の音階が 1 段ずつ上がり、3・6・10 連続で和音が足される。</summary>
    public class ComboScaleTests
    {
        [Test]
        public void 連なりが伸びるたびに音階は必ず上がる()
        {
            int prev = ComboScale.SemitoneOf(1);
            for (int combo = 2; combo <= 11; combo++)
            {
                int now = ComboScale.SemitoneOf(combo);
                Assert.Greater(now, prev, $"combo={combo}");
                prev = now;
            }
        }

        [Test]
        public void 一連なり目は根音で_ペンタトニックで上がる()
        {
            int[] expected = { 0, 2, 4, 7, 9, 12, 14, 16, 19, 21, 24 };
            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(expected[i], ComboScale.SemitoneOf(i + 1), $"combo={i + 1}");
        }

        [Test]
        public void 上限の段で頭打ちになる()
        {
            int top = ComboScale.SemitoneOf(ComboScale.DefaultMaxStep + 1);
            Assert.AreEqual(top, ComboScale.SemitoneOf(ComboScale.DefaultMaxStep + 2));
            Assert.AreEqual(top, ComboScale.SemitoneOf(99));
        }

        [TestCase(0)]
        [TestCase(-3)]
        public void 連なり0以下は根音(int combo)
        {
            Assert.AreEqual(0, ComboScale.SemitoneOf(combo));
        }

        [Test]
        public void 半音12でピッチ2倍()
        {
            Assert.AreEqual(1f, ComboScale.PitchOf(0), 1e-5f);
            Assert.AreEqual(2f, ComboScale.PitchOf(12), 1e-5f);
        }

        [Test]
        public void 節目3_6_10だけ和音が足され_節目ごとに厚くなる()
        {
            Assert.IsEmpty(ComboScale.ChordOf(2));
            Assert.IsEmpty(ComboScale.ChordOf(4));
            Assert.IsEmpty(ComboScale.ChordOf(11));

            int c3 = ComboScale.ChordOf(3).Length;
            int c6 = ComboScale.ChordOf(6).Length;
            int c10 = ComboScale.ChordOf(10).Length;
            Assert.Greater(c3, 0);
            Assert.Greater(c6, c3);
            Assert.Greater(c10, c6);
        }

        [Test]
        public void 節目は設定で差し替えられる()
        {
            int[] milestones = { 2, 5 };
            Assert.AreEqual(1, ComboScale.MilestoneLevelOf(2, milestones));
            Assert.AreEqual(2, ComboScale.MilestoneLevelOf(5, milestones));
            Assert.AreEqual(0, ComboScale.MilestoneLevelOf(3, milestones));
        }
    }
}
