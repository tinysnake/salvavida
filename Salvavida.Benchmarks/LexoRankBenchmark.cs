using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Configs;
using System.Runtime.CompilerServices;

namespace Salvavida.Benchmarks
{
    [SimpleJob(RuntimeMoniker.Net90)]
    [MemoryDiagnoser]
    public class LexoRankBenchmark
    {
        private const int DEFAULT_PRECISION_DIGITS = 2;
        private const string TEST_RANK = "0~VV~VV";
        private const string TEST_NEXT = "0~VV~VW";
        private const string TEST_PREV = "0~VV~V0";

        [Benchmark(Baseline = true)]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Parse_StringVersion()
        {
            var span = TEST_RANK.AsSpan();
            LexoRank.Parse(span, out var bucketId, out var chunkId, out var rank);
            // Prevent optimization
            if (bucketId.Length == 0) throw new Exception();
        }

        [Benchmark]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Parse_SpanVersion()
        {
            var span = TEST_RANK.AsSpan();
            LexoRank.Parse(span, out var bucketId, out var chunkId, out var rank);
            if (bucketId.Length == 0) throw new Exception();
        }

        [Benchmark]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Build_SpanVersion()
        {
            Span<char> buffer = stackalloc char[20];
            var result = LexoRank.Build("0".AsSpan(), "VV".AsSpan(), "VVV".AsSpan(), buffer);
            if (result == 0) throw new Exception();
        }

        [Benchmark]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void GenPrev_StringVersion()
        {
            var result = LexoRank.GenPrev(TEST_RANK, DEFAULT_PRECISION_DIGITS);
            if (result.Length == 0) throw new Exception();
        }

        [Benchmark]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void GenPrev_SpanVersion()
        {
            Span<char> buffer = stackalloc char[20];
            var length = LexoRank.GenPrev(TEST_RANK.AsSpan(), DEFAULT_PRECISION_DIGITS, buffer);
            if (length == 0) throw new Exception();
        }

        [Benchmark]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void GenNext_StringVersion()
        {
            var result = LexoRank.GenNext(TEST_RANK, DEFAULT_PRECISION_DIGITS);
            if (result.Length == 0) throw new Exception();
        }

        [Benchmark]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void GenNext_SpanVersion()
        {
            Span<char> buffer = stackalloc char[20];
            var length = LexoRank.GenNext(TEST_RANK.AsSpan(), DEFAULT_PRECISION_DIGITS, buffer);
            if (length == 0) throw new Exception();
        }

        [Benchmark]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Middle_StringVersion()
        {
            var result = LexoRank.Middle(TEST_PREV, TEST_NEXT, DEFAULT_PRECISION_DIGITS);
            if (result.Length == 0) throw new Exception();
        }

        [Benchmark]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Middle_SpanVersion()
        {
            Span<char> buffer = stackalloc char[20];
            var length = LexoRank.Middle(TEST_PREV.AsSpan(), TEST_NEXT.AsSpan(), DEFAULT_PRECISION_DIGITS, buffer);
            if (length == 0) throw new Exception();
        }

        [Benchmark]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Generate_StringVersion()
        {
            var result = LexoRank.Generate(TEST_PREV, TEST_NEXT, DEFAULT_PRECISION_DIGITS);
            if (result.Length == 0) throw new Exception();
        }

        [Benchmark]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Generate_SpanVersion()
        {
            Span<char> buffer = stackalloc char[20];
            var length = LexoRank.Generate(TEST_PREV.AsSpan(), TEST_NEXT.AsSpan(), DEFAULT_PRECISION_DIGITS, buffer);
            if (length == 0) throw new Exception();
        }

        [Benchmark]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void GetInitValue_StringVersion()
        {
            var result = LexoRank.GetInitValue(DEFAULT_PRECISION_DIGITS);
            if (result.Length == 0) throw new Exception();
        }

        [Benchmark]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Compare_SpanVersion()
        {
            var result = LexoRank.Compare(TEST_PREV.AsSpan(), TEST_NEXT.AsSpan());
            if (result == 0) throw new Exception();
        }

        [Benchmark]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void CalculatePrecisionDigits()
        {
            var result = LexoRank.CalculatePrecisionDigits(3844);
            if (result == 0) throw new Exception();
        }
    }
}