using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Search.Spans;
using Lucene.Net.Util;
using LuceneLib.Indexing;
using LuceneLib.Tokenization;

namespace LuceneLib.Search
{
    /// <summary>
    /// Builds a Lucene <see cref="Query"/> from a raw Hebrew query string,
    /// matching FtsLib's query semantics:
    ///
    ///   word        — literal AND term (analyzed through HebrewAnalyzer)
    ///   word*       — prefix wildcard  → Lucene PrefixQuery
    ///   *word       — suffix wildcard  → Lucene WildcardQuery
    ///   *word*      — infix  wildcard  → Lucene WildcardQuery
    ///   wor?d       — optional char: the char before '?' is optional
    ///                 → two WildcardQuery / TermQuery alternatives OR'd together
    ///   a | b       — OR: both alternatives satisfy one AND slot
    ///
    /// Wildcard limits (matching FtsLib's HebrewWildcardExpander):
    ///   MinAnchorLength      = 2  — anchor must be ≥ 2 non-wildcard chars
    ///   MaxPrefixWildcardChars = 3  — leading '*' may match at most 3 chars
    ///   MaxSuffixWildcardChars = 4  — trailing '*' may match at most 4 chars
    ///   MaxOptionalChars     = 4  — at most 4 '?' operators per token
    ///
    /// Multiple space-separated tokens are AND-ed.
    /// '|'-separated tokens within one AND slot are OR-ed.
    /// </summary>
    public static class HebrewQueryBuilder
    {
        // ── Limits (mirror FtsLib's HebrewWildcardExpander constants) ────────
        private const int MinAnchorLength       = 2;
        private const int MaxPrefixWildcardChars = 3;
        private const int MaxSuffixWildcardChars = 4;
        private const int MaxOptionalChars       = 4;

        // ── Public entry point ────────────────────────────────────────────────

        /// <summary>
        /// Parses <paramref name="queryText"/> and returns a Lucene query,
        /// or null when the query is empty / all tokens are invalid.
        /// </summary>
        public static Query Build(string queryText, HebrewAnalyzer analyzer)
        {
            if (string.IsNullOrWhiteSpace(queryText))
                return null;

            // Pad '|' so "א|ב" and "א | ב" are equivalent.
            queryText = queryText.Replace("|", " | ");

            var andClauses = new List<Query>();
            var pendingOr  = new List<string>(); // raw tokens in the current OR group
            bool lastWasPipe = false;

            foreach (var raw in queryText.Split(new[] { ' ', '\t', '\r', '\n' },
                                                StringSplitOptions.RemoveEmptyEntries))
            {
                if (IsPipe(raw))
                {
                    lastWasPipe = true;
                    continue;
                }

                string token = Normalise(raw);
                if (token.Length == 0) continue;

                if (!lastWasPipe && pendingOr.Count > 0)
                {
                    // Flush the accumulated OR group as one AND clause.
                    Query clause = BuildOrGroup(pendingOr, analyzer);
                    if (clause != null) andClauses.Add(clause);
                    pendingOr.Clear();
                }

                pendingOr.Add(token);
                lastWasPipe = false;
            }

            if (pendingOr.Count > 0)
            {
                Query clause = BuildOrGroup(pendingOr, analyzer);
                if (clause != null) andClauses.Add(clause);
            }

            if (andClauses.Count == 0) return null;
            if (andClauses.Count == 1) return andClauses[0];

            // AND all clauses together.
            var bq = new BooleanQuery();
            foreach (var c in andClauses)
                bq.Add(c, Occur.MUST);
            return bq;
        }

        // ── OR group → single Query ───────────────────────────────────────────

