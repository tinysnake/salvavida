using System;
using System.Runtime.CompilerServices;

namespace Salvavida
{
    /// <summary>
    /// LexoRank is a string-based ranking system that allows O(1) insertions and deletions
    /// by using lexicographic ordering of strings.
    /// </summary>
    public static class LexoRank
    {
        public const string CHARSET = LexoRankHelper.CHARSET;
        public const string SEPARATOR = "~";
        public const int DEFAULT_BUCKET_SIZE = 100;
        public const int REBALANCE_LENGTH_THRESHOLD = 4;

        private const int MAX_RESULT_LENGTH = 256;

        public static readonly string DEFAULT_PREFIX = LexoRankHelper.MIDDLE_CHAR.ToString(); // "V"

        // Thread-local buffer for Between() to reuse
        [ThreadStatic] private static char[]? t_buffer;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Span<char> GetThreadBuffer()
        {
            var buf = t_buffer;
            if (buf == null)
                t_buffer = buf = new char[MAX_RESULT_LENGTH];
            return buf.AsSpan();
        }

        /// <summary>
        /// Smart insertion method that automatically selects the best strategy:
        /// - Both null: Returns initial rank (middle value)
        /// - prev null: Uses GenPrev(next) for uniform distribution
        /// - next null: Uses GenNext(prev) for uniform distribution
        /// - Both provided: Uses Between for midpoint calculation
        /// </summary>
        public static string Insert(string? prev, string? next, int bucketSize = DEFAULT_BUCKET_SIZE)
        {
            if (prev == null && next == null)
                return InitialValue(bucketSize);

            if (prev == null)
                return GenPrev(next!);

            if (next == null)
                return GenNext(prev);

            return Between(prev, next, bucketSize);
        }

        /// <summary>
        /// Returns the initial rank value (middle position).
        /// </summary>
        public static string InitialValue(int bucketSize = DEFAULT_BUCKET_SIZE)
        {
            var buffer = GetThreadBuffer();
            int len = ComputeInitialRankSpan(buffer, bucketSize);
            return new string(buffer[..len]);
        }

        /// <summary>
        /// Computes a rank string between prev and next.
        /// Both prev and next must be non-null and prev must be less than next.
        /// For flexible insertion, use Insert() method instead.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when prev or next is null.</exception>
        /// <exception cref="ArgumentException">Thrown when prev >= next.</exception>
        public static string Between(string? prev, string? next, int bucketSize = DEFAULT_BUCKET_SIZE)
        {
            if (prev == null)
                throw new ArgumentNullException(nameof(prev), "prev cannot be null. Use Insert() for flexible insertion, or GenPrev() for inserting before a rank.");
            if (next == null)
                throw new ArgumentNullException(nameof(next), "next cannot be null. Use Insert() for flexible insertion, or GenNext() for inserting after a rank.");

            var buffer = GetThreadBuffer();
            int len = BetweenSpanCore(prev, next, buffer, bucketSize);
            return new string(buffer[..len]);
        }

        /// <summary>
        /// High-performance version that writes result to destination buffer.
        /// Both prev and next must be non-null.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when prev or next is null.</exception>
        public static int BetweenSpan(string? prev, string? next, Span<char> destination, int bucketSize = DEFAULT_BUCKET_SIZE)
        {
            if (prev == null)
                throw new ArgumentNullException(nameof(prev), "prev cannot be null. Use Insert() for flexible insertion.");
            if (next == null)
                throw new ArgumentNullException(nameof(next), "next cannot be null. Use Insert() for flexible insertion.");

            return BetweenSpanCore(prev, next, destination, bucketSize);
        }

        private static int BetweenSpanCore(string prev, string next, Span<char> destination, int bucketSize)
        {
            // Both must be non-null at this point (validated by Between method)
            if (string.Equals(prev, next, StringComparison.Ordinal))
                throw new ArgumentException("prev and next cannot be equal", nameof(next));

            int pSepIdx = FindSeparator(prev);
            int nSepIdx = FindSeparator(next);

            ReadOnlySpan<char> prevPrefix = pSepIdx >= 0 ? prev.AsSpan(0, pSepIdx) : ReadOnlySpan<char>.Empty;
            ReadOnlySpan<char> nextPrefix = nSepIdx >= 0 ? next.AsSpan(0, nSepIdx) : ReadOnlySpan<char>.Empty;
            ReadOnlySpan<char> prevLexo = pSepIdx >= 0 ? prev.AsSpan(pSepIdx + 1) : prev.AsSpan();
            ReadOnlySpan<char> nextLexo = nSepIdx >= 0 ? next.AsSpan(nSepIdx + 1) : next.AsSpan();

            ReadOnlySpan<char> prefix = !prevPrefix.IsEmpty ? prevPrefix : (!nextPrefix.IsEmpty ? nextPrefix : DEFAULT_PREFIX);

            // Cross-bucket
            if (!prevPrefix.IsEmpty && !nextPrefix.IsEmpty && !SequenceEqual(prevPrefix, nextPrefix))
            {
                return BuildAppendSpan(destination, prefix, prevLexo, LexoRankHelper.MIDDLE_CHAR);
            }

            // Both lexo parts empty
            if (prevLexo.IsEmpty && nextLexo.IsEmpty)
            {
                throw new ArgumentException($"Invalid prev and next rank: both lexo parts are empty");
            }

            if (prevLexo.IsEmpty)
                throw new ArgumentException($"Invalid prev rank: '{prev}' - lexo part cannot be empty", nameof(prev));

            if (nextLexo.IsEmpty)
                throw new ArgumentException($"Invalid next rank: '{next}' - lexo part cannot be empty", nameof(next));

            return MidpointSpan(destination, prefix, prevLexo, nextLexo);
        }

        private static int MidpointSpan(Span<char> destination, ReadOnlySpan<char> prefix, ReadOnlySpan<char> prevLexo, ReadOnlySpan<char> nextLexo)
        {
            int diffIdx = -1;
            int prevVal = 0, nextVal = 0;

            int minLen = Math.Min(prevLexo.Length, nextLexo.Length);

            // Find first difference
            for (int i = 0; i < minLen; i++)
            {
                if (prevLexo[i] != nextLexo[i])
                {
                    diffIdx = i;
                    prevVal = LexoRankHelper.CharToIndex(prevLexo[i]);
                    nextVal = LexoRankHelper.CharToIndex(nextLexo[i]);
                    break;
                }
            }

            // Handle prefix case
            if (diffIdx < 0)
            {
                if (prevLexo.Length < nextLexo.Length)
                {
                    diffIdx = prevLexo.Length;
                    prevVal = -1;
                    nextVal = LexoRankHelper.CharToIndex(nextLexo[diffIdx]);

                    if (nextVal == 0)
                        return MidpointAfterDiffSpan(destination, prefix, prevLexo, nextLexo, diffIdx);
                }
                else if (prevLexo.Length > nextLexo.Length)
                {
                    diffIdx = nextLexo.Length;
                    prevVal = LexoRankHelper.CharToIndex(prevLexo[diffIdx]);
                    nextVal = -1;
                }
                else
                {
                    throw new ArgumentException("prev and next cannot be equal");
                }
            }

            if (prevVal >= nextVal)
                throw new ArgumentException($"prev must be less than next");

            // Use LexoDecimal for precise midpoint calculation
            int midVal = ComputeMidpointChar(prevVal, nextVal);

            // Special case: when prevLexo is a prefix of nextLexo and midVal would be 0
            bool prevIsPrefix = (diffIdx == prevLexo.Length);
            if (prevIsPrefix && midVal == 0 && nextVal > 0)
            {
                int resultLen = prefix.Length + 1 + diffIdx + 2;
                if (destination.Length < resultLen)
                    throw new ArgumentException($"Destination buffer too small.", nameof(destination));

                int pos = 0;
                prefix.CopyTo(destination.Slice(pos));
                pos += prefix.Length;
                destination[pos++] = SEPARATOR[0];
                for (int i = 0; i < diffIdx && i < prevLexo.Length; i++)
                    destination[pos++] = prevLexo[i];
                destination[pos++] = CHARSET[0];
                destination[pos++] = LexoRankHelper.MIDDLE_CHAR;
                return pos;
            }

            if (midVal > prevVal)
            {
                int resultLen = prefix.Length + 1 + diffIdx + 1;
                if (destination.Length < resultLen)
                    throw new ArgumentException($"Destination buffer too small.", nameof(destination));

                int pos = 0;
                prefix.CopyTo(destination.Slice(pos));
                pos += prefix.Length;
                destination[pos++] = SEPARATOR[0];
                for (int i = 0; i < diffIdx && i < prevLexo.Length; i++)
                    destination[pos++] = prevLexo[i];
                destination[pos++] = CHARSET[midVal];
                return pos;
            }

            return MidpointAfterDiffSpan(destination, prefix, prevLexo, nextLexo, diffIdx);
        }

