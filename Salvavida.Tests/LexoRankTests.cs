using Xunit;

namespace Salvavida.Tests
{
    public class LexoRankTests(ITestOutputHelper output)
    {
        #region Insert Method Tests

        [Fact]
        public void Insert_BothNull_ReturnsInitialValue()
        {
            var result = LexoRank.Insert(null, null);
            Assert.Contains(LexoRank.SEPARATOR, result);
            Assert.StartsWith(LexoRank.DEFAULT_PREFIX + LexoRank.SEPARATOR, result);
            var lexoPart = result.Split(LexoRank.SEPARATOR)[1];
            Assert.Equal(2, lexoPart.Length); // bucketSize=100 needs 2 chars (62^2=3844)
        }

        [Fact]
        public void Insert_BothNull_LargeBucketSize_ReturnsLongerRank()
        {
            var result = LexoRank.Insert(null, null, 5000);
            var lexoPart = result.Split(LexoRank.SEPARATOR)[1];
            Assert.Equal(3, lexoPart.Length); // 62^2=3844 < 5000, needs 3 chars
        }

        [Fact]
        public void Insert_PrevNull_UsesGenPrev()
        {
            var result = LexoRank.Insert(null, "V~m");
            Assert.True(string.CompareOrdinal(result, "V~m") < 0);
        }

        [Fact]
        public void Insert_NextNull_UsesGenNext()
        {
            var result = LexoRank.Insert("V~m", null);
            Assert.True(string.CompareOrdinal(result, "V~m") > 0);
        }

        [Fact]
        public void Insert_BothProvided_UsesBetween()
        {
            var result = LexoRank.Insert("V~a", "V~z");
            Assert.True(string.CompareOrdinal(result, "V~a") > 0);
            Assert.True(string.CompareOrdinal(result, "V~z") < 0);
        }

        #endregion

        #region InitialValue Method Tests

        [Fact]
        public void InitialValue_ReturnsValidRank()
        {
            var result = LexoRank.InitialValue();
            Assert.Contains(LexoRank.SEPARATOR, result);
            Assert.StartsWith(LexoRank.DEFAULT_PREFIX + LexoRank.SEPARATOR, result);
        }

        [Fact]
        public void InitialValue_LargeBucketSize_ReturnsLongerRank()
        {
            var result = LexoRank.InitialValue(5000);
            var lexoPart = result.Split(LexoRank.SEPARATOR)[1];
            Assert.Equal(3, lexoPart.Length);
        }

        #endregion

        #region Between Method Tests (Strict Mode)

