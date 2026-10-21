using System;
using System.Collections.Generic;
using FtsLib.Tokenization;

namespace FtsLib.Search
{
    /// <summary>
    /// Parses a raw query string into a <see cref="ParsedQuery"/>: FtsLib's operators over
    /// Otzaria's word rule (<see cref="WordRule"/>, the rule the indexer uses).
    ///
    /// Rules:
    ///   - A paired reading (X) [Y] keeps only Y (Otzaria's keep_second_of_paired_readings).
    ///   - Tokens are split on whitespace.
    ///   - A bare '|' token acts as an OR separator: the tokens immediately to its left and right
    ///     are placed in the same OR group. Consecutive '|'-separated tokens all belong to the
    ///     same group. Example: "word1 | word2 | word3 word4" = group{word1,word2,word3} AND word4.
    ///     A leading or trailing '|', or consecutive '||', is ignored.
    ///   - A token is cut into pieces at every char that is not a word char, a quote, a
    ///     transparent char or a query operator ('?', '%'). On each piece: '%' at the start / end
    ///     = grammatical prefixes / suffixes; a '~' or '~N' suffix (N = 1-9, clamped) = fuzzy.
    ///   - The rest of a piece is cut into words by the indexer's word rule, '*' and '?' counting
    ///     as word chars: other transparent chars (. [ ] ~ ^ $ \ +, bidi controls) join and
    ///     vanish, quotes vanish from the term (FtsLib's terms carry none), marks are stripped,
    ///     presentation forms fold, every script is lowercased and none splits from another.
    ///   - A word with '*' or '?' is a wildcard (it wins over '~' and '%').
    ///   - A literal of any length is a word (one letter included). One whose term has
    ///     HebrewChars.MaxTermBytes bytes or more has no index term (Otzaria's RemoveLongFilter):
    ///     it is an unindexed slot, which matches nothing but keeps its one position (it counts
    ///     in the word distance's denominator, as a skipped wildcard).
    ///   - Every word of a piece takes the piece's operators.
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

            // Otzaria's sanitizer: a paired reading keeps its second reading.
            query = WordRule.KeepSecondOfPairedReadings(query);

            // Pad every '|' with spaces so that "a|b" and "a | b" are treated identically.
            query = query.Replace("|", " | ");

            // pendingGroup: the sub-patterns collected so far for the current OR group.
            var pendingGroup = new List<SubPattern>();
            bool lastWasPipe = false; // was the previous meaningful token a '|'?

            foreach (var raw in query.Split(new[] { ' ', '\t', '\r', '\n' },
                                            StringSplitOptions.RemoveEmptyEntries))
            {
                if (IsPipeToken(raw))
                {
                    lastWasPipe = true;
                    continue;
                }

                foreach (var sp in ParseToken(raw))
                {
                    if (!lastWasPipe && pendingGroup.Count > 0)
                    {
                        // No pipe between the previous word and this one: close the group.
                        groups.Add(new QueryGroup(pendingGroup));
                        pendingGroup = new List<SubPattern>();
                    }

                    pendingGroup.Add(sp);
                    lastWasPipe = false;
                }
            }

            if (pendingGroup.Count > 0)
                groups.Add(new QueryGroup(pendingGroup));

