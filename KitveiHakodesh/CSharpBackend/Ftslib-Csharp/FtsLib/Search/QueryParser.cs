using System;
using System.Collections.Generic;
using System.Text;

namespace FtsLib.Search
{
    /// <summary>
    /// Parses a raw query string into a <see cref="ParsedQuery"/>.
    ///
    /// Rules:
    ///   - Tokens are split on whitespace.
    ///   - A bare '|' token (after normalisation) acts as an OR separator: the tokens
    ///     immediately to its left and right are placed in the same OR group.
    ///     Consecutive '|'-separated tokens all belong to the same group.
    ///     Example: "word1 | word2 | word3 word4" → group{word1,word2,word3} AND word4.
    ///   - A token that contains '*' or '?' is a wildcard term.
    ///   - A token that ends with '~' or '~N' (N = 1–3) is a fuzzy term.
    ///     If a token has both a wildcard character and a fuzzy suffix, the wildcard
    ///     wins: the fuzzy suffix is stripped and the token is treated as a wildcard.
    ///   - All others are literals.
    ///   - Nikud (U+05B0–U+05C7) and cantillation (U+0591–U+05AF) are stripped from
    ///     every token so patterns match the normalised terms stored in the index.
    ///   - English letters are lowercased.
    ///   - Non-letter, non-'*', non-'~', non-digit characters are dropped.
    ///   - Empty tokens (after stripping) are ignored.
    ///   - A leading or trailing '|', or consecutive '||', is treated as if the
    ///     missing side is absent (the pipe is simply ignored).
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

            // Pre-process: pad every '|' character with spaces so that "א|ב" and
            // "א | ב" are treated identically.  This lets users omit spaces around
            // the pipe without affecting the tokenisation logic below.
            query = query.Replace("|", " | ");

            // First pass: tokenise into SubPatterns and bare OR-pipe markers.
            // A raw token of exactly "|" (after whitespace split) is an OR separator.
            // We accumulate SubPatterns into a pending OR group; when we see a
            // non-pipe token after a non-pipe token (i.e. no pipe between them),
            // we flush the pending group and start a new one.

            // pendingGroup: the sub-patterns collected so far for the current OR group.
            var pendingGroup = new List<SubPattern>();
            bool lastWasPipe = false; // was the previous meaningful token a '|'?

            foreach (var raw in query.Split(new[] { ' ', '\t', '\r', '\n' },
                                            StringSplitOptions.RemoveEmptyEntries))
            {
                // Detect a bare pipe token (before normalisation, since '|' is dropped
                // by Normalise).  A raw token is a pipe separator when it consists
                // entirely of '|' characters.
                bool isPipe = IsPipeToken(raw);

                if (isPipe)
                {
                    lastWasPipe = true;
                    continue;
                }

                // Parse the token into a SubPattern.
                SubPattern? sp = ParseToken(raw);
                if (sp == null) continue; // empty after normalisation

                if (!lastWasPipe && pendingGroup.Count > 0)
                {
                    // No pipe between the previous token and this one → flush the
                    // accumulated OR group as a completed QueryGroup.
                    groups.Add(new QueryGroup(pendingGroup));
                    pendingGroup = new List<SubPattern>();
                }

                pendingGroup.Add(sp.Value);
                lastWasPipe = false;
            }

            // Flush the last pending group.
            if (pendingGroup.Count > 0)
                groups.Add(new QueryGroup(pendingGroup));

