using NUnit.Framework;

namespace Salvavida.Tests
{
    public class LexoRankTests
    {
        [Test]
        public void Between_NullNull_DefaultBucketSize_ReturnsComputedInitialRank()
        {
            var result = LexoRank.Between(null, null);
            Assert.That(result, Does.Contain(LexoRank.SEPARATOR));
            Assert.That(result, Does.StartWith(LexoRank.DEFAULT_PREFIX + LexoRank.SEPARATOR));
            var lexoPart = result.Split(LexoRank.SEPARATOR)[1];
            Assert.That(lexoPart.Length, Is.EqualTo(2)); // bucketSize=100 needs 2 chars (62^2=3844)
        }

        [Test]
        public void Between_NullNull_LargeBucketSize_ReturnsLongerRank()
        {
            var result = LexoRank.Between(null, null, 5000);
            var lexoPart = result.Split(LexoRank.SEPARATOR)[1];
            Assert.That(lexoPart.Length, Is.EqualTo(3)); // 62^2=3844 < 5000, needs 3 chars
        }

        [Test]
        public void Between_SameBucket_ReturnsMidpoint()
        {
            var result = LexoRank.Between("V~a", "V~z");
            Assert.That(result, Does.StartWith("V~"));
            Assert.That(result, Is.GreaterThan("V~a").Using<string>(StringComparer.Ordinal));
            Assert.That(result, Is.LessThan("V~z").Using<string>(StringComparer.Ordinal));
        }

        [Test]
        public void Between_CrossBucket_ReturnsValueBetween()
        {
            var result = LexoRank.Between("V~z", "W~a");
            Assert.That(result, Is.GreaterThan("V~z").Using<string>(StringComparer.Ordinal));
            Assert.That(result, Is.LessThan("W~a").Using<string>(StringComparer.Ordinal));
            Assert.That(result, Does.StartWith("V~"));
        }

        [Test]
        public void Between_AdjacentChars_AppendsMiddleChar()
        {
            var result = LexoRank.Between("V~V", "V~W");
            Assert.That(result, Is.GreaterThan("V~V").Using<string>(StringComparer.Ordinal));
            Assert.That(result, Is.LessThan("V~W").Using<string>(StringComparer.Ordinal));
            // 当 diffIdx 位置的字符相邻时，在后续位置找中间值
            // "V" vs "W" 相邻，检查下一位置：prevLexo 补位 '0' vs 'z'
            // midVal = (0 + 61) / 2 = 30 = 'U'
            Assert.That(result, Is.EqualTo("V~VU"));
        }

        [Test]
        public void Between_PrecisionExpansion_ReturnsValidRank()
        {
            var result = LexoRank.Between("A~a", "A~b");
            Assert.That(result, Does.StartWith("A~"));
            Assert.That(result.Length, Is.GreaterThan(3));
            Assert.That(result, Is.GreaterThan("A~a").Using<string>(StringComparer.Ordinal));
            Assert.That(result, Is.LessThan("A~b").Using<string>(StringComparer.Ordinal));
        }

        [Test]
        public void Between_EqualNonNull_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => LexoRank.Between("V~abc", "V~abc"));
        }

        [Test]
        public void Between_InsertAtBeginning_ReturnsLessThanNext()
        {
            var result = LexoRank.Between(null, "V~VV");
            Assert.That(result, Is.LessThan("V~VV").Using<string>(StringComparer.Ordinal));
        }

        [Test]
        public void Between_InsertAtEnd_ReturnsGreaterThanPrev()
        {
            var result = LexoRank.Between("V~VV", null);
            Assert.That(result, Is.GreaterThan("V~VV").Using<string>(StringComparer.Ordinal));
        }

        [Test]
        public void Between_PrevIsPrefixOfNext_ReturnsValidRank()
        {
            var result = LexoRank.Between("V~a", "V~a1");
            Assert.That(result, Is.GreaterThan("V~a").Using<string>(StringComparer.Ordinal));
            Assert.That(result, Is.LessThan("V~a1").Using<string>(StringComparer.Ordinal));
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
            Assert.That(LexoRank.Compare("V~a", "V~b"), Is.EqualTo(string.CompareOrdinal("V~a", "V~b")));
            Assert.That(LexoRank.Compare("V~z", "B~a"), Is.EqualTo(string.CompareOrdinal("V~z", "B~a")));
            Assert.That(LexoRank.Compare("V~V", "V~V"), Is.EqualTo(0));
        }

        [Test]
        public void Constants_Separator_IsTilde()
        {
            Assert.That(LexoRank.SEPARATOR, Is.EqualTo("~"));
        }

        [Test]
        public void Constants_DefaultBucketSize_Is100()
        {
            Assert.That(LexoRank.DEFAULT_BUCKET_SIZE, Is.EqualTo(100));
        }

        [Test]
        public void Between_AdjacentChars_Insert100Times_NoErrorAndMaintainsOrder()
        {
            var ranks = new List<string> { "V~VV", "V~VW" };

            for (int i = 0; i < 100; i++)
            {
                var newRank = LexoRank.Between(ranks[i], ranks[i + 1]);
                ranks.Insert(i + 1, newRank);
            }

            Assert.That(ranks.Count, Is.EqualTo(102));

            string longestRank = ranks.OrderByDescending(r => r.Length).First();
            TestContext.Out.WriteLine($"Longest rank: {longestRank} (length: {longestRank.Length})");

            for (int i = 1; i < ranks.Count; i++)
            {
                Assert.That(ranks[i], Is.GreaterThan(ranks[i - 1]).Using<string>(StringComparer.Ordinal));
            }
        }
    }
}
