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
        /// Expands <paramref name="term"/> to all index terms within
        /// <paramref name="maxDistance"/> edits.
        /// </summary>
        public static List<string> Expand(
            string                       term,
            int                          maxDistance,
            IReadOnlyList<SegmentHandle> segments)
        {
            if (maxDistance > MaxAllowedDistance) maxDistance = MaxAllowedDistance;
            if (maxDistance < 1)                  maxDistance = 1;

            HashSet<string> candidates;

            if (term.Length >= 4)
            {
                // Standard trigram filter
                var ngrams = BuildNgrams(term, 3);
                candidates = QueryByNgrams(ngrams, segments);
            }
            else if (term.Length == 3)
            {
                // Bigram filter: a 3-char word has only one trigram (itself), which
                // misses 1-edit neighbours. Bigrams give much better recall.
                var ngrams = BuildNgrams(term, 2);
                candidates = QueryByNgrams(ngrams, segments);
            }
            else
            {
                // ≤ 2 chars: no n-grams possible, fall back to infix LIKE scan.
                candidates = QueryBySubstring(term, segments);
            }

            // Phase 2: Levenshtein confirmation
            var results = new List<string>(candidates.Count);
            foreach (var candidate in candidates)
            {
                if (Levenshtein.Distance(term, candidate, maxDistance) <= maxDistance)
                    results.Add(candidate);
            }

            return results;
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
        /// Queries each segment for terms containing at least one of the given n-grams.
        /// Uses UNION strategy (OR across n-grams) to maximise recall.
        /// </summary>
        private static HashSet<string> QueryByNgrams(
            List<string>                 ngrams,
            IReadOnlyList<SegmentHandle> segments)
        {
            var results = new HashSet<string>(System.StringComparer.Ordinal);

            // Build SQL once — parameter names match list indices exactly.
            var sb = new StringBuilder("SELECT term FROM term_index WHERE ");
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
                            results.Add(reader.GetString(0));
                }
            }

            return results;
        }

        /// <summary>
        /// Fallback for terms of 2 chars or fewer: queries with a simple infix LIKE.
        /// </summary>
        private static HashSet<string> QueryBySubstring(
            string                       term,
            IReadOnlyList<SegmentHandle> segments)
        {
            var results = new HashSet<string>(System.StringComparer.Ordinal);
            string pattern = "%" + EscapeLike(term) + "%";

            foreach (var seg in segments)
            {
                using (var cmd = seg.Conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT term FROM term_index WHERE term LIKE @p ESCAPE '\\'";
                    cmd.Parameters.Add("@p", System.Data.DbType.String).Value = pattern;

                    using (var reader = cmd.ExecuteReader())
                        while (reader.Read())
                            results.Add(reader.GetString(0));
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
