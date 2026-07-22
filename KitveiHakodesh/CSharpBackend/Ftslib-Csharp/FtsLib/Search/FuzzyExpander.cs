using FtsLib.Indexing;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
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
    ///      SOUNDNESS RULE (q-gram lower bound): a term within d edits of an
    ///      L-char query shares at least (L - n + 1) - d·n of the query's
    ///      n-grams — each edit destroys at most n grams. Requiring ≥1 shared
    ///      gram is therefore only sound when L ≥ n·(d+1). Below that the filter
    ///      EXCLUDES true matches: e.g. trigrams on a 4-5 letter word at d=1 —
    ///      one mid-word substitution destroys every trigram, so distance-1
    ///      neighbours of the dominant Hebrew word length silently vanished
    ///      (שלום~1 could never find שלים).
    ///
    ///      N-gram size by term length L and distance d:
    ///        L ≥ 3(d+1)  → trigrams
    ///        L ≥ 2(d+1)  → bigrams
    ///        otherwise   → no gram filter is sound; scan the ±d length window
    ///                      (a Levenshtein-d match differs by ≤ d chars in length)
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

            // Pick the largest n-gram size that is SOUND for this term length and
            // distance (see the class doc's q-gram lower bound): requiring ≥1
            // shared gram only guarantees no false negatives when L ≥ n·(d+1).
            // When no gram size qualifies, scan the ±d length window with no gram
            // filter at all — the Levenshtein confirm below does the real work.
            int n = 0;
            if      (term.Length >= 3 * (maxDistance + 1)) n = 3;
            else if (term.Length >= 2 * (maxDistance + 1)) n = 2;

            Dictionary<string, long> candidates = n > 0
                ? QueryByNgrams(BuildNgrams(term, n), segments, cache)
                : QueryByLengthWindow(term, maxDistance, segments, cache);

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
        /// <summary>Above this many union candidates the full LIKE scan is comparable, so the
        /// sidecar is skipped and the scan runs (mirrors HebrewWildcardExpander's routing).</summary>
        private const int TrigramCandidateCap = 8000;

        private static Dictionary<string, long> QueryByNgrams(
            List<string>                 ngrams,
            IReadOnlyList<SegmentHandle> segments,
            TermChunkCache               cache)
        {
            var results = new Dictionary<string, long>(System.StringComparer.Ordinal);

            // Whether the sidecar is usable: n-grams must be trigrams (the sidecar's key length).
            // The 3-char-word case uses BIGRAMS (see Expand) which the trigram sidecar cannot
            // serve — those fall through to the scan below, unchanged.
            bool ngramsAreTrigrams = ngrams.Count > 0 && ngrams[0].Length == TrigramIndex.MinRun;

            // Build the OR-LIKE confirm SQL once — parameter names match list indices exactly.
            // Chunk metadata is piggybacked so resolve skips its point SELECTs (F01).
            var sb = new StringBuilder(
                "SELECT term, skip_offset, skip_count, offset, length, count FROM term_index WHERE ");
            int likeStart = sb.Length;
            for (int i = 0; i < ngrams.Count; i++)
            {
                if (i > 0) sb.Append(" OR ");
                sb.Append("term LIKE @t").Append(i).Append(" ESCAPE '\\'");
            }
            string likeClause = sb.ToString(likeStart, sb.Length - likeStart);
            string scanSql = sb.ToString();

            foreach (var seg in segments)
            {
                using (var cmd = seg.Conn.CreateCommand())
                {
                    // Trigram route: UNION the sidecar posting lists (OR semantics), then let
                    // SQLite confirm the SAME OR-LIKE on just those rowids. Identical results,
                    // no full scan. Falls back to the scan if the sidecar is absent, the n-grams
                    // aren't trigrams, or the union isn't selective enough.
                    bool routed = ngramsAreTrigrams && seg.Trigram != null &&
                                  TrySetupNgramConfirm(seg.Trigram, ngrams, likeClause, cmd);
                    if (!routed)
                        cmd.CommandText = scanSql;

                    // Add parameters in the same order as the SQL — list guarantees this.
                    for (int i = 0; i < ngrams.Count; i++)
                        cmd.Parameters.Add($"@t{i}", SqliteType.Text).Value
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
        /// Sets <paramref name="cmd"/> to a rowid-restricted OR-LIKE confirm over the UNION of the
        /// trigrams' sidecar posting lists, or returns false to use the full scan. The union is a
        /// superset of the OR-LIKE matches (a term matching %g% contains g, so it is in g's
        /// posting list), and the same OR-LIKE confirms — so results are identical.
        /// </summary>
        private static bool TrySetupNgramConfirm(TrigramIndex.Reader tr, List<string> ngrams,
                                                 string likeClause, SqliteCommand cmd)
        {
            var union = new HashSet<int>();
            foreach (var g in ngrams)
            {
                int[] l = tr.Lookup(g);
                for (int i = 0; i < l.Length; i++) union.Add(l[i]);
                if (union.Count > TrigramCandidateCap) return false; // not selective enough → scan
            }

            const string cols = "SELECT term, skip_offset, skip_count, offset, length, count FROM term_index WHERE ";
            if (union.Count == 0) { cmd.CommandText = cols + "0"; return true; } // no candidates

            var sb = new StringBuilder(cols, cols.Length + union.Count * 7 + likeClause.Length + 16);
            sb.Append("rowid IN (");
            bool first = true;
            foreach (int id in union) { if (!first) sb.Append(','); first = false; sb.Append(id); }
            sb.Append(") AND (").Append(likeClause).Append(')');
            cmd.CommandText = sb.ToString();
            return true;
        }

        /// <summary>
        /// Prefilter-free candidate source for terms too short for any SOUND n-gram
        /// filter: every index term whose character length is within ±maxDistance of
        /// the query's (a Levenshtein-d match can differ by at most d characters in
        /// length, so the window loses nothing). The Levenshtein confirm in
        /// <see cref="Expand"/> does the real filtering.
        ///
        /// Replaces the old substring-LIKE fallback, which required candidates to
        /// CONTAIN the whole query — "אב~1" could match superstrings like "אבג" but
        /// never the equally-close substitution "אג".
        /// </summary>
        private static Dictionary<string, long> QueryByLengthWindow(
            string                       term,
            int                          maxDistance,
            IReadOnlyList<SegmentHandle> segments,
            TermChunkCache               cache)
        {
            var results = new Dictionary<string, long>(System.StringComparer.Ordinal);
            int lo = term.Length - maxDistance; if (lo < 1) lo = 1;
            int hi = term.Length + maxDistance;

            foreach (var seg in segments)
            {
                using (var cmd = seg.Conn.CreateCommand())
                {
                    // SQLite length() counts characters for TEXT; index terms are
                    // BMP-only (Hebrew + ASCII letters), so it equals C# .Length.
                    cmd.CommandText =
                        "SELECT term, skip_offset, skip_count, offset, length, count " +
                        "FROM term_index WHERE length(term) BETWEEN @lo AND @hi";
                    cmd.Parameters.Add("@lo", SqliteType.Integer).Value = lo;
                    cmd.Parameters.Add("@hi", SqliteType.Integer).Value = hi;

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
