using System;
using System.Buffers;

namespace Salvavida
{
    public static class LexoRank
    {
        // Base62 charset: 0-9 (48-57), A-Z (65-90), a-z (97-122) in ASCII order
        public const string CHARSET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        public const int REBALANCE_LENGTH_THRESHOLD = 4;
        public const string DEFAULT_PREFIX = "A";
        public const string INITIAL_RANK = DEFAULT_PREFIX + "~m";

        // Precomputed charset index lookup for O(1) decode (avoids string.IndexOf per char)
        private static readonly byte[] s_charsetIndex = BuildCharsetIndex();

        private static byte[] BuildCharsetIndex()
        {
            var map = new byte[128]; // ASCII range
            for (byte i = 0; i < CHARSET.Length; i++)
                map[CHARSET[i]] = i;
            return map;
        }

        public static string Between(string? prev, string? next)
        {
            if (prev == null && next == null)
                return INITIAL_RANK;

            if (prev != null && next != null && string.Equals(prev, next, StringComparison.Ordinal))
                throw new ArgumentException("prev and next cannot be equal", nameof(next));

            // Parse prefix and lexoValue using Span to avoid allocations
            ReadOnlySpan<char> prevSpan = prev.AsSpan();
            ReadOnlySpan<char> nextSpan = next.AsSpan();

            int pSepIdx = prev != null ? prev.IndexOf('~') : -1;
            int nSepIdx = next != null ? next.IndexOf('~') : -1;

            ReadOnlySpan<char> prevPrefix = pSepIdx >= 0 ? prevSpan.Slice(0, pSepIdx) : ReadOnlySpan<char>.Empty;
            ReadOnlySpan<char> nextPrefix = nSepIdx >= 0 ? nextSpan.Slice(0, nSepIdx) : ReadOnlySpan<char>.Empty;
            ReadOnlySpan<char> prevLexo = pSepIdx >= 0 ? prevSpan.Slice(pSepIdx + 1) : prevSpan;
            ReadOnlySpan<char> nextLexo = nSepIdx >= 0 ? nextSpan.Slice(nSepIdx + 1) : nextSpan;

            // Determine prefix: use prev's if available, else next's, else default
            ReadOnlySpan<char> prefix = !prevPrefix.IsEmpty ? prevPrefix : (!nextPrefix.IsEmpty ? nextPrefix : DEFAULT_PREFIX);

            // Cross-bucket: different prefixes
            if (!prevPrefix.IsEmpty && !nextPrefix.IsEmpty && !prevPrefix.SequenceEqual(nextPrefix))
            {
                // Step 2b: append minimum char to prev's lexoValue
                int newLen = prevLexo.Length + 1;
                var buffer = newLen <= 64 ? stackalloc char[64] : new char[newLen];
                prevLexo.CopyTo(buffer);
                buffer[prevLexo.Length] = '0';
                int totalLen = prefix.Length + 1 + newLen;
                var resultBuffer = totalLen <= 128 ? stackalloc char[128] : new char[totalLen];
                prefix.CopyTo(resultBuffer);
                resultBuffer[prefix.Length] = '~';
                buffer.Slice(0, newLen).CopyTo(resultBuffer.Slice(prefix.Length + 1));
                return resultBuffer.Slice(0, totalLen).ToString();
            }

            // Same bucket: compute midpoint using big integer arithmetic on stack
            int maxLen = Math.Max(prevLexo.Length, nextLexo.Length) + 1;
            Span<byte> prevBytes = maxLen <= 64 ? stackalloc byte[64] : new byte[maxLen];
            Span<byte> nextBytes = maxLen <= 64 ? stackalloc byte[64] : new byte[maxLen];
            Span<byte> sumBytes = maxLen <= 64 ? stackalloc byte[64] : new byte[maxLen];

            int prevLen = prevLexo.IsEmpty ? 0 : DecodeToBytes(prevLexo, prevBytes);
            int nextLen = nextLexo.IsEmpty ? 0 : DecodeToBytes(nextLexo, nextBytes);

            // Handle null boundaries
            if (prevLexo.IsEmpty && nextLexo.IsEmpty)
                return INITIAL_RANK;
            if (prevLexo.IsEmpty)
            {
                // Insert at beginning: next / 2
                DivideByTwo(nextBytes, nextLen, sumBytes, out int sl);
                return EncodeResult(prefix, sumBytes, sl);
            }
            if (nextLexo.IsEmpty)
            {
                // Insert at end: prev + 1
                AddOne(prevBytes, prevLen, sumBytes, out int sl2);
                return EncodeResult(prefix, sumBytes, sl2);
            }

            // Midpoint: (prev + next) / 2
            int totalLen2 = AddBigNumbers(prevBytes, prevLen, nextBytes, nextLen, sumBytes);
            int midLen = DivideByTwo(sumBytes, totalLen2, sumBytes, out int _);

            // Check if adjacent (midpoint == prev means no room)
            if (midLen == prevLen && sumBytes.Slice(0, midLen).SequenceEqual(prevBytes.Slice(0, prevLen)))
            {
                // Precision expansion: append minimum char
                int newLen2 = prevLexo.Length + 1;
                var buf = newLen2 <= 64 ? stackalloc char[64] : new char[newLen2];
                prevLexo.CopyTo(buf);
                buf[prevLexo.Length] = '0';
                int totalLen3 = prefix.Length + 1 + newLen2;
                var res = totalLen3 <= 128 ? stackalloc char[128] : new char[totalLen3];
                prefix.CopyTo(res);
                res[prefix.Length] = '~';
                buf.Slice(0, newLen2).CopyTo(res.Slice(prefix.Length + 1));
                return res.Slice(0, totalLen3).ToString();
            }

            return EncodeResult(prefix, sumBytes, midLen);
        }

