using System;
using Xunit;

namespace Salvavida.Tests
{
    public class LexoRankTests
    {
        private const int LESSER_PRECISION_DIGITS = 1;
        private const int DEFAULT_PRECISION_DIGITS = 2;

        #region Generate Tests

        [Fact]
        public void Generate_BothNull_ReturnsInitialValue()
        {
            var result = LexoRank.Generate(null, null, DEFAULT_PRECISION_DIGITS);
            Assert.NotNull(result);
            // Should return complete format: {BucketId}~{ChunkId}~{Rank}
            Assert.Equal("0~VV~VV", result);
        }

        [Fact]
        public void Generate_ValidFormat()
        {
            var result = LexoRank.Generate(null, null, DEFAULT_PRECISION_DIGITS);

            // Parse should work
            LexoRank.Parse(result.AsSpan(), out var bucketId, out var chunkId, out var rank);
            Assert.Equal("0", bucketId.ToString());
            Assert.Equal("VV", chunkId.ToString());
            Assert.True(rank.Length >= DEFAULT_PRECISION_DIGITS);
        }

        [Fact]
        public void Generate_BothNull_LargeBucketSize_ReturnsLongerRank()
        {
            var precisionDigits = 4;
            var result = LexoRank.Generate(null, null, precisionDigits);

            Assert.NotNull(result);
            Assert.True(result.Length >= precisionDigits);
        }

        [Fact]
        public void Generate_PrevNull_UsesGenPrev()
        {
            var initValue = LexoRank.GetInitValue(DEFAULT_PRECISION_DIGITS);
            var next = initValue;
            var result = LexoRank.Generate(null, next, DEFAULT_PRECISION_DIGITS);

            Assert.NotNull(result);
            LexoRank.Parse(next.AsSpan(), out var nextBucketId, out var nextChunkId, out var nextRank);
            LexoRank.Parse(result.AsSpan(), out var resultBucketId, out var resultChunkId, out var resultRank);

            Assert.True(LexoRank.Compare(resultRank, nextRank) < 0);
        }

        [Fact]
        public void Generate_NextNull_UsesGenNext()
        {
            var initValue = LexoRank.GetInitValue(DEFAULT_PRECISION_DIGITS);
            var prev = initValue;
            var result = LexoRank.Generate(prev, null, DEFAULT_PRECISION_DIGITS);

            Assert.NotNull(result);
            LexoRank.Parse(prev.AsSpan(), out var prevBucketId, out var prevChunkId, out var prevRank);
            LexoRank.Parse(result.AsSpan(), out var resultBucketId, out var resultChunkId, out var resultRank);

            Assert.True(LexoRank.Compare(resultRank, prevRank) > 0);
        }

        [Fact]
        public void Generate_BothProvided_UsesMiddle()
        {
            var prev = "0~VV~VV";
            var next = "0~VV~VW"; // Both hardcoded for consistency
            var result = LexoRank.Generate(prev, next, DEFAULT_PRECISION_DIGITS);

            Assert.NotNull(result);
            LexoRank.Parse(prev.AsSpan(), out var prevBucketId, out var prevChunkId, out var prevRank);
            LexoRank.Parse(next.AsSpan(), out var nextBucketId, out var nextChunkId, out var nextRank);
            LexoRank.Parse(result.AsSpan(), out var resultBucketId, out var resultChunkId, out var resultRank);

            Assert.True(LexoRank.Compare(resultRank, prevRank) > 0);
            Assert.True(LexoRank.Compare(resultRank, nextRank) < 0);
        }

        #endregion

        #region Middle Tests

        [Fact]
        public void Middle_PrevNull_ThrowsArgumentNullException()
        {
            var initValue = LexoRank.GetInitValue(DEFAULT_PRECISION_DIGITS);
            Assert.Throws<ArgumentNullException>(() => LexoRank.Middle(null!, initValue, DEFAULT_PRECISION_DIGITS));
        }

