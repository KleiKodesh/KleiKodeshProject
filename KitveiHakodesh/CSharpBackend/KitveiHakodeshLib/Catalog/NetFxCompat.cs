using System.Collections.Generic;

// ── net48 compatibility shims for the SHARED catalog source ──────────────────────────────
//
// Catalog\CatalogTocIndex.cs and friends are compiled from KitveiHakodeshService's Catalog
// folder (see the <Compile Link> items in the csproj). They are written against net10, and
// this file is what lets that exact source also compile on net48 — so the two legs stay one
// engine instead of two implementations that drift.
//
// The rule: patch the gap HERE, never by editing the shared files. Anything added to the
// shared source that needs a net48 equivalent gets its shim added below.
//
// The gaps, all mechanical:
//
//   1. The SQLite provider. The service uses Microsoft.Data.Sqlite; the hosted app already
//      ships System.Data.SQLite and has no reason to carry a second provider. The catalog
//      code only ever uses plain ADO.NET (CreateCommand / CommandText / AddWithValue /
//      ExecuteReader), which both providers implement identically, so a type alias is the
//      whole adaptation. The alias lives in each shared file's namespace via the global
//      alias below.
//
//   2. Dictionary.GetValueOrDefault, a .NET Core extension with no net48 counterpart.
//
//   3. The MessagePack attributes on CatalogTocHit. MessagePack is the service's wire format;
//      the hosted app replies in JSON over the WebView2 bridge and has no reason to take the
//      dependency. Declaring the two attributes locally lets the shared file keep its
//      annotations while they compile to inert markers here. IgnoreMember still carries real
//      meaning on this side — it marks the internal ranking fields that must NOT go on the
//      wire — so CatalogTocHandler mirrors it by projecting only the public fields by hand.

//   4. System.Index / System.Range. The shared source uses `x[^1]` and `x[..^1]`, which the
//      compiler lowers to these types. They ship in .NET Core but not in net48, and the
//      compiler is happy to use our own definitions as long as the shapes match — this is the
//      standard net48 polyfill, not a workaround.
//
//   5. The string.Join / EndsWith / Split overloads that take a char. net48 only has the
//      string-taking ones.
//
// Not every gap is shimmable from here: an extension method is only found through a using
// of its declaring namespace, so a shared file in a namespace this one does not cover
// cannot see these. DbContentStamp.cs (namespace .Common) is the case in point — it uses
// IndexOf(string, StringComparison) directly rather than Contains(string, StringComparison),
// which is .NET Core only. Prefer an overload both frameworks already have over reaching
// for a shim that would not bind.

namespace System
{
    /// <summary>net48 polyfill for the type behind the `^n` (index-from-end) operator.</summary>
    internal readonly struct Index : IEquatable<Index>
    {
        private readonly int _value;

        public Index(int value, bool fromEnd = false)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            // Encoded exactly as the BCL does: from-end indices are stored as ~value, so the
            // sign bit doubles as the FromEnd flag and the struct stays one int wide.
            _value = fromEnd ? ~value : value;
        }

        public int Value => _value < 0 ? ~_value : _value;
        public bool IsFromEnd => _value < 0;

        public static implicit operator Index(int value) => new Index(value);

        public int GetOffset(int length) => IsFromEnd ? length - Value : Value;

        public bool Equals(Index other) => _value == other._value;
        public override bool Equals(object obj) => obj is Index other && Equals(other);
        public override int GetHashCode() => _value;
    }

    /// <summary>net48 polyfill for the type behind the `a..b` range operator.</summary>
    internal readonly struct Range : IEquatable<Range>
    {
        public Index Start { get; }
        public Index End { get; }

        public Range(Index start, Index end)
        {
            Start = start;
            End = end;
        }

        public static Range StartAt(Index start) => new Range(start, new Index(0, fromEnd: true));
        public static Range EndAt(Index end) => new Range(new Index(0), end);
        public static Range All => new Range(new Index(0), new Index(0, fromEnd: true));

        public (int Offset, int Length) GetOffsetAndLength(int length)
        {
            int start = Start.GetOffset(length);
            int end = End.GetOffset(length);
            if ((uint)end > (uint)length || (uint)start > (uint)end)
                throw new ArgumentOutOfRangeException(nameof(length));
            return (start, end - start);
        }

        public bool Equals(Range other) => Start.Equals(other.Start) && End.Equals(other.End);
        public override bool Equals(object obj) => obj is Range other && Equals(other);
        public override int GetHashCode() => Start.GetHashCode() * 31 + End.GetHashCode();
    }
}

// NOTE: no System.Runtime.CompilerServices.RuntimeHelpers polyfill here. Declaring that type
// SHADOWS the real one for the whole assembly, and Helpers\FontsProvider.cs needs the BCL's
// OffsetToStringData from it. The shared source only ranges over strings (which the compiler
// lowers to Substring, needing no helper), never over arrays, so none is required.

namespace MessagePack
{
    /// <summary>net48 stand-in for MessagePack's attribute of the same name. Inert: nothing
    /// on this leg serialises with MessagePack.</summary>
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
    internal sealed class MessagePackObjectAttribute : System.Attribute
    {
        public MessagePackObjectAttribute(bool keyAsPropertyName = false)
        {
            KeyAsPropertyName = keyAsPropertyName;
        }

        public bool KeyAsPropertyName { get; }
    }

    /// <summary>net48 stand-in. See the note above: the hosted leg honours the intent by
    /// projecting the wire shape explicitly rather than by reflection.</summary>
    [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field)]
    internal sealed class IgnoreMemberAttribute : System.Attribute
    {
    }
}

namespace KitveiHakodeshService.Catalog
{
    /// <summary>
    /// The char-taking string overloads .NET Core added and net48 lacks. Extension methods
    /// cannot add static members, so string.Join(char, ...) is handled at its call sites by
    /// the shared source using the string form; only the instance methods are shimmed here.
    /// </summary>
    internal static class NetFxStringExtensions
    {
        /// <summary>net48 has EndsWith(string) but not EndsWith(char).</summary>
        public static bool EndsWith(this string s, char value)
            => s.Length > 0 && s[s.Length - 1] == value;

        /// <summary>net48 has StartsWith(string) but not StartsWith(char).</summary>
        public static bool StartsWith(this string s, char value)
            => s.Length > 0 && s[0] == value;

        /// <summary>net48 has Contains(string) but not Contains(char).</summary>
        public static bool Contains(this string s, char value) => s.IndexOf(value) >= 0;
    }

    internal static class NetFxDictionaryExtensions
    {
        /// <summary>
        /// net48 has no Dictionary.GetValueOrDefault. Matches the .NET Core semantics the
        /// shared source relies on: the value when present, otherwise default(TValue).
        /// </summary>
        public static TValue GetValueOrDefault<TKey, TValue>(
            this IReadOnlyDictionary<TKey, TValue> source, TKey key)
        {
            TValue value;
            return source.TryGetValue(key, out value) ? value : default(TValue);
        }

        /// <summary>As above, with an explicit fallback instead of default(TValue).</summary>
        public static TValue GetValueOrDefault<TKey, TValue>(
            this IReadOnlyDictionary<TKey, TValue> source, TKey key, TValue defaultValue)
        {
            TValue value;
            return source.TryGetValue(key, out value) ? value : defaultValue;
        }
    }
}
