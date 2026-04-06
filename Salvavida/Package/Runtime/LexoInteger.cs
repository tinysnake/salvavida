using System;
using System.Text;

namespace Salvavida
{
    /// <summary>
    /// Represents an integer in Base62 numeral system.
    /// Used for precise arithmetic operations in LexoRank calculations.
    /// </summary>
    public sealed class LexoInteger : IComparable<LexoInteger>, IEquatable<LexoInteger>
    {
        private static readonly int[] ZeroMag = { 0 };
        private static readonly int[] OneMag = { 1 };

        private readonly int[] _mag;
        private readonly int _sign;

        private LexoInteger(int sign, int[] mag)
        {
            _sign = sign;
            _mag = mag;
        }

        /// <summary>
        /// Gets the zero value.
        /// </summary>
        public static LexoInteger Zero { get; } = new LexoInteger(0, ZeroMag);

        /// <summary>
        /// Gets the one value.
        /// </summary>
        public static LexoInteger One { get; } = new LexoInteger(1, OneMag);

        /// <summary>
        /// Checks if this integer is zero.
        /// </summary>
        public bool IsZero => _sign == 0 && _mag.Length == 1 && _mag[0] == 0;

        /// <summary>
        /// Checks if this integer is one.
        /// </summary>
        public bool IsOne => _sign == 1 && _mag.Length == 1 && _mag[0] == 1;

        /// <summary>
        /// Gets the magnitude at the specified index.
        /// </summary>
        public int GetMag(int index) => _mag[index];

        /// <summary>
        /// Gets the length of the magnitude array.
        /// </summary>
        public int Length => _mag.Length;

        /// <summary>
        /// Parses a Base62 string to a LexoInteger.
        /// </summary>
        public static LexoInteger Parse(string str)
        {
            if (string.IsNullOrEmpty(str))
                throw new ArgumentException("String cannot be null or empty", nameof(str));

            var s = str;
            var sign = 1;

            if (str[0] == '+')
            {
                s = str.Substring(1);
            }
            else if (str[0] == '-')
            {
                s = str.Substring(1);
                sign = -1;
            }

            var mag = new int[s.Length];
            var strIndex = s.Length - 1;

            for (var magIndex = 0; strIndex >= 0; magIndex++, strIndex--)
            {
                mag[magIndex] = LexoRankHelper.CharToIndex(s[strIndex]);
            }

            return Make(sign, mag);
        }

        /// <summary>
        /// Creates a LexoInteger from sign and magnitude.
        /// </summary>
        public static LexoInteger Make(int sign, int[] mag)
        {
            // Find actual length (trim leading zeros)
            int actualLength;
            for (actualLength = mag.Length; actualLength > 0 && mag[actualLength - 1] == 0; actualLength--)
            {
            }

            if (actualLength == 0)
                return Zero;

            if (actualLength == mag.Length)
                return new LexoInteger(sign, mag);

            var nmag = new int[actualLength];
            Array.Copy(mag, 0, nmag, 0, actualLength);
            return new LexoInteger(sign, nmag);
        }

        /// <summary>
        /// Adds another LexoInteger to this one.
        /// </summary>
        public LexoInteger Add(LexoInteger other)
        {
            if (IsZero) return other;
            if (other.IsZero) return this;

            if (_sign != other._sign)
            {
                if (_sign == -1)
                {
                    return Negate().Subtract(other).Negate();
                }
                return Subtract(other.Negate());
            }

            var result = Add(_mag, other._mag);
            return Make(_sign, result);
        }

        /// <summary>
        /// Subtracts another LexoInteger from this one.
        /// </summary>
        public LexoInteger Subtract(LexoInteger other)
        {
            if (IsZero) return other.Negate();
            if (other.IsZero) return this;

            if (_sign != other._sign)
            {
                if (_sign == -1)
                {
                    return Negate().Add(other).Negate();
                }
                return Add(other.Negate());
            }

            var cmp = Compare(_mag, other._mag);
            if (cmp == 0) return Zero;

            return cmp < 0
                ? Make(_sign == -1 ? 1 : -1, Subtract(other._mag, _mag))
                : Make(_sign == -1 ? -1 : 1, Subtract(_mag, other._mag));
        }

        /// <summary>
        /// Multiplies this LexoInteger by another.
        /// </summary>
        public LexoInteger Multiply(LexoInteger other)
        {
            if (IsZero || other.IsZero) return Zero;
            if (IsOne) return _sign == other._sign ? other : other.Negate();
            if (other.IsOne) return _sign == other._sign ? this : Negate();

            var newMag = Multiply(_mag, other._mag);
            return Make(_sign == other._sign ? 1 : -1, newMag);
        }

        /// <summary>
        /// Negates this integer.
        /// </summary>
        public LexoInteger Negate() => IsZero ? this : new LexoInteger(_sign == 1 ? -1 : 1, _mag);

        /// <summary>
        /// Shifts left by the specified number of positions (multiplies by base^times).
        /// </summary>
        public LexoInteger ShiftLeft(int times = 1)
        {
            if (times == 0) return this;
            if (times < 0) return ShiftRight(Math.Abs(times));
            if (IsZero) return this;

            var nmag = new int[_mag.Length + times];
            Array.Copy(_mag, 0, nmag, times, _mag.Length);
            return Make(_sign, nmag);
        }