            return new ParsedQuery(groups);
        }

        // ── Token parsing ─────────────────────────────────────────────

        /// <summary>
        /// Returns true when <paramref name="raw"/> is a pipe separator token
        /// (consists entirely of '|' characters).
        /// </summary>
        private static bool IsPipeToken(string raw)
        {
            foreach (char c in raw)
                if (c != '|') return false;
            return true;
        }

        /// <summary>
        /// Parses a single whitespace-delimited token into a <see cref="SubPattern"/>.
        /// Returns null when the token is empty after normalisation.
        /// </summary>
        private static SubPattern? ParseToken(string raw)
        {
            // ── Strip grammar expansion markers (%) ───────────────────
            // '%' at the start means expand prefixes; '%' at the end means expand
            // suffixes. These are stripped before normalisation so Normalise never
            // sees them (it would drop them anyway, but being explicit is cleaner).
            // '*' overrides '%': if the token also contains '*', it is treated as a
            // plain wildcard and the grammar flags are ignored.
            bool grammarPrefix = raw.StartsWith("%");
            bool grammarSuffix = raw.EndsWith("%");
            if (grammarPrefix || grammarSuffix)
                raw = raw.Trim('%');

            // Split off a trailing fuzzy suffix (~  or ~N) before normalising,
            // because '~' and digits would otherwise be dropped by Normalise.
            string tokenText = raw;
            bool   isFuzzy   = false;
            int    fuzzyDist = 1;

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
                    tokenText = prefix;
                }
                // else: '~' is not a fuzzy marker here — treat as noise (dropped by Normalise)
            }

            string normalised = Normalise(tokenText);
            if (normalised.Length == 0) return null;

            bool isWildcard = normalised.IndexOf('*') >= 0 || normalised.IndexOf('?') >= 0;

            // Fuzzy + wildcard on the same token is not supported.
            // The wildcard wins: the fuzzy suffix is silently stripped.
            if (isFuzzy && isWildcard) isFuzzy = false;

            // '*' overrides '%': if the token is a wildcard the grammar flags are ignored.
            if (isWildcard)
                grammarPrefix = grammarSuffix = false;

            return new SubPattern(normalised, isWildcard, isFuzzy, fuzzyDist,
                                  grammarExpandPrefixes: grammarPrefix,
                                  grammarExpandSuffixes: grammarSuffix);
        }

        // ── Normalisation ─────────────────────────────────────────────

        /// <summary>
        /// Strips nikud/cantillation, lowercases ASCII, drops non-letter non-'*' chars.
        /// Preserves '*' and '?' so the caller can detect wildcard position.
        /// </summary>
        private static string Normalise(string token)
        {
            var sb = new StringBuilder(token.Length);
            foreach (char c in token)
            {
                // Strip nikud (U+05B0–U+05C7) and cantillation (U+0591–U+05AF)
                if (c >= '\u0591' && c <= '\u05C7') continue;

                if (c == '*') { sb.Append('*'); continue; }
                if (c == '?') { sb.Append('?'); continue; }

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
    /// Each group is an OR set of one or more sub-patterns; across groups the
    /// semantics are AND.  A single-sub-pattern group is the common case (a plain
    /// literal, wildcard, or fuzzy term).
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
    /// One AND slot in the query, containing one or more OR alternatives.
    ///
    /// The common case is a single sub-pattern (a plain literal, wildcard, or fuzzy
    /// term).  When the user writes "word1 | word2", both sub-patterns are placed in
    /// the same group and their expansions are merged into one OR set before the
    /// intersection search.
    /// </summary>
    internal sealed class QueryGroup
    {
        /// <summary>
        /// The OR alternatives for this AND slot.
        /// Always contains at least one element.
        /// </summary>
        public readonly IReadOnlyList<SubPattern> Alternatives;

        public QueryGroup(List<SubPattern> alternatives)
        {
            Alternatives = alternatives;
        }

        // ── Convenience accessors for the single-alternative case ─────

        /// <summary>
        /// True when this group contains exactly one alternative.
        /// </summary>
        public bool IsSingle => Alternatives.Count == 1;

        /// <summary>
        /// The normalised token text of the first (and usually only) alternative.
        /// For wildcards this still contains '*' characters.
        /// For fuzzy terms the '~' suffix has been removed.
        /// </summary>
        public string Pattern => Alternatives[0].Pattern;

        /// <summary>True when the first alternative is a wildcard.</summary>
        public bool IsWildcard => Alternatives[0].IsWildcard;

        /// <summary>True when the first alternative is a fuzzy term.</summary>
        public bool IsFuzzy => Alternatives[0].IsFuzzy;

        /// <summary>Fuzzy distance of the first alternative.</summary>
        public int FuzzyDistance => Alternatives[0].FuzzyDistance;

        /// <summary>True when the first alternative is a grammar-expansion term.</summary>
        public bool IsGrammar => Alternatives[0].IsGrammar;
    }

    /// <summary>
    /// One OR alternative within a <see cref="QueryGroup"/>.
    /// </summary>
    internal struct SubPattern
    {
        /// <summary>
        /// The normalised token text.
        /// For wildcards this still contains '*' characters.
        /// For fuzzy terms the '~' suffix has been removed.
        /// For grammar terms the '%' markers have been removed.
        /// </summary>
        public readonly string Pattern;

        /// <summary>True when <see cref="Pattern"/> contains at least one '*' or '?'.</summary>
        public readonly bool IsWildcard;

        /// <summary>True when this token is a fuzzy term (ends with '~' or '~N').</summary>
        public readonly bool IsFuzzy;

        /// <summary>
        /// Maximum edit distance for fuzzy matching (1–3).
        /// Meaningful only when <see cref="IsFuzzy"/> is true.
        /// </summary>
        public readonly int FuzzyDistance;

        /// <summary>True when this token has a leading '%' — expand grammatical prefixes.</summary>
        public readonly bool GrammarExpandPrefixes;

        /// <summary>True when this token has a trailing '%' — expand grammatical suffixes.</summary>
        public readonly bool GrammarExpandSuffixes;

        /// <summary>True when this is a grammar-expansion token (at least one % marker).</summary>
        public bool IsGrammar => GrammarExpandPrefixes || GrammarExpandSuffixes;

        public SubPattern(string pattern, bool isWildcard,
                          bool isFuzzy = false, int fuzzyDistance = 1,
                          bool grammarExpandPrefixes = false, bool grammarExpandSuffixes = false)
        {
            Pattern                = pattern;
            IsWildcard             = isWildcard;
            IsFuzzy                = isFuzzy;
            FuzzyDistance          = fuzzyDistance;
            GrammarExpandPrefixes  = grammarExpandPrefixes;
            GrammarExpandSuffixes  = grammarExpandSuffixes;
        }
    }
}
