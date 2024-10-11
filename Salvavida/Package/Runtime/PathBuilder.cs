using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Salvavida
{
    public class PathBuilder : IEqualityComparer<PathBuilder>
    {
        public enum Type
        {
            Property,
            Collection
        }

        public readonly struct PathSegment
        {
            public PathSegment(Type type, int start, int end)
            {
                this.type = type;
                this.start = start;
                this.end = end;
            }

            public readonly Type type;
            public readonly int start;
            public readonly int end;

            public Range Range => new(start, end);

            public void Deconstruct(out Type type, out Range range)
            {
                type = this.type;
                range = Range;
            }
        }

        public static int defaultMaxLength = 1024;
        public static char propertySaperator = '.';

        internal PathBuilder() : this(defaultMaxLength)
        {
        }

        internal PathBuilder(int maxLength)
        {
            _memory = new char[maxLength];
            _segments = new List<PathSegment>();
        }

        private readonly Memory<char> _memory;
        private readonly List<PathSegment> _segments;
        private int _length = 0;

        public bool IsEmpty => _length == 0;

        public int SegmentCount => _segments.Count;

        public int MaxLength => _memory.Length;

        public PathBuilder Push(ReadOnlySpan<char> segment, Type type)
        {
            if (segment.IsEmpty)
                throw new ArgumentNullException(nameof(segment));
            var span = _memory.Span;
            if (_segments.Count > 0)
            {
                span[_length] = GetSeperateChar(type);
                _length++;
            }
            segment.CopyTo(span[_length..]);
            var start = _length;
            _length += segment.Length;
            var end = _length;
            _segments.Add(new PathSegment(type, start, end));
            return this;
        }

        private char GetSeperateChar(Type type) => type switch
        {
            Type.Collection => '/',
            Type.Property => propertySaperator,
            _ => throw new NotSupportedException()
        };

        public PathBuilder Push(string segment, Type type) => Push(segment.AsSpan(), type);


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PathBuilder Pop()
        {
            PopAsSpan();
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<char> PopAsSpan() => PopAsSpan(out _);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<char> PopAsSpan(out Type type)
        {
            if (_segments.Count <= 0)
                throw new InvalidOperationException("no more segments");
            Range range;
            (type, range) = _segments[^1];
            _segments.RemoveAt(_segments.Count - 1);
            var segment = _memory.Span[range];
#if DEBUG
            if (range.Start.IsFromEnd)
                throw new NotSupportedException();
#endif
            _length = _segments.Count == 0 ? 0 : range.Start.Value - 1;
            return segment;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string PopString() => PopAsSpan().ToString();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string PopString(out Type type) => PopAsSpan(out type).ToString();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<char> AsSpan() => _memory.Span[.._length];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString() => _memory[.._length].ToString();

        public ReadOnlySpan<char> GetSegmentSpan(Index segmentIndex)
        {
            var index = segmentIndex.Value;
            if (segmentIndex.IsFromEnd)
                index = _segments.Count - index;
            if ((uint)index >= (uint)_segments.Count)
                throw new ArgumentOutOfRangeException(nameof(segmentIndex));
            return _memory.Span[_segments[index].Range];
        }

        public string GetSegmentString(Index segmentIndex) => GetSegmentSpan(segmentIndex).ToString();

        public void CopyTo(PathBuilder target)
        {
            if (!target.IsEmpty)
                throw new ArgumentException("target PathBuilder is not empty, cannot copy");
            if (target.MaxLength < MaxLength)
                throw new ArgumentException("target PathBuilder.MaxLength is shorter");

            _memory.Span.CopyTo(target._memory.Span);
            target._segments.Clear();
            target._segments.AddRange(_segments);
            target._length = _length;
        }

        public void Clear()
        {
            _segments.Clear();
            _length = 0;
        }

        public bool Equals(PathBuilder x, PathBuilder y)
        {
            return x._memory.Span[..x._length].SequenceEqual(y._memory.Span[..y._length]);
        }

        public int GetHashCode(PathBuilder obj)
        {
            var hashCode = new HashCode();
            foreach (var ch in _memory.Span[.._length])
            {
                hashCode.Add(ch);
            }
            return hashCode.ToHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj is PathBuilder pb)
                return Equals(this, pb);
            return false;
        }

        public override int GetHashCode()
        {
            return GetHashCode(this);
        }
    }
}