        private static int ComputeMidpointChar(int prevVal, int nextVal)
        {
            // Use LexoDecimal for precise calculation
            var prevDec = LexoDecimal.From(LexoInteger.Make(1, new[] { prevVal }));
            var nextDec = LexoDecimal.From(LexoInteger.Make(1, new[] { nextVal }));

            var sum = prevDec.Add(nextDec);
            var mid = sum.Multiply(LexoDecimal.Half());
            var midFloor = mid.Floor();

            return midFloor.GetMag(0);
        }

        private static int MidpointAfterDiffSpan(Span<char> destination, ReadOnlySpan<char> prefix, ReadOnlySpan<char> prevLexo, ReadOnlySpan<char> nextLexo, int diffIdx)
        {
            bool prevIsPrefix = (diffIdx == prevLexo.Length);

            // Special case: prevIsPrefix and nextLexo starts with '0' at diffIdx
            if (prevIsPrefix && diffIdx < nextLexo.Length && nextLexo[diffIdx] == CHARSET[0])
            {
                for (int i = diffIdx + 1; i < nextLexo.Length; i++)
                {
                    int nextCharVal = LexoRankHelper.CharToIndex(nextLexo[i]);
                    if (nextCharVal > 0)
                    {
                        int midVal = nextCharVal / 2;
                        int resultLen = prefix.Length + 1 + i + 1;
                        if (destination.Length < resultLen)
                            throw new ArgumentException($"Destination buffer too small.", nameof(destination));

                        int pos = 0;
                        prefix.CopyTo(destination.Slice(pos));
                        pos += prefix.Length;
                        destination[pos++] = SEPARATOR[0];
                        prevLexo.CopyTo(destination.Slice(pos));
                        pos += prevLexo.Length;
                        for (int j = diffIdx; j < i; j++)
                            destination[pos++] = CHARSET[0];
                        destination[pos++] = CHARSET[midVal];
                        return pos;
                    }
                }

                int appendResultLen = prefix.Length + 1 + prevLexo.Length + 1;
                if (destination.Length < appendResultLen)
                    throw new ArgumentException($"Destination buffer too small.", nameof(destination));

                int p = 0;
                prefix.CopyTo(destination.Slice(p));
                p += prefix.Length;
                destination[p++] = SEPARATOR[0];
                prevLexo.CopyTo(destination.Slice(p));
                p += prevLexo.Length;
                destination[p++] = LexoRankHelper.MIDDLE_CHAR;
                return p;
            }

            for (int i = diffIdx + 1; ; i++)
            {
                int prevCharVal = (i < prevLexo.Length) ? LexoRankHelper.CharToIndex(prevLexo[i]) : 0;
                int nextCharVal = prevIsPrefix && i < nextLexo.Length
                    ? LexoRankHelper.CharToIndex(nextLexo[i])
                    : (prevIsPrefix ? 0 : CHARSET.Length - 1);

                int midVal = ComputeMidpointChar(prevCharVal, nextCharVal);

                if (midVal > prevCharVal)
                {
                    int resultLen = prefix.Length + 1 + i + 1;
                    if (destination.Length < resultLen)
                        throw new ArgumentException($"Destination buffer too small.", nameof(destination));

                    int pos = 0;
                    prefix.CopyTo(destination.Slice(pos));
                    pos += prefix.Length;
                    destination[pos++] = SEPARATOR[0];
                    for (int j = 0; j < i && j < prevLexo.Length; j++)
                        destination[pos++] = prevLexo[j];
                    destination[pos++] = CHARSET[midVal];
                    return pos;
                }

                if (prevIsPrefix && i >= nextLexo.Length)
                {
                    int resultLen = prefix.Length + 1 + prevLexo.Length + 1;
                    if (destination.Length < resultLen)
                        throw new ArgumentException($"Destination buffer too small.", nameof(destination));

                    int pos = 0;
                    prefix.CopyTo(destination.Slice(pos));
                    pos += prefix.Length;
                    destination[pos++] = SEPARATOR[0];
                    prevLexo.CopyTo(destination.Slice(pos));
                    pos += prevLexo.Length;
                    destination[pos++] = CHARSET[0];
                    return pos;
                }
            }
        }

