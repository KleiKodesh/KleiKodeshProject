using FtsLib.Indexing;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Text;

namespace FtsLib.Search
{
    /// <summary>
    /// Expands a fuzzy query term into the set of index terms within a given
    /// Levenshtein edit distance.
    ///
    /// Algorithm (two-phase):
    ///   1. N-gram filter — generate n-grams of the query term and query each
    ///      segment's term_index with LIKE '%ngram%'. Uses UNION (OR) across all
    ///      n-grams to maximise recall.
    ///
    ///      N-gram size by term length:
    ///        ≤ 2 chars  → substring LIKE scan (no n-grams possible)
    ///        3 chars    → bigrams  (2-char substrings) — a 3-char word has only one
    ///                     trigram (itself), which misses 1-edit neighbours; bigrams
    ///                     give much better recall
    ///        ≥ 4 chars  → trigrams (3-char substrings)
    ///
    ///   2. Levenshtein confirm — filter candidates to those whose edit distance
    ///      from the query term is ≤ maxDistance (clamped to 3).
    ///
    /// Returns a deduplicated list of matching terms across all live segments.
    /// Returns an empty list when nothing matches.
    /// </summary>
    internal static class FuzzyExpander
    {
        /// <summary>Maximum allowed edit distance (hard cap).</summary>
        public const int MaxAllowedDistance = 3;

        /// <summary>
        /// Tuned default for <see cref="MaxExpandedTerms"/> — the chosen sweet spot,
        /// matching the wildcard default.
        /// Measured with FtsLibTest capsweep on the FULL tier (2026-07-17): fuzzy
        /// expansions are naturally small (יצחק~1 = 43 terms, יצחק~2 = 135); the
        /// worst observed case, אמר~2 = 2,030 terms, keeps 99.9998% of its results
        /// at this cap (2,481,178 of 2,481,183 — the closest-first ordering trims
        /// only the farthest 30 terms). Tighter caps lose real results (1000 →
        /// 90.5%) without saving time — fuzzy search cost is postings volume, not
        /// term count — so in practice the cap is a safety valve against
        /// pathological expansions.
        ///
        /// Lucene reference (verified against lucene/main source): FuzzyQuery ships
        /// defaultMaxExpansions = 50 with TopTermsBlendedFreqScoringRewrite — a
        /// bounded priority queue ordered by boost = 1 − editDistance/min(|query|,
        /// |term|) (exact match = 1.0, ties broken by term bytes; FuzzyTermsEnum
        /// even shrinks its Levenshtein automaton as the queue bottom rises).
        /// Terms beyond the top-50 are silently dropped — Lucene accepts fuzzy
        /// recall loss by design. Our contract is never to drop reachable results
        /// without cause, so this valve sits ~40x looser than Lucene's;
        /// the closest-first ordering mirrors Lucene's similarity ordering.
        /// </summary>
        public const int DefaultMaxExpandedTerms = 2000;

        /// <summary>
        /// Maximum number of index terms a single fuzzy token may expand to.
        /// 0 = unlimited. When the expansion exceeds the cap, the closest terms
        /// win: lower edit distance first, then shorter term, then higher doc
        /// count, then ordinal (deterministic across runs and runtimes).
        /// Runtime-settable so hosts and test rigs can tune it.
        /// </summary>
        public static int MaxExpandedTerms = DefaultMaxExpandedTerms;

        /// <summary>
        /// Expands <paramref name="term"/> to all index terms within
        /// <paramref name="maxDistance"/> edits (capped at
        /// <see cref="MaxExpandedTerms"/> terms, closest first).
        /// </summary>
        public static List<string> Expand(
            string                       term,
            int                          maxDistance,
            IReadOnlyList<SegmentHandle> segments,
            TermChunkCache               cache = null)
        {
            if (maxDistance > MaxAllowedDistance) maxDistance = MaxAllowedDistance;
            if (maxDistance < 1)                  maxDistance = 1;

            Dictionary<string, long> candidates;

            if (term.Length >= 4)
            {
                // Standard trigram filter
                var ngrams = BuildNgrams(term, 3);
                candidates = QueryByNgrams(ngrams, segments, cache);
            }
            else if (term.Length == 3)
            {
                // Bigram filter: a 3-char word has only one trigram (itself), which
                // misses 1-edit neighbours. Bigrams give much better recall.
                var ngrams = BuildNgrams(term, 2);
                candidates = QueryByNgrams(ngrams, segments, cache);
            }
            else
            {
                // ≤ 2 chars: no n-grams possible, fall back to infix LIKE scan.
                candidates = QueryBySubstring(term, segments, cache);
            }

            // Phase 2: Levenshtein confirmation. The actual distance is kept so
            // the cap can prefer the closest matches.
            var confirmed = new List<Scored>(candidates.Count);
            foreach (var kv in candidates)
            {
                int d = Levenshtein.Distance(term, kv.Key, maxDistance);
                if (d <= maxDistance)
                    confirmed.Add(new Scored { Term = kv.Key, Distance = d, Count = kv.Value });
            }

            int cap = MaxExpandedTerms;
            if (cap > 0 && confirmed.Count > cap)
            {
                confirmed.Sort(CompareClosestFirst);
                confirmed.RemoveRange(cap, confirmed.Count - cap);
            }

            var results = new List<string>(confirmed.Count);
            foreach (var s in confirmed)
                results.Add(s.Term);
            return results;
        }

