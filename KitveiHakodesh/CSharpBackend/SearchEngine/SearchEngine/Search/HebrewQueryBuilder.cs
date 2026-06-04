using System;
using System.Collections.Generic;
using Lucene.Net.Search;
using Lucene.Net.Search.Spans;
using SearchEngine.Tokenization;

namespace SearchEngine.Search
{
    /// <summary>
    /// Builds a Lucene <see cref="Query"/> from a raw Hebrew query string.
    ///
    /// Supported syntax:
    ///   word        — literal AND term (analyzed through HebrewAnalyzer)
    ///   word*       — prefix wildcard  → PrefixQuery
    ///   *word       — suffix wildcard  → WildcardQuery
    ///   *word*      — infix  wildcard  → WildcardQuery
    ///   wor?d       — optional char (FtsLib-style): the char before '?' is optional
    ///   word~       — fuzzy, edit distance 1  → FuzzyQuery(maxEdits=1)
    ///   word~2      — fuzzy, edit distance 2  → FuzzyQuery(maxEdits=2)
    ///   word~3      — fuzzy, edit distance 3  → FuzzyQuery(maxEdits=2) [Lucene cap]
    ///   a | b       — OR: both alternatives satisfy one AND slot
    ///   %word       — grammar prefix expansion (ו, ב, כ, ל, מ, ש, ה + stacked forms)
    ///   word%       — grammar suffix expansion (ים, ות, ין, ך, ו, ה, …)
    ///   %word%      — both prefix and suffix expansion
    ///   ~word       — כתיב חסר/מלא expansion (inserts/removes ו and י variants)
    ///
    /// Rules:
    ///   - Fuzzy suffix '~' / '~N' is detected before the leading '~' (ketiv) marker,
    ///     so "word~" is fuzzy and "~word" is ketiv — they are distinct operators.
    ///   - Wildcard ('*' / '?') wins over fuzzy: if both appear the fuzzy suffix is dropped.
    ///   - '*' overrides '%': a token with '*' is a plain wildcard; '%' is ignored.
    ///   - '~' (ketiv) and '%' are compatible: ~%word% applies both expansions.
    ///   - Multiple space-separated tokens are AND-ed.
    ///   - '|'-separated tokens within one AND slot are OR-ed.
    ///
    /// Term building helpers are in <c>HebrewQueryBuilder.TermBuilders.cs</c>.
    /// </summary>
    public static partial class HebrewQueryBuilder
    {
        // ── Limits ────────────────────────────────────────────────────

        /// <summary>
        /// Lucene.Net <see cref="FuzzyQuery"/> hard cap on maxEdits (1 or 2).
        /// FtsLib allows distance 3; we clamp it here and document the difference.
        /// </summary>
        public const int MaxFuzzyDistance = 2;

        /// <summary>
        /// Minimum term length for fuzzy matching. Single- and two-char terms
        /// produce too many false positives and are treated as literals instead.
        /// </summary>
        public const int MinFuzzyTermLength = 3;

        // ── Parsed token ──────────────────────────────────────────────

        private struct ParsedToken
        {
            public string Pattern;
            public bool   GrammarPrefix;
            public bool   GrammarSuffix;
            public bool   Ketiv;
            public bool   IsWildcard;
            public bool   IsFuzzy;
            public int    FuzzyDistance;
        }

        // ── Public entry points ───────────────────────────────────────

        /// <summary>
        /// Builds a <see cref="BooleanQuery"/> (AND of OR-groups) for index search.
        /// Returns null when the query is empty / all tokens are invalid.
        /// </summary>
        public static Query Build(string queryText, HebrewAnalyzer analyzer)
        {
            var slots = ParseSlots(queryText);
            if (slots == null) return null;

            var andClauses = new List<Query>();
            foreach (var slot in slots)
            {
                Query clause = BuildBoolOrGroup(slot, analyzer);
                if (clause != null) andClauses.Add(clause);
            }

            if (andClauses.Count == 0) return null;
            if (andClauses.Count == 1) return andClauses[0];

            var bq = new BooleanQuery();
            foreach (var c in andClauses)
                bq.Add(c, Occur.MUST);
            return bq;
        }

