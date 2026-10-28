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
    ///   - A word with '*' or '?' is a wildcard (it wins over '~' and '%'), except that '*' on
    ///     one side and '%' on the other combine: "*word%" = any prefix + grammatical
    ///     suffixes, "%word*" = grammatical prefixes + any suffix.
    ///   - A literal of any length is a word (one letter included). One whose term has
    ///     HebrewChars.MaxTermBytes bytes or more has no index term (Otzaria's RemoveLongFilter):
    ///     it is an unindexed slot, which matches nothing but keeps its one position (it counts
    ///     in the word distance's denominator, as a skipped wildcard).
    ///   - Every word of a piece takes the piece's operators.
    ///   - Per-word markers: '^' anywhere among a piece's leading signs ("*^word" = "^*word")
    ///     = spelling variants for this word; '+' / '=' among its trailing signs ("word%+",
    ///     "word=", or leading) = inflections / synonyms, which the host rewrites before
    ///     parsing, so here they are only dropped.
    ///   - Quoted phrases (Otzaria's smartSearchQuotedPhrases, see <see cref="QuotedPhrases"/>):
    ///     the words of a quoted span are literal (their operators and markers dropped) and
    ///     <see cref="ParsedQuery.Phrases"/> lists each phrase's groups, which a hit must
    ///     carry adjacent and in order.
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

            // Quoted phrases, as typed-word indexes (read before the '|' padding, which
            // adds no typed word).
            var quoted = QuotedPhrases(query);
            var phraseOfTyped = new Dictionary<int, int>();
            for (int p = 0; p < quoted.Count; p++)
                foreach (int t in quoted[p]) phraseOfTyped[t] = p;
            var phraseGroups = new List<List<int>>();
            for (int p = 0; p < quoted.Count; p++) phraseGroups.Add(new List<int>());
            bool wordOutsidePhrase = false;

            // Pad every '|' with spaces so that "a|b" and "a | b" are treated identically.
            query = query.Replace("|", " | ");

            // pendingGroup: the sub-patterns collected so far for the current OR group.
            var pendingGroup = new List<SubPattern>();
            bool lastWasPipe = false; // was the previous meaningful token a '|'?
            int typed = -1;           // the typed word's index (pipes are not words)

            foreach (var raw in query.Split(new[] { ' ', '\t', '\r', '\n' },
                                            StringSplitOptions.RemoveEmptyEntries))
            {
                if (IsPipeToken(raw))
                {
                    lastWasPipe = true;
                    continue;
                }
                typed++;
                bool inPhrase = phraseOfTyped.TryGetValue(typed, out int phrase);

                foreach (var sp in ParseToken(raw, inPhrase))
                {
                    if (!lastWasPipe && pendingGroup.Count > 0)
                    {
                        // No pipe between the previous word and this one: close the group.
                        groups.Add(new QueryGroup(pendingGroup));
                        pendingGroup = new List<SubPattern>();
                    }

                    pendingGroup.Add(sp);
                    lastWasPipe = false;
                    // the group this sub-pattern is in (pendingGroup becomes groups[Count])
                    if (inPhrase)
                    {
                        var list = phraseGroups[phrase];
                        if (list.Count == 0 || list[list.Count - 1] != groups.Count) list.Add(groups.Count);
                    }
                    else wordOutsidePhrase = true;
                }
            }

            if (pendingGroup.Count > 0)
                groups.Add(new QueryGroup(pendingGroup));

            var phrases = new List<IReadOnlyList<int>>();
            foreach (var g in phraseGroups) if (g.Count > 0) phrases.Add(g);
            // One phrase holding every word: Otzaria's literal phrase search.
            bool wholePhrase = phrases.Count == 1 && !wordOutsidePhrase;
            phrases.RemoveAll(g => g.Count < 2);   // one word: its literal group says it all
            return new ParsedQuery(groups, phrases, wholePhrase);
        }

        /// <summary>A char that opens or closes a quoted phrase: '"', gershayim, and the
        /// typographic double quotes (Otzaria's smartSearchQuotedPhrases).</summary>
        private static bool IsPhraseQuote(char c)
            => c == '"' || c == '\u05F4' || c == '\u201C' || c == '\u201D' || c == '\u201E';

        private static bool IsHebrewLetter(char c) => c >= '\u05D0' && c <= '\u05EA';

        /// <summary>
        /// The quoted phrases of <paramref name="query"/> (Otzaria's smartSearchQuotedPhrases,
        /// lib/search/utils/smart_lexical_in_book_search.dart), as the typed-word indexes each
        /// covers. A quote char opens a phrase, the next one closes it, unless the quote's
        /// previous char (nikud / te'amim skipped, a maqaf counts) and its next char are both
        /// Hebrew letters: that quote is an acronym's gershayim and quotes nothing. An
        /// unclosed quote and an empty phrase are ignored. A typed word (cut on space, tab,
        /// CR, LF and '|') belongs to a phrase when it overlaps the quoted span.
        /// </summary>
        internal static List<List<int>> QuotedPhrases(string query)
        {
            var result = new List<List<int>>();
            var spans = new List<(int Start, int End)>();
            int open = -1;
            char previous = '\0';
            bool hasPrevious = false;
            for (int i = 0; i < query.Length; i++)
            {
                char c = query[i];
                bool nextLetter = i + 1 < query.Length && IsHebrewLetter(query[i + 1]);
                if (IsPhraseQuote(c) && !(hasPrevious && IsHebrewLetter(previous) && nextLetter))
                {
                    if (open < 0) open = i + 1;
                    else
                    {
                        if (query.Substring(open, i - open).Trim().Length > 0) spans.Add((open, i));
                        open = -1;
                    }
                }
                if (!((c >= '\u0591' && c <= '\u05BD') || (c >= '\u05BF' && c <= '\u05C7')))
                {
                    previous = c;
                    hasPrevious = true;
                }
            }
            if (spans.Count == 0) return result;

            var words = new List<(int Start, int End)>();
            int start = 0;
            for (int i = 0; i <= query.Length; i++)
            {
                if (i == query.Length || query[i] == '|' || query[i] == ' ' || query[i] == '\t' || query[i] == '\r' || query[i] == '\n')
                {
                    if (i > start) words.Add((start, i));
                    start = i + 1;
                }
            }
            foreach (var (s, e) in spans)
            {
                var phrase = new List<int>();
                for (int k = 0; k < words.Count; k++)
                    if (words[k].Start < e && words[k].End > s) phrase.Add(k);
                if (phrase.Count > 0) result.Add(phrase);
            }
            return result;
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
        private static List<SubPattern> ParseToken(string raw, bool literal = false)
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
                    if (i > pieceStart) ParsePiece(raw.Substring(pieceStart, i - pieceStart), result, literal);
                    pieceStart = i + units;
                }
                i += units;
            }
            return result;
        }

        /// <summary>One piece: its operators, then its words by the word rule.
        /// <paramref name="literal"/> (a quoted phrase's word): the operators and markers are
        /// dropped and every word is a plain literal.</summary>
        private static void ParsePiece(string raw, List<SubPattern> into, bool literal = false)
        {
            WordMarkers.Split(raw, out bool expandKetiv, out _, out _, out raw);

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
                // '%' survives a wildcard only on the side without a '*' (and with no '?').
                string stars = normalised.Trim('*');
                bool starsOnly = isWildcard && stars.Length > 0 && stars.IndexOf('*') < 0 && stars.IndexOf('?') < 0;
                bool prefixes = grammarPrefix && (!isWildcard || (starsOnly && !normalised.StartsWith("*", StringComparison.Ordinal)));
                bool suffixes = grammarSuffix && (!isWildcard || (starsOnly && !normalised.EndsWith("*", StringComparison.Ordinal)));

                if (literal)
                {
                    // A quoted phrase's word is searched as written.
                    if (isWildcard)
                    {
                        normalised = normalised.Replace("*", "").Replace("?", "");
                        if (normalised.Length == 0) continue;
                        termBytes = System.Text.Encoding.UTF8.GetByteCount(normalised);
                    }
                    into.Add(new SubPattern(normalised, false,
                                            isUnindexed: !HebrewChars.IsIndexableLength(termBytes)));
                    continue;
                }

                // A literal whose term is too long has no index term: an unindexed slot.
                bool unindexed = !isWildcard && !fuzzy && !prefixes && !suffixes
                    && !HebrewChars.IsIndexableLength(termBytes);

                into.Add(new SubPattern(normalised, isWildcard, fuzzy, fuzzyDist,
                                        grammarExpandPrefixes: prefixes,
                                        grammarExpandSuffixes: suffixes,
                                        isUnindexed: unindexed,
                                        expandKetiv: expandKetiv));
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

        /// <summary>Quoted phrases of two or more groups: each phrase's group indexes, in
        /// order. A hit carries each phrase's groups adjacent and in order.</summary>
        public readonly IReadOnlyList<IReadOnlyList<int>> Phrases;

        /// <summary>The query is one quoted phrase holding every word (Otzaria's literal
        /// phrase search).</summary>
        public readonly bool IsWholePhrase;

        public ParsedQuery(List<QueryGroup> groups)
            : this(groups, new List<IReadOnlyList<int>>(), false) { }

        public ParsedQuery(List<QueryGroup> groups, List<IReadOnlyList<int>> phrases, bool wholePhrase)
        {
            Groups = groups;
            Phrases = phrases;
            IsWholePhrase = wholePhrase;
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

        /// <summary>True when this word asked for spelling variants itself (a leading '^').</summary>
        public readonly bool ExpandKetiv;

        public SubPattern(string pattern, bool isWildcard,
                          bool isFuzzy = false, int fuzzyDistance = 1,
                          bool grammarExpandPrefixes = false, bool grammarExpandSuffixes = false,
                          bool isUnindexed = false, bool expandKetiv = false)
        {
            Pattern                = pattern;
            IsWildcard             = isWildcard;
            IsFuzzy                = isFuzzy;
            FuzzyDistance          = fuzzyDistance;
            GrammarExpandPrefixes  = grammarExpandPrefixes;
            GrammarExpandSuffixes  = grammarExpandSuffixes;
            IsUnindexed            = isUnindexed;
            ExpandKetiv            = expandKetiv;
        }
    }
}