        [Fact]
        public void Between_PrevNull_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => LexoRank.Between(null, "V~m"));
            Assert.Equal("prev", ex.ParamName);
        }

        [Fact]
        public void Between_NextNull_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => LexoRank.Between("V~m", null));
            Assert.Equal("next", ex.ParamName);
        }

        [Fact]
        public void Between_BothNull_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => LexoRank.Between(null, null));
            Assert.Equal("prev", ex.ParamName);
        }

        [Fact]
        public void Between_SameBucket_ReturnsMidpoint()
        {
            var result = LexoRank.Between("V~a", "V~z");
            Assert.StartsWith("V~", result);
            Assert.True(string.CompareOrdinal(result, "V~a") > 0);
            Assert.True(string.CompareOrdinal(result, "V~z") < 0);
        }

        [Fact]
        public void Between_CrossBucket_ReturnsValueBetween()
        {
            var result = LexoRank.Between("V~z", "W~a");
            Assert.True(string.CompareOrdinal(result, "V~z") > 0);
            Assert.True(string.CompareOrdinal(result, "W~a") < 0);
            Assert.StartsWith("V~", result);
        }

        [Fact]
        public void Between_AdjacentChars_AppendsMiddleChar()
        {
            var result = LexoRank.Between("V~V", "V~W");
            Assert.True(string.CompareOrdinal(result, "V~V") > 0, $"Expected result > 'V~V', but got '{result}'");
            Assert.True(string.CompareOrdinal(result, "V~W") < 0, $"Expected result < 'V~W', but got '{result}'");
            // The result should be longer than both inputs (precision expansion)
            Assert.True(result.Length > 3, $"Expected longer result for precision expansion, but got '{result}'");
        }

        [Fact]
        public void Between_PrecisionExpansion_ReturnsValidRank()
        {
            var result = LexoRank.Between("A~a", "A~b");
            Assert.StartsWith("A~", result);
            Assert.True(result.Length > 3);
            Assert.True(string.CompareOrdinal(result, "A~a") > 0);
            Assert.True(string.CompareOrdinal(result, "A~b") < 0);
        }

        [Fact]
        public void Between_EqualNonNull_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => LexoRank.Between("V~abc", "V~abc"));
        }

        [Fact]
        public void Between_PrevIsPrefixOfNext_ReturnsValidRank()
        {
            var result = LexoRank.Between("V~a", "V~a1");
            Assert.True(string.CompareOrdinal(result, "V~a") > 0);
            Assert.True(string.CompareOrdinal(result, "V~a1") < 0);
        }

        [Fact]
        public void Between_AdjacentChars_Insert100Times_NoErrorAndMaintainsOrder()
        {
            var ranks = new List<string> { "V~VV", "V~VW" };

            for (int i = 0; i < 100; i++)
            {
                var newRank = LexoRank.Between(ranks[i], ranks[i + 1]);
                ranks.Insert(i + 1, newRank);
            }

            Assert.Equal(102, ranks.Count);

            string longestRank = ranks.OrderByDescending(r => r.Length).First();
            output.WriteLine($"Longest rank: {longestRank} (length: {longestRank.Length})");

            for (int i = 1; i < ranks.Count; i++)
            {
                Assert.True(string.CompareOrdinal(ranks[i], ranks[i - 1]) > 0);
            }
        }

        #endregion

        #region GenNext/GenPrev Tests

        [Fact]
        public void GenNext_IncrementsBy8()
        {
            var initial = LexoRank.Insert(null, null);
            var next = LexoRank.GenNext(initial);
            Assert.True(string.CompareOrdinal(next, initial) > 0);
        }

        [Fact]
        public void GenPrev_DecrementsBy8()
        {
            var initial = LexoRank.Insert(null, null);
            var prev = LexoRank.GenPrev(initial);
            Assert.True(string.CompareOrdinal(prev, initial) < 0);
        }

        [Fact]
        public void GenNext_GenPrev_RoundTrip()
        {
            var initial = LexoRank.Insert(null, null);
            var next = LexoRank.GenNext(initial);
            var back = LexoRank.GenPrev(next);
            Assert.Equal(initial, back);
        }

        #endregion

        #region Other Tests

        [Fact]
        public void Rebalance_ProducesSortedRanks()
        {
            var ranks = LexoRank.Rebalance(10);
            Assert.Equal(10, ranks.Length);
            for (int i = 1; i < ranks.Length; i++)
            {
                Assert.True(string.CompareOrdinal(ranks[i], ranks[i - 1]) > 0);
            }
        }

        [Fact]
        public void Rebalance_ZeroCount_ReturnsEmpty()
        {
            var ranks = LexoRank.Rebalance(0);
            Assert.Empty(ranks);
        }

        [Fact]
        public void Rebalance_SingleElement_ReturnsOneRank()
        {
            var ranks = LexoRank.Rebalance(1);
            Assert.Single(ranks);
        }

        [Fact]
        public void NeedsRebalance_SingleElement_ReturnsFalse()
        {
            Assert.False(LexoRank.NeedsRebalance(1, 10));
        }

        [Fact]
        public void NeedsRebalance_BelowThreshold_ReturnsFalse()
        {
            Assert.False(LexoRank.NeedsRebalance(100, 3));
        }

        [Fact]
        public void NeedsRebalance_AboveThreshold_ReturnsTrue()
        {
            Assert.True(LexoRank.NeedsRebalance(100, 6));
        }

        [Fact]
        public void Compare_MatchesStringCompareOrdinal()
        {
            Assert.Equal(string.CompareOrdinal("V~a", "V~b"), LexoRank.Compare("V~a", "V~b"));
            Assert.Equal(string.CompareOrdinal("V~z", "B~a"), LexoRank.Compare("V~z", "B~a"));
            Assert.Equal(0, LexoRank.Compare("V~V", "V~V"));
        }

        [Fact]
        public void Constants_Separator_IsTilde()
        {
            Assert.Equal("~", LexoRank.SEPARATOR);
        }

        [Fact]
        public void Constants_DefaultBucketSize_Is100()
        {
            Assert.Equal(100, LexoRank.DEFAULT_BUCKET_SIZE);
        }

        #endregion
    }
}