        /// <summary>
        /// Builds a <see cref="SpanQuery"/> for proximity-aware highlighting.
        /// Returns null when the query is empty / all tokens are invalid.
        /// </summary>
        public static SpanQuery BuildSpan(string queryText, HebrewAnalyzer analyzer,
                                          int slop, bool inOrder)
        {
            var slots = ParseSlots(queryText);
            if (slots == null) return null;

            var spanClauses = new List<SpanQuery>();
            foreach (var slot in slots)
            {
                SpanQuery clause = BuildSpanOrGroup(slot, analyzer);
                if (clause != null) spanClauses.Add(clause);
            }

            if (spanClauses.Count == 0) return null;
            if (spanClauses.Count == 1) return spanClauses[0];

            return new SpanNearQuery(spanClauses.ToArray(), slop, inOrder);
        }

        // ── Slot parsing ──────────────────────────────────────────────

        private static List<List<ParsedToken>> ParseSlots(string queryText)
        {
            if (string.IsNullOrWhiteSpace(queryText))
                return null;

            // Only allocate the replaced string when '|' actually appears.
            if (queryText.IndexOf('|') >= 0)
                queryText = queryText.Replace("|", " | ");

            var slots        = new List<List<ParsedToken>>();
            var pendingOr    = new List<ParsedToken>();
            bool lastWasPipe = false;

            foreach (var raw in queryText.Split(new[] { ' ', '\t', '\r', '\n' },
                                                StringSplitOptions.RemoveEmptyEntries))
            {
                if (IsPipe(raw)) { lastWasPipe = true; continue; }

                ParsedToken? pt = ParseToken(raw);
                if (pt == null) continue;

                if (!lastWasPipe && pendingOr.Count > 0)
                {
                    slots.Add(new List<ParsedToken>(pendingOr));
                    pendingOr.Clear();
                }

                pendingOr.Add(pt.Value);
                lastWasPipe = false;
            }

            if (pendingOr.Count > 0)
                slots.Add(new List<ParsedToken>(pendingOr));

            return slots.Count == 0 ? null : slots;
        }

        /// <summary>
        /// Parses a single raw token into a <see cref="ParsedToken"/>.
        /// Returns null when the token is empty after normalisation.
        ///
        /// Detection order (each step strips its marker before the next):
        ///   1. Leading '~'  → ketiv expansion flag
        ///   2. Leading '%'  → grammar prefix flag
        ///   3. Trailing '%' → grammar suffix flag
        ///   4. Trailing '~' or '~N' → fuzzy flag + distance
        ///   5. Normalise remaining text
        ///   6. Wildcard detection ('*' / '?') — wins over fuzzy if both present
        /// </summary>
        private static ParsedToken? ParseToken(string raw)
        {
            bool ketiv = raw.Length > 0 && raw[0] == '~';
            if (ketiv) raw = raw.Substring(1);

            bool grammarPrefix = raw.Length > 0 && raw[0] == '%';
            bool grammarSuffix = raw.Length > 0 && raw[raw.Length - 1] == '%';
            if (grammarPrefix) raw = raw.Substring(1);
            if (grammarSuffix && raw.Length > 0) raw = raw.Substring(0, raw.Length - 1);

            bool isFuzzy   = false;
            int  fuzzyDist = 1;
            int  tildePos  = raw.LastIndexOf('~');
            if (tildePos >= 0)
            {
                string fuzzySuffix = raw.Substring(tildePos + 1);
                bool validSuffix   = fuzzySuffix.Length == 0
                                  || (fuzzySuffix.Length == 1
                                      && fuzzySuffix[0] >= '1'
                                      && fuzzySuffix[0] <= '9');
                if (validSuffix)
                {
                    isFuzzy   = true;
                    fuzzyDist = fuzzySuffix.Length == 0 ? 1 : (fuzzySuffix[0] - '0');
                    if (fuzzyDist > MaxFuzzyDistance) fuzzyDist = MaxFuzzyDistance;
                    raw = raw.Substring(0, tildePos);
                }
            }

            string pattern = Normalise(raw);
            if (pattern.Length == 0) return null;

            bool isWildcard = pattern.IndexOf('*') >= 0 || pattern.IndexOf('?') >= 0;
            if (isWildcard) isFuzzy = false;

            if (isFuzzy && pattern.Length < MinFuzzyTermLength) isFuzzy = false;

            if (pattern.IndexOf('*') >= 0)
            {
                grammarPrefix = false;
                grammarSuffix = false;
            }

            return new ParsedToken
            {
                Pattern       = pattern,
                GrammarPrefix = grammarPrefix,
                GrammarSuffix = grammarSuffix,
                Ketiv         = ketiv,
                IsWildcard    = isWildcard,
                IsFuzzy       = isFuzzy,
                FuzzyDistance = fuzzyDist,
            };
        }