        /// <summary>
        /// Generates the next rank after the given rank.
        /// </summary>
        public static string GenNext(string rank)
        {
            SplitRank(rank, out var prefix, out var lexo);
            var integer = LexoInteger.Parse(lexo.ToString());
            var nextInt = GenNextInteger(integer);
            var buffer = GetThreadBuffer();
            int len = FormatInteger(buffer, prefix, nextInt);
            return new string(buffer[..len]);
        }

        /// <summary>
        /// Generates the previous rank before the given rank.
        /// </summary>
        public static string GenPrev(string rank)
        {
            SplitRank(rank, out var prefix, out var lexo);
            var integer = LexoInteger.Parse(lexo.ToString());
            
            // Handle edge cases where simple subtraction doesn't work
            var lexoStr = lexo.ToString();
            
            // If the value is very small (single digit close to 0)
            if (lexoStr.Length == 1)
            {
                var firstCharValue = LexoRankHelper.CharToIndex(lexoStr[0]);
                if (firstCharValue <= 8)
                {
                    // "0" can't go lower
                    if (firstCharValue == 0)
                        throw new InvalidOperationException("Cannot generate rank before the minimum possible rank.");
                    
                    // Return "0" + max char, e.g., "1" -> "0z"
                    var buffer = GetThreadBuffer();
                    int pos = 0;
                    prefix.CopyTo(buffer);
                    pos += prefix.Length;
                    buffer[pos++] = SEPARATOR[0];
                    buffer[pos++] = CHARSET[0]; // "0"
                    buffer[pos++] = CHARSET[CHARSET.Length - 1]; // "z"
                    return new string(buffer[..pos]);
                }
            }
            
            var prevInt = GenPrevInteger(integer);
            var buf = GetThreadBuffer();
            int len = FormatInteger(buf, prefix, prevInt);
            return new string(buf[..len]);
        }

        /// <summary>
        /// Returns the minimum possible rank.
        /// </summary>
        public static string Min(string prefix = "V")
        {
            var buffer = GetThreadBuffer();
            int len = FormatInteger(buffer, prefix.AsSpan(), LexoInteger.Zero);
            return new string(buffer[..len]);
        }

        /// <summary>
        /// Returns the maximum possible rank.
        /// </summary>
        public static string Max(string prefix = "V")
        {
            var maxInt = LexoInteger.Parse("zzzzzz");
            var buffer = GetThreadBuffer();
            int len = FormatInteger(buffer, prefix.AsSpan(), maxInt);
            return new string(buffer[..len]);
        }

        /// <summary>
        /// Returns the middle rank.
        /// </summary>
        public static string Middle(string prefix = "V")
        {
            var minInt = LexoInteger.Zero;
            var maxInt = LexoInteger.Parse("zzzzzz");
            var midInt = BetweenInteger(minInt, maxInt);
            var buffer = GetThreadBuffer();
            int len = FormatInteger(buffer, prefix.AsSpan(), midInt);
            return new string(buffer[..len]);
        }

