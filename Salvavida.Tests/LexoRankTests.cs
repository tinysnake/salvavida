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
    }
}