        // ── Token expansion ───────────────────────────────────────────

        private static HashSet<string> ExpandToken(ParsedToken pt)
        {
            var forms = new HashSet<string>(StringComparer.Ordinal) { pt.Pattern };

            if (pt.GrammarPrefix || pt.GrammarSuffix)
            {
                var grammarForms = GrammarExpander.Expand(
                    pt.Pattern, pt.GrammarPrefix, pt.GrammarSuffix);
                foreach (var f in grammarForms)
                    forms.Add(f);
            }

            if (pt.Ketiv)
            {
                var toExpand = new List<string>(forms);
                foreach (var f in toExpand)
                    foreach (var v in KetivExpander.Expand(f))
                        forms.Add(v);
            }

            return forms;
        }

        // ── Boolean OR group ──────────────────────────────────────────

        private static Query BuildBoolOrGroup(List<ParsedToken> tokens, HebrewAnalyzer analyzer)
        {
            var alternatives = new List<Query>();

            foreach (var pt in tokens)
            {
                bool needsExpansion = pt.GrammarPrefix || pt.GrammarSuffix || pt.Ketiv;

                if (pt.IsFuzzy)
                {
                    if (needsExpansion)
                        foreach (var form in ExpandToken(pt))
                        { var q = BuildFuzzyQuery(form, pt.FuzzyDistance); if (q != null) alternatives.Add(q); }
                    else
                    { var q = BuildFuzzyQuery(pt.Pattern, pt.FuzzyDistance); if (q != null) alternatives.Add(q); }
                }
                else if (pt.IsWildcard && !needsExpansion)
                {
                    var q = BuildWildcardQuery(pt.Pattern);
                    if (q != null) alternatives.Add(q);
                }
                else if (!pt.IsWildcard && !needsExpansion)
                {
                    var q = BuildLiteralQuery(pt.Pattern, analyzer);
                    if (q != null) alternatives.Add(q);
                }
                else
                {
                    foreach (var form in ExpandToken(pt))
                    {
                        bool isWild = form.IndexOf('*') >= 0 || form.IndexOf('?') >= 0;
                        var q = isWild ? BuildWildcardQuery(form) : BuildLiteralQuery(form, analyzer);
                        if (q != null) alternatives.Add(q);
                    }
                }
            }

            if (alternatives.Count == 0) return null;
            if (alternatives.Count == 1) return alternatives[0];

            var bq = new BooleanQuery();
            foreach (var q in alternatives)
                bq.Add(q, Occur.SHOULD);
            return bq;
        }

        // ── Span OR group ─────────────────────────────────────────────

        private static SpanQuery BuildSpanOrGroup(List<ParsedToken> tokens, HebrewAnalyzer analyzer)
        {
            var alternatives = new List<SpanQuery>();

            foreach (var pt in tokens)
            {
                bool needsExpansion = pt.GrammarPrefix || pt.GrammarSuffix || pt.Ketiv;

                if (pt.IsFuzzy)
                {
                    if (needsExpansion)
                        foreach (var form in ExpandToken(pt))
                        { var q = BuildSpanFuzzyQuery(form, pt.FuzzyDistance); if (q != null) alternatives.Add(q); }
                    else
                    { var q = BuildSpanFuzzyQuery(pt.Pattern, pt.FuzzyDistance); if (q != null) alternatives.Add(q); }
                }
                else if (pt.IsWildcard && !needsExpansion)
                {
                    var q = BuildSpanWildcardQuery(pt.Pattern);
                    if (q != null) alternatives.Add(q);
                }
                else if (!pt.IsWildcard && !needsExpansion)
                {
                    var q = BuildSpanLiteralQuery(pt.Pattern, analyzer);
                    if (q != null) alternatives.Add(q);
                }
                else
                {
                    foreach (var form in ExpandToken(pt))
                    {
                        bool isWild = form.IndexOf('*') >= 0 || form.IndexOf('?') >= 0;
                        var q = isWild ? BuildSpanWildcardQuery(form) : BuildSpanLiteralQuery(form, analyzer);
                        if (q != null) alternatives.Add(q);
                    }
                }
            }

            if (alternatives.Count == 0) return null;
            if (alternatives.Count == 1) return alternatives[0];

            return new SpanOrQuery(alternatives.ToArray());
        }
    }
}
