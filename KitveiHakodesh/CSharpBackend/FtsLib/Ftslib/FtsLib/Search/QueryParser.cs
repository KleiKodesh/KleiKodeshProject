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
    ///   - Geresh (U+05F3), gershayim (U+05F4), and ASCII double-quote (") are stripped
    ///     so that Hebrew abbreviations like רשב"א are treated as a single token (רשבא),
    ///     matching the way the indexer stores them.
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
            // ── Grammar markers '%' ───────────────────────────────────
            // Detect leading '%' (prefix expansion) and trailing '%' (suffix expansion)
            // before any other processing.  '%' is otherwise dropped by Normalise.
            bool grammarPrefix = raw.Length > 0 && raw[0] == '%';
            bool grammarSuffix = raw.Length > 0 && raw[raw.Length - 1] == '%';

            // Strip the '%' markers so they don't interfere with fuzzy/wildcard detection.
            string rawStripped = raw;
            if (grammarPrefix) rawStripped = rawStripped.Substring(1);
            if (grammarSuffix && rawStripped.Length > 0)
                rawStripped = rawStripped.Substring(0, rawStripped.Length - 1);

            // ── Fuzzy suffix '~' / '~N' ───────────────────────────────
            // Split off a trailing fuzzy suffix before normalising, because '~' and
            // digits would otherwise be dropped by Normalise.
            string tokenText = rawStripped;
            bool   isFuzzy   = false;
            int    fuzzyDist = 1;

            int tildePos = rawStripped.LastIndexOf('~');
            if (tildePos >= 0)
            {
                string fuzzySuffix = rawStripped.Substring(tildePos + 1); // "" or "1"/"2"/"3"
                string fuzzyPrefix = rawStripped.Substring(0, tildePos);

                // suffix must be empty or a single digit 1–9
                if (fuzzySuffix.Length == 0 || (fuzzySuffix.Length == 1 && fuzzySuffix[0] >= '1' && fuzzySuffix[0] <= '9'))
                {
                    isFuzzy   = true;
                    fuzzyDist = fuzzySuffix.Length == 0 ? 1 : (fuzzySuffix[0] - '0');
                    if (fuzzyDist > FuzzyExpander.MaxAllowedDistance)
                        fuzzyDist = FuzzyExpander.MaxAllowedDistance;
                    tokenText = fuzzyPrefix;
                }
                // else: '~' is not a fuzzy marker here — treat as noise (dropped by Normalise)
            }

            string normalised = Normalise(tokenText);
            if (normalised.Length == 0) return null;

            bool isWildcard = normalised.IndexOf('*') >= 0 || normalised.IndexOf('?') >= 0;

            // Fuzzy + wildcard on the same token is not supported.
            // The wildcard wins: the fuzzy suffix is silently stripped.
            if (isFuzzy && isWildcard) isFuzzy = false;

            // '*' overrides '%': if the token is a star-wildcard, grammar expansion
            // is suppressed (the LIKE query already covers prefix/suffix/infix).
            // '?' is compatible with '%': grammar expansion still applies.
            bool starWildcard = normalised.IndexOf('*') >= 0;
            if (starWildcard)
            {
                grammarPrefix = false;
                grammarSuffix = false;
            }

            return new SubPattern(normalised, isWildcard, isFuzzy, fuzzyDist,
                                  grammarPrefix, grammarSuffix);
        }

        // ── Normalisation ─────────────────────────────────────────────

        /// <summary>
        /// Strips nikud/cantillation, lowercases ASCII, drops non-letter non-'*' non-'?' chars.
        /// Preserves '*' and '?' so the caller can detect wildcard position.
        /// '%' markers are stripped by <see cref="ParseToken"/> before this is called,
        /// so they never reach here.
        /// Geresh (U+05F3), gershayim (U+05F4), and ASCII double-quote (U+0022) are
        /// silently dropped so that abbreviations like רשב"א normalise to רשבא,
        /// matching the single token produced by the indexer.
        /// </summary>
        private static string Normalise(string token)
        {
            var sb = new StringBuilder(token.Length);
            foreach (char c in token)
            {
                // Strip nikud (U+05B0–U+05C7) and cantillation (U+0591–U+05AF)
                if (c >= '\u0591' && c <= '\u05C7') continue;

                // Strip geresh / gershayim — Hebrew abbreviation marks.
                // Also strip ASCII " which users type when they lack the Unicode key.
                if (c == '\u05F3' || c == '\u05F4' || c == '"') continue;

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
        /// For grammar terms the '%' markers have been removed.
        /// </summary>
        public string Pattern => Alternatives[0].Pattern;

        /// <summary>True when the first alternative is a wildcard.</summary>
        public bool IsWildcard => Alternatives[0].IsWildcard;

        /// <summary>True when the first alternative is a fuzzy term.</summary>
        public bool IsFuzzy => Alternatives[0].IsFuzzy;

        /// <summary>Fuzzy distance of the first alternative.</summary>
        public int FuzzyDistance => Alternatives[0].FuzzyDistance;

        /// <summary>True when the first alternative requests grammar prefix expansion.</summary>
        public bool GrammarPrefix => Alternatives[0].GrammarPrefix;

        /// <summary>True when the first alternative requests grammar suffix expansion.</summary>
        public bool GrammarSuffix => Alternatives[0].GrammarSuffix;

        /// <summary>True when the first alternative requests any grammar expansion.</summary>
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

        /// <summary>
        /// True when this token should be expanded with grammatical prefixes
        /// (קידומות דקדוקיות). Set when the original token had a leading '%'.
        /// Ignored when <see cref="IsWildcard"/> is true (i.e. token contains '*').
        /// </summary>
        public readonly bool GrammarPrefix;

        /// <summary>
        /// True when this token should be expanded with grammatical suffixes
        /// (סיומות דקדוקיות). Set when the original token had a trailing '%'.
        /// Ignored when <see cref="IsWildcard"/> is true (i.e. token contains '*').
        /// </summary>
        public readonly bool GrammarSuffix;

        /// <summary>
        /// True when any grammar expansion is requested
        /// (<see cref="GrammarPrefix"/> or <see cref="GrammarSuffix"/>).
        /// </summary>
        public bool IsGrammar => GrammarPrefix || GrammarSuffix;

        public SubPattern(string pattern, bool isWildcard,
                          bool isFuzzy = false, int fuzzyDistance = 1,
                          bool grammarPrefix = false, bool grammarSuffix = false)
        {
            Pattern       = pattern;
            IsWildcard    = isWildcard;
            IsFuzzy       = isFuzzy;
            FuzzyDistance = fuzzyDistance;
            GrammarPrefix = grammarPrefix;
            GrammarSuffix = grammarSuffix;
        }
    }
}
