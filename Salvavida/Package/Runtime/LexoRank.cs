using System;
using System.Runtime.CompilerServices;

namespace Salvavida
{
    public static class LexoRank
    {
        public const string CHARSET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        public const string SEPARATOR = "~";
        public const int DEFAULT_BUCKET_SIZE = 100;
        public const int REBALANCE_LENGTH_THRESHOLD = 4;

        private const int MAX_RESULT_LENGTH = 256;
        private static readonly int MIDDLE_INDEX = CHARSET.Length / 2; // 31
        private static readonly char MIDDLE_CHAR = CHARSET[MIDDLE_INDEX]; // 'V'
        public static readonly string DEFAULT_PREFIX = MIDDLE_CHAR.ToString(); // "V"

        private static readonly byte[] CharToIndexTable = CreateLookupTable();

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

        private static byte[] CreateLookupTable()
        {
            var table = new byte[128];
            for (int i = 0; i < CHARSET.Length; i++)
            {
                table[CHARSET[i]] = (byte)i;
            }
            return table;
        }

        /// <summary>
        /// Computes a rank string between prev and next.
        /// </summary>
        public static string Between(string? prev, string? next, int bucketSize = DEFAULT_BUCKET_SIZE)
        {
            var buffer = GetThreadBuffer();
            int len = BetweenSpanCore(prev, next, buffer, bucketSize);
            return new string(buffer[..len]);
        }

        /// <summary>
        /// High-performance version that writes result to destination buffer.
        /// </summary>
        /// <param name="prev">Previous rank (can be null)</param>
        /// <param name="next">Next rank (can be null)</param>
        /// <param name="destination">Buffer to write result to</param>
        /// <param name="bucketSize">Expected number of insertions</param>
        /// <returns>Number of characters written to destination</returns>
        public static int BetweenSpan(string? prev, string? next, Span<char> destination, int bucketSize = DEFAULT_BUCKET_SIZE)
        {
            return BetweenSpanCore(prev, next, destination, bucketSize);
        }

        private static int BetweenSpanCore(string? prev, string? next, Span<char> destination, int bucketSize)
        {
            if (prev == null && next == null)
                return ComputeInitialRankSpan(destination, bucketSize);

            if (prev != null && next != null && string.Equals(prev, next, StringComparison.Ordinal))
                throw new ArgumentException("prev and next cannot be equal", nameof(next));

            int pSepIdx = prev != null ? FindSeparator(prev) : -1;
            int nSepIdx = next != null ? FindSeparator(next) : -1;

            ReadOnlySpan<char> prevPrefix = pSepIdx >= 0 ? prev.AsSpan(0, pSepIdx) : ReadOnlySpan<char>.Empty;
            ReadOnlySpan<char> nextPrefix = nSepIdx >= 0 ? next.AsSpan(0, nSepIdx) : ReadOnlySpan<char>.Empty;
            ReadOnlySpan<char> prevLexo = pSepIdx >= 0 ? prev.AsSpan(pSepIdx + 1) : (prev != null ? prev.AsSpan() : ReadOnlySpan<char>.Empty);
            ReadOnlySpan<char> nextLexo = nSepIdx >= 0 ? next.AsSpan(nSepIdx + 1) : (next != null ? next.AsSpan() : ReadOnlySpan<char>.Empty);

            ReadOnlySpan<char> prefix = !prevPrefix.IsEmpty ? prevPrefix : (!nextPrefix.IsEmpty ? nextPrefix : DEFAULT_PREFIX);

            // Cross-bucket
            if (!prevPrefix.IsEmpty && !nextPrefix.IsEmpty && !SequenceEqual(prevPrefix, nextPrefix))
            {
                return BuildAppendSpan(destination, prefix, prevLexo, MIDDLE_CHAR);
            }

            // Both lexo parts empty
            if (prevLexo.IsEmpty && nextLexo.IsEmpty)
            {
                if (prev == null)
                    throw new ArgumentException($"Invalid next rank: '{next}' - lexo part cannot be empty", nameof(next));
                throw new ArgumentException($"Invalid prev rank: '{prev}' - lexo part cannot be empty", nameof(prev));
            }

            // prev is null, next has lexo
            if (prevLexo.IsEmpty)
                return ComputeBeforeSpan(destination, nextLexo, prefix, bucketSize);

            // next is null, prev has lexo
            if (nextLexo.IsEmpty)
                return ComputeAfterSpan(destination, prevLexo, prefix, bucketSize);

            return MidpointSpan(destination, prefix, prevLexo, nextLexo);
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
                throw new ArgumentException($"Destination buffer too small. Need at least {totalLen} characters.", nameof(destination));

            int pos = 0;
            DEFAULT_PREFIX.AsSpan().CopyTo(destination);
            pos += DEFAULT_PREFIX.Length;
            destination[pos++] = SEPARATOR[0];
            for (int i = 0; i < minLen; i++)
                destination[pos++] = MIDDLE_CHAR;

            return pos;
        }

