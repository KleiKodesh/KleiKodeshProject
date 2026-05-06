using FtsLib.Indexing;
using FtsLib.Search;
using System.Collections.Generic;
using System.Threading;

namespace FtsLib.SeforimDb
{
    /// <summary>
    /// Executes a parsed query against the index and fetches matching rows
    /// from the seforim database.
    ///
    /// Query syntax (handled by <see cref="QueryParser"/>):
    ///   word        — literal AND term
    ///   word*       — wildcard (prefix / infix / suffix)
    ///   wor?d       — optional char: 'd' before '?' is optional → matches "word" and "wrd"
    ///   word~       — fuzzy, edit distance 1 (default)
    ///   word~2      — fuzzy, edit distance 2
    ///   word~3      — fuzzy, edit distance 3 (maximum)
    ///   a | b       — OR: lines matching a OR b satisfy this AND slot
    ///
    /// Multiple tokens are AND-ed; '|'-separated tokens are OR-ed within one AND slot.
    /// Wildcard/fuzzy tokens are OR-expanded internally; OR groups merge all expansions.
    /// </summary>
    internal static class SearchPipeline
    {
        /// <summary>
        /// Parses <paramref name="query"/>, expands wildcards/fuzzy terms, runs the
        /// intersection search, fetches rows from the DB, and returns results as a
        /// lazy enumerable.
        ///
        /// Each <see cref="SearchResult"/> carries <c>MatchedTerms</c> — the full set
        /// of concrete index terms that were OR-expanded from the query. The snippet
        /// system uses these to highlight the actual matched forms (e.g. ביצחק when
        /// the query was יצחק~) rather than the raw pattern.
        /// </summary>
        /// <param name="query">Raw query string from the user.</param>
        /// <param name="indexPath">Directory containing the segment files.</param>
        /// <param name="dbPath">Path to the seforim SQLite database.</param>
        /// <param name="cap">Maximum results to return. 0 = no cap.</param>
        /// <param name="ct">Cancellation token — checked during expansion, intersection, and DB fetch.</param>
        internal static IEnumerable<SearchResult> Search(
            string            query,
            string            indexPath,
            string            dbPath,
            List<(string dat, string db)> livePaths,
            int               cap = 0,
            CancellationToken ct  = default)
        {
            var parsed = QueryParser.Parse(query);
            if (parsed.IsEmpty) yield break;

            using (var reader = new IndexReader(indexPath, livePaths))
            {
                // Expand each group into a flat list of concrete terms.
                // Within a group terms are OR-ed; across groups they are AND-ed.
                var groups         = new List<IEnumerable<string>>(parsed.Groups.Count);
                var expandedGroups = new List<IReadOnlyCollection<string>>(parsed.Groups.Count);

                foreach (var group in parsed.Groups)
                {
                    ct.ThrowIfCancellationRequested();

                    var groupTerms = ExpandGroup(group, reader, ct, out bool hardMiss);
                    if (hardMiss) yield break;   // a fuzzy alternative had no candidates
                    if (groupTerms.Count == 0) continue; // wildcard anchor too short — skip
                    groups.Add(groupTerms);
                    expandedGroups.Add(groupTerms);
                }

                if (groups.Count == 0) yield break;

                IReadOnlyList<IReadOnlyCollection<string>> matchedGroups = expandedGroups;
                int originalGroupCount = parsed.Groups.Count;

                int yielded = 0;
                using (var db = new ZayitDb(dbPath))
                {
                    foreach (var (lineId, content, bookTitle) in db.FetchSearchResultsStreaming(reader.Search(groups, ct)))
                    {
                        ct.ThrowIfCancellationRequested();
                        yield return new SearchResult(lineId, bookTitle, content, matchedGroups, originalGroupCount);
                        yielded++;
                        if (cap > 0 && yielded >= cap) yield break;
                    }
                }
            }
        }

        /// <summary>
        /// Returns the normalised query terms for a raw query string.
        /// Fuzzy/wildcard markers are stripped — only the base word forms are returned.
        /// Used as a fallback when the caller does not have a <see cref="SearchResult"/>
        /// with pre-computed <c>MatchedTerms</c>.
        /// </summary>
        internal static IReadOnlyList<string> ExtractTerms(string query)
        {
            var parsed = QueryParser.Parse(query);
            var terms  = new List<string>(parsed.Groups.Count);
            foreach (var g in parsed.Groups)
                foreach (var alt in g.Alternatives)
                    terms.Add(alt.Pattern);
            return terms;
        }

        /// <summary>
        /// Returns only the matching line IDs — no database fetch at all.
        /// Use when the caller only needs IDs (counting, on-demand content loading).
        /// </summary>
        internal static IEnumerable<int> SearchIds(
            string            query,
            string            indexPath,
            List<(string dat, string db)> livePaths,
            CancellationToken ct = default)
        {
            var parsed = QueryParser.Parse(query);
            if (parsed.IsEmpty) yield break;

            using (var reader = new IndexReader(indexPath, livePaths))
            {
                var groups = new List<IEnumerable<string>>(parsed.Groups.Count);

                foreach (var group in parsed.Groups)
                {
                    ct.ThrowIfCancellationRequested();

                    var groupTerms = ExpandGroup(group, reader, ct, out bool hardMiss);
                    if (hardMiss) yield break;
                    if (groupTerms.Count == 0) continue;
                    groups.Add(groupTerms);
                }

                if (groups.Count == 0) yield break;

                foreach (var id in reader.Search(groups, ct))
                    yield return id;
            }
        }

        // ── Group expansion ───────────────────────────────────────────

        /// <summary>
        /// Expands all OR alternatives in <paramref name="group"/> into a single
        /// deduplicated list of concrete index terms.
        ///
        /// <paramref name="hardMiss"/> is set to true when a fuzzy alternative
        /// produced zero candidates — the caller should abort the whole query
        /// (no results possible).  Wildcard alternatives that produce zero results
        /// are silently skipped (anchor too short or no matches).
        /// </summary>
        private static List<string> ExpandGroup(
            QueryGroup        group,
            IndexReader       reader,
            CancellationToken ct,
            out bool          hardMiss)
        {
            hardMiss = false;

            // Fast path: single literal alternative (the common case).
            if (group.IsSingle && !group.IsWildcard && !group.IsFuzzy)
                return new List<string> { group.Pattern };

            var seen   = new HashSet<string>(System.StringComparer.Ordinal);
            var result = new List<string>();

            foreach (var alt in group.Alternatives)
            {
                ct.ThrowIfCancellationRequested();

                List<string> expanded;

                if (alt.IsFuzzy)
                {
                    expanded = reader.ExpandFuzzy(alt.Pattern, alt.FuzzyDistance);
                    if (expanded.Count == 0)
                    {
                        // A fuzzy alternative with no candidates is a hard miss:
                        // the user explicitly asked for this word and it isn't in the
                        // index at all, so the AND group can never be satisfied.
                        hardMiss = true;
                        return result;
                    }
                }
                else if (alt.IsWildcard)
                {
                    expanded = reader.ExpandWildcard(alt.Pattern);
                    // Wildcard with no matches: skip this alternative, keep others.
                    if (expanded.Count == 0) continue;
                }
                else
                {
                    expanded = new List<string> { alt.Pattern };
                }

                foreach (var term in expanded)
                    if (seen.Add(term))
                        result.Add(term);
            }

            return result;
        }
    }
}
