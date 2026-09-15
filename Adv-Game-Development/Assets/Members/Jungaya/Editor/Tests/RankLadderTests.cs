using NUnit.Framework;

namespace Toufuku.Scoring.Tests
{
    /// <summary>
    /// ランクの昇格・降格（ヒステリシス） — Issue #61（仕様書 v8 7章「ランクの閾値」／付録B RANK.UP・RANK.DOWN）
    ///   昇格 C→B 60 / B→A 150 / A→S 250、降格 B→C 40 / A→B 120 / S→A 210
    /// </summary>
    public class RankLadderTests
    {
        static readonly RankThresholds T = RankThresholds.Default;

        [TestCase(ShrineRank.C, 59f, ShrineRank.C)]
        [TestCase(ShrineRank.C, 60f, ShrineRank.B)]
        [TestCase(ShrineRank.B, 149f, ShrineRank.B)]
        [TestCase(ShrineRank.B, 150f, ShrineRank.A)]
        [TestCase(ShrineRank.A, 249f, ShrineRank.A)]
        [TestCase(ShrineRank.A, 250f, ShrineRank.S)]
        public void 昇格の値以上で上がる(ShrineRank current, float rating, ShrineRank expected)
        {
            Assert.AreEqual(expected, RankLadder.Next(current, rating, T));
        }

        [TestCase(ShrineRank.B, 40f, ShrineRank.B)]
        [TestCase(ShrineRank.B, 39f, ShrineRank.C)]
        [TestCase(ShrineRank.A, 120f, ShrineRank.A)]
        [TestCase(ShrineRank.A, 119f, ShrineRank.B)]
        [TestCase(ShrineRank.S, 210f, ShrineRank.S)]
        [TestCase(ShrineRank.S, 209f, ShrineRank.A)]
        public void 降格の値より下がったら下がる(ShrineRank current, float rating, ShrineRank expected)
        {
            Assert.AreEqual(expected, RankLadder.Next(current, rating, T));
        }

        [Test]
        public void 境界の間ではランクがちらつかない()
        {
            // 60 で B に上がったあと、50 と 60 を行き来しても B のまま
            ShrineRank rank = RankLadder.Next(ShrineRank.C, 60f, T);
            Assert.AreEqual(ShrineRank.B, rank);
            rank = RankLadder.Next(rank, 50f, T);
            Assert.AreEqual(ShrineRank.B, rank);
            rank = RankLadder.Next(rank, 60f, T);
            Assert.AreEqual(ShrineRank.B, rank);

            // C のままなら 50 では上がらない
            Assert.AreEqual(ShrineRank.C, RankLadder.Next(ShrineRank.C, 50f, T));
        }

        [Test]
        public void 大きく動いたら何段でも動く()
        {
            Assert.AreEqual(ShrineRank.S, RankLadder.Next(ShrineRank.C, 300f, T));
            Assert.AreEqual(ShrineRank.C, RankLadder.Next(ShrineRank.S, 0f, T));
            Assert.AreEqual(ShrineRank.B, RankLadder.Next(ShrineRank.S, 119f, T), "S から 119 なら A を通って B");
        }

        [Test]
        public void 高いほうのランク()
        {
            Assert.AreEqual(ShrineRank.A, RankLadder.Higher(ShrineRank.A, ShrineRank.B));
            Assert.AreEqual(ShrineRank.S, RankLadder.Higher(ShrineRank.C, ShrineRank.S));
        }

        [Test]
        public void 付録Bのしきい値は正しい組み合わせ()
        {
            Assert.IsTrue(RankLadder.IsValid(T, 300f, out string error), error);
        }

        [Test]
        public void ヒステリシスがないしきい値はだめ()
        {
            var t = RankThresholds.Default;
            t.demoteToC = 60f;
            Assert.IsFalse(RankLadder.IsValid(t, 300f, out _));
        }

        [Test]
        public void 上限に届かないSはだめ()
        {
            Assert.IsFalse(RankLadder.IsValid(T, 200f, out _));
        }
    }
}
