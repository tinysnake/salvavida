using Xunit;

namespace Salvavida.Tests
{
    public class LexoRankSymmetryTests(ITestOutputHelper output)
    {
        [Fact]
        public void GenPrevAndGenNext_ReturnMinimalConsumption()
        {
            var before = LexoRank.GenPrev("V~VV");
            Assert.True(string.CompareOrdinal(before, "V~VV") < 0, $"Before should be < 'V~VV', got '{before}'");

            var after = LexoRank.GenNext("V~VV");
            Assert.True(string.CompareOrdinal(after, "V~VV") > 0, $"After should be > 'V~VV', got '{after}'");

            output.WriteLine($"Before 'V~VV': {before}");
            output.WriteLine($"After 'V~VV': {after}");

            // Verify ordering
            Assert.True(string.CompareOrdinal(before, after) < 0, "Before should be < After");
        }

        [Fact]
        public void GenPrevAndGenNext_EdgeCaseStartChar()
        {
            // After "V~0" should work
            var after = LexoRank.GenNext("V~0");
            Assert.True(string.CompareOrdinal(after, "V~0") > 0, $"After should be > 'V~0', got '{after}'");

            output.WriteLine($"After 'V~0': {after}");

            // Before "V~1" is at the edge - may need rebalancing
            // This tests that we can insert at the edge with longer strings
            var before = LexoRank.GenPrev("V~1");
            Assert.True(string.CompareOrdinal(before, "V~1") < 0, $"Before should be < 'V~1', got '{before}'");
            output.WriteLine($"Before 'V~1': {before}");
        }

        [Fact]
        public void GenNext_EdgeCaseLastChar_Appends()
        {
            var after = LexoRank.GenNext("V~z");
            Assert.True(string.CompareOrdinal(after, "V~z") > 0, $"After should be > 'V~z', got '{after}'");

            output.WriteLine($"After 'V~z': {after}");

            // The after value should be greater than "V~z"
            // It can be "V~zV" (append) or a longer value
        }

        [Fact]
        public void Insert_PrevNull_UsesGenPrev()
        {
            var result = LexoRank.Insert(null, "V~m");
            Assert.True(string.CompareOrdinal(result, "V~m") < 0);
            output.WriteLine($"Insert(null, 'V~m'): {result}");
        }

        [Fact]
        public void Insert_NextNull_UsesGenNext()
        {
            var result = LexoRank.Insert("V~m", null);
            Assert.True(string.CompareOrdinal(result, "V~m") > 0);
            output.WriteLine($"Insert('V~m', null): {result}");
        }

        [Fact]
        public void Insert_BothNull_ReturnsInitialValue()
        {
            var result = LexoRank.Insert(null, null);
            Assert.Contains(LexoRank.SEPARATOR, result);
            output.WriteLine($"Insert(null, null): {result}");
        }
    }
}