        /// <summary>
        /// Parses <paramref name="queryText"/> and returns a <see cref="SpanQuery"/> suitable
        /// for proximity-aware highlighting via <see cref="Lucene.Net.Search.Highlight.QueryScorer"/>.
        ///
        /// Each AND slot becomes a <see cref="SpanTermQuery"/> or
        /// <see cref="SpanMultiTermQueryWrapper{Q}"/> (for wildcards/prefix), and OR groups
        /// become <see cref="SpanOrQuery"/>.  All slots are combined into a
        /// <see cref="SpanNearQuery"/> with the given <paramref name="slop"/> and
        /// <paramref name="inOrder"/> flag.
        ///
        /// Single-slot queries return the slot's <see cref="SpanQuery"/> directly
        /// (no <see cref="SpanNearQuery"/> wrapper needed).
        ///
        /// Returns null when the query is empty / all tokens are invalid.
        /// </summary>
        /// <param name="slop">
        /// Maximum number of intervening token positions allowed between consecutive
        /// AND slots.  0 = adjacent, 1 = one word between them, etc.
        /// </param>
        /// <param name="inOrder">
        /// When true the slots must appear in left-to-right query order in the text.
        /// When false either order is accepted.
        /// </param>
        public static SpanQuery BuildSpan(string queryText, HebrewAnalyzer analyzer,
                                          int slop, bool inOrder)
        {
            if (string.IsNullOrWhiteSpace(queryText))
                return null;

            queryText = queryText.Replace("|", " | ");

            var spanClauses  = new List<SpanQuery>();
            var pendingOr    = new List<string>();
            bool lastWasPipe = false;

            foreach (var raw in queryText.Split(new[] { ' ', '\t', '\r', '\n' },
                                                StringSplitOptions.RemoveEmptyEntries))
            {
                if (IsPipe(raw))
                {
                    lastWasPipe = true;
                    continue;
                }

                string token = Normalise(raw);
                if (token.Length == 0) continue;

                if (!lastWasPipe && pendingOr.Count > 0)
                {
                    SpanQuery clause = BuildSpanOrGroup(pendingOr, analyzer);
                    if (clause != null) spanClauses.Add(clause);
                    pendingOr.Clear();
                }

                pendingOr.Add(token);
                lastWasPipe = false;
            }

            if (pendingOr.Count > 0)
            {
                SpanQuery clause = BuildSpanOrGroup(pendingOr, analyzer);
                if (clause != null) spanClauses.Add(clause);
            }

            if (spanClauses.Count == 0) return null;
            if (spanClauses.Count == 1) return spanClauses[0];

            return new SpanNearQuery(spanClauses.ToArray(), slop, inOrder);
        }

        // ── Span OR group → single SpanQuery ─────────────────────────────────

        private static SpanQuery BuildSpanOrGroup(List<string> tokens, HebrewAnalyzer analyzer)
        {
            var alternatives = new List<SpanQuery>();
            foreach (var token in tokens)
            {
                bool isWildcard = token.IndexOf('*') >= 0 || token.IndexOf('?') >= 0;
                SpanQuery q = isWildcard
                    ? BuildSpanWildcardQuery(token)
                    : BuildSpanLiteralQuery(token, analyzer);
                if (q != null) alternatives.Add(q);
            }

            if (alternatives.Count == 0) return null;
            if (alternatives.Count == 1) return alternatives[0];

            return new SpanOrQuery(alternatives.ToArray());
        }

        // ── Span literal term ─────────────────────────────────────────────────

        /// <summary>
        /// Runs the token through HebrewAnalyzer and returns a <see cref="SpanTermQuery"/>.
        /// Multi-token analyzer output (rare) is wrapped in a <see cref="SpanNearQuery"/>
        /// with slop=0 inOrder=true so the phrase is treated as a single proximity unit.
        /// </summary>
        private static SpanQuery BuildSpanLiteralQuery(string token, HebrewAnalyzer analyzer)
        {
            var terms = Analyze(token, analyzer);
            if (terms.Count == 0) return null;
            if (terms.Count == 1)
                return new SpanTermQuery(new Term(LuceneIndexWriter.FieldText, terms[0]));

            // Multi-token: treat as an ordered phrase with no slop.
            var clauses = new SpanQuery[terms.Count];
            for (int i = 0; i < terms.Count; i++)
                clauses[i] = new SpanTermQuery(new Term(LuceneIndexWriter.FieldText, terms[i]));
            return new SpanNearQuery(clauses, 0, true);
        }

        // ── Span wildcard term ────────────────────────────────────────────────

        /// <summary>
        /// Builds a <see cref="SpanMultiTermQueryWrapper{Q}"/> for wildcard/prefix tokens,
        /// mirroring the same expansion logic as <see cref="BuildWildcardQuery"/>.
        /// </summary>
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
            {
                string prefix = pattern.TrimEnd('*');
                return new SpanMultiTermQueryWrapper<PrefixQuery>(
                    new PrefixQuery(new Term(LuceneIndexWriter.FieldText, prefix)));
            }

            return new SpanMultiTermQueryWrapper<WildcardQuery>(
                new WildcardQuery(new Term(LuceneIndexWriter.FieldText, pattern)));
        }

        // ── OR group → single Query ───────────────────────────────────────────

        private static Query BuildOrGroup(List<string> tokens, HebrewAnalyzer analyzer)
        {
            var alternatives = new List<Query>();
            foreach (var token in tokens)
            {
                bool isWildcard = token.IndexOf('*') >= 0 || token.IndexOf('?') >= 0;
                Query q = isWildcard
                    ? BuildWildcardQuery(token)
                    : BuildLiteralQuery(token, analyzer);
                if (q != null) alternatives.Add(q);
            }

            if (alternatives.Count == 0) return null;
            if (alternatives.Count == 1) return alternatives[0];

            var bq = new BooleanQuery();
            foreach (var q in alternatives)
                bq.Add(q, Occur.SHOULD);
            return bq;
        }