        /// <summary>
        /// Checks if a rank is the minimum possible rank.
        /// </summary>
        public static bool IsMin(string rank)
        {
            SplitRank(rank, out _, out var lexo);
            var integer = LexoInteger.Parse(lexo.ToString());
            return integer.IsZero;
        }

        /// <summary>
        /// Checks if a rank is the maximum possible rank.
        /// </summary>
        public static bool IsMax(string rank)
        {
            SplitRank(rank, out _, out var lexo);
            var integer = LexoInteger.Parse(lexo.ToString());
            var maxInt = LexoInteger.Parse("zzzzzz");
            return integer.Equals(maxInt);
        }

        private static LexoInteger GenNextInteger(LexoInteger integer)
        {
            var maxInt = LexoInteger.Parse("zzzzzz");
            var initialMinInt = LexoInteger.Parse("100000");
            var eightInt = LexoInteger.Parse("8");

            if (integer.IsZero)
                return initialMinInt;

            var nextInt = integer.Add(eightInt);
            
            // Check if the result has more digits (overflow/ carry)
            // This would make it lexicographically SMALLER, which is wrong
            // e.g., "z" + 8 = 69 = "17" but "17" < "z" lexicographically
            if (nextInt.Length > integer.Length)
            {
                // Instead of midpoint, append a character to the original
                // "z" -> "zV" (V is middle char)
                return LexoInteger.Parse(integer.Format() + "V");
            }
            
            if (nextInt.CompareTo(maxInt) >= 0)
                return BetweenInteger(integer, maxInt);

            return nextInt;
        }

        private static LexoInteger GenPrevInteger(LexoInteger integer)
        {
            var minInt = LexoInteger.Zero;
            var maxInt = LexoInteger.Parse("zzzzzz");
            var initialMaxInt = LexoInteger.Parse("yzzzzz");
            var eightInt = LexoInteger.Parse("8");

            if (integer.Equals(maxInt))
                return initialMaxInt;

            if (integer.IsZero)
            {
                // Cannot go below zero - this is the minimum
                throw new InvalidOperationException("Cannot generate rank before the minimum possible rank.");
            }

            var prevInt = integer.Subtract(eightInt);
            
            // If subtraction would go below zero
            if (prevInt.CompareTo(minInt) <= 0 || prevInt.IsZero)
            {
                // For small values like "1", we need a value that's < "1" lexicographically
                // "0z" < "1" because '0' < '1'
                // But we need to use the actual format
                var format = integer.Format();
                
                // If it's a single character that's close to minimum
                if (format.Length == 1)
                {
                    var firstCharValue = LexoRankHelper.CharToIndex(format[0]);
                    if (firstCharValue <= 8)
                    {
                        // Return "0" followed by appropriate char
                        // e.g., for "1" (value 1), return "0z" (value 61)
                        // "0z" < "1" lexicographically
                        return LexoInteger.Parse("0z");
                    }
                }
                
                // Otherwise use midpoint
                return BetweenInteger(minInt, integer);
            }

            return prevInt;
        }

        private static LexoInteger BetweenInteger(LexoInteger left, LexoInteger right)
        {
            var leftDec = LexoDecimal.From(left);
            var rightDec = LexoDecimal.From(right);

            var sum = leftDec.Add(rightDec);
            var mid = sum.Multiply(LexoDecimal.Half());
            var midFloor = mid.Floor();

            if (midFloor.CompareTo(left) <= 0)
                return left.Add(LexoInteger.One);

            if (midFloor.CompareTo(right) >= 0)
                throw new ArgumentException("Midpoint calculation error: result >= right");

            return midFloor;
        }

        private static void SplitRank(string rank, out ReadOnlySpan<char> prefix, out ReadOnlySpan<char> lexo)
        {
            var sepIdx = FindSeparator(rank);
            if (sepIdx < 0)
            {
                prefix = DEFAULT_PREFIX.AsSpan();
                lexo = rank.AsSpan();
            }
            else
            {
                prefix = rank.AsSpan(0, sepIdx);
                lexo = rank.AsSpan(sepIdx + 1);
            }
        }

