using NUnit.Framework;

namespace Salvavida.Tests
{
    public class LexoRankTests
    {
        [Test]
        public void Between_NullNull_ReturnsInitialRank()
        {
            var result = LexoRank.Between(null, null);
            Assert.That(result, Is.EqualTo("A~m"));
        }

        [Test]
        public void Between_SameBucket_ReturnsMidpoint()
        {
            var result = LexoRank.Between("A~a", "A~z");
            Assert.That(result, Does.StartWith("A~"));
            Assert.That(result, Is.GreaterThan("A~a").Using<string>(StringComparer.Ordinal));
            Assert.That(result, Is.LessThan("A~z").Using<string>(StringComparer.Ordinal));
        }

        [Test]
        public void Between_CrossBucket_AppendsToPrev()
        {
            var result = LexoRank.Between("A~z", "B~a");
            Assert.That(result, Is.EqualTo("A~z0"));
        }

        [Test]
        public void Between_PrecisionExpansion_AppendsMinimumChar()
        {
            var result = LexoRank.Between("A~a", "A~b");
            Assert.That(result, Does.StartWith("A~a"));
            Assert.That(result.Length, Is.GreaterThan(3));
            Assert.That(result, Is.GreaterThan("A~a").Using<string>(StringComparer.Ordinal));
            Assert.That(result, Is.LessThan("A~b").Using<string>(StringComparer.Ordinal));
        }

        [Test]
        public void Between_EqualNonNull_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => LexoRank.Between("A~abc", "A~abc"));
        }

        [Test]
        public void Between_InsertAtBeginning()
        {
            var result = LexoRank.Between(null, "A~m");
            Assert.That(result, Is.LessThan("A~m").Using<string>(StringComparer.Ordinal));
        }

        [Test]
        public void Between_InsertAtEnd()
        {
            var result = LexoRank.Between("A~m", null);
            Assert.That(result, Is.GreaterThan("A~m").Using<string>(StringComparer.Ordinal));
        }

        [Test]
        public void Rebalance_ProducesSortedRanks()
        {
            var ranks = LexoRank.Rebalance(10);
            Assert.That(ranks.Length, Is.EqualTo(10));
            for (int i = 1; i < ranks.Length; i++)
            {
                Assert.That(ranks[i], Is.GreaterThan(ranks[i - 1]).Using<string>(StringComparer.Ordinal));
            }
        }

        [Test]
        public void Rebalance_ZeroCount_ReturnsEmpty()
        {
            var ranks = LexoRank.Rebalance(0);
            Assert.That(ranks, Is.Empty);
        }

        [Test]
        public void Rebalance_SingleElement_ReturnsOneRank()
        {
            var ranks = LexoRank.Rebalance(1);
            Assert.That(ranks.Length, Is.EqualTo(1));
        }

        [Test]
        public void NeedsRebalance_SingleElement_ReturnsFalse()
        {
            Assert.That(LexoRank.NeedsRebalance(1, 10), Is.False);
        }

        [Test]
        public void NeedsRebalance_BelowThreshold_ReturnsFalse()
        {
            Assert.That(LexoRank.NeedsRebalance(100, 3), Is.False);
        }

        [Test]
        public void NeedsRebalance_AboveThreshold_ReturnsTrue()
        {
            Assert.That(LexoRank.NeedsRebalance(100, 6), Is.True);
        }

        [Test]
        public void Compare_MatchesStringCompareOrdinal()
        {
            Assert.That(LexoRank.Compare("A~a", "A~b"), Is.EqualTo(string.CompareOrdinal("A~a", "A~b")));
            Assert.That(LexoRank.Compare("A~z", "B~a"), Is.EqualTo(string.CompareOrdinal("A~z", "B~a")));
            Assert.That(LexoRank.Compare("A~m", "A~m"), Is.EqualTo(0));
        }
    }
}
