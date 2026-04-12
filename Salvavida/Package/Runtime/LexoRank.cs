using System;

namespace Salvavida
{
    /// <summary>
    /// A LexoRank variant implementation using Base62 encoding.
    /// Format: {BucketId}~{Rank}
    /// </summary>
    public static class LexoRank
    {
        private const string CHARSET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        private const char SEPARATOR = '~';
        private const int DEFAULT_STEP_SIZE = 8;
        private const int BASE = 62;

        #region Public API - Precision Calculation

        /// <summary>
        /// Calculates the precision digits needed for a given chunk size.
        /// </summary>
        /// <param name="chunkSize">The maximum number of items in a chunk</param>
        /// <returns>The number of digits needed to represent chunkSize in Base62</returns>
        public static int CalculatePrecisionDigits(int chunkSize)
        {
            if (chunkSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(chunkSize), "ChunkSize must be greater than 0");

            if (chunkSize == 1)
                return 1;

            return (int)Math.Ceiling(Math.Log(chunkSize, BASE));
        }

        #endregion

        #region Public API - Parse and Build

        /// <summary>
        /// Compares two rank strings using Base62 order.
        /// </summary>
        /// <param name="a">First rank</param>
        /// <param name="b">Second rank</param>
        /// <returns>-1 if a &lt; b, 0 if a == b, 1 if a &gt; b</returns>
        public static int Compare(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
        {
            return CompareInternal(a, b);
        }

        #endregion

        #region Public API - Parse and Build

        /// <summary>
        /// Parses a LexoRank string into its components.
        /// </summary>
        /// <param name="lexoRank">The LexoRank string to parse</param>
        /// <param name="bucketId">The bucket ID</param>
        /// <param name="rank">The rank value</param>
        public static void Parse(ReadOnlySpan<char> lexoRank,
            out ReadOnlySpan<char> bucketId,
            out ReadOnlySpan<char> rank)
        {
            var sepIndex = lexoRank.IndexOf(SEPARATOR);
            if (sepIndex < 0)
                throw new FormatException($"LexoRank must contain '{SEPARATOR}' separator");

            bucketId = lexoRank[..sepIndex];
            rank = lexoRank[(sepIndex + 1)..];
        }

        /// <summary>
        /// Builds a LexoRank string from its components.
        /// </summary>
        /// <param name="bucketId">The bucket ID</param>
        /// <param name="rank">The rank value</param>
        /// <returns>A formatted LexoRank string</returns>
        public static ReadOnlySpan<char> Build(ReadOnlySpan<char> bucketId, ReadOnlySpan<char> rank)
        {
            var totalLength = bucketId.Length + 1 + rank.Length;
            Span<char> buffer = stackalloc char[totalLength];

            var position = 0;
            bucketId.CopyTo(buffer[position..]);
            position += bucketId.Length;
            buffer[position++] = SEPARATOR;
            rank.CopyTo(buffer[position..]);

            return new string(buffer);
        }

        /// <summary>
        /// Builds a LexoRank string from its components.
        /// </summary>
        /// <param name="bucketId">The bucket ID</param>
        /// <param name="rank">The rank value</param>
        /// <param name="buffer">The buffer to write the result</param>
        /// <returns>The length of the written result</returns>
        public static int Build(ReadOnlySpan<char> bucketId, ReadOnlySpan<char> rank, Span<char> buffer)
        {
            var totalLength = bucketId.Length + 1 + rank.Length;
            if(buffer.Length< totalLength)
                throw new ArgumentException($"Buffer length {buffer.Length} is too small for the result length {totalLength}", nameof(buffer));

            var position = 0;
            bucketId.CopyTo(buffer[position..]);
            position += bucketId.Length;
            buffer[position++] = SEPARATOR;
            rank.CopyTo(buffer[position..]);

            return totalLength;
        }

        #endregion

        #region Public API - GetInitValue

        /// <summary>
        /// Gets the initial rank value for a given precision.
        /// </summary>
        /// <param name="precisionDigits">The number of digits for precision</param>
        /// <param name="bucketId">The bucket ID (default: "0")</param>
        /// <returns>The initial rank value with complete format {BucketId}~{Rank}</returns>
        public static string GetInitValue(int precisionDigits, string? bucketId = null)
        {
            if (precisionDigits <= 0)
                throw new ArgumentOutOfRangeException(nameof(precisionDigits), "PrecisionDigits must be greater than 0");
            if (string.IsNullOrEmpty(bucketId))
                bucketId = "0";

            // Calculate middle value in Base62
            // 'V' is the middle character (31 in 0-61 range)
            // For any precision, we fill with 'V' to maintain balanced precision
            Span<char> buffer = stackalloc char[precisionDigits];

            for (int i = 0; i < precisionDigits; i++)
            {
                buffer[i] = 'V';
            }

            var rank = new string(buffer);
            return $"{bucketId}~{rank}";
        }

        #endregion

        #region Public API - GenPrev (String Version)

        /// <summary>
        /// Generates a rank value before the given rank.
        /// </summary>
        /// <param name="rank">The current rank</param>
        /// <param name="precisionDigits">The precision digits</param>
        /// <param name="stepSize">The step size for decrement (default: 8)</param>
        /// <returns>A rank value before the input rank</returns>
        public static string GenPrev(string rank, int precisionDigits, int stepSize = DEFAULT_STEP_SIZE)
        {
            if (string.IsNullOrEmpty(rank))
                throw new ArgumentNullException(nameof(rank));

            Span<char> buffer = stackalloc char[rank.Length + 1];
            var length = GenPrev(rank.AsSpan(), precisionDigits, buffer, stepSize);
            return new string(buffer[..length]);
        }

        #endregion

        #region Public API - GenPrev (Span Version)

        /// <summary>
        /// Generates a rank value before the given rank (high-performance version).
        /// </summary>
        /// <param name="rank">The current rank</param>
        /// <param name="precisionDigits">The precision digits</param>
        /// <param name="buffer">The buffer to write the result</param>
        /// <param name="stepSize">The step size for decrement (default: 8)</param>
        /// <returns>The length of the written result</returns>
        public static int GenPrev(ReadOnlySpan<char> rank, int precisionDigits, Span<char> buffer, int stepSize = DEFAULT_STEP_SIZE)
        {
            if (rank.IsEmpty)
                throw new ArgumentNullException(nameof(rank));
            if (precisionDigits <= 0)
                throw new ArgumentOutOfRangeException(nameof(precisionDigits), "PrecisionDigits must be greater than 0");
            if (stepSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(stepSize), "StepSize must be greater than 0");

            // Parse the input rank
            Parse(rank, out var bucketId, out var rankValue);

            // Try to decrement
            Span<char> rankBuffer = stackalloc char[rankValue.Length + 1];
            var length = DecrementRank(rankValue, stepSize, precisionDigits, rankBuffer, out var underflow);

            // If underflow occurred, use Middle instead
            if (underflow)
            {
                var effectivePrecision = Math.Min(rankValue.Length, precisionDigits);
                var minValue = GetMinValue(effectivePrecision);
                length = CalculateMiddle(minValue, rankValue, rankBuffer);
                return Build(bucketId, rankBuffer[..length], buffer);
            }

            // Build the result
            var result = Build(bucketId, rankBuffer[..length], buffer);
            return result;
        }

        #endregion

        #region Public API - GenNext (String Version)

        /// <summary>
        /// Generates a rank value after the given rank.
        /// </summary>
        /// <param name="rank">The current rank</param>
        /// <param name="precisionDigits">The precision digits</param>
        /// <param name="stepSize">The step size for increment (default: 8)</param>
        /// <returns>A rank value after the input rank</returns>
        public static string GenNext(string rank, int precisionDigits, int stepSize = DEFAULT_STEP_SIZE)
        {
            if (string.IsNullOrEmpty(rank))
                throw new ArgumentNullException(nameof(rank));

            Span<char> buffer = stackalloc char[rank.Length + 1];
            var length = GenNext(rank.AsSpan(), precisionDigits, buffer, stepSize);
            return new string(buffer[..length]);
        }

        #endregion

        #region Public API - GenNext (Span Version)

        /// <summary>
        /// Generates a rank value after the given rank (high-performance version).
        /// </summary>
        /// <param name="rank">The current rank</param>
        /// <param name="precisionDigits">The precision digits</param>
        /// <param name="buffer">The buffer to write the result</param>
        /// <param name="stepSize">The step size for increment (default: 8)</param>
        /// <returns>The length of the written result</returns>
        public static int GenNext(ReadOnlySpan<char> rank, int precisionDigits, Span<char> buffer, int stepSize = DEFAULT_STEP_SIZE)
        {
            if (rank.IsEmpty)
                throw new ArgumentNullException(nameof(rank));
            if (precisionDigits <= 0)
                throw new ArgumentOutOfRangeException(nameof(precisionDigits), "PrecisionDigits must be greater than 0");
            if (stepSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(stepSize), "StepSize must be greater than 0");

            // Parse the input rank
            Parse(rank, out var bucketId, out var rankValue);

            // Try to increment
            Span<char> rankBuffer = stackalloc char[rankValue.Length + 2];
            var length = IncrementRank(rankValue, stepSize, precisionDigits, rankBuffer, out var overflow);

            // If overflow occurred, use Middle instead
            if (overflow)
            {
                var effectivePrecision = Math.Max(rankValue.Length, precisionDigits);
                var maxValue = GetMaxValue(effectivePrecision);
                length = CalculateMiddle(rankValue, maxValue, rankBuffer);
                return Build(bucketId, rankBuffer[..length], buffer);
            }

            // Build the result
            var result = Build(bucketId, rankBuffer[..length], buffer);
            return result;
        }

        #endregion

        #region Public API - Middle (String Version)

        /// <summary>
        /// Generates a rank value between two ranks.
        /// </summary>
        /// <param name="prev">The previous rank (must be less than next)</param>
        /// <param name="next">The next rank (must be greater than prev)</param>
        /// <param name="precisionDigits">The precision digits</param>
        /// <returns>A rank value between prev and next</returns>
        public static string Middle(string prev, string next, int precisionDigits)
        {
            if (string.IsNullOrEmpty(prev))
                throw new ArgumentNullException(nameof(prev));
            if (string.IsNullOrEmpty(next))
                throw new ArgumentNullException(nameof(next));

            Span<char> buffer = stackalloc char[Math.Max(prev.Length, next.Length) + 1];
            var length = Middle(prev.AsSpan(), next.AsSpan(), precisionDigits, buffer);
            return new string(buffer[..length]);
        }

        #endregion

        #region Public API - Middle (Span Version)

        /// <summary>
        /// Generates a rank value between two ranks (high-performance version).
        /// </summary>
        /// <param name="prev">The previous rank (must be less than next)</param>
        /// <param name="next">The next rank (must be greater than prev)</param>
        /// <param name="precisionDigits">The precision digits</param>
        /// <param name="buffer">The buffer to write the result</param>
        /// <returns>The length of the written result</returns>
        public static int Middle(ReadOnlySpan<char> prev, ReadOnlySpan<char> next, int precisionDigits, Span<char> buffer)
        {
            if (prev.IsEmpty)
                throw new ArgumentNullException(nameof(prev));
            if (next.IsEmpty)
                throw new ArgumentNullException(nameof(next));
            if (precisionDigits <= 0)
                throw new ArgumentOutOfRangeException(nameof(precisionDigits), "PrecisionDigits must be greater than 0");

            // Parse both ranks to get bucket info
            Parse(prev, out var prevBucketId, out var prevRank);
            Parse(next, out var nextBucketId, out var nextRank);

            // Validate that bucket IDs match
            if (!prevBucketId.SequenceEqual(nextBucketId))
                throw new ArgumentException("BucketId mismatch: prev and next must be in the same bucket");

            // Check if ranks are equal
            if (prevRank.SequenceEqual(nextRank))
                throw new ArgumentException("prev and next cannot be equal");

            // Check order
            if (CompareInternal(prevRank, nextRank) >= 0)
                throw new ArgumentException("prev must be less than next");

            // Calculate middle rank
            Span<char> rankBuffer = stackalloc char[Math.Max(prevRank.Length, nextRank.Length) + 1];
            var rankLength = CalculateMiddle(prevRank, nextRank, rankBuffer);

            // Build the result
            var fullRank = rankBuffer[..rankLength];
            var result = Build(prevBucketId, fullRank, buffer);
            return result;
        }

        #endregion

        #region Public API - Generate (String Version)

        /// <summary>
        /// Universal method for generating rank values.
        /// </summary>
        /// <param name="prev">The previous rank (null for GenPrev)</param>
        /// <param name="next">The next rank (null for GenNext)</param>
        /// <param name="precisionDigits">The precision digits</param>
        /// <param name="bucketId">The bucket ID (default: "0")</param>
        /// <param name="stepSize">The step size (default: 8)</param>
        /// <returns>A generated rank value</returns>
        public static string Generate(string? prev, string? next, int precisionDigits, string bucketId = "0", int stepSize = DEFAULT_STEP_SIZE)
        {
            if (prev == null && next == null)
                return GetInitValue(precisionDigits, bucketId);

            if (prev == null)
                return GenPrev(next!, precisionDigits, stepSize);

            if (next == null)
                return GenNext(prev, precisionDigits, stepSize);

            return Middle(prev, next, precisionDigits);
        }

        #endregion

        #region Public API - Generate (Span Version)

        /// <summary>
        /// Universal method for generating rank values (high-performance version).
        /// When both prev and next are null, generates initial value.
        /// When next is null, generates next value after prev.
        /// When prev is null, generates previous value before next.
        /// When both are provided, generates middle value.
        /// </summary>
        /// <param name="prev">The previous rank (empty for GenPrev)</param>
        /// <param name="next">The next rank (empty for GenNext)</param>
        /// <param name="precisionDigits">The precision digits</param>
        /// <param name="buffer">The buffer to write the result</param>
        /// <param name="bucketId">The bucket ID (default: "0")</param>
        /// <param name="stepSize">The step size (default: 8)</param>
        /// <returns>The length of the written result</returns>
        public static int Generate(ReadOnlySpan<char> prev, ReadOnlySpan<char> next, int precisionDigits, Span<char> buffer, string bucketId = "0", int stepSize = DEFAULT_STEP_SIZE)
        {
            if (prev.IsEmpty && next.IsEmpty)
            {
                var initValue = GetInitValue(precisionDigits, bucketId);
                initValue.AsSpan().CopyTo(buffer);
                return initValue.Length;
            }

            if (prev.IsEmpty)
                return GenPrev(next, precisionDigits, buffer, stepSize);

            if (next.IsEmpty)
                return GenNext(prev, precisionDigits, buffer, stepSize);

            return Middle(prev, next, precisionDigits, buffer);
        }

        #endregion

        #region Public API - Rebalance

        /// <summary>
        /// Calculates the starting rank for a rebalanced sequence.
        /// </summary>
        /// <param name="currentRank">The current rank to derive bucket from</param>
        /// <param name="count">Number of items to distribute</param>
        /// <param name="step">The step size between consecutive ranks</param>
        /// <param name="reverseOrder">If true, ranks should be generated in reverse (GenNext) direction</param>
        /// <returns>The starting rank for the rebalanced sequence</returns>
        public static string Rebalance(string currentRank, int count, out int step, out bool reverseOrder)
        {
            if (string.IsNullOrEmpty(currentRank))
                throw new ArgumentNullException(nameof(currentRank));
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be greater than 0");

            int precisionDigits = CalculatePrecisionDigits(count);
            long totalCapacity = (long)Math.Pow(BASE, precisionDigits);
            step = (int)(totalCapacity / (count + 1));

            Parse(currentRank.AsSpan(), out var bucketId, out var rankValue);

            long offset = (long)count * step / 2;

            Span<char> initBuffer = stackalloc char[precisionDigits];
            for (int i = 0; i < precisionDigits; i++)
                initBuffer[i] = 'V';

            Span<char> rankBuffer = stackalloc char[initBuffer.Length + 2];
            var length = DecrementRank(initBuffer, (int)offset, precisionDigits, rankBuffer, out var underflow);
            if (underflow)
            {
                var minVal = GetMinValue(precisionDigits);
                length = CalculateMiddle(minVal.AsSpan(), initBuffer, rankBuffer);
            }

            reverseOrder = false;
            return Build(bucketId, rankBuffer[..length]).ToString();
        }

        #endregion

        #region Private Helper Methods

        private static int CharToValue(char c)
        {
            if (c >= '0' && c <= '9')
                return c - '0';
            if (c >= 'A' && c <= 'Z')
                return c - 'A' + 10;
            if (c >= 'a' && c <= 'z')
                return c - 'a' + 36;

            throw new ArgumentException($"Invalid character '{c}' in rank", nameof(c));
        }

        private static char ValueToChar(int v)
        {
            if (v < 0 || v >= BASE)
                throw new ArgumentOutOfRangeException(nameof(v), $"Value {v} is out of Base62 range");

            return CHARSET[v];
        }

        private static int CompareInternal(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
        {
            // Compare from left to right using dictionary order (lexicographic order)
            var minLen = Math.Min(a.Length, b.Length);

            // Compare common prefix
            for (int i = 0; i < minLen; i++)
            {
                var valA = CharToValue(a[i]);
                var valB = CharToValue(b[i]);

                if (valA < valB)
                    return -1;
                if (valA > valB)
                    return 1;
            }

            // If all compared characters are equal, shorter string is smaller
            // This is standard dictionary order: "A" < "AA"
            return a.Length.CompareTo(b.Length);
        }

        private static bool IsNearMinValue(ReadOnlySpan<char> rank)
        {
            // Check if the rank is near the minimum value (starts with '0' and has small values)
            if (rank.Length == 0)
                return true;

            // Check first few characters
            var checkLength = Math.Min(3, rank.Length);
            var zeroCount = 0;

            for (int i = 0; i < checkLength; i++)
            {
                if (rank[i] == '0')
                    zeroCount++;
            }

            // If first 3 characters are '0', we're near minimum
            return zeroCount == checkLength;
        }

        private static bool IsNearMaxValue(ReadOnlySpan<char> rank)
        {
            // Check if the rank is near the maximum value (starts with 'z' and has large values)
            if (rank.Length == 0)
                return true;

            // Check first few characters
            var checkLength = Math.Min(3, rank.Length);
            var zCount = 0;

            for (int i = 0; i < checkLength; i++)
            {
                if (rank[i] == 'z')
                    zCount++;
            }

            // If first 3 characters are 'z', we're near maximum
            return zCount == checkLength;
        }

        private static string GetMinValue(int precisionDigits)
        {
            // Minimum value in Base62 with given precision
            return new string('0', precisionDigits);
        }

        private static string GetMaxValue(int precisionDigits)
        {
            // Maximum value in Base62 with given precision
            return new string('z', precisionDigits);
        }

        private static int DecrementRank(ReadOnlySpan<char> rank, int stepSize, int precisionDigits, Span<char> buffer, out bool underflow)
        {
            underflow = false;

            // Copy rank to buffer
            rank.CopyTo(buffer);
            var length = rank.Length;

            // Perform decrement from right to left
            var borrow = stepSize;
            for (int i = length - 1; i >= 0 && borrow > 0; i--)
            {
                var val = CharToValue(buffer[i]);
                var newVal = val - borrow;

                if (newVal < 0)
                {
                    borrow = (-newVal + BASE - 1) / BASE;
                    newVal = ((newVal % BASE) + BASE) % BASE;
                }
                else
                {
                    borrow = 0;
                }

                buffer[i] = ValueToChar(newVal);
            }

            // If we still have borrow after processing all digits, it's underflow
            if (borrow > 0)
            {
                underflow = true;
                return 0;
            }

            // Ensure result doesn't end with '0'
            if (buffer[length - 1] == '0' && length > precisionDigits)
            {
                length--;
            }

            return length;
        }

        private static int IncrementRank(ReadOnlySpan<char> rank, int stepSize, int precisionDigits, Span<char> buffer, out bool overflow)
        {
            overflow = false;

            // Copy rank to buffer
            rank.CopyTo(buffer);
            var length = rank.Length;

            // Perform increment from right to left
            var carry = stepSize;
            for (int i = length - 1; i >= 0 && carry > 0; i--)
            {
                var val = CharToValue(buffer[i]);
                var newVal = val + carry;

                if (newVal >= BASE)
                {
                    carry = newVal / BASE;
                    newVal = newVal % BASE;
                }
                else
                {
                    carry = 0;
                }

                buffer[i] = ValueToChar(newVal);
            }

            // If we still have carry, expanding would violate dictionary order
            // For example: 't' + 8 would give '11' which is less than 't'
            // So we report overflow and use Middle instead
            if (carry > 0)
            {
                overflow = true;
                return 0;
            }

            // Ensure result doesn't end with '0'
            if (buffer[length - 1] == '0' && length > precisionDigits)
            {
                length--;
            }

            return length;
        }

        private static int CalculateMiddle(ReadOnlySpan<char> prev, ReadOnlySpan<char> next, Span<char> buffer)
        {
            // Find the middle value between prev and next
            var maxLen = Math.Max(prev.Length, next.Length);

            // Pad to same length with zeros
            Span<char> a = stackalloc char[maxLen + 1];
            Span<char> b = stackalloc char[maxLen + 1];

            prev.CopyTo(a);
            next.CopyTo(b);

            // Fill remaining with zeros
            for (int i = prev.Length; i <= maxLen; i++)
                a[i] = '0';
            for (int i = next.Length; i <= maxLen; i++)
                b[i] = '0';

            // Find the first position where they differ
            var diffPos = -1;
            for (int i = 0; i < maxLen; i++)
            {
                if (a[i] != b[i])
                {
                    diffPos = i;
                    break;
                }
            }

            if (diffPos < 0)
            {
                // They are equal (should not happen due to earlier check)
                throw new ArgumentException("prev and next are equal");
            }

            // Get values at diff position
            var valA = CharToValue(a[diffPos]);
            var valB = CharToValue(b[diffPos]);
            int mid;

            // If they differ by more than 1, we can find a middle value directly
            if (valB - valA > 1)
            {
                mid = (valA + valB) / 2;
                a[..(diffPos + 1)].CopyTo(buffer);
                buffer[diffPos] = ValueToChar(mid);

                return diffPos + 1;
            }

            // If they differ by exactly 1, we need to look at the next position
            // Conceptually: prev at diffPos "borrows" from next position, so we add BASE (62)
            // mid = (prev[diffPos+1] + next[diffPos+1] + BASE) / 2
            diffPos++;
            var nextValA = CharToValue(a[diffPos]);
            var nextValB = CharToValue(b[diffPos]);
            mid = (nextValA + nextValB + BASE) / 2;

            // Copy the common prefix up to diffPos
            a[..diffPos].CopyTo(buffer);
            buffer[diffPos] = ValueToChar(mid);

            return diffPos + 1;
        }

        #endregion
    }
}
