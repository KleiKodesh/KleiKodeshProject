// The net48 leg's compile-time polyfills, in one place. One source tree compiles on both
// legs (project rule 6), and the catalog code — ported verbatim because it is dense with
// Hebrew source text that must not be rewritten — uses two things net48's BCL predates:
//
//   System.Index / System.Range     the compiler LOWERS `s[^1]` and `s[a..b]` itself
//                                   (Substring for strings, Count arithmetic for lists);
//                                   all it needs is these two types to exist. .NET Core 3+
//                                   ships them; net48 does not.
//   GetValueOrDefault               dictionary extension from CollectionExtensions, which
//                                   net48 also lacks. Declared in the BCL's own namespace,
//                                   exactly where the real one lives, so call sites compile
//                                   identically on both legs.
//
// Everything here is `internal` and `#if NETFRAMEWORK`, so the modern leg keeps the
// framework's own types and nothing leaks out of this assembly.

#if NETFRAMEWORK
namespace System
{
    using System.Collections.Generic;

    /// <summary>Compile-time stand-in for System.Index (the `^` operator's type).
    /// Mirrors the runtime implementation: from-end indexes are stored as complements.</summary>
    internal readonly struct Index : IEquatable<Index>
    {
        private readonly int _value;

        public Index(int value, bool fromEnd = false)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            _value = fromEnd ? ~value : value;
        }

        private Index(int value) => _value = value;

        public static Index Start => new Index(0);
        public static Index End => new Index(~0);

        public static Index FromStart(int value)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            return new Index(value);
        }

        public static Index FromEnd(int value)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            return new Index(~value);
        }

        public int Value => _value < 0 ? ~_value : _value;
        public bool IsFromEnd => _value < 0;

        public int GetOffset(int length)
        {
            int offset = _value;
            if (IsFromEnd) offset += length + 1;
            return offset;
        }

        public static implicit operator Index(int value) => FromStart(value);

        public bool Equals(Index other) => _value == other._value;
        public override bool Equals(object? value) => value is Index other && Equals(other);
        public override int GetHashCode() => _value;
        public override string ToString() => IsFromEnd ? "^" + Value : Value.ToString();
    }

    /// <summary>Compile-time stand-in for System.Range (the `..` operator's type).</summary>
    internal readonly struct Range : IEquatable<Range>
    {
        public Index Start { get; }
        public Index End { get; }

        public Range(Index start, Index end)
        {
            Start = start;
            End = end;
        }

        public static Range StartAt(Index start) => new Range(start, Index.End);
        public static Range EndAt(Index end) => new Range(Index.Start, end);
        public static Range All => new Range(Index.Start, Index.End);

        public (int Offset, int Length) GetOffsetAndLength(int length)
        {
            int start = Start.GetOffset(length);
            int end = End.GetOffset(length);
            if ((uint)end > (uint)length || (uint)start > (uint)end)
                throw new ArgumentOutOfRangeException(nameof(length));
            return (start, end - start);
        }

        public bool Equals(Range other) => other.Start.Equals(Start) && other.End.Equals(End);
        public override bool Equals(object? value) => value is Range other && Equals(other);
        public override int GetHashCode() => (Start.GetHashCode() * 31) + End.GetHashCode();
        public override string ToString() => Start + ".." + End;
    }
}

namespace System.Collections.Generic
{
    /// <summary>The two GetValueOrDefault overloads net48's BCL lacks. On IReadOnlyDictionary,
    /// like the real CollectionExtensions, so Dictionary call sites bind through the interface.</summary>
    internal static class NetFrameworkDictionaryPolyfills
    {
        public static TValue? GetValueOrDefault<TKey, TValue>(
            this IReadOnlyDictionary<TKey, TValue> dictionary, TKey key)
            => dictionary.TryGetValue(key, out TValue? value) ? value : default;

        public static TValue GetValueOrDefault<TKey, TValue>(
            this IReadOnlyDictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue)
            => dictionary.TryGetValue(key, out TValue? value) ? value : defaultValue;
    }
}
#endif
