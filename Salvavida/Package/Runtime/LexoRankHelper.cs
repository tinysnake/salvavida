using System;

namespace Salvavida
{
    /// <summary>
    /// Helper class for Base62 conversions used by LexoRank.
    /// </summary>
    internal static class LexoRankHelper
    {
        /// <summary>
        /// Base62 charset: 0-9, A-Z, a-z in ASCII order.
        /// </summary>
        public const string CHARSET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

        /// <summary>
        /// The base of the numeral system (62).
        /// </summary>
        public const int BASE = 62;

        /// <summary>
        /// The radix point character (decimal separator).
        /// </summary>
        public const char RADIX_POINT = ':';

        /// <summary>
        /// The positive sign character.
        /// </summary>
        public const char POSITIVE_CHAR = '+';

        /// <summary>
        /// The negative sign character.
        /// </summary>
        public const char NEGATIVE_CHAR = '-';

        /// <summary>
        /// The middle index in the charset (31, character 'V').
        /// </summary>
        public const int MIDDLE_INDEX = 31;

        /// <summary>
        /// The middle character ('V').
        /// </summary>
        public const char MIDDLE_CHAR = 'V';

        private static readonly int[] CharToIndexTable = CreateLookupTable();

        private static int[] CreateLookupTable()
        {
            var table = new int[128];
            for (int i = 0; i < CHARSET.Length; i++)
            {
                table[CHARSET[i]] = i;
            }
            return table;
        }

        /// <summary>
        /// Converts a character to its numeric index.
        /// </summary>
        public static int CharToIndex(char c)
        {
            if (c < 128)
                return CharToIndexTable[c];
            throw new ArgumentException($"Invalid character: '{c}'", nameof(c));
        }

        /// <summary>
        /// Converts a numeric index to its character representation.
        /// </summary>
        public static char IndexToChar(int index)
        {
            if (index < 0 || index >= BASE)
                throw new ArgumentOutOfRangeException(nameof(index), $"Index must be between 0 and {BASE - 1}");
            return CHARSET[index];
        }
    }
}
