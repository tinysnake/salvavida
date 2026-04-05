using NUnit.Framework;
using System;

namespace Salvavida.Tests
{
    public class LexoRankSymmetryTests
    {
        [Test]
        public void ComputeBeforeAndAfter_ReturnMinimalConsumption()
        {
            // When prev=null, next="V~VV"
            var before = LexoRank.Between(null, "V~VV");
            Assert.That(before, Is.LessThan("V~VV").Using<string>(StringComparer.Ordinal));
            
            // When prev="V~VV", next=null
            var after = LexoRank.Between("V~VV", null);
            Assert.That(after, Is.GreaterThan("V~VV").Using<string>(StringComparer.Ordinal));
            
            Console.WriteLine($"Before 'V~VV': {before}");
            Console.WriteLine($"After 'V~VV': {after}");
            
            // Verify the results use append strategy (minimal consumption)
            // Before: "VV" -> "VU" (last char - 1, only 1 position consumed)
            // After: "VV" -> "VVV" (append 'V', only 1 position consumed)
            Assert.That(before, Is.EqualTo("V~VU"));
            Assert.That(after, Is.EqualTo("V~VVV"));
        }

        [Test]
        public void ComputeBeforeAndAfter_EdgeCaseStartChar()
        {
            // When prev=null, next="V~0" (last char is '0')
            // Note: ComputeBeforeSpan will try to reduce last char, but '0' can't be reduced
            var before = LexoRank.Between(null, "V~0");
            Assert.That(before, Is.LessThan("V~0").Using<string>(StringComparer.Ordinal));
            
            // When prev="V~0", next=null
            var after = LexoRank.Between("V~0", null);
            Assert.That(after, Is.GreaterThan("V~0").Using<string>(StringComparer.Ordinal));
            
            Console.WriteLine($"Before 'V~0': {before}");
            Console.WriteLine($"After 'V~0': {after}");
            
            // After: "0" -> "0V" (append 'V')
            Assert.That(after, Is.EqualTo("V~0V"));
            
            // Before falls back to prefix manipulation (U~V) since lastChar is '0' and firstChar is '0'
            Assert.That(before, Is.EqualTo("U~V"));
        }

        [Test]
        public void ComputeAfter_EdgeCaseLastChar_Appends()
        {
            // When prev="V~z", next=null (ends with 'z', last char)
            var after = LexoRank.Between("V~z", null);
            Assert.That(after, Is.GreaterThan("V~z").Using<string>(StringComparer.Ordinal));
            
            Console.WriteLine($"After 'V~z': {after}");
            
            // Should append 'V' (MIDDLE_CHAR)
            Assert.That(after, Is.EqualTo("V~zV"));
        }
    }
}