        private static int FormatInteger(Span<char> destination, ReadOnlySpan<char> prefix, LexoInteger integer)
        {
            var formatVal = integer.Format();
            int totalLen = prefix.Length + 1 + formatVal.Length;

            if (destination.Length < totalLen)
                throw new ArgumentException($"Destination buffer too small.", nameof(destination));

            int pos = 0;
            prefix.CopyTo(destination);
            pos += prefix.Length;
            destination[pos++] = SEPARATOR[0];

            for (int i = 0; i < formatVal.Length; i++)
                destination[pos++] = formatVal[i];

            return pos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindSeparator(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == SEPARATOR[0])
                    return i;
            }
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool SequenceEqual(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        private static int ComputeInitialRankSpan(Span<char> destination, int bucketSize)
        {
            int minLen = MinLengthForCount(bucketSize);
            int totalLen = DEFAULT_PREFIX.Length + 1 + minLen;

            if (destination.Length < totalLen)
                throw new ArgumentException($"Destination buffer too small.", nameof(destination));

            int pos = 0;
            DEFAULT_PREFIX.AsSpan().CopyTo(destination);
            pos += DEFAULT_PREFIX.Length;
            destination[pos++] = SEPARATOR[0];
            for (int i = 0; i < minLen; i++)
                destination[pos++] = LexoRankHelper.MIDDLE_CHAR;

            return pos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int BuildAppendSpan(Span<char> destination, ReadOnlySpan<char> prefix, ReadOnlySpan<char> lexo, char appendChar)
        {
            int totalLen = prefix.Length + 1 + lexo.Length + 1;
            if (destination.Length < totalLen)
                throw new ArgumentException($"Destination buffer too small.", nameof(destination));

            int pos = 0;
            prefix.CopyTo(destination.Slice(pos));
            pos += prefix.Length;
            destination[pos++] = SEPARATOR[0];
            lexo.CopyTo(destination.Slice(pos));
            pos += lexo.Length;
            destination[pos++] = appendChar;
            return pos;
        }

        public static bool NeedsRebalance(int elementCount, int maxRankLength)
        {
            if (elementCount <= 1) return false;
            if (maxRankLength <= REBALANCE_LENGTH_THRESHOLD) return false;
            var minLength = MinLengthForCount(elementCount);
            return maxRankLength > minLength + 1;
        }

        public static string[] Rebalance(int count)
        {
            if (count <= 0) return Array.Empty<string>();
            var result = new string[count];
            int minLen = Math.Max(1, MinLengthForCount(count));
            var range = (long)Math.Pow(62, minLen) - 1;
            var step = range / (count + 1);
            for (int i = 0; i < count; i++)
            {
                result[i] = Base62Encode(step * (i + 1), minLen);
            }
            return result;
        }

        public static int Compare(string a, string b) => string.CompareOrdinal(a, b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int MinLengthForCount(int count)
        {
            int len = 1;
            long capacity = 62;
            while (capacity < count && len < 10)
            {
                len++;
                capacity *= 62;
            }
            return len;
        }

        private static string Base62Encode(long value, int minLength = 1)
        {
            if (value == 0)
            {
                if (minLength <= 1) return CHARSET.Substring(0, 1);
                return new string(CHARSET[0], minLength);
            }
            Span<char> buf = stackalloc char[16];
            int pos = 16;
            while (value > 0)
            {
                buf[--pos] = CHARSET[(int)(value % 62)];
                value /= 62;
            }
            int len = 16 - pos;
            if (len < minLength)
            {
                Span<char> padded = stackalloc char[minLength];
                for (int i = 0; i < minLength - len; i++)
                    padded[i] = CHARSET[0];
                buf.Slice(pos, len).CopyTo(padded.Slice(minLength - len));
                return padded.Slice(0, minLength).ToString();
            }
            return buf.Slice(pos, len).ToString();
        }
    }
}
