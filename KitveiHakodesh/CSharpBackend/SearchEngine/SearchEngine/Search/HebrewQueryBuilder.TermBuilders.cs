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
    /// Individual term query builders (literal, wildcard, fuzzy — both Boolean and Span),
    /// '?' unrolling, and shared text helpers for <see cref="HebrewQueryBuilder"/>.
    /// </summary>
    public static partial class HebrewQueryBuilder
    {
        private const int MinAnchorLength  = 2;
        private const int MaxOptionalChars = 4;

        // ── Literal term (Boolean) ────────────────────────────────────

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

        // ── Wildcard term (Boolean) ───────────────────────────────────

        private static Query BuildWildcardQuery(string token)
        {
            if (AnchorLength(token) < MinAnchorLength) return null;

            if (token.IndexOf('?') < 0)
                return BuildStarQuery(token);

            int optCount = CountEffectiveOptionals(token);
            if (optCount > MaxOptionalChars) return null;

            var subPatterns  = new HashSet<string>(StringComparer.Ordinal);
            ExpandOptionals(token, 0, new StringBuilder(token.Length), subPatterns);

            var alternatives = new List<Query>();
            var seen         = new HashSet<string>(StringComparer.Ordinal);

            foreach (var sub in subPatterns)
            {
                if (!seen.Add(sub)) continue;
                if (AnchorLength(sub) < MinAnchorLength) continue;
                var q = sub.IndexOf('*') >= 0 ? BuildStarQuery(sub) : BuildExactWildcardTerm(sub);
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
            if (!pattern.StartsWith("*") && pattern.EndsWith("*"))
                return new PrefixQuery(new Term(LuceneIndexWriter.FieldText, pattern.TrimEnd('*')));
            return new WildcardQuery(new Term(LuceneIndexWriter.FieldText, pattern));
        }

        private static Query BuildExactWildcardTerm(string term)
            => new TermQuery(new Term(LuceneIndexWriter.FieldText, term));

        // ── Fuzzy term (Boolean) ──────────────────────────────────────

        private static Query BuildFuzzyQuery(string token, int maxEdits)
        {
            if (token.Length < MinFuzzyTermLength) return null;
            if (maxEdits > MaxFuzzyDistance) maxEdits = MaxFuzzyDistance;
            if (maxEdits < 1)                maxEdits = 1;
            return new FuzzyQuery(new Term(LuceneIndexWriter.FieldText, token), maxEdits);
        }

        // ── Literal term (Span) ───────────────────────────────────────

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

        // ── Wildcard term (Span) ──────────────────────────────────────

        private static SpanQuery BuildSpanWildcardQuery(string token)
        {
            if (AnchorLength(token) < MinAnchorLength) return null;

            if (token.IndexOf('?') < 0)
                return BuildSpanStarQuery(token);

            int optCount = CountEffectiveOptionals(token);
            if (optCount > MaxOptionalChars) return null;

            var subPatterns  = new HashSet<string>(StringComparer.Ordinal);
            ExpandOptionals(token, 0, new StringBuilder(token.Length), subPatterns);

            var alternatives = new List<SpanQuery>();
            var seen         = new HashSet<string>(StringComparer.Ordinal);

            foreach (var sub in subPatterns)
            {
                if (!seen.Add(sub)) continue;
                if (AnchorLength(sub) < MinAnchorLength) continue;
                var q = sub.IndexOf('*') >= 0
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
            if (!pattern.StartsWith("*") && pattern.EndsWith("*"))
                return new SpanMultiTermQueryWrapper<PrefixQuery>(
                    new PrefixQuery(new Term(LuceneIndexWriter.FieldText, pattern.TrimEnd('*'))));
            return new SpanMultiTermQueryWrapper<WildcardQuery>(
                new WildcardQuery(new Term(LuceneIndexWriter.FieldText, pattern)));
        }

        // ── Fuzzy term (Span) ─────────────────────────────────────────

        private static SpanQuery BuildSpanFuzzyQuery(string token, int maxEdits)
        {
            if (token.Length < MinFuzzyTermLength) return null;
            if (maxEdits > MaxFuzzyDistance) maxEdits = MaxFuzzyDistance;
            if (maxEdits < 1)                maxEdits = 1;
            return new SpanMultiTermQueryWrapper<FuzzyQuery>(
                new FuzzyQuery(new Term(LuceneIndexWriter.FieldText, token), maxEdits));
        }

        // ── '?' unrolling ─────────────────────────────────────────────

        private static void ExpandOptionals(
            string pattern, int pos, StringBuilder current, HashSet<string> results)
        {
            if (pos == pattern.Length) { results.Add(current.ToString()); return; }

            char c = pattern[pos];
            if (c != '?')
            {
                current.Append(c);
                ExpandOptionals(pattern, pos + 1, current, results);
                current.Length--;
                return;
            }

            bool hasTarget = current.Length > 0 && current[current.Length - 1] != '*';
            if (!hasTarget) { ExpandOptionals(pattern, pos + 1, current, results); return; }

            // Branch 1: keep the preceding char (char is present).
            ExpandOptionals(pattern, pos + 1, current, results);

            // Branch 2: drop the preceding char (char is absent).
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

        // ── Shared helpers ────────────────────────────────────────────

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