        private static int ComputeBeforeSpan(Span<char> destination, ReadOnlySpan<char> nextLexo, ReadOnlySpan<char> prefix, int bucketSize)
        {
            int targetLen = MinLengthForCount(bucketSize);

            if (nextLexo.Length <= targetLen)
            {
                int firstCharIdx = CharToIndexFast(nextLexo[0]);
                if (firstCharIdx > 0)
                {
                    int midIdx = firstCharIdx / 2;
                    return BuildSingleCharSpan(destination, prefix, CHARSET[midIdx]);
                }

                int prefixIdx = CharToIndexFast(prefix[0]);
                if (prefixIdx > 0)
                {
                    // Use a lower prefix to get a rank that sorts before next
                    int totalLen = prefix.Length + 2;
                    if (destination.Length < totalLen)
                        throw new ArgumentException($"Destination buffer too small. Need at least {totalLen} characters.", nameof(destination));

                    int pos = 0;
                    destination[pos++] = CHARSET[prefixIdx - 1];
                    for (int i = 1; i < prefix.Length; i++)
                        destination[pos++] = prefix[i];
                    destination[pos++] = SEPARATOR[0];
                    destination[pos++] = MIDDLE_CHAR;
                    return pos;
                }

                throw new InvalidOperationException("Cannot generate rank before the minimum possible rank. Consider rebalancing.");
            }

            int len = Math.Min(targetLen, nextLexo.Length - 1);
            int resultLen = prefix.Length + 1 + len;
            if (destination.Length < resultLen)
                throw new ArgumentException($"Destination buffer too small. Need at least {resultLen} characters.", nameof(destination));

            int p = 0;
            prefix.CopyTo(destination.Slice(p));
            p += prefix.Length;
            destination[p++] = SEPARATOR[0];

            for (int i = 0; i < len; i++)
            {
                int charIdx = CharToIndexFast(nextLexo[i]);
                destination[p++] = charIdx > 0 ? CHARSET[charIdx / 2] : CHARSET[0];
            }
            return p;
        }

        private static int ComputeAfterSpan(Span<char> destination, ReadOnlySpan<char> prevLexo, ReadOnlySpan<char> prefix, int bucketSize)
        {
            int targetLen = MinLengthForCount(bucketSize);

            if (prevLexo.Length < targetLen)
            {
                int totalLen = prefix.Length + 1 + targetLen;
                if (destination.Length < totalLen)
                    throw new ArgumentException($"Destination buffer too small. Need at least {totalLen} characters.", nameof(destination));

                int pos = 0;
                prefix.CopyTo(destination.Slice(pos));
                pos += prefix.Length;
                destination[pos++] = SEPARATOR[0];
                prevLexo.CopyTo(destination.Slice(pos));
                pos += prevLexo.Length;
                for (int i = prevLexo.Length; i < targetLen; i++)
                    destination[pos++] = MIDDLE_CHAR;
                return pos;
            }

            return BuildAppendSpan(destination, prefix, prevLexo, MIDDLE_CHAR);
        }

