using Xunit;

namespace Salvavida.Tests
{
    public class LexoRankSymmetryTests(ITestOutputHelper output)
    {
        [Fact]
        public void ComputeBeforeAndAfter_ReturnMinimalConsumption()
        {
            var before = LexoRank.Between(null, "V~VV");
            Assert.True(string.CompareOrdinal(before, "V~VV") < 0);

            var after = LexoRank.Between("V~VV", null);
            Assert.True(string.CompareOrdinal(after, "V~VV") > 0);

            output.WriteLine($"Before 'V~VV': {before}");
            output.WriteLine($"After 'V~VV': {after}");

            Assert.Equal("V~VU", before);
            Assert.Equal("V~VVV", after);
        }

        [Fact]
        public void ComputeBeforeAndAfter_EdgeCaseStartChar()
        {
            var before = LexoRank.Between(null, "V~0");
            Assert.True(string.CompareOrdinal(before, "V~0") < 0);

            var after = LexoRank.Between("V~0", null);
            Assert.True(string.CompareOrdinal(after, "V~0") > 0);

            output.WriteLine($"Before 'V~0': {before}");
            output.WriteLine($"After 'V~0': {after}");

            Assert.Equal("V~0V", after);
            Assert.Equal("U~V", before);
        }

        [Fact]
        public void ComputeAfter_EdgeCaseLastChar_Appends()
        {
            var after = LexoRank.Between("V~z", null);
            Assert.True(string.CompareOrdinal(after, "V~z") > 0);

            output.WriteLine($"After 'V~z': {after}");

            Assert.Equal("V~zV", after);
        }
    }
}