        [Fact]
        public void Middle_NextNull_ThrowsArgumentNullException()
        {
            var initValue = LexoRank.GetInitValue(DEFAULT_PRECISION_DIGITS);
            Assert.Throws<ArgumentNullException>(() => LexoRank.Middle(initValue, null!, DEFAULT_PRECISION_DIGITS));
        }

        [Fact]
        public void Middle_BothNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => LexoRank.Middle(null!, null!, 2));
        }

        [Fact]
        public void Middle_SameBucket_ReturnsMidpoint()
        {
            var prev = "0~VV~VV";
            var next = "0~VV~VW";
            var result = LexoRank.Middle(prev, next, DEFAULT_PRECISION_DIGITS);

            Assert.NotNull(result);

            LexoRank.Parse(prev.AsSpan(), out var prevBucketId, out var prevChunkId, out var prevRank);
            LexoRank.Parse(next.AsSpan(), out var nextBucketId, out var nextChunkId, out var nextRank);
            LexoRank.Parse(result.AsSpan(), out var resultBucketId, out var resultChunkId, out var resultRank);

            Assert.Equal(prevBucketId.ToString(), resultBucketId.ToString());
            Assert.Equal(prevChunkId.ToString(), resultChunkId.ToString());
            Assert.True(LexoRank.Compare(resultRank, prevRank) > 0);
            Assert.True(LexoRank.Compare(resultRank, nextRank) < 0);
        }

        [Fact]
        public void Middle_CrossBucket_ThrowsArgumentException()
        {
            var prev = "0~VV~VV";
            var next = "1~VV~VW";

            Assert.Throws<ArgumentException>(() => LexoRank.Middle(prev, next, DEFAULT_PRECISION_DIGITS));
        }

        [Fact]
        public void Middle_AdjacentChars_AppendsMiddleChar()
        {
            var prev = "0~VV~AA";
            var next = "0~VV~AB";
            var result = LexoRank.Middle(prev, next, 2);

            Assert.NotNull(result);
            LexoRank.Parse(result.AsSpan(), out var resultBucketId, out var resultChunkId, out var resultRank);

            // Should be "AAV" or similar (expanded precision)
            Assert.StartsWith("AA", resultRank.ToString());
            Assert.True(resultRank.Length > 2);
        }

        [Fact]
        public void Middle_PrecisionExpansion_ReturnsValidRank()
        {
            var prev = "0~VV~A";
            var next = "0~VV~B";
            var result = LexoRank.Middle(prev, next, 1);

            Assert.NotNull(result);
            LexoRank.Parse(prev.AsSpan(), out var prevBucketId, out var prevChunkId, out var prevRank);
            LexoRank.Parse(next.AsSpan(), out var nextBucketId, out var nextChunkId, out var nextRank);
            LexoRank.Parse(result.AsSpan(), out var resultBucketId, out var resultChunkId, out var resultRank);

            Assert.True(LexoRank.Compare(resultRank, prevRank) > 0);
            Assert.True(LexoRank.Compare(resultRank, nextRank) < 0);
        }

        [Fact]
        public void Middle_EqualNonNull_ThrowsArgumentException()
        {
            var prev = "0~VV~VV";
            var next = "0~VV~VV";

            Assert.Throws<ArgumentException>(() => LexoRank.Middle(prev, next, DEFAULT_PRECISION_DIGITS));
        }

        [Fact]
        public void Middle_PrevIsPrefixOfNext_ReturnsValidRank()
        {
            var prev = "0~VV~A";
            var next = "0~VV~AB";
            var result = LexoRank.Middle(prev, next, 1);

            Assert.NotNull(result);
            LexoRank.Parse(prev.AsSpan(), out var prevBucketId, out var prevChunkId, out var prevRank);
            LexoRank.Parse(next.AsSpan(), out var nextBucketId, out var nextChunkId, out var nextRank);
            LexoRank.Parse(result.AsSpan(), out var resultBucketId, out var resultChunkId, out var resultRank);

            Assert.True(LexoRank.Compare(resultRank, prevRank) > 0);
            Assert.True(LexoRank.Compare(resultRank, nextRank) < 0);
        }

        [Fact]
        public void Middle_AdjacentChars_Insert100Times_NoErrorAndMaintainsOrder()
        {
            // Test inserting 100 values between two adjacent ranks
            var prev = "0~VV~A";
            var next = "0~VV~B";
            var ranks = new System.Collections.Generic.List<string> { prev, next };

            for (int i = 0; i < 100; i++)
            {
                // Find a gap and insert in the middle
                var insertIndex = 1; // Always insert between first two elements for simplicity
                var newRank = LexoRank.Middle(ranks[insertIndex - 1], ranks[insertIndex], DEFAULT_PRECISION_DIGITS);
                ranks.Insert(insertIndex, newRank);
            }

            // Verify order is maintained
            for (int i = 1; i < ranks.Count; i++)
            {
                LexoRank.Parse(ranks[i - 1].AsSpan(), out _, out _, out var prevRank);
                LexoRank.Parse(ranks[i].AsSpan(), out _, out _, out var currRank);
                Assert.True(LexoRank.Compare(prevRank, currRank) < 0, $"Order violated at index {i}");
            }
        }

        [Fact]
        public void Middle_AlwaysProducesValidRank()
        {
            var testCases = new[]
            {
                ("0~VV~VV", "0~VV~VW"),
                ("0~VV~AA", "0~VV~AB"),
                ("0~VV~A0", "0~VV~A1"),
                ("0~VV~A", "0~VV~B"),  // Adjacent chars
            };

            foreach (var (prev, next) in testCases)
            {
                var result = LexoRank.Middle(prev, next, DEFAULT_PRECISION_DIGITS);
                Assert.NotNull(result);

                LexoRank.Parse(result.AsSpan(), out var resultBucketId, out var resultChunkId, out var resultRank);
                Assert.False(resultRank.ToString().EndsWith('0'), $"Rank should not end with '0': {result}");
            }
        }

        #endregion

        #region GenNext Tests

        [Fact]
        public void GenNext_NeverRunOutOfPrecision()
        {
            var initValue = LexoRank.GetInitValue(LESSER_PRECISION_DIGITS);
            var rank = initValue;

            // Generate 100 consecutive next values starting from middle
            for (int i = 0; i < 100; i++)
            {
                var next = LexoRank.GenNext(rank, LESSER_PRECISION_DIGITS);
                //Console.WriteLine($"rank: {rank}, next: {next}");
                Assert.NotNull(next);

                LexoRank.Parse(next.AsSpan(), out var nextBucketId, out var nextChunkId, out var nextRank);
                LexoRank.Parse(rank.AsSpan(), out var rankBucketId, out var rankChunkId, out var rankValue);

                Assert.True(LexoRank.Compare(nextRank, rankValue) > 0);
                rank = next;
            }
        }

        #endregion

        #region GenPrev Tests

        [Fact]
        public void GenPrev_NeverRunOutOfPrecision()
        {
            var rank = LexoRank.GetInitValue(LESSER_PRECISION_DIGITS); // Already complete format

            // Generate 100 consecutive prev values starting from middle
            for (int i = 0; i < 100; i++)
            {
                var prev = LexoRank.GenPrev(rank, LESSER_PRECISION_DIGITS);
                //Console.WriteLine($"prev: {prev}, rank: {rank}");
                Assert.NotNull(prev);

                LexoRank.Parse(prev.AsSpan(), out var prevBucketId, out var prevChunkId, out var prevRank);
                LexoRank.Parse(rank.AsSpan(), out var rankBucketId, out var rankChunkId, out var rankValue);

                Assert.True(LexoRank.Compare(prevRank, rankValue) < 0);
                rank = prev;
            }
        }

        #endregion

        #region Rapid Insert Tests

        [Fact]
        public void RapidInsertsAtBeginning_ProducesOrderedRanks()
        {
            var initValue = LexoRank.GetInitValue(LESSER_PRECISION_DIGITS);
            var ranks = new System.Collections.Generic.List<string> { initValue };

            // Insert 100 items at the beginning
            for (int i = 0; i < 100; i++)
            {
                var newRank = LexoRank.GenPrev(ranks[0], LESSER_PRECISION_DIGITS);
                //Console.WriteLine($"first: {ranks[0]}, newRank: {newRank}");
                ranks.Insert(0, newRank);
            }

            // Verify order
            for (int i = 1; i < ranks.Count; i++)
            {
                LexoRank.Parse(ranks[i - 1].AsSpan(), out _, out _, out var prevRank);
                LexoRank.Parse(ranks[i].AsSpan(), out _, out _, out var currRank);
                var cmp = LexoRank.Compare(prevRank, currRank);
                Assert.True(cmp < 0, $"Order violated at index {i}: prev={ranks[i-1]}, curr={ranks[i]}, cmp={cmp}");
            }
        }

        [Fact]
        public void RapidInsertsAtEnd_ProducesOrderedRanks()
        {
            var initValue = LexoRank.GetInitValue(LESSER_PRECISION_DIGITS);
            var ranks = new System.Collections.Generic.List<string> { initValue };

            // Insert 100 items at the end
            for (int i = 0; i < 100; i++)
            {
                var last = ranks[ranks.Count - 1];
                var newRank = LexoRank.GenNext(last, LESSER_PRECISION_DIGITS);
                ranks.Add(newRank);
                //Console.WriteLine($"last: {last}, newRank: {newRank}");
            }

            // Verify order
            for (int i = 1; i < ranks.Count; i++)
            {
                LexoRank.Parse(ranks[i - 1].AsSpan(), out _, out _, out var prevRank);
                LexoRank.Parse(ranks[i].AsSpan(), out _, out _, out var currRank);
                var cmp = LexoRank.Compare(prevRank, currRank);
                Assert.True(cmp < 0, $"Order violated at index {i}: prev={ranks[i-1]}, curr={ranks[i]}, cmp={cmp}");
            }
        }

        [Fact]
        public void RandomInsert_UsingInsertMethod_ProducesOrderedRanks()
        {
            var ranks = new System.Collections.Generic.List<string>
            {
                LexoRank.GetInitValue(DEFAULT_PRECISION_DIGITS), // Start with one rank
            };

            var random = new Random(42);
            for (int i = 0; i < 100; i++)
            {
                var insertIndex = random.Next(1, ranks.Count+1);
                var prev = insertIndex == 0 ? null : ranks[insertIndex - 1];
                var next = insertIndex == ranks.Count ? null : ranks[insertIndex];
                try
                {
                    var newRank = LexoRank.Generate(prev, next, DEFAULT_PRECISION_DIGITS);
                    //Console.WriteLine($"Insert No.{i}, Inserting at index {insertIndex}: prev={prev}, next={next}, newRank={newRank}");
                    ranks.Insert(insertIndex, newRank);
                }
                catch
                {
                    //Console.WriteLine($"Insert No.{i}, Inserting at index {insertIndex}: prev={prev}, next={next}, newRank=ERROR");
                    throw;
                }
            }

            // Verify we have at least some inserts
            Assert.True(ranks.Count > 10, $"Expected at least 10 ranks, got {ranks.Count}");

            // Verify order
            for (int i = 1; i < ranks.Count; i++)
            {
                LexoRank.Parse(ranks[i - 1].AsSpan(), out _, out _, out var prevRank);
                LexoRank.Parse(ranks[i].AsSpan(), out _, out _, out var currRank);
                var cmp = LexoRank.Compare(prevRank, currRank);
                Assert.True(cmp < 0, $"Order violated at index {i}: prev={ranks[i-1]}, curr={ranks[i]}, cmp={cmp}");
            }
        }

        #endregion

        #region Parse and Build Tests

        [Fact]
        public void Parse_ValidFormat_ReturnsComponents()
        {
            var lexoRank = "123~45~z0";
            LexoRank.Parse(lexoRank.AsSpan(), out var bucketId, out var chunkId, out var rank);

            Assert.Equal("123", bucketId.ToString());
            Assert.Equal("45", chunkId.ToString());
            Assert.Equal("z0", rank.ToString());
        }

        [Fact]
        public void Parse_InvalidFormat_ThrowsException()
        {
            var invalidRanks = new[] { "no-separator", "only-one~separator", "" };

            foreach (var invalid in invalidRanks)
            {
                Assert.Throws<FormatException>(() => LexoRank.Parse(invalid.AsSpan(), out _, out _, out _));
            }
        }

        [Fact]
        public void Build_ValidComponents_ReturnsFormattedString()
        {
            var result = LexoRank.Build("123".AsSpan(), "45".AsSpan(), "z0".AsSpan());
            Assert.Equal("123~45~z0", result.ToString());
        }

        [Fact]
        public void Parse_Build_RoundTrip()
        {
            var original = "abc~xyz~test";
            LexoRank.Parse(original.AsSpan(), out var bucketId, out var chunkId, out var rank);
            var rebuilt = LexoRank.Build(bucketId, chunkId, rank);

            Assert.Equal(original, rebuilt.ToString());
        }

        #endregion

        #region CalculatePrecisionDigits Tests

        [Fact]
        public void CalculatePrecisionDigits_SmallSize_Returns1()
        {
            Assert.Equal(1, LexoRank.CalculatePrecisionDigits(1));
            Assert.Equal(1, LexoRank.CalculatePrecisionDigits(10));
            Assert.Equal(1, LexoRank.CalculatePrecisionDigits(61));
        }

        [Fact]
        public void CalculatePrecisionDigits_MediumSize_Returns2()
        {
            Assert.Equal(2, LexoRank.CalculatePrecisionDigits(63));  // 62^1 + 1
            Assert.Equal(2, LexoRank.CalculatePrecisionDigits(100));
            Assert.Equal(2, LexoRank.CalculatePrecisionDigits(1000));
            Assert.Equal(2, LexoRank.CalculatePrecisionDigits(3843)); // 62^2 - 1
        }

        [Fact]
        public void CalculatePrecisionDigits_LargeSize_Returns3()
        {
            Assert.Equal(3, LexoRank.CalculatePrecisionDigits(3845)); // 62^2 + 1
            Assert.Equal(3, LexoRank.CalculatePrecisionDigits(10000));
            Assert.Equal(3, LexoRank.CalculatePrecisionDigits(100000));
        }

        [Fact]
        public void CalculatePrecisionDigits_InvalidSize_ThrowsException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => LexoRank.CalculatePrecisionDigits(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => LexoRank.CalculatePrecisionDigits(-1));
        }

        #endregion

        #region GetInitValue Tests

        [Fact]
        public void GetInitValue_ReturnsMiddleValue()
        {
            var result1 = LexoRank.GetInitValue(1);
            var result2 = LexoRank.GetInitValue(2);
            var result3 = LexoRank.GetInitValue(3);

            // Should return complete format with defaults: BucketId="0", ChunkId="VV"
            Assert.Equal("0~VV~V", result1);
            Assert.Equal("0~VV~VV", result2);
            Assert.Equal("0~VV~VVV", result3);
        }

        [Fact]
        public void GetInitValue_InvalidPrecision_ThrowsException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => LexoRank.GetInitValue(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => LexoRank.GetInitValue(-1));
        }

        #endregion

        #region Span API Tests

        [Fact]
        public void GenPrev_SpanVersion_WorksCorrectly()
        {
            var initValue = LexoRank.GetInitValue(DEFAULT_PRECISION_DIGITS);
            var rank = initValue.AsSpan();
            Span<char> buffer = stackalloc char[100];
            var length = LexoRank.GenPrev(rank, DEFAULT_PRECISION_DIGITS, buffer);

            var result = buffer[..length];
            Assert.True(length > 0);
            
            // Parse and compare ranks only
            LexoRank.Parse(result, out _, out _, out var resultRank);
            LexoRank.Parse(initValue.AsSpan(), out _, out _, out var initRank);
            Assert.True(LexoRank.Compare(resultRank, initRank) < 0);
        }

        [Fact]
        public void GenNext_SpanVersion_WorksCorrectly()
        {
            var initValue = LexoRank.GetInitValue(DEFAULT_PRECISION_DIGITS);
            var rank = initValue.AsSpan();
            Span<char> buffer = stackalloc char[100];
            var length = LexoRank.GenNext(rank, DEFAULT_PRECISION_DIGITS, buffer);

            var result = buffer[..length];
            Assert.True(length > 0);
            
            // Parse and compare ranks only
            LexoRank.Parse(result, out _, out _, out var resultRank);
            LexoRank.Parse(initValue.AsSpan(), out _, out _, out var initRank);
            Assert.True(LexoRank.Compare(resultRank, initRank) > 0);
        }

        [Fact]
        public void Middle_SpanVersion_WorksCorrectly()
        {
            var prev = "0~VV~VV".AsSpan();
            var next = "0~VV~VW".AsSpan();
            Span<char> buffer = stackalloc char[100];
            var length = LexoRank.Middle(prev, next, DEFAULT_PRECISION_DIGITS, buffer);

            var result = buffer[..length].ToString();
            Assert.NotNull(result);

            LexoRank.Parse(result.AsSpan(), out var resultBucketId, out var resultChunkId, out var resultRank);
            LexoRank.Parse(prev, out var prevBucketId, out var prevChunkId, out var prevRank);
            LexoRank.Parse(next, out var nextBucketId, out var nextChunkId, out var nextRank);

            Assert.True(LexoRank.Compare(resultRank, prevRank) > 0);
            Assert.True(LexoRank.Compare(resultRank, nextRank) < 0);
        }

        [Fact]
        public void Generate_SpanVersion_WorksCorrectly()
        {
            var expectedInitValue = LexoRank.GetInitValue(DEFAULT_PRECISION_DIGITS);
            Span<char> buffer = stackalloc char[100];

            // Both empty (equivalent to null)
            var length = LexoRank.Generate(ReadOnlySpan<char>.Empty, ReadOnlySpan<char>.Empty, DEFAULT_PRECISION_DIGITS, buffer);
            var initValue = buffer[..length];
            Assert.Equal(expectedInitValue, initValue.ToString());

            // Prev empty
            length = LexoRank.Generate(ReadOnlySpan<char>.Empty, expectedInitValue.AsSpan(), DEFAULT_PRECISION_DIGITS, buffer);
            var prevValue = buffer[..length];
            LexoRank.Parse(prevValue, out _, out _, out var prevRank);
            LexoRank.Parse(expectedInitValue.AsSpan(), out _, out _, out var initRank);
            Assert.True(LexoRank.Compare(prevRank, initRank) < 0);

            // Next empty
            length = LexoRank.Generate(expectedInitValue.AsSpan(), ReadOnlySpan<char>.Empty, DEFAULT_PRECISION_DIGITS, buffer);
            var nextValue = buffer[..length];
            LexoRank.Parse(nextValue, out _, out _, out var nextRank);
            Assert.True(LexoRank.Compare(nextRank, initRank) > 0);
        }

        #endregion

        #region Rebalance Tests

        [Fact]
        public void Rebalance_ReturnsValidStartRank()
        {
            var currentRank = "0~VV~VV";
            var result = LexoRank.Rebalance(currentRank, 10, false, out int step, out bool reverseOrder);

            Assert.NotNull(result);
            Assert.True(step > 0);
            Assert.False(reverseOrder);
            LexoRank.Parse(result.AsSpan(), out var bucketId, out var chunkId, out var rank);
            Assert.Equal("0", bucketId.ToString());
            Assert.Equal("VV", chunkId.ToString());
        }

        [Fact]
        public void Rebalance_RebalanceChunk_ReturnsNewChunkId()
        {
            var currentRank = "A~VV~someRank";
            var result = LexoRank.Rebalance(currentRank, 10, true, out int step, out bool reverseOrder);

            Assert.NotNull(result);
            Assert.True(step > 0);
            Assert.True(reverseOrder);
            LexoRank.Parse(result.AsSpan(), out var bucketId, out var chunkId, out var rank);
            Assert.Equal("A", bucketId.ToString());
            Assert.Equal("someRank", rank.ToString());
            Assert.NotEqual("VV", chunkId.ToString());
        }

        [Fact]
        public void Rebalance_RebalanceChunkFalse_ReverseOrderIsFalse()
        {
            var currentRank = "0~VV~VV";
            var result = LexoRank.Rebalance(currentRank, 10, false, out int step, out bool reverseOrder);

            Assert.NotNull(result);
            Assert.True(step > 0);
            Assert.False(reverseOrder);
        }

        [Fact]
        public void Rebalance_RebalanceChunkTrue_ReverseOrderIsTrue()
        {
            var currentRank = "0~VV~VV";
            var result = LexoRank.Rebalance(currentRank, 10, true, out int step, out bool reverseOrder);

            Assert.NotNull(result);
            Assert.True(step > 0);
            Assert.True(reverseOrder);
        }

        [Fact]
        public void Rebalance_BucketIdIncreasing_ReverseOrderIsFalse()
        {
            var currentRank = "0~VV~VV";
            var result = LexoRank.Rebalance(currentRank, 10, false, out int step, out bool reverseOrder);

            if (reverseOrder)
            {
                Assert.Fail("reverseOrder should be false when bucketId is increasing (0 → 1 → 2)");
            }
            Assert.False(reverseOrder);
        }

        [Fact]
        public void Rebalance_BucketIdDecreasingToZero_ReverseOrderIsTrue()
        {
            var currentRank = "2~VV~VV";
            var result = LexoRank.Rebalance(currentRank, 10, false, out int step, out bool reverseOrder);

            LexoRank.Parse(result.AsSpan(), out var resultBucketId, out _, out _);
            if (resultBucketId.SequenceEqual("0".AsSpan()))
            {
                Assert.True(reverseOrder, "reverseOrder should be true when bucketId wraps from 2 back to 0");
            }
        }

        [Fact]
        public void Rebalance_StepIsConsistent()
        {
            var currentRank = "0~VV~VV";
            var result = LexoRank.Rebalance(currentRank, 5, false, out int step, out _);

            // step = 62^1 / (5+1) = 62/6 = 10
            Assert.Equal(10, step);
        }

        [Fact]
        public void Rebalance_GeneratedRanksAreOrdered()
        {
            var currentRank = "0~VV~VV";
            var startRank = LexoRank.Rebalance(currentRank, 5, false, out int step, out bool reverseOrder);

            Assert.False(reverseOrder);

            Span<char> buffer = stackalloc char[128];
            string prevRank = startRank;
            int precisionDigits = LexoRank.CalculatePrecisionDigits(5);

            for (int i = 1; i <= 5; i++)
            {
                var length = LexoRank.GenNext(prevRank, precisionDigits, buffer, step);
                var nextRank = new string(buffer[..length]);

                LexoRank.Parse(prevRank.AsSpan(), out _, out _, out var prevRankValue);
                LexoRank.Parse(nextRank.AsSpan(), out _, out _, out var nextRankValue);

                Assert.True(LexoRank.Compare(prevRankValue, nextRankValue) < 0);
                prevRank = nextRank;
            }
        }

        [Fact]
        public void Rebalance_InvalidCount_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                LexoRank.Rebalance("0~VV~VV", 0, false, out _, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                LexoRank.Rebalance("0~VV~VV", -1, false, out _, out _));
        }

        [Fact]
        public void Rebalance_NullRank_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                LexoRank.Rebalance(null, 10, false, out _, out _));
            Assert.Throws<ArgumentNullException>(() =>
                LexoRank.Rebalance("", 10, false, out _, out _));
        }

        #endregion
    }
}
