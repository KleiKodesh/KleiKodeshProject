using System;
using System.Collections.Generic;
using System.Text;

namespace FtsLib.Core
{
    /// <summary>
    /// Parses a raw query string into a <see cref="ParsedQuery"/>.
    ///
    /// Rules:
    ///   - Tokens are split on whitespace.
    ///   - A token that contains '*' is a wildcard term; all others are literals.
    ///   - Nikud (U+05B0–U+05C7) and cantillation (U+0591–U+05AF) are stripped from
    ///     every token so patterns match the normalised terms stored in the index.
    ///   - English letters are lowercased.
    ///   - Non-letter, non-'*' characters are dropped.
    ///   - Empty tokens (after stripping) are ignored.
    /// </summary>
    public static class QueryParser
    {
        /// <summary>
        /// Parses <paramref name="query"/> and returns a <see cref="ParsedQuery"/>
        /// whose <see cref="ParsedQuery.Groups"/> list is ready for wildcard expansion.
        /// </summary>
        public static ParsedQuery Parse(string query)
        {
            var groups = new List<QueryGroup>();

            if (string.IsNullOrWhiteSpace(query))
                return new ParsedQuery(groups);

            foreach (var raw in query.Split(new[] { ' ', '\t', '\r', '\n' },
                                            StringSplitOptions.RemoveEmptyEntries))
            {
                string normalised = Normalise(raw);
                if (normalised.Length == 0) continue;

                bool isWildcard = normalised.IndexOf('*') >= 0;
                groups.Add(new QueryGroup(normalised, isWildcard));
            }

            return new ParsedQuery(groups);
        }

        // ── Normalisation ─────────────────────────────────────────────

        /// <summary>
        /// Strips nikud/cantillation, lowercases ASCII, drops non-letter non-'*' chars.
        /// Preserves '*' so the caller can detect wildcard position.
        /// </summary>
        private static string Normalise(string token)
        {
            var sb = new StringBuilder(token.Length);
            foreach (char c in token)
            {
                // Strip nikud (U+05B0–U+05C7) and cantillation (U+0591–U+05AF)
                if (c >= '\u0591' && c <= '\u05C7') continue;

                if (c == '*') { sb.Append('*'); continue; }

                // Hebrew letters U+05D0–U+05EA
                if (c >= '\u05D0' && c <= '\u05EA') { sb.Append(c); continue; }

                // ASCII letters — lowercase
                if (c >= 'A' && c <= 'Z') { sb.Append((char)(c | 32)); continue; }
                if (c >= 'a' && c <= 'z') { sb.Append(c); continue; }

                // Everything else (digits, punctuation, etc.) is dropped
            }
            return sb.ToString();
        }
    }

    // ── Value types ───────────────────────────────────────────────────

    /// <summary>
    /// The result of parsing a query: an ordered list of groups.
    /// Each group is either a single literal term or a wildcard pattern
    /// that must be expanded before searching.
    /// </summary>
    public sealed class ParsedQuery
    {
        public readonly IReadOnlyList<QueryGroup> Groups;

        public ParsedQuery(List<QueryGroup> groups)
        {
            Groups = groups;
        }

        public bool IsEmpty => Groups.Count == 0;
    }

    /// <summary>
    /// One token from the query.
    /// </summary>
    public sealed class QueryGroup
    {
        /// <summary>
        /// The normalised token text.
        /// For wildcards this still contains '*' characters.
        /// </summary>
        public readonly string Pattern;

        /// <summary>True when <see cref="Pattern"/> contains at least one '*'.</summary>
        public readonly bool IsWildcard;

        public QueryGroup(string pattern, bool isWildcard)
        {
            Pattern    = pattern;
            IsWildcard = isWildcard;
        }
    }
}