        // ── Expansion cap ─────────────────────────────────────────────

        private struct Scored
        {
            public string Term;
            public int    Distance;
            public long   Count;
        }

        /// <summary>
        /// Orders fuzzy candidates: lower edit distance first, then shorter term,
        /// then higher document count, then ordinal — fully deterministic.
        /// </summary>
        private static int CompareClosestFirst(Scored a, Scored b)
        {
            int c = a.Distance.CompareTo(b.Distance);
            if (c != 0) return c;
            c = a.Term.Length.CompareTo(b.Term.Length);
            if (c != 0) return c;
            c = b.Count.CompareTo(a.Count); // higher count first
            if (c != 0) return c;
            return string.CompareOrdinal(a.Term, b.Term);
        }

        // ── N-gram generation ─────────────────────────────────────────

        /// <summary>
        /// Returns the distinct n-grams (substrings of length <paramref name="n"/>)
        /// of <paramref name="s"/> in first-seen order.
        /// Returns an empty list when <c>s.Length &lt; n</c>.
        /// </summary>
        internal static List<string> BuildNgrams(string s, int n)
        {
            var seen = new HashSet<string>();
            var list = new List<string>();
            for (int i = 0; i <= s.Length - n; i++)
            {
                string ng = s.Substring(i, n);
                if (seen.Add(ng)) list.Add(ng);
            }
            return list;
        }

        // ── Segment queries ───────────────────────────────────────────

        /// <summary>
        /// Queries each segment for terms containing at least one of the given n-grams,
        /// mapping each term to its total document count (summed across segments).
        /// Uses UNION strategy (OR across n-grams) to maximise recall.
        /// </summary>
        private static Dictionary<string, long> QueryByNgrams(
            List<string>                 ngrams,
            IReadOnlyList<SegmentHandle> segments,
            TermChunkCache               cache)
        {
            var results = new Dictionary<string, long>(System.StringComparer.Ordinal);

            // Build SQL once — parameter names match list indices exactly.
            // Chunk metadata is piggybacked so resolve skips its point SELECTs (F01).
            var sb = new StringBuilder(
                "SELECT term, skip_offset, skip_count, offset, length, count FROM term_index WHERE ");
            for (int i = 0; i < ngrams.Count; i++)
            {
                if (i > 0) sb.Append(" OR ");
                sb.Append("term LIKE @t").Append(i).Append(" ESCAPE '\\'");
            }
            string sql = sb.ToString();

            foreach (var seg in segments)
            {
                using (var cmd = seg.Conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    // Add parameters in the same order as the SQL — list guarantees this.
                    for (int i = 0; i < ngrams.Count; i++)
                        cmd.Parameters.Add($"@t{i}", System.Data.DbType.String).Value
                            = "%" + EscapeLike(ngrams[i]) + "%";

                    using (var reader = cmd.ExecuteReader())
                        while (reader.Read())
                        {
                            string t     = reader.GetString(0);
                            int    count = reader.GetInt32(5);
                            results.TryGetValue(t, out long total);
                            results[t] = total + count;
                            cache?.Add(t, new SegmentChunk(seg,
                                reader.GetInt64(1),
                                reader.GetInt32(2),
                                reader.GetInt64(3),
                                reader.GetInt32(4),
                                count));
                        }
                }
            }

            return results;
        }

        /// <summary>
        /// Fallback for terms of 2 chars or fewer: queries with a simple infix LIKE.
        /// </summary>
        private static Dictionary<string, long> QueryBySubstring(
            string                       term,
            IReadOnlyList<SegmentHandle> segments,
            TermChunkCache               cache)
        {
            var results = new Dictionary<string, long>(System.StringComparer.Ordinal);
            string pattern = "%" + EscapeLike(term) + "%";

            foreach (var seg in segments)
            {
                using (var cmd = seg.Conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT term, skip_offset, skip_count, offset, length, count " +
                        "FROM term_index WHERE term LIKE @p ESCAPE '\\'";
                    cmd.Parameters.Add("@p", System.Data.DbType.String).Value = pattern;

                    using (var reader = cmd.ExecuteReader())
                        while (reader.Read())
                        {
                            string t     = reader.GetString(0);
                            int    count = reader.GetInt32(5);
                            results.TryGetValue(t, out long total);
                            results[t] = total + count;
                            cache?.Add(t, new SegmentChunk(seg,
                                reader.GetInt64(1),
                                reader.GetInt32(2),
                                reader.GetInt64(3),
                                reader.GetInt32(4),
                                count));
                        }
                }
            }

            return results;
        }

        // ── Helpers ───────────────────────────────────────────────────

        private static string EscapeLike(string s)
        {
            // Escape SQLite LIKE special characters
            return s.Replace("\\", "\\\\")
                    .Replace("%",  "\\%")
                    .Replace("_",  "\\_");
        }
    }
}
