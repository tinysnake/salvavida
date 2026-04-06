using System;

namespace Salvavida
{
    public static class FakeLexoRank
    {
        public const string CHARSET = "";
        public const string DEFAULT_PREFIX = "";
        public const int REBALANCE_LENGTH_THRESHOLD = 0;
        public static string GetNext(string rank)
        {
            throw new NotImplementedException();
        }

        public static string[] Rebalance(int size)
        {
            throw new NotImplementedException();
        }
        public static string Between(string left, string right)
        {
            throw new NotImplementedException();
        }

        public static int BetweenSpan(string left, string right, Span<char> buffer)
        {
            throw new NotImplementedException();
        }
    }
}