using System;
using System.Text;

namespace Salvavida
{
    /// <summarysummary>
    /// Represents a decimal value in Base62 numeral system.
    /// Used for precise midpoint calculations in LexoRank.
    /// </summary>
    public sealed class LexoDecimal : IComparable<LexoDecimal>, IEquatable<LexoDecimal>
    {
        private readonly LexoInteger _mag;
        private readonly int _sig; // scale (number of fractional digits)

        private LexoDecimal(LexoInteger mag, int sig)
        {
            _mag = mag;
            _sig = sig;
        }

        /// <summary>
        /// Gets the scale (number of fractional digits).
        /// </summary>
        public int Scale => _sig;

        /// <summary>
        /// Gets the magnitude (integer part with fractional digits).
        /// </summary>
        public LexoInteger Magnitude => _mag;

        /// <summary>
        /// Parses a Base62 string to a LexoDecimal.
        /// </summary>
        public static LexoDecimal Parse(string str)
        {
            var radixPoint = LexoRankHelper.RADIX_POINT;
            var partialIndex = str.IndexOf(radixPoint);

            if (str.LastIndexOf(radixPoint) != partialIndex)
                throw new FormatException($"More than one radix point '{radixPoint}'");

            if (partialIndex < 0)
                return Make(LexoInteger.Parse(str), 0);

            var intStr = str.Substring(0, partialIndex) + str.Substring(partialIndex + 1);
            return Make(LexoInteger.Parse(intStr), str.Length - 1 - partialIndex);
        }

        /// <summary>
        /// Creates a LexoDecimal from a LexoInteger.
        /// </summary>
        public static LexoDecimal From(LexoInteger integer) => Make(integer, 0);

        /// <summary>
        /// Creates a LexoDecimal from a LexoInteger and scale.
        /// </summary>
        public static LexoDecimal Make(LexoInteger integer, int sig)
        {
            if (integer.IsZero)
                return new LexoDecimal(LexoInteger.Zero, 0);

            // Count trailing zeros that can be removed
            var zeroCount = 0;
            for (var i = 0; i < sig && integer.GetMag(i) == 0; i++)
            {
                zeroCount++;
            }

            var newInteger = integer.ShiftRight(zeroCount);
            var newSig = sig - zeroCount;
            return new LexoDecimal(newInteger, newSig);
        }

        /// <summary>
        /// Returns half of the base (0.5 in this numeral system).
        /// </summary>
        public static LexoDecimal Half()
        {
            var mid = LexoRankHelper.BASE / 2; // 31 for base62
            return Make(LexoInteger.Make(1, new[] { mid }), 1);
        }

        /// <summary>
        /// Adds another LexoDecimal to this one.
        /// </summary>
        public LexoDecimal Add(LexoDecimal other)
        {
            var (thisMag, otherMag, scale) = AlignScales(other);
            return Make(thisMag.Add(otherMag), scale);
        }

        /// <summary>
        /// Subtracts another LexoDecimal from this one.
        /// </summary>
        public LexoDecimal Subtract(LexoDecimal other)
        {
            var (thisMag, otherMag, scale) = AlignScales(other);
            return Make(thisMag.Subtract(otherMag), scale);
        }

        /// <summary>
        /// Multiplies this LexoDecimal by another.
        /// </summary>
        public LexoDecimal Multiply(LexoDecimal other) =>
            Make(_mag.Multiply(other._mag), _sig + other._sig);

        /// <summary>
        /// Returns the floor of this decimal.
        /// </summary>
        public LexoInteger Floor() => _mag.ShiftRight(_sig);

        /// <summary>
        /// Returns the ceiling of this decimal.
        /// </summary>
        public LexoInteger Ceil()
        {
            if (IsExact()) return _mag;

            var floor = Floor();
            return floor.Add(LexoInteger.One);
        }

        /// <summary>
        /// Checks if the decimal is exact (has no fractional part).
        /// </summary>
        public bool IsExact()
        {
            if (_sig == 0) return true;

            for (var i = 0; i < _sig; i++)
            {
                if (_mag.GetMag(i) != 0)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Sets the scale of the decimal.
        /// </summary>
        public LexoDecimal SetScale(int nSig) => SetScale(nSig, false);

        /// <summary>
        /// Sets the scale of the decimal with optional ceiling rounding.
        /// </summary>
        public LexoDecimal SetScale(int nSig, bool ceiling)
        {
            if (nSig >= _sig) return this;
            if (nSig < 0) nSig = 0;

            var diff = _sig - nSig;
            var nmag = _mag.ShiftRight(diff);

            if (ceiling)
                nmag = nmag.Add(LexoInteger.One);

            return Make(nmag, nSig);
        }

        /// <summary>
        /// Formats this decimal as a Base62 string.
        /// </summary>
        public string Format()
        {
            var intStr = _mag.Format();
            if (_sig == 0) return intStr;

            var sb = new StringBuilder(intStr);
            var head = sb[0];
            var specialHead = head == '+' || head == '-';

            if (specialHead)
                sb.Remove(0, 1);

            while (sb.Length < _sig + 1)
                sb.Insert(0, '0');

            sb.Insert(sb.Length - _sig, LexoRankHelper.RADIX_POINT);

            if (sb.Length - _sig == 0)
                sb.Insert(0, '0');

            if (specialHead)
                sb.Insert(0, head);

            return sb.ToString();
        }

        public override string ToString() => Format();

        private (LexoInteger thisMag, LexoInteger otherMag, int scale) AlignScales(LexoDecimal other)
        {
            var thisMag = _mag;
            var thisSig = _sig;
            var otherMag = other._mag;
            var otherSig = other._sig;

            while (thisSig < otherSig)
            {
                thisMag = thisMag.ShiftLeft();
                thisSig++;
            }

            while (thisSig > otherSig)
            {
                otherMag = otherMag.ShiftLeft();
                otherSig++;
            }

            return (thisMag, otherMag, thisSig);
        }

        public int CompareTo(LexoDecimal? other)
        {
            if (other is null) return 1;
            if (ReferenceEquals(this, other)) return 0;

            var (thisMag, otherMag, _) = AlignScales(other);
            return thisMag.CompareTo(otherMag);
        }

        public bool Equals(LexoDecimal? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return _mag.Equals(other._mag) && _sig == other._sig;
        }

        public override bool Equals(object? obj) => obj is LexoDecimal other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (_mag.GetHashCode() * 397) ^ _sig;
            }
        }

        public static bool operator ==(LexoDecimal? left, LexoDecimal? right) =>
            left?.Equals(right) ?? right is null;

        public static bool operator !=(LexoDecimal? left, LexoDecimal? right) =>
            !(left == right);

        public static bool operator <(LexoDecimal? left, LexoDecimal? right) =>
            left is null ? right is not null : left.CompareTo(right) < 0;

        public static bool operator >(LexoDecimal? left, LexoDecimal? right) =>
            left is not null && left.CompareTo(right) > 0;

        public static bool operator <=(LexoDecimal? left, LexoDecimal? right) =>
            left is null || left.CompareTo(right) <= 0;

        public static bool operator >=(LexoDecimal? left, LexoDecimal? right) =>
            left is null ? right is null : left.CompareTo(right) >= 0;
    }
}