        /// <summary>
        /// Shifts right by the specified number of positions (divides by base^times).
        /// </summary>
        public LexoInteger ShiftRight(int times = 1)
        {
            if (_mag.Length - times <= 0) return Zero;
            if (IsZero) return this;

            var nmag = new int[_mag.Length - times];
            Array.Copy(_mag, times, nmag, 0, nmag.Length);
            return Make(_sign, nmag);
        }

        /// <summary>
        /// Formats this integer as a Base62 string.
        /// </summary>
        public string Format()
        {
            if (IsZero) return "0";

            var sb = new StringBuilder();
            foreach (var digit in _mag)
            {
                sb.Insert(0, LexoRankHelper.IndexToChar(digit));
            }

            if (_sign == -1)
                sb.Insert(0, '-');

            return sb.ToString();
        }

        public override string ToString() => Format();

        private static int[] Add(int[] l, int[] r)
        {
            const int @base = LexoRankHelper.BASE;
            var estimatedSize = Math.Max(l.Length, r.Length);
            var result = new int[estimatedSize];
            var carry = 0;

            for (var i = 0; i < estimatedSize; i++)
            {
                var lNum = i < l.Length ? l[i] : 0;
                var rNum = i < r.Length ? r[i] : 0;
                var sum = lNum + rNum + carry;

                carry = 0;
                while (sum >= @base)
                {
                    sum -= @base;
                    carry++;
                }

                result[i] = sum;
            }

            // Extend with carry if needed
            if (carry > 0)
            {
                var extendedMag = new int[result.Length + 1];
                Array.Copy(result, 0, extendedMag, 0, result.Length);
                extendedMag[result.Length] = carry;
                return extendedMag;
            }

            return result;
        }

        private static int[] Subtract(int[] l, int[] r)
        {
            const int @base = LexoRankHelper.BASE;

            // Compute r's complement
            var rComplement = new int[l.Length];
            for (var i = 0; i < l.Length; i++)
            {
                rComplement[i] = @base - 1;
            }
            for (var i = 0; i < r.Length; i++)
            {
                rComplement[i] = @base - 1 - r[i];
            }

            // Add l + rComplement
            var rSum = Add(l, rComplement);

            // Drop the overflow digit and add 1
            var result = new int[rSum.Length - 1];
            Array.Copy(rSum, 0, result, 0, result.Length);

            // Add 1
            return Add(result, OneMag);
        }

        private static int[] Multiply(int[] l, int[] r)
        {
            const int @base = LexoRankHelper.BASE;
            var result = new int[l.Length + r.Length];

            for (var li = 0; li < l.Length; li++)
            {
                for (var ri = 0; ri < r.Length; ri++)
                {
                    var resultIndex = li + ri;
                    result[resultIndex] += l[li] * r[ri];

                    while (result[resultIndex] >= @base)
                    {
                        result[resultIndex] -= @base;
                        result[resultIndex + 1]++;
                    }
                }
            }

            return result;
        }

        private static int Compare(int[] l, int[] r)
        {
            if (l.Length < r.Length) return -1;
            if (l.Length > r.Length) return 1;

            for (var i = l.Length - 1; i >= 0; i--)
            {
                if (l[i] < r[i]) return -1;
                if (l[i] > r[i]) return 1;
            }

            return 0;
        }

        public int CompareTo(LexoInteger? other)
        {
            if (other is null) return 1;
            if (ReferenceEquals(this, other)) return 0;

            if (_sign == -1)
            {
                if (other._sign == -1)
                {
                    var cmp = Compare(_mag, other._mag);
                    if (cmp == -1) return 1;
                    return cmp == 1 ? -1 : 0;
                }
                return -1;
            }

            if (_sign == 1)
                return other._sign == 1 ? Compare(_mag, other._mag) : 1;

            // this is zero
            if (other._sign == -1) return 1;
            return other._sign == 1 ? -1 : 0;
        }

        public bool Equals(LexoInteger? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return _sign == other._sign && Compare(_mag, other._mag) == 0;
        }

        public override bool Equals(object? obj) => obj is LexoInteger other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = _mag != null ? _mag.GetHashCode() : 0;
                hashCode = (hashCode * 397) ^ _sign;
                return hashCode;
            }
        }

        public static bool operator ==(LexoInteger? left, LexoInteger? right) =>
            left?.Equals(right) ?? right is null;

        public static bool operator !=(LexoInteger? left, LexoInteger? right) =>
            !(left == right);

        public static bool operator <(LexoInteger? left, LexoInteger? right) =>
            left is null ? right is not null : left.CompareTo(right) < 0;

        public static bool operator >(LexoInteger? left, LexoInteger? right) =>
            left is not null && left.CompareTo(right) > 0;

        public static bool operator <=(LexoInteger? left, LexoInteger? right) =>
            left is null || left.CompareTo(right) <= 0;

        public static bool operator >=(LexoInteger? left, LexoInteger? right) =>
            left is null ? right is null : left.CompareTo(right) >= 0;
    }
}
