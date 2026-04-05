using Xunit;

namespace Salvavida.Tests
{
    public class LexoRankCollectionIntegrationTests
    {
        [Fact]
        public void Between_AlwaysProducesValidRank()
        {
            var ranks = new List<string> { "V~a", "V~m", "V~z", "B~a", "B~z" };
            foreach (var prev in ranks)
            {
                foreach (var next in ranks)
                {
                    if (prev == next) continue;
                    if (string.CompareOrdinal(prev, next) >= 0) continue;

                    var result = LexoRank.Between(prev, next);
                    Assert.True(string.CompareOrdinal(result, prev) > 0);
                    Assert.True(string.CompareOrdinal(result, next) < 0);
                    Assert.Contains(LexoRank.SEPARATOR, result);
                }
            }
        }

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
        public void Rebalance_LargeCount_ProducesSortedRanks()
        {
            var ranks = LexoRank.Rebalance(1000);
            Assert.Equal(1000, ranks.Length);
            for (int i = 1; i < ranks.Length; i++)
            {
                Assert.True(string.CompareOrdinal(ranks[i], ranks[i - 1]) > 0);
            }
        }

        [Fact]
        public void RapidInserts_ProducesOrderedRanks()
        {
            var ranks = new List<string>();
            for (int i = 0; i < 100; i++)
            {
                string? prev = ranks.Count > 0 ? ranks[ranks.Count - 1] : null;
                ranks.Add(LexoRank.Between(prev, null));
            }
            for (int i = 1; i < ranks.Count; i++)
            {
                Assert.True(string.CompareOrdinal(ranks[i], ranks[i - 1]) > 0);
            }
        }

        [Fact]
        public void RapidInsertsAtBeginning_ProducesOrderedRanks()
        {
            var ranks = new List<string>();
            for (int i = 0; i < 100; i++)
            {
                string? next = ranks.Count > 0 ? ranks[0] : null;
                ranks.Insert(0, LexoRank.Between(null, next));
            }
            for (int i = 1; i < ranks.Count; i++)
            {
                Assert.True(string.CompareOrdinal(ranks[i], ranks[i - 1]) > 0);
            }
        }

        [Fact]
        public void RapidInsertsAtEnd_ProducesOrderedRanks()
        {
            var ranks = new List<string>();
            for (int i = 0; i < 100; i++)
            {
                string? prev = ranks.Count > 0 ? ranks[ranks.Count - 1] : null;
                ranks.Add(LexoRank.Between(prev, null));
            }
            for (int i = 1; i < ranks.Count; i++)
            {
                Assert.True(string.CompareOrdinal(ranks[i], ranks[i - 1]) > 0);
            }
        }

        [Fact]
        public void RapidInsertsInMiddle_ProducesOrderedRanks()
        {
            var ranks = new List<string>();
            ranks.Add(LexoRank.Between(null, null));
            ranks.Add(LexoRank.Between(ranks[0], null));

            for (int i = 0; i < 100; i++)
            {
                int midIdx = ranks.Count / 2;
                string prev = ranks[midIdx - 1];
                string next = ranks[midIdx];
                ranks.Insert(midIdx, LexoRank.Between(prev, next));
            }

            for (int i = 1; i < ranks.Count; i++)
            {
                Assert.True(string.CompareOrdinal(ranks[i], ranks[i - 1]) > 0);
            }
        }

        [Fact]
        public void RandomInsert_ProducesOrderedRanks()
        {
            var r1 = LexoRank.Between(null, null);
            var r0 = LexoRank.Between(null, r1);
            var r2 = LexoRank.Between(r1, null);
            var ranks = new List<string> { r0, r1, r2 };
            var rng = new Random(42);

            for (int i = 0; i < 100; i++)
            {
                int idx = rng.Next(0, ranks.Count + 1);
                string? prev = idx > 0 ? ranks[idx - 1] : null;
                string? next = idx < ranks.Count ? ranks[idx] : null;
                ranks.Insert(idx, LexoRank.Between(prev, next));
            }

            for (int i = 1; i < ranks.Count; i++)
            {
                Assert.True(string.CompareOrdinal(ranks[i], ranks[i - 1]) > 0);
            }
        }