            return new ParsedQuery(groups);
        }

        private static bool IsPipeToken(string raw)
        {
            foreach (char c in raw)
                if (c != '|') return false;
            return true;
        }

        /// <summary>A char that stays inside a piece: a word char, a quote, a transparent char, or
        /// a query operator ('?' wildcard, '%' grammar; '*' and '~' are transparent chars).</summary>
        private static bool IsPieceChar(int c)
            => HebrewChars.IsWordChar(c) || HebrewChars.IsQuote(c) || HebrewChars.IsTransparent(c) || c == '?' || c == '%';

        /// <summary>The words of one whitespace token, piece by piece.</summary>
        private static List<SubPattern> ParseToken(string raw)
        {
            var result = new List<SubPattern>();
            int i = 0, n = raw.Length, pieceStart = 0;
            while (i <= n)
            {
                int c = -1, units = 1;
                if (i < n)
                {
                    c = raw[i];
                    if (char.IsHighSurrogate(raw[i]) && i + 1 < n && char.IsLowSurrogate(raw[i + 1]))
                    {
                        c = char.ConvertToUtf32(raw[i], raw[i + 1]);
                        units = 2;
                    }
                }
                if (c < 0 || !IsPieceChar(c))
                {
                    if (i > pieceStart) ParsePiece(raw.Substring(pieceStart, i - pieceStart), result);
                    pieceStart = i + units;
                }
                i += units;
            }
            return result;
        }

        /// <summary>One piece: its operators, then its words by the word rule.</summary>
        private static void ParsePiece(string raw, List<SubPattern> into)
        {
            bool grammarPrefix = raw.StartsWith("%", StringComparison.Ordinal);
            bool grammarSuffix = raw.EndsWith("%", StringComparison.Ordinal);
            if (grammarPrefix || grammarSuffix)
                raw = raw.Trim('%');

            // Fuzzy suffix: "word~" (distance 1) or "word~N" (N = 1-9, clamped).
            string text = raw;
            bool isFuzzy = false;
            int fuzzyDist = 1;
            int tildePos = raw.LastIndexOf('~');
            if (tildePos >= 0)
            {
                string suffix = raw.Substring(tildePos + 1);
                if (suffix.Length == 0 || (suffix.Length == 1 && suffix[0] >= '1' && suffix[0] <= '9'))
                {
                    isFuzzy = true;
                    fuzzyDist = suffix.Length == 0 ? 1 : (suffix[0] - '0');
                    if (fuzzyDist > FuzzyExpander.MaxAllowedDistance)
                        fuzzyDist = FuzzyExpander.MaxAllowedDistance;
                    text = raw.Substring(0, tildePos);
                }
            }

            int pos = 0;
            while (WordRule.NextTokenBoundaries(text, pos, true, out int start, out int end))
            {
                pos = end;
                string normalised = WordRule.Normalize(text.Substring(start, end - start), true, out int termBytes);
                normalised = StripQuotes(normalised);
                if (normalised.Length == 0) continue;

                bool isWildcard = normalised.IndexOf('*') >= 0 || normalised.IndexOf('?') >= 0;
                bool fuzzy = isFuzzy && !isWildcard;        // a wildcard wins over '~'
                bool prefixes = grammarPrefix && !isWildcard, suffixes = grammarSuffix && !isWildcard;

                // A literal whose term is too long has no index term: an unindexed slot.
                bool unindexed = !isWildcard && !fuzzy && !prefixes && !suffixes
                    && !HebrewChars.IsIndexableLength(termBytes);

                into.Add(new SubPattern(normalised, isWildcard, fuzzy, fuzzyDist,
                                        grammarExpandPrefixes: prefixes,
                                        grammarExpandSuffixes: suffixes,
                                        isUnindexed: unindexed));
            }
        }

        /// <summary>FtsLib's terms carry no quotes.</summary>
        private static string StripQuotes(string s)
        {
            if (s.IndexOf('\'') < 0 && s.IndexOf('"') < 0) return s;
            return s.Replace("'", "").Replace("\"", "");
        }
    }

    // ── Result types ─────────────────────────────────────────────────────────────

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

        /// <summary>True when the first alternative is a literal too long to be indexed.</summary>
        public bool IsUnindexed => Alternatives[0].IsUnindexed;
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
        /// Maximum edit distance for fuzzy matching.
        /// Meaningful only when <see cref="IsFuzzy"/> is true.
        /// </summary>
        public readonly int FuzzyDistance;

        /// <summary>True when this token has a leading '%' - expand grammatical prefixes.</summary>
        public readonly bool GrammarExpandPrefixes;

        /// <summary>True when this token has a trailing '%' - expand grammatical suffixes.</summary>
        public readonly bool GrammarExpandSuffixes;

        /// <summary>True when this is a grammar-expansion token (at least one % marker).</summary>
        public bool IsGrammar => GrammarExpandPrefixes || GrammarExpandSuffixes;

        /// <summary>
        /// True for a literal whose term is too long to be indexed (HebrewChars.MaxTermBytes or
        /// more): it expands to no term and is no constraint, but its slot keeps its one position
        /// (it counts in the word distance's denominator).
        /// </summary>
        public readonly bool IsUnindexed;

        public SubPattern(string pattern, bool isWildcard,
                          bool isFuzzy = false, int fuzzyDistance = 1,
                          bool grammarExpandPrefixes = false, bool grammarExpandSuffixes = false,
                          bool isUnindexed = false)
        {
            Pattern                = pattern;
            IsWildcard             = isWildcard;
            IsFuzzy                = isFuzzy;
            FuzzyDistance          = fuzzyDistance;
            GrammarExpandPrefixes  = grammarExpandPrefixes;
            GrammarExpandSuffixes  = grammarExpandSuffixes;
            IsUnindexed            = isUnindexed;
        }
    }
}