        /// <summary>
        /// Decode Base62 string to big-endian byte array on the given span.
        /// Returns the number of bytes written.
        /// </summary>
        private static int DecodeToBytes(ReadOnlySpan<char> lexo, Span<byte> outBytes)
        {
            int byteLen = 1;
            outBytes[0] = 0;
            foreach (char c in lexo)
            {
                byte digit = s_charsetIndex[c];
                // Multiply current number by 62, add digit
                byte carry = 0;
                for (int i = byteLen - 1; i >= 0; i--)
                {
                    int val = outBytes[i] * 62 + carry;
                    outBytes[i] = (byte)(val % 256);
                    carry = (byte)(val / 256);
                }
                // Add digit
                int pos = byteLen - 1;
                int sum = outBytes[pos] + digit;
                outBytes[pos] = (byte)(sum % 256);
                carry = (byte)(sum / 256);
                while (carry > 0 && pos > 0)
                {
                    pos--;
                    sum = outBytes[pos] + carry;
                    outBytes[pos] = (byte)(sum % 256);
                    carry = (byte)(sum / 256);
                }
                if (carry > 0 && byteLen < outBytes.Length)
                {
                    // Shift right to make room
                    for (int i = byteLen; i > 0; i--)
                        outBytes[i] = outBytes[i - 1];
                    outBytes[0] = carry;
                    byteLen++;
                }
            }
            return byteLen;
        }

        /// <summary>
        /// Add two big-endian big integers. Returns total bytes written to outBytes.
        /// </summary>
        private static int AddBigNumbers(ReadOnlySpan<byte> a, int aLen, ReadOnlySpan<byte> b, int bLen, Span<byte> outBytes)
        {
            int maxLen = Math.Max(aLen, bLen);
            byte carry = 0;
            for (int i = 0; i < maxLen; i++)
            {
                int av = i < aLen ? a[aLen - 1 - i] : 0;
                int bv = i < bLen ? b[bLen - 1 - i] : 0;
                int sum = av + bv + carry;
                outBytes[maxLen - 1 - i] = (byte)(sum % 256);
                carry = (byte)(sum / 256);
            }
            if (carry > 0)
            {
                for (int i = maxLen; i > 0; i--)
                    outBytes[i] = outBytes[i - 1];
                outBytes[0] = carry;
                return maxLen + 1;
            }
            return maxLen;
        }