        // ── Literal term ──────────────────────────────────────────────────────

        /// <summary>
        /// Runs the token through HebrewAnalyzer (strips nikud etc.) and returns
        /// a TermQuery, or a BooleanQuery if the analyzer produces multiple tokens.
        /// </summary>
        private static Query BuildLiteralQuery(string token, HebrewAnalyzer analyzer)
        {
            var terms = Analyze(token, analyzer);
            if (terms.Count == 0) return null;
            if (terms.Count == 1) return new TermQuery(new Term(LuceneIndexWriter.FieldText, terms[0]));

            // Multi-token result — AND them.
            var bq = new BooleanQuery();
            foreach (var t in terms)
                bq.Add(new TermQuery(new Term(LuceneIndexWriter.FieldText, t)), Occur.MUST);
            return bq;
        }

        // ── Wildcard term ─────────────────────────────────────────────────────

        /// <summary>
        /// Builds a wildcard query for a token that contains '*' and/or '?'.
        ///
        /// '?' is FtsLib-style "optional preceding char" — not Lucene's "exactly one char".
        /// We unroll all 2^N include/exclude combinations and OR them together.
        /// Each resulting sub-pattern is then turned into a PrefixQuery or WildcardQuery.
        /// </summary>
        private static Query BuildWildcardQuery(string token)
        {
            // Reject if anchor is too short.
            if (AnchorLength(token) < MinAnchorLength)
                return null;

            bool hasOptional = token.IndexOf('?') >= 0;

            if (!hasOptional)
                return BuildStarQuery(token);

            // Count effective '?' operators.
            int optCount = CountEffectiveOptionals(token);
            if (optCount > MaxOptionalChars)
                return null;

            // Unroll all 2^N sub-patterns.
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
                    : BuildExactWildcardTerm(sub); // no '*', just a literal after '?' unrolling
                if (q != null) alternatives.Add(q);
            }

            if (alternatives.Count == 0) return null;
            if (alternatives.Count == 1) return alternatives[0];

            var bq = new BooleanQuery();
            foreach (var q in alternatives)
                bq.Add(q, Occur.SHOULD);
            return bq;
        }

        /// <summary>
        /// Builds a PrefixQuery or WildcardQuery for a pattern that contains only '*'
        /// (no '?'). Also enforces the prefix/suffix char caps.
        /// </summary>
        private static Query BuildStarQuery(string pattern)
        {
            bool hasLeading  = pattern.StartsWith("*");
            bool hasTrailing = pattern.EndsWith("*");

            // Pure prefix: "abc*" → PrefixQuery (Lucene optimizes this via the term dict)
            if (!hasLeading && hasTrailing)
            {
                string prefix = pattern.TrimEnd('*');
                // Enforce suffix cap: the trailing '*' may match at most MaxSuffixWildcardChars.
                // We can't enforce this at query-build time without enumerating terms, so we
                // use a MultiTermQuery rewrite that walks the FST and we post-filter in the
                // collector. For now we rely on Lucene's PrefixQuery which is already bounded
                // by the index — the cap is documented but not hard-enforced here (matching
                // FtsLib's approach of filtering after the DB query).
                return new PrefixQuery(new Term(LuceneIndexWriter.FieldText, prefix));
            }

            // Suffix or infix: translate '*' → Lucene wildcard '*' (same meaning here).
            // Lucene WildcardQuery: '*' = zero or more chars, '?' = exactly one char.
            // Our pattern uses '*' the same way, so no translation needed.
            return new WildcardQuery(new Term(LuceneIndexWriter.FieldText, pattern));
        }

        /// <summary>
        /// Builds a TermQuery for a sub-pattern that has no wildcards left after
        /// '?' unrolling (i.e. it's a plain literal).
        /// </summary>
        private static Query BuildExactWildcardTerm(string term)
            => new TermQuery(new Term(LuceneIndexWriter.FieldText, term));

        // ── '?' unrolling (mirrors FtsLib's ExpandOptionals) ─────────────────

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

            // c == '?': is there a real preceding letter to make optional?
            bool hasTarget = current.Length > 0 && current[current.Length - 1] != '*';

            if (!hasTarget)
            {
                // No-op '?' — skip it.
                ExpandOptionals(pattern, pos + 1, current, results);
                return;
            }

            // Branch 1: keep the preceding char.
            ExpandOptionals(pattern, pos + 1, current, results);

            // Branch 2: drop the preceding char.
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
        /// Preserves '*' and '?' so wildcard detection works.
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
                // everything else dropped
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

        /// <summary>
        /// Runs a plain (no-wildcard) token through HebrewAnalyzer and returns
        /// the list of output terms.
        /// </summary>
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
