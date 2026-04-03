using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace Salvavida.Tests
{
    public class LexoRankCollectionIntegrationTests
    {
        // === LexoRank Algorithm Extended Tests ===

        [Test]
        public void Between_AlwaysProducesValidRank()
        {
            var ranks = new List<string> { "A~a", "A~m", "A~z", "B~a", "B~z" };
            foreach (var prev in ranks)
            {
                foreach (var next in ranks)
                {
                    if (prev == next) continue;
                    if (string.CompareOrdinal(prev, next) >= 0) continue;

                    var result = LexoRank.Between(prev, next);
                    Assert.That(result, Is.GreaterThan(prev).Using<string>(StringComparer.Ordinal));
                    Assert.That(result, Is.LessThan(next).Using<string>(StringComparer.Ordinal));
                    Assert.That(result, Does.Contain("~"));
                }
            }
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
        public void Rebalance_LargeCount_ProducesSortedRanks()
        {
            var ranks = LexoRank.Rebalance(1000);
            Assert.That(ranks.Length, Is.EqualTo(1000));
            for (int i = 1; i < ranks.Length; i++)
            {
                Assert.That(ranks[i], Is.GreaterThan(ranks[i - 1]).Using<string>(StringComparer.Ordinal));
            }
        }

        [Test]
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
                Assert.That(ranks[i], Is.GreaterThan(ranks[i - 1]).Using<string>(StringComparer.Ordinal));
            }
        }

        [Test]
        public void RapidInsertsAtBeginning_ProducesOrderedRanks()
        {
            var ranks = new List<string>();
            for (int i = 0; i < 50; i++)
            {
                string? next = ranks.Count > 0 ? ranks[0] : null;
                ranks.Insert(0, LexoRank.Between(null, next));
            }
            for (int i = 1; i < ranks.Count; i++)
            {
                Assert.That(ranks[i], Is.GreaterThan(ranks[i - 1]).Using<string>(StringComparer.Ordinal));
            }
        }

        [Test]
        public void RapidInsertsInMiddle_ProducesOrderedRanks()
        {
            var ranks = new List<string>
            {
                "A~a",
                "A~z"
            };

            var rng = new System.Random(42);
            for (int i = 0; i < 20; i++)
            {
                int idx = rng.Next(0, ranks.Count);
                string? prev = idx > 0 ? ranks[idx - 1] : null;
                string? next = idx < ranks.Count ? ranks[idx] : null;
                ranks.Insert(idx, LexoRank.Between(prev, next));
            }

            for (int i = 1; i < ranks.Count; i++)
            {
                Assert.That(ranks[i], Is.GreaterThan(ranks[i - 1]).Using<string>(StringComparer.Ordinal));
            }
        }

            for (int i = 1; i < ranks.Count; i++)
            {
                Assert.That(ranks[i], Is.GreaterThan(ranks[i - 1]).Using<string>(StringComparer.Ordinal));
            }
        }

        [Test]
        public void CrossBucketInsert_ProducesCorrectRank()
        {
            var result = LexoRank.Between("A~z", "B~a");
            Assert.That(result, Is.GreaterThan("A~z").Using<string>(StringComparer.Ordinal));
            Assert.That(result, Is.LessThan("B~a").Using<string>(StringComparer.Ordinal));
            Assert.That(result, Does.StartWith("A~"));
        }

        [Test]
        public void Compare_MatchesStringCompareOrdinal()
        {
            Assert.That(LexoRank.Compare("A~a", "A~b"), Is.EqualTo(string.CompareOrdinal("A~a", "A~b")));
            Assert.That(LexoRank.Compare("A~z", "B~a"), Is.EqualTo(string.CompareOrdinal("A~z", "B~a")));
            Assert.That(LexoRank.Compare("A~m", "A~m"), Is.EqualTo(0));
            Assert.That(LexoRank.Compare("B~a", "A~z"), Is.GreaterThan(0));
        }

        // === BucketMeta Tests ===

        [Test]
        public void BucketMeta_DefaultValues_AreEmpty()
        {
            var meta = new BucketMeta();
            Assert.That(meta.BucketId, Is.Null);
            Assert.That(meta.Count, Is.EqualTo(0));
        }

        [Test]
        public void BucketMeta_CanSetValues()
        {
            var meta = new BucketMeta { BucketId = "A", Count = 100 };
            Assert.That(meta.BucketId, Is.EqualTo("A"));
            Assert.That(meta.Count, Is.EqualTo(100));
        }

        // === CollectionMetadata Tests ===

        [Test]
        public void CollectionMetadata_BucketMetas_DefaultNull()
        {
            var metadata = new CollectionMetadata();
            Assert.That(metadata.BucketMetas, Is.Null);
            Assert.That(metadata.BucketMultiplier, Is.EqualTo(3));
        }

        [Test]
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
            Assert.That(metadata.BucketMetas.Length, Is.EqualTo(2));
            Assert.That(metadata.BucketMetas[0].BucketId, Is.EqualTo("A"));
            Assert.That(metadata.BucketMetas[1].Count, Is.EqualTo(30));
            Assert.That(metadata.BucketMultiplier, Is.EqualTo(5));
        }

        [Test]
        public void CollectionMetadata_HasSerializableAttribute()
        {
            var type = typeof(CollectionMetadata);
            var attr = Attribute.GetCustomAttribute(type, typeof(SerializableAttribute));
            Assert.That(attr, Is.Not.Null, "CollectionMetadata should have [Serializable] attribute");
        }

        [Test]
        public void BucketMeta_HasSerializableAttribute()
        {
            var type = typeof(BucketMeta);
            var attr = Attribute.GetCustomAttribute(type, typeof(SerializableAttribute));
            Assert.That(attr, Is.Not.Null, "BucketMeta should have [Serializable] attribute");
        }

        // === NeedsRebalance Tests ===

        [Test]
        public void NeedsRebalance_ZeroElements_ReturnsFalse()
        {
            Assert.That(LexoRank.NeedsRebalance(0, 10), Is.False);
        }

        [Test]
        public void NeedsRebalance_AtThreshold_ReturnsFalse()
        {
            Assert.That(LexoRank.NeedsRebalance(100, LexoRank.REBALANCE_LENGTH_THRESHOLD), Is.False);
        }

        [Test]
        public void NeedsRebalance_AboveThreshold_WithOptimalLength_ReturnsFalse()
        {
            Assert.That(LexoRank.NeedsRebalance(100, 3), Is.False);
        }

        [Test]
        public void NeedsRebalance_AboveThreshold_WithExcessiveLength_ReturnsTrue()
        {
            Assert.That(LexoRank.NeedsRebalance(100, 6), Is.True);
        }
    }
}