        [Fact]
        public void CrossBucketInsert_ProducesCorrectRank()
        {
            var result = LexoRank.Between("A~z", "B~a");
            Assert.True(string.CompareOrdinal(result, "A~z") > 0);
            Assert.True(string.CompareOrdinal(result, "B~a") < 0);
            Assert.StartsWith("A~", result);
        }

        [Fact]
        public void Compare_MatchesStringCompareOrdinal()
        {
            Assert.Equal(string.CompareOrdinal("A~a", "A~b"), LexoRank.Compare("A~a", "A~b"));
            Assert.Equal(string.CompareOrdinal("A~z", "B~a"), LexoRank.Compare("A~z", "B~a"));
            Assert.Equal(0, LexoRank.Compare("A~m", "A~m"));
            Assert.True(LexoRank.Compare("B~a", "A~z") > 0);
        }

        [Fact]
        public void BucketMeta_DefaultValues_AreEmpty()
        {
            var meta = new BucketMeta();
            Assert.Null(meta.BucketId);
            Assert.Equal(0, meta.Count);
        }

        [Fact]
        public void BucketMeta_CanSetValues()
        {
            var meta = new BucketMeta { BucketId = "A", Count = 100 };
            Assert.Equal("A", meta.BucketId);
            Assert.Equal(100, meta.Count);
        }

        [Fact]
        public void CollectionMetadata_BucketMetas_DefaultNull()
        {
            var metadata = new CollectionMetadata();
            Assert.Null(metadata.BucketMetas);
            Assert.Equal(3, metadata.BucketMultiplier);
        }

        [Fact]
        public void CollectionMetadata_CanSetBucketMetas()
        {
            var metadata = new CollectionMetadata
            {
                BucketMetas = new[]
                {
                    new BucketMeta { BucketId = "A", Count = 50 },
                    new BucketMeta { BucketId = "B", Count = 30 }
                },
                BucketMultiplier = 5
            };
            Assert.Equal(2, metadata.BucketMetas.Length);
            Assert.Equal("A", metadata.BucketMetas[0].BucketId);
            Assert.Equal(30, metadata.BucketMetas[1].Count);
            Assert.Equal(5, metadata.BucketMultiplier);
        }

        [Fact]
        public void CollectionMetadata_HasSerializableAttribute()
        {
            var type = typeof(CollectionMetadata);
            var attr = Attribute.GetCustomAttribute(type, typeof(SerializableAttribute));
            Assert.NotNull(attr);
        }

        [Fact]
        public void BucketMeta_HasSerializableAttribute()
        {
            var type = typeof(BucketMeta);
            var attr = Attribute.GetCustomAttribute(type, typeof(SerializableAttribute));
            Assert.NotNull(attr);
        }

        [Fact]
        public void NeedsRebalance_ZeroElements_ReturnsFalse()
        {
            Assert.False(LexoRank.NeedsRebalance(0, 10));
        }

        [Fact]
        public void NeedsRebalance_AtThreshold_ReturnsFalse()
        {
            Assert.False(LexoRank.NeedsRebalance(100, LexoRank.REBALANCE_LENGTH_THRESHOLD));
        }

        [Fact]
        public void NeedsRebalance_AboveThreshold_WithOptimalLength_ReturnsFalse()
        {
            Assert.False(LexoRank.NeedsRebalance(100, 3));
        }

        [Fact]
        public void NeedsRebalance_AboveThreshold_WithExcessiveLength_ReturnsTrue()
        {
            Assert.True(LexoRank.NeedsRebalance(100, 6));
        }
    }
}
