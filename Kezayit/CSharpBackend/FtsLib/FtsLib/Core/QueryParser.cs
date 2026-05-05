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
    ///   - A token that contains '*' is a wildcard term.
    ///   - A token that ends with '~' or '~N' (N = 1–3) is a fuzzy term.
    ///     Wildcard and fuzzy cannot be combined on the same token.
    ///   - All others are literals.
    ///   - Nikud (U+05B0–U+05C7) and cantillation (U+0591–U+05AF) are stripped from
    ///     every token so patterns match the normalised terms stored in the index.
    ///   - English letters are lowercased.
    ///   - Non-letter, non-'*', non-'~', non-digit characters are dropped.
    ///   - Empty tokens (after stripping) are ignored.
    /// </summary>
    internal static class QueryParser
    {
        /// <summary>
        /// Parses <paramref name="query"/> and returns a <see cref="ParsedQuery"/>
        /// whose <see cref="ParsedQuery.Groups"/> list is ready for expansion.
        /// </summary>
        public static ParsedQuery Parse(string query)
        {
            var groups = new List<QueryGroup>();

            if (string.IsNullOrWhiteSpace(query))
                return new ParsedQuery(groups);

            foreach (var raw in query.Split(new[] { ' ', '\t', '\r', '\n' },
                                            StringSplitOptions.RemoveEmptyEntries))
            {
                // Split off a trailing fuzzy suffix (~  or ~N) before normalising,
                // because '~' and digits would otherwise be dropped by Normalise.
                string tokenText  = raw;
                bool   isFuzzy    = false;
                int    fuzzyDist  = 1;

                int tildePos = raw.LastIndexOf('~');
                if (tildePos >= 0)
                {
                    string suffix = raw.Substring(tildePos + 1); // "" or "1"/"2"/"3"
                    string prefix = raw.Substring(0, tildePos);

                    // suffix must be empty or a single digit 1–9
                    if (suffix.Length == 0 || (suffix.Length == 1 && suffix[0] >= '1' && suffix[0] <= '9'))
                    {
                        isFuzzy   = true;
                        fuzzyDist = suffix.Length == 0 ? 1 : (suffix[0] - '0');
                        if (fuzzyDist > FuzzyExpander.MaxAllowedDistance)
                            fuzzyDist = FuzzyExpander.MaxAllowedDistance;
                        tokenText = prefix; // normalise only the word part
                    }
                    // else: '~' is not a fuzzy marker here — treat as noise (dropped by Normalise)
                }

                string normalised = Normalise(tokenText);
                if (normalised.Length == 0) continue;

                bool isWildcard = normalised.IndexOf('*') >= 0;

                // Fuzzy + wildcard on the same token is not supported
                if (isFuzzy && isWildcard) isFuzzy = false;

                groups.Add(new QueryGroup(normalised, isWildcard, isFuzzy, fuzzyDist));
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
    /// Each group is either a literal term, a wildcard pattern, or a fuzzy term
    /// that must be expanded before searching.
    /// </summary>
    internal sealed class ParsedQuery
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
    internal sealed class QueryGroup
    {
        /// <summary>
        /// The normalised token text.
        /// For wildcards this still contains '*' characters.
        /// For fuzzy terms the '~' suffix has been removed.
        /// </summary>
        public readonly string Pattern;

        /// <summary>True when <see cref="Pattern"/> contains at least one '*'.</summary>
        public readonly bool IsWildcard;

        /// <summary>True when this token is a fuzzy term (ends with '~' or '~N').</summary>
        public readonly bool IsFuzzy;

        /// <summary>
        /// Maximum edit distance for fuzzy matching (1–3).
        /// Meaningful only when <see cref="IsFuzzy"/> is true.
        /// </summary>
        public readonly int FuzzyDistance;

        public QueryGroup(string pattern, bool isWildcard,
                          bool isFuzzy = false, int fuzzyDistance = 1)
        {
            Pattern       = pattern;
            IsWildcard    = isWildcard;
            IsFuzzy       = isFuzzy;
            FuzzyDistance = fuzzyDistance;
        }
    }
}