        private static int MidpointSpan(Span<char> destination, ReadOnlySpan<char> prefix, ReadOnlySpan<char> prevLexo, ReadOnlySpan<char> nextLexo)
        {
            int diffIdx = -1;
            int prevVal = 0, nextVal = 0;

            int maxLen = Math.Max(prevLexo.Length, nextLexo.Length);
            for (int i = 0; i < maxLen; i++)
            {
                char pc = i < prevLexo.Length ? prevLexo[i] : CHARSET[0];
                char nc = i < nextLexo.Length ? nextLexo[i] : CHARSET[0];
                if (pc != nc)
                {
                    diffIdx = i;
                    prevVal = CharToIndexFast(pc);
                    nextVal = CharToIndexFast(nc);
                    break;
                }
            }

            if (diffIdx < 0)
            {
                diffIdx = prevLexo.Length;
                prevVal = 0;
                nextVal = CharToIndexFast(nextLexo[diffIdx]);
            }

            if (prevVal >= nextVal)
                throw new ArgumentException($"prev must be less than next (at position {diffIdx}: {prevVal} >= {nextVal})");

            int midVal = (prevVal + nextVal) / 2;
            if (midVal > prevVal)
            {
                int resultLen = prefix.Length + 1 + diffIdx + 1;
                if (destination.Length < resultLen)
                    throw new ArgumentException($"Destination buffer too small. Need at least {resultLen} characters.", nameof(destination));

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

        private static int MidpointAfterDiffSpan(Span<char> destination, ReadOnlySpan<char> prefix, ReadOnlySpan<char> prevLexo, ReadOnlySpan<char> nextLexo, int diffIdx)
        {
            // 当 diffIdx == prevLexo.Length 时，prevLexo 是 nextLexo 的前缀
            // 此时 nextLexo[diffIdx] 已经确定了比 prevLexo 的补位 '0' 大
            // 后续位置的上限应该是 nextLexo 的对应字符，而不是 'z'
            bool prevIsPrefix = (diffIdx == prevLexo.Length);
            
            for (int i = diffIdx + 1; ; i++)
            {
                int prevCharVal = (i < prevLexo.Length) ? CharToIndexFast(prevLexo[i]) : 0;
                // 如果 prevLexo 是 nextLexo 的前缀，后续位置用 nextLexo 的字符作为上限
                // 否则用 'z' 作为上限（因为 nextLexo 在 diffIdx 已经大于 prevLexo）
                int nextCharVal = prevIsPrefix && i < nextLexo.Length 
                    ? CharToIndexFast(nextLexo[i]) 
                    : (prevIsPrefix ? 0 : CHARSET.Length - 1);

                int midVal = (prevCharVal + nextCharVal) / 2;

                if (midVal > prevCharVal)
                {
                    int resultLen = prefix.Length + 1 + i + 1;
                    if (destination.Length < resultLen)
                        throw new ArgumentException($"Destination buffer too small. Need at least {resultLen} characters.", nameof(destination));

                    int pos = 0;
                    prefix.CopyTo(destination.Slice(pos));
                    pos += prefix.Length;
                    destination[pos++] = SEPARATOR[0];
                    for (int j = 0; j < i && j < prevLexo.Length; j++)
                        destination[pos++] = prevLexo[j];
                    destination[pos++] = CHARSET[midVal];
                    return pos;
                }
                
                // 如果 prevIsPrefix 且 i 超出 nextLexo 长度，后续都补 '0'
                // midVal = 0 == prevCharVal，继续循环
                // 但这会无限循环！需要在 prevIsPrefix 时特殊处理
                if (prevIsPrefix && i >= nextLexo.Length)
                {
                    // prevLexo 和 nextLexo 在 i 位置都补 '0'，无法取中间值
                    // 此时应该在 prevLexo 末尾追加 '0'，形成 prevLexo + "0"
                    // 这会比 prevLexo 大，比 nextLexo 小（因为 nextLexo 在 diffIdx 有 '1' > '0'）
                    // 但这样追加 '0' 后，结果会是 prevLexo + "0"，即 "a" + "0" = "a0"
                    // 这正是我们要的！
                    int resultLen = prefix.Length + 1 + prevLexo.Length + 1;
                    if (destination.Length < resultLen)
                        throw new ArgumentException($"Destination buffer too small. Need at least {resultLen} characters.", nameof(destination));
                    
                    int pos = 0;
                    prefix.CopyTo(destination.Slice(pos));
                    pos += prefix.Length;
                    destination[pos++] = SEPARATOR[0];
                    prevLexo.CopyTo(destination.Slice(pos));
                    pos += prevLexo.Length;
                    destination[pos++] = CHARSET[0]; // 追加 '0'
                    return pos;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CharToIndexFast(char c)
        {
            return c < 128 ? CharToIndexTable[c] : 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int BuildAppendSpan(Span<char> destination, ReadOnlySpan<char> prefix, ReadOnlySpan<char> lexo, char appendChar)
        {
            int totalLen = prefix.Length + 1 + lexo.Length + 1;
            if (destination.Length < totalLen)
                throw new ArgumentException($"Destination buffer too small. Need at least {totalLen} characters.", nameof(destination));

            int pos = 0;
            prefix.CopyTo(destination.Slice(pos));
            pos += prefix.Length;
            destination[pos++] = SEPARATOR[0];
            lexo.CopyTo(destination.Slice(pos));
            pos += lexo.Length;
            destination[pos++] = appendChar;
            return pos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int BuildSingleCharSpan(Span<char> destination, ReadOnlySpan<char> prefix, char c)
        {
            int totalLen = prefix.Length + 2;
            if (destination.Length < totalLen)
                throw new ArgumentException($"Destination buffer too small. Need at least {totalLen} characters.", nameof(destination));

            int pos = 0;
            prefix.CopyTo(destination.Slice(pos));
            pos += prefix.Length;
            destination[pos++] = SEPARATOR[0];
            destination[pos++] = c;
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