        private static int DivideByTwo(ReadOnlySpan<byte> num, int len, Span<byte> outBytes, out int outLen)
        {
            byte remainder = 0;
            for (int i = 0; i < len; i++)
            {
                int val = (remainder << 8) | num[i];
                outBytes[i] = (byte)(val / 2);
                remainder = (byte)(val % 2);
            }
            outLen = len;
            // Trim leading zeros
            while (outLen > 1 && outBytes[0] == 0)
            {
                outBytes.Slice(1, outLen - 1).CopyTo(outBytes);
                outLen--;
            }
            return outLen;
        }

        private static void AddOne(ReadOnlySpan<byte> num, int len, Span<byte> outBytes, out int outLen)
        {
            num.Slice(0, len).CopyTo(outBytes);
            outLen = len;
            byte carry = 1;
            for (int i = outLen - 1; i >= 0 && carry > 0; i--)
            {
                int sum = outBytes[i] + carry;
                outBytes[i] = (byte)(sum % 256);
                carry = (byte)(sum / 256);
            }
            if (carry > 0 && outLen < outBytes.Length)
            {
                for (int i = outLen; i > 0; i--)
                    outBytes[i] = outBytes[i - 1];
                outBytes[0] = carry;
                outLen++;
            }
        }

        private static string EncodeResult(ReadOnlySpan<char> prefix, ReadOnlySpan<byte> bytes, int len)
        {
            // Encode big-endian bytes to Base62 using ArrayPool for the output buffer
            int maxChars = len * 2 + 2; // upper bound
            char[]? rented = maxChars > 128 ? ArrayPool<char>.Shared.Rent(maxChars) : null;
            Span<char> buf = rented != null ? rented.AsSpan(0, maxChars) : stackalloc char[128];

            // Copy bytes to a working span
            Span<byte> work = len <= 64 ? stackalloc byte[64] : new byte[len];
            bytes.Slice(0, len).CopyTo(work);

            int pos = 0;
            while (true)
            {
                // Check if all zeros
                bool allZero = true;
                for (int i = 0; i < len; i++)
                {
                    if (work[i] != 0) { allZero = false; break; }
                }
                if (allZero) break;

                // Divide by 62, collect remainder
                byte remainder = 0;
                for (int i = 0; i < len; i++)
                {
                    int val = (remainder << 8) | work[i];
                    work[i] = (byte)(val / 62);
                    remainder = (byte)(val % 62);
                }
                buf[pos++] = CHARSET[remainder];
            }

            if (pos == 0) buf[pos++] = CHARSET[0];

            // Build final: prefix + "~" + reversed base62
            int totalLen = prefix.Length + 1 + pos;
            char[]? rented2 = totalLen > 128 ? ArrayPool<char>.Shared.Rent(totalLen) : null;
            Span<char> result = rented2 != null ? rented2.AsSpan(0, totalLen) : stackalloc char[128];
            prefix.CopyTo(result);
            result[prefix.Length] = '~';
            // Reverse the base62 chars
            for (int i = 0; i < pos; i++)
                result[prefix.Length + 1 + i] = buf[pos - 1 - i];

            string str = result.Slice(0, totalLen).ToString();
            if (rented != null) ArrayPool<char>.Shared.Return(rented);
            if (rented2 != null) ArrayPool<char>.Shared.Return(rented2);
            return str;
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
            var range = (long)Math.Pow(62, Math.Max(1, MinLengthForCount(count))) - 1;
            var step = range / (count + 1);
            for (int i = 0; i < count; i++)
            {
                result[i] = Base62Encode(step * (i + 1));
            }
            return result;
        }

        public static int Compare(string a, string b) => string.CompareOrdinal(a, b);

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

        // Simple long-based encode for Rebalance (short ranks, no big number needed)
        private static string Base62Encode(long value)
        {
            if (value == 0) return CHARSET.Substring(0, 1);
            Span<char> buf = stackalloc char[16];
            int pos = 16;
            while (value > 0)
            {
                buf[--pos] = CHARSET[(int)(value % 62)];
                value /= 62;
            }
            return buf.Slice(pos, 16 - pos).ToString();
        }
    }
}
