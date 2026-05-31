using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Search.Spans;
using Lucene.Net.Util;
using SearchEngine.Indexing;
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
    /// Wildcard limits:
    ///   MinAnchorLength      = 2
    ///   MaxOptionalChars     = 4
    ///
    /// Fuzzy limits:
    ///   MaxFuzzyDistance     = 2  (Lucene.Net FuzzyQuery hard cap; FtsLib distance 3
    ///                              is silently clamped to 2)
    ///   MinFuzzyTermLength   = 3  (single/two-char terms are not fuzzied)
    /// </summary>
    public static class HebrewQueryBuilder
    {
        // ── Limits ────────────────────────────────────────────────────────────
        private const int MinAnchorLength  = 2;
        private const int MaxOptionalChars = 4;

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

        // ── Parsed token ──────────────────────────────────────────────────────

        private struct ParsedToken
        {
            public string Pattern;       // normalised, no '%', '~' prefix, or '~N' suffix
            public bool   GrammarPrefix; // leading '%'
            public bool   GrammarSuffix; // trailing '%'
            public bool   Ketiv;         // leading '~' (כתיב expansion)
            public bool   IsWildcard;    // contains '*' or '?'
            public bool   IsFuzzy;       // trailing '~' or '~N' suffix
            public int    FuzzyDistance; // 1–2 (clamped from FtsLib's 1–3)
        }

        // ── Public entry points ───────────────────────────────────────────────

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

        // ── Slot parsing ──────────────────────────────────────────────────────

        /// <summary>
        /// Tokenises the query string into AND slots, each slot being a list of
        /// OR-alternative <see cref="ParsedToken"/>s.
        /// Returns null when the input is empty.
        /// </summary>
        private static List<List<ParsedToken>> ParseSlots(string queryText)
        {
            if (string.IsNullOrWhiteSpace(queryText))
                return null;

            queryText = queryText.Replace("|", " | ");

            var slots        = new List<List<ParsedToken>>();
            var pendingOr    = new List<ParsedToken>();
            bool lastWasPipe = false;

            foreach (var raw in queryText.Split(new[] { ' ', '\t', '\r', '\n' },
                                                StringSplitOptions.RemoveEmptyEntries))
            {
                if (IsPipe(raw))
                {
                    lastWasPipe = true;
                    continue;
                }

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
        /// Detection order (important — each step strips its marker before the next):
        ///   1. Leading '~'  → ketiv expansion flag (consumed before normalisation)
        ///   2. Leading '%'  → grammar prefix flag
        ///   3. Trailing '%' → grammar suffix flag
        ///   4. Trailing '~' or '~N' → fuzzy flag + distance (consumed before normalisation)
        ///   5. Normalise remaining text
        ///   6. Wildcard detection ('*' / '?') — wins over fuzzy if both present
        /// </summary>
        private static ParsedToken? ParseToken(string raw)
        {
            // Step 1: leading '~' → ketiv expansion.
            // Must be checked before the fuzzy suffix so "~word" is ketiv, not fuzzy.
            bool ketiv = raw.Length > 0 && raw[0] == '~';
            if (ketiv) raw = raw.Substring(1);

            // Step 2 & 3: '%' grammar markers.
            bool grammarPrefix = raw.Length > 0 && raw[0] == '%';
            bool grammarSuffix = raw.Length > 0 && raw[raw.Length - 1] == '%';
            if (grammarPrefix) raw = raw.Substring(1);
            if (grammarSuffix && raw.Length > 0) raw = raw.Substring(0, raw.Length - 1);

            // Step 4: trailing '~' or '~N' → fuzzy.
            // Scan from the right: accept "~" or "~1" / "~2" / "~3".
            bool isFuzzy    = false;
            int  fuzzyDist  = 1;
            int  tildePos   = raw.LastIndexOf('~');
            if (tildePos >= 0)
            {
                string fuzzySuffix = raw.Substring(tildePos + 1); // "" or "1"/"2"/"3"
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
                // else: '~' is noise — dropped by Normalise below
            }

            // Step 5: normalise.
            string pattern = Normalise(raw);
            if (pattern.Length == 0) return null;

            // Step 6: wildcard detection. Wildcard wins over fuzzy.
            bool isWildcard = pattern.IndexOf('*') >= 0 || pattern.IndexOf('?') >= 0;
            if (isWildcard) isFuzzy = false;

            // Short terms are not fuzzied (too many false positives).
            if (isFuzzy && pattern.Length < MinFuzzyTermLength) isFuzzy = false;

            // '*' overrides '%': grammar expansion is suppressed for star-wildcards.
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

        // ── Expansion: token → set of string alternatives ─────────────────────

        /// <summary>
        /// Expands a single <see cref="ParsedToken"/> into the full set of string
        /// alternatives (grammar forms + ketiv variants), ready to be turned into
        /// Lucene queries.
        /// </summary>
        private static HashSet<string> ExpandToken(ParsedToken pt)
        {
            // Start with the base form.
            var forms = new HashSet<string>(StringComparer.Ordinal) { pt.Pattern };

            // Grammar expansion (prefix/suffix).
            if (pt.GrammarPrefix || pt.GrammarSuffix)
            {
                var grammarForms = GrammarExpander.Expand(
                    pt.Pattern, pt.GrammarPrefix, pt.GrammarSuffix);
                foreach (var f in grammarForms)
                    forms.Add(f);
            }

            // Ketiv חסר/מלא expansion — applied to every grammar form.
            if (pt.Ketiv)
            {
                var toExpand = new List<string>(forms);
                foreach (var f in toExpand)
                {
                    foreach (var v in KetivExpander.Expand(f))
                        forms.Add(v);
                }
            }

            return forms;
        }

        // ── Boolean OR group ──────────────────────────────────────────────────

        private static Query BuildBoolOrGroup(List<ParsedToken> tokens, HebrewAnalyzer analyzer)
        {
            var alternatives = new List<Query>();

            foreach (var pt in tokens)
            {
                if (pt.IsFuzzy)
                {
                    // Fuzzy term — grammar/ketiv expansion is applied first, then
                    // each resulting form gets its own FuzzyQuery.
                    bool needsExpansion = pt.GrammarPrefix || pt.GrammarSuffix || pt.Ketiv;
                    if (needsExpansion)
                    {
                        var forms = ExpandToken(pt);
                        foreach (var form in forms)
                        {
                            Query q = BuildFuzzyQuery(form, pt.FuzzyDistance);
                            if (q != null) alternatives.Add(q);
                        }
                    }
                    else
                    {
                        Query q = BuildFuzzyQuery(pt.Pattern, pt.FuzzyDistance);
                        if (q != null) alternatives.Add(q);
                    }
                }
                else if (pt.IsWildcard && !pt.GrammarPrefix && !pt.GrammarSuffix && !pt.Ketiv)
                {
                    // Pure wildcard — no expansion needed.
                    Query q = BuildWildcardQuery(pt.Pattern);
                    if (q != null) alternatives.Add(q);
                }
                else if (!pt.IsWildcard && !pt.GrammarPrefix && !pt.GrammarSuffix && !pt.Ketiv)
                {
                    // Plain literal — fast path.
                    Query q = BuildLiteralQuery(pt.Pattern, analyzer);
                    if (q != null) alternatives.Add(q);
                }
                else
                {
                    // Expanded token — each form becomes an OR alternative.
                    var forms = ExpandToken(pt);
                    foreach (var form in forms)
                    {
                        bool isWild = form.IndexOf('*') >= 0 || form.IndexOf('?') >= 0;
                        Query q = isWild
                            ? BuildWildcardQuery(form)
                            : BuildLiteralQuery(form, analyzer);
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

        // ── Span OR group ─────────────────────────────────────────────────────

        private static SpanQuery BuildSpanOrGroup(List<ParsedToken> tokens, HebrewAnalyzer analyzer)
        {
            var alternatives = new List<SpanQuery>();

            foreach (var pt in tokens)
            {
                if (pt.IsFuzzy)
                {
                    bool needsExpansion = pt.GrammarPrefix || pt.GrammarSuffix || pt.Ketiv;
                    if (needsExpansion)
                    {
                        var forms = ExpandToken(pt);
                        foreach (var form in forms)
                        {
                            SpanQuery q = BuildSpanFuzzyQuery(form, pt.FuzzyDistance);
                            if (q != null) alternatives.Add(q);
                        }
                    }
                    else
                    {
                        SpanQuery q = BuildSpanFuzzyQuery(pt.Pattern, pt.FuzzyDistance);
                        if (q != null) alternatives.Add(q);
                    }
                }
                else if (pt.IsWildcard && !pt.GrammarPrefix && !pt.GrammarSuffix && !pt.Ketiv)
                {
                    SpanQuery q = BuildSpanWildcardQuery(pt.Pattern);
                    if (q != null) alternatives.Add(q);
                }
                else if (!pt.IsWildcard && !pt.GrammarPrefix && !pt.GrammarSuffix && !pt.Ketiv)
                {
                    SpanQuery q = BuildSpanLiteralQuery(pt.Pattern, analyzer);
                    if (q != null) alternatives.Add(q);
                }
                else
                {
                    var forms = ExpandToken(pt);
                    foreach (var form in forms)
                    {
                        bool isWild = form.IndexOf('*') >= 0 || form.IndexOf('?') >= 0;
                        SpanQuery q = isWild
                            ? BuildSpanWildcardQuery(form)
                            : BuildSpanLiteralQuery(form, analyzer);
                        if (q != null) alternatives.Add(q);
                    }
                }
            }

            if (alternatives.Count == 0) return null;
            if (alternatives.Count == 1) return alternatives[0];

            return new SpanOrQuery(alternatives.ToArray());
        }

        // ── Literal term (Boolean) ────────────────────────────────────────────

        private static Query BuildLiteralQuery(string token, HebrewAnalyzer analyzer)
        {
            var terms = Analyze(token, analyzer);
            if (terms.Count == 0) return null;
            if (terms.Count == 1) return new TermQuery(new Term(LuceneIndexWriter.FieldText, terms[0]));

            var bq = new BooleanQuery();
            foreach (var t in terms)
                bq.Add(new TermQuery(new Term(LuceneIndexWriter.FieldText, t)), Occur.MUST);
            return bq;
        }

        // ── Wildcard term (Boolean) ───────────────────────────────────────────

        private static Query BuildWildcardQuery(string token)
        {
            if (AnchorLength(token) < MinAnchorLength)
                return null;

            bool hasOptional = token.IndexOf('?') >= 0;
            if (!hasOptional)
                return BuildStarQuery(token);

            int optCount = CountEffectiveOptionals(token);
            if (optCount > MaxOptionalChars)
                return null;

            var subPatterns = new HashSet<string>(StringComparer.Ordinal);
            ExpandOptionals(token, 0, new StringBuilder(token.Length), subPatterns);

            var alternatives = new List<Query>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var sub in subPatterns)
            {
                if (!seen.Add(sub)) continue;
                if (AnchorLength(sub) < MinAnchorLength) continue;

                Query q = sub.IndexOf('*') >= 0
                    ? BuildStarQuery(sub)
                    : BuildExactWildcardTerm(sub);
                if (q != null) alternatives.Add(q);
            }

            if (alternatives.Count == 0) return null;
            if (alternatives.Count == 1) return alternatives[0];

            var bq = new BooleanQuery();
            foreach (var q in alternatives)
                bq.Add(q, Occur.SHOULD);
            return bq;
        }

        private static Query BuildStarQuery(string pattern)
        {
            bool hasLeading  = pattern.StartsWith("*");
            bool hasTrailing = pattern.EndsWith("*");

            if (!hasLeading && hasTrailing)
                return new PrefixQuery(new Term(LuceneIndexWriter.FieldText, pattern.TrimEnd('*')));

            return new WildcardQuery(new Term(LuceneIndexWriter.FieldText, pattern));
        }

        private static Query BuildExactWildcardTerm(string term)
            => new TermQuery(new Term(LuceneIndexWriter.FieldText, term));

        // ── Fuzzy term (Boolean) ──────────────────────────────────────────────

        /// <summary>
        /// Builds a <see cref="FuzzyQuery"/> for <paramref name="token"/> with the
        /// given edit distance (1 or 2 — Lucene.Net hard cap).
        ///
        /// Returns null when the token is shorter than <see cref="MinFuzzyTermLength"/>
        /// (already guarded in <see cref="ParseToken"/>, but checked again for safety
        /// when called from the grammar/ketiv expansion path).
        /// </summary>
        private static Query BuildFuzzyQuery(string token, int maxEdits)
        {
            if (token.Length < MinFuzzyTermLength) return null;
            if (maxEdits > MaxFuzzyDistance) maxEdits = MaxFuzzyDistance;
            if (maxEdits < 1)                maxEdits = 1;
            return new FuzzyQuery(new Term(LuceneIndexWriter.FieldText, token), maxEdits);
        }

        // ── Fuzzy term (Span) ─────────────────────────────────────────────────

        /// <summary>
        /// Wraps a <see cref="FuzzyQuery"/> in a <see cref="SpanMultiTermQueryWrapper{T}"/>
        /// so it can participate in proximity / ordered span queries.
        /// </summary>
        private static SpanQuery BuildSpanFuzzyQuery(string token, int maxEdits)
        {
            if (token.Length < MinFuzzyTermLength) return null;
            if (maxEdits > MaxFuzzyDistance) maxEdits = MaxFuzzyDistance;
            if (maxEdits < 1)                maxEdits = 1;
            return new SpanMultiTermQueryWrapper<FuzzyQuery>(
                new FuzzyQuery(new Term(LuceneIndexWriter.FieldText, token), maxEdits));
        }

        // ── Literal term (Span) ───────────────────────────────────────────────

        private static SpanQuery BuildSpanLiteralQuery(string token, HebrewAnalyzer analyzer)
        {
            var terms = Analyze(token, analyzer);
            if (terms.Count == 0) return null;
            if (terms.Count == 1)
                return new SpanTermQuery(new Term(LuceneIndexWriter.FieldText, terms[0]));

            var clauses = new SpanQuery[terms.Count];
            for (int i = 0; i < terms.Count; i++)
                clauses[i] = new SpanTermQuery(new Term(LuceneIndexWriter.FieldText, terms[i]));
            return new SpanNearQuery(clauses, 0, true);
        }

        // ── Wildcard term (Span) ──────────────────────────────────────────────

        private static SpanQuery BuildSpanWildcardQuery(string token)
        {
            if (AnchorLength(token) < MinAnchorLength)
                return null;

            bool hasOptional = token.IndexOf('?') >= 0;
            if (!hasOptional)
                return BuildSpanStarQuery(token);

            int optCount = CountEffectiveOptionals(token);
            if (optCount > MaxOptionalChars)
                return null;

            var subPatterns = new HashSet<string>(StringComparer.Ordinal);
            ExpandOptionals(token, 0, new StringBuilder(token.Length), subPatterns);

            var alternatives = new List<SpanQuery>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var sub in subPatterns)
            {
                if (!seen.Add(sub)) continue;
                if (AnchorLength(sub) < MinAnchorLength) continue;

                SpanQuery q = sub.IndexOf('*') >= 0
                    ? BuildSpanStarQuery(sub)
                    : new SpanTermQuery(new Term(LuceneIndexWriter.FieldText, sub));
                if (q != null) alternatives.Add(q);
            }

            if (alternatives.Count == 0) return null;
            if (alternatives.Count == 1) return alternatives[0];

            return new SpanOrQuery(alternatives.ToArray());
        }

        private static SpanQuery BuildSpanStarQuery(string pattern)
        {
            bool hasLeading  = pattern.StartsWith("*");
            bool hasTrailing = pattern.EndsWith("*");

            if (!hasLeading && hasTrailing)
                return new SpanMultiTermQueryWrapper<PrefixQuery>(
                    new PrefixQuery(new Term(LuceneIndexWriter.FieldText, pattern.TrimEnd('*'))));

            return new SpanMultiTermQueryWrapper<WildcardQuery>(
                new WildcardQuery(new Term(LuceneIndexWriter.FieldText, pattern)));
        }

        // ── '?' unrolling ─────────────────────────────────────────────────────

        private static void ExpandOptionals(
            string pattern, int pos, StringBuilder current, HashSet<string> results)
        {
            if (pos == pattern.Length)
            {
                results.Add(current.ToString());
                return;
            }

            char c = pattern[pos];

            if (c != '?')
            {
                current.Append(c);
                ExpandOptionals(pattern, pos + 1, current, results);
                current.Length--;
                return;
            }

            bool hasTarget = current.Length > 0 && current[current.Length - 1] != '*';

            if (!hasTarget)
            {
                ExpandOptionals(pattern, pos + 1, current, results);
                return;
            }

            ExpandOptionals(pattern, pos + 1, current, results);

            char saved = current[current.Length - 1];
            current.Length--;
            ExpandOptionals(pattern, pos + 1, current, results);
            current.Append(saved);
        }

        private static int CountEffectiveOptionals(string pattern)
        {
            int count = 0;
            for (int i = 0; i < pattern.Length; i++)
            {
                if (pattern[i] != '?') continue;
                if (i == 0) continue;
                char prev = pattern[i - 1];
                if (prev == '*' || prev == '?') continue;
                count++;
            }
            return count;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static bool IsPipe(string raw)
        {
            foreach (char c in raw)
                if (c != '|') return false;
            return true;
        }

        /// <summary>
        /// Strips nikud/cantillation, geresh/gershayim, lowercases ASCII.
        /// Preserves '*' and '?'. Drops '%' and '~' (already consumed by ParseToken).
        /// </summary>
        private static string Normalise(string token)
        {
            var sb = new StringBuilder(token.Length);
            foreach (char c in token)
            {
                if (c >= '\u0591' && c <= '\u05C7') continue; // nikud + cantillation
                if (c == '\u05F3' || c == '\u05F4' || c == '"') continue; // geresh/gershayim
                if (c == '*' || c == '?') { sb.Append(c); continue; }
                if (c >= '\u05D0' && c <= '\u05EA') { sb.Append(c); continue; } // Hebrew
                if (c >= 'A' && c <= 'Z') { sb.Append((char)(c | 32)); continue; }
                if (c >= 'a' && c <= 'z') { sb.Append(c); continue; }
                // everything else (including '%', '~') dropped
            }
            return sb.ToString();
        }

        private static int AnchorLength(string pattern)
        {
            int n = 0;
            foreach (char c in pattern)
                if (c != '*' && c != '?') n++;
            return n;
        }

        private static List<string> Analyze(string token, HebrewAnalyzer analyzer)
        {
            var result = new List<string>();
            using (var ts = analyzer.GetTokenStream(LuceneIndexWriter.FieldText,
                                                    new StringReader(token)))
            {
                var attr = ts.GetAttribute<ICharTermAttribute>();
                ts.Reset();
                while (ts.IncrementToken())
                    result.Add(attr.ToString());
                ts.End();
            }
            return result;
        }
    }
}
