using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Salvavida;

namespace Salvavida.Benchmarks
{
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, iterationCount: 10)]
    public class LexoRankBenchmark
    {
        // Scenario 1: Initial rank (null, null)
        [Benchmark(Description = "Initial rank (null, null)")]
        public string Between_Initial()
        {
            return LexoRank.Between(null, null);
        }

        [Benchmark(Description = "Initial rank - Span API")]
        public int BetweenSpan_Initial()
        {
            Span<char> buf = stackalloc char[32];
            return LexoRank.BetweenSpan(null, null, buf);
        }

        // Scenario 2: Insert at beginning
        [Benchmark(Description = "Insert at beginning")]
        public string Between_AtBeginning()
        {
            return LexoRank.Between(null, "V~VV");
        }

        [Benchmark(Description = "Insert at beginning - Span API")]
        public int BetweenSpan_AtBeginning()
        {
            Span<char> buf = stackalloc char[32];
            return LexoRank.BetweenSpan(null, "V~VV", buf);
        }

        // Scenario 3: Insert at end
        [Benchmark(Description = "Insert at end")]
        public string Between_AtEnd()
        {
            return LexoRank.Between("V~VV", null);
        }

        [Benchmark(Description = "Insert at end - Span API")]
        public int BetweenSpan_AtEnd()
        {
            Span<char> buf = stackalloc char[32];
            return LexoRank.BetweenSpan("V~VV", null, buf);
        }

        // Scenario 4: Midpoint (common case)
        [Benchmark(Description = "Midpoint (V~a, V~z)")]
        public string Between_Midpoint()
        {
            return LexoRank.Between("V~a", "V~z");
        }

        [Benchmark(Description = "Midpoint - Span API")]
        public int BetweenSpan_Midpoint()
        {
            Span<char> buf = stackalloc char[32];
            return LexoRank.BetweenSpan("V~a", "V~z", buf);
        }

        // Scenario 5: Adjacent chars (precision expansion)
        [Benchmark(Description = "Adjacent chars (V~V, V~W)")]
        public string Between_Adjacent()
        {
            return LexoRank.Between("V~V", "V~W");
        }

        [Benchmark(Description = "Adjacent chars - Span API")]
        public int BetweenSpan_Adjacent()
        {
            Span<char> buf = stackalloc char[32];
            return LexoRank.BetweenSpan("V~V", "V~W", buf);
        }

        // Scenario 6: Bulk operation (simulating InsertRange)
        [Benchmark(Description = "Bulk 100 inserts")]
        public string[] Between_Bulk100()
        {
            var results = new string[100];
            string? prev = null;
            string next = "V~VV";
            for (int i = 0; i < 100; i++)
            {
                results[i] = LexoRank.Between(prev, next);
                prev = results[i];
            }
            return results;
        }

        [Benchmark(Description = "Bulk 100 inserts - Span API")]
        public int BetweenSpan_Bulk100()
        {
            Span<char> buf = stackalloc char[64];
            string? prev = null;
            string next = "V~VV";
            int totalLen = 0;
            for (int i = 0; i < 100; i++)
            {
                int len = LexoRank.BetweenSpan(prev, next, buf);
                totalLen += len;
                prev = new string(buf.Slice(0, len));
            }
            return totalLen;
        }
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<LexoRankBenchmark>();
        }
    }
}
