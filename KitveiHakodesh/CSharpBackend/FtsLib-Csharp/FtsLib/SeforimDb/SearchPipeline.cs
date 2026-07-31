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
        /// <param name="filterIds">Optional line-ID keep-set: only these IDs can be
        /// returned. Null = no filtering; an empty collection matches nothing.</param>
        /// <param name="ct">Cancellation token — checked during expansion, intersection, and DB fetch.</param>
        internal static IEnumerable<SearchResult> Search(
            string            query,
            string            indexPath,
            string            dbPath,
            List<(string dat, string db)> livePaths,
            SearchLease       lease,
            int               cap = 0,
            bool              expandKetiv = false,
            IEnumerable<int>  filterIds = null,
            CancellationToken ct  = default)
        {
            var parsed = QueryParser.Parse(query);
            if (parsed.IsEmpty)
            {
                lease?.Dispose();
                yield break;
            }

            var filter = BuildFilter(filterIds);

            // Phase 1 — under the search lease: everything that touches segment
            // files (expansion + intersection). The matching IDs are materialized
            // so the lease is released BEFORE the DB content fetch below. Holding
            // it across the fetch (minutes for broad queries) would let a queued
            // merge commit block every new search behind the write lock for the
            // whole stream — the lease must only live for milliseconds-to-seconds.
            var ids = new List<int>();
            IReadOnlyList<IReadOnlyCollection<string>> matchedGroups;
            int originalGroupCount = parsed.Groups.Count;
            DocSourceMap docMap;

            using (var reader = new IndexReader(indexPath, livePaths, lease))
            {
                var groups         = new List<IEnumerable<string>>(parsed.Groups.Count);
                var expandedGroups = new List<IReadOnlyCollection<string>>(parsed.Groups.Count);

                foreach (var group in parsed.Groups)
                {
                    ct.ThrowIfCancellationRequested();

                    var groupTerms = ExpandGroup(group, reader, expandKetiv, ct, out bool hardMiss);
                    if (hardMiss) yield break;
                    if (groupTerms.Count == 0) continue;
                    groups.Add(groupTerms);
                    expandedGroups.Add(groupTerms);
                }

                if (groups.Count == 0) yield break;

                matchedGroups = expandedGroups;

                foreach (var id in reader.Search(groups, filter, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    ids.Add(id);
                    if (cap > 0 && ids.Count >= cap) break;
                }

                // Snapshot the docId→corpus map while the segment files are still
                // leased — after the reader disposes, a merge may delete them.
                docMap = reader.GetDocSourceMap();
            }

            if (ids.Count == 0) yield break;

            // Prepared ONCE per query: the snippet-side term→group map, shared by
            // every result row (immutable — safe across snippet threads). Building
            // it per line was O(#expanded terms) per snippet.
            var prepared = FtsLib.Snippets.PreparedQueryGroups.FromGroups(matchedGroups);

            // Phase 2 — lease released: stream content rows from the DB, routed by
            // corpus. Result ids are docIds; SplitBySource returns null when every
            // id is library-identity (docId == line.id) — the fast path, which is
            // byte-identical to the single-corpus fetch. Otherwise each contiguous
            // run is fetched from its corpus DB by source-local id and yielded
            // under its app-visible docId, preserving the ascending-id contract.
            var runs = docMap.SplitBySource(ids);
            if (runs == null)
            {
                using (var db = new ZayitDb(dbPath))
                {
                    foreach (var (lineId, content, bookTitle) in db.FetchSearchResultsStreaming(ids))
                    {
                        ct.ThrowIfCancellationRequested();
                        yield return new SearchResult(lineId, bookTitle, content, matchedGroups, originalGroupCount, prepared);
                    }
                }
                yield break;
            }

            foreach (var run in runs)
            {
                // Only the library corpus has a content DB wired up so far. A
                // non-library run means the index was built with a multi-corpus
                // DocSourceMap this pipeline doesn't know how to fetch for yet —
                // fail loudly rather than mislabel content.
                if (run.Source != DocSourceMap.LibrarySource)
                    throw new System.NotSupportedException(
                        $"FTS: no content database registered for corpus {run.Source} " +
                        $"({run.Count} result(s)); only the library (source 0) is wired up.");

                long offset = run.Offset;
                using (var db = new ZayitDb(dbPath))
                {
                    foreach (var (srcId, content, bookTitle) in
                             db.FetchSearchResultsStreaming(TranslateRun(ids, run)))
                    {
                        ct.ThrowIfCancellationRequested();
                        int docId = (int)(srcId - offset); // back to the app-visible id
                        yield return new SearchResult(docId, bookTitle, content, matchedGroups, originalGroupCount, prepared);
                    }
                }
            }
        }

        /// <summary>Lazily translates one source run's docIds to source-local ids.</summary>
        private static IEnumerable<int> TranslateRun(List<int> ids, DocSourceMap.SourceRun run)
        {
            for (int i = 0; i < run.Count; i++)
                yield return (int)(ids[run.Start + i] + run.Offset);
        }

        /// <summary>
        /// Same result set as <see cref="Search"/>, but Phase 2 (the DB content fetch —
        /// the dominant cost of a broad query) runs across up to <paramref name="maxDop"/>
        /// connections in parallel and the results are returned as an ordered array
        /// (ascending line ID), not a lazy stream. Phase 1 (expansion + intersection,
        /// under the search lease) is unchanged; the lease is released before the fetch.
        /// </summary>
        internal static SearchResult[] SearchParallel(
            string            query,
            string            indexPath,
            string            dbPath,
            List<(string dat, string db)> livePaths,
            SearchLease       lease,
            int               maxDop,
            bool              expandKetiv = false,
            IEnumerable<int>  filterIds = null,
            CancellationToken ct  = default)
        {
            var parsed = QueryParser.Parse(query);
            if (parsed.IsEmpty)
            {
                lease?.Dispose();
                return System.Array.Empty<SearchResult>();
            }

            var filter = BuildFilter(filterIds);

            var ids = new List<int>();
            IReadOnlyList<IReadOnlyCollection<string>> matchedGroups = null;
            int originalGroupCount = parsed.Groups.Count;
            DocSourceMap docMap;

            // Phase 1 — under the lease (see Search for the release-before-fetch rationale).
            using (var reader = new IndexReader(indexPath, livePaths, lease))
            {
                var groups         = new List<IEnumerable<string>>(parsed.Groups.Count);
                var expandedGroups = new List<IReadOnlyCollection<string>>(parsed.Groups.Count);

                foreach (var group in parsed.Groups)
                {
                    ct.ThrowIfCancellationRequested();

                    var groupTerms = ExpandGroup(group, reader, expandKetiv, ct, out bool hardMiss);
                    if (hardMiss) return System.Array.Empty<SearchResult>();
                    if (groupTerms.Count == 0) continue;
                    groups.Add(groupTerms);
                    expandedGroups.Add(groupTerms);
                }

                if (groups.Count == 0) return System.Array.Empty<SearchResult>();

                matchedGroups = expandedGroups;

                foreach (var id in reader.Search(groups, filter, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    ids.Add(id);
                }

                // Snapshot the docId→corpus map while segment files are leased
                // (see Search).
                docMap = reader.GetDocSourceMap();
            }

            if (ids.Count == 0) return System.Array.Empty<SearchResult>();

            // Prepared ONCE per query (see Search) — shared by every fetched row.
            var prepared = FtsLib.Snippets.PreparedQueryGroups.FromGroups(matchedGroups);

            // Phase 2 — lease released: parallel content fetch across connections,
            // routed by corpus (see Search for the run semantics). Null runs =
            // pure library-identity — the fetch below is byte-identical to the
            // single-corpus path.
            var runs = docMap.SplitBySource(ids);
            SearchResult[] arr;
            if (runs == null)
            {
                arr = ZayitDb.FetchSearchResultsParallel(
                    dbPath, ids, matchedGroups, originalGroupCount, prepared, maxDop, ct);
            }
            else
            {
                arr = new SearchResult[ids.Count];
                int write = 0;
                foreach (var run in runs)
                {
                    if (run.Source != DocSourceMap.LibrarySource)
                        throw new System.NotSupportedException(
                            $"FTS: no content database registered for corpus {run.Source} " +
                            $"({run.Count} result(s)); only the library (source 0) is wired up.");

                    var srcIds = new List<int>(run.Count);
                    for (int i = 0; i < run.Count; i++)
                        srcIds.Add((int)(ids[run.Start + i] + run.Offset));

                    var part = ZayitDb.FetchSearchResultsParallel(
                        dbPath, srcIds, matchedGroups, originalGroupCount, prepared, maxDop, ct);

                    // Results carry source-local ids — re-label with the app-visible
                    // docId. Offset 0 (library) needs no re-wrap.
                    if (run.Offset == 0)
                    {
                        for (int i = 0; i < part.Length; i++) arr[write++] = part[i];
                    }
                    else
                    {
                        for (int i = 0; i < part.Length; i++)
                        {
                            var r = part[i];
                            arr[write++] = r == null ? null : new SearchResult(
                                (int)(r.LineId - run.Offset), r.BookTitle, r.Content,
                                matchedGroups, originalGroupCount, prepared);
                        }
                    }
                }
                if (write < arr.Length)
                    System.Array.Resize(ref arr, write);
            }

            // Every matched id comes from the line table, so each slot is filled; guard
            // against a null (row vanished mid-search) by compacting, preserving order.
            int nulls = 0;
            for (int i = 0; i < arr.Length; i++) if (arr[i] == null) nulls++;
            if (nulls == 0) return arr;

            var compact = new SearchResult[arr.Length - nulls];
            int j = 0;
            for (int i = 0; i < arr.Length; i++) if (arr[i] != null) compact[j++] = arr[i];
            return compact;
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
            SearchLease       lease,
            bool              expandKetiv = false,
            IEnumerable<int>  filterIds = null,
            CancellationToken ct = default)
        {
            var parsed = QueryParser.Parse(query);
            if (parsed.IsEmpty)
            {
                lease?.Dispose();
                yield break;
            }

            var filter = BuildFilter(filterIds);

            // Materialize under the lease, yield after releasing it — a slow
            // consumer of this enumerable must not keep segment files leased
            // (see Search for the full rationale).
            var ids = new List<int>();
            using (var reader = new IndexReader(indexPath, livePaths, lease))
            {
                var groups = new List<IEnumerable<string>>(parsed.Groups.Count);

                foreach (var group in parsed.Groups)
                {
                    ct.ThrowIfCancellationRequested();

                    var groupTerms = ExpandGroup(group, reader, expandKetiv, ct, out bool hardMiss);
                    if (hardMiss) yield break;
                    if (groupTerms.Count == 0) continue;
                    groups.Add(groupTerms);
                }

                if (groups.Count == 0) yield break;

                foreach (var id in reader.Search(groups, filter, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    ids.Add(id);
                }
            }

            foreach (var id in ids)
                yield return id;
        }

        // ── Filter construction ───────────────────────────────────────

        /// <summary>
        /// Materializes caller-supplied line IDs into a <see cref="RoaringBitmap"/>
        /// keep-set. Null in → null out (no filtering). IDs may arrive in any
        /// order and contain duplicates. An empty collection yields an empty
        /// bitmap, which matches nothing.
        ///
        /// Negative IDs are dropped: no indexed line ever has one, and
        /// RoaringBitmap orders values as unsigned — a negative ID would sort
        /// AFTER every real ID, breaking the ascending-order contract the
        /// intersection leap-frog depends on (it would spin forever).
        /// </summary>
        private static RoaringBitmap BuildFilter(IEnumerable<int> filterIds)
        {
            if (filterIds == null) return null;
            var bitmap = new RoaringBitmap();
            foreach (var id in filterIds)
                if (id >= 0)
                    bitmap.Add(id);
            return bitmap;
        }

        // ── Group expansion ───────────────────────────────────────────

        /// <summary>
        /// Expands all OR alternatives in <paramref name="group"/> into a single
        /// deduplicated list of concrete index terms.
        ///
        /// Zero-expansion semantics (one consistent rule): an alternative that
        /// expands to nothing simply contributes nothing to the group. If the WHOLE
        /// group ends up empty even though it contained at least one real constraint
        /// (a literal, fuzzy, grammar, or supported-wildcard alternative), the group
        /// is unsatisfiable and <paramref name="hardMiss"/> is set — AND semantics
        /// require the whole query to return no results. Only a group consisting
        /// entirely of REJECTED wildcard patterns (unsupported: anchor too short /
        /// too many '?' operators — see <see cref="HebrewWildcardExpander"/>) is
        /// skipped as an AND slot, the documented behaviour for unsupported patterns.
        ///
        /// (Previously an empty FUZZY alternative aborted the whole query even when
        /// an OR sibling had matches, while empty wildcard/grammar alternatives
        /// silently DROPPED the AND slot — broadening the query instead of
        /// returning the correct empty result.)
        /// </summary>
        private static List<string> ExpandGroup(
            QueryGroup        group,
            IndexReader       reader,
            bool              expandKetiv,
            CancellationToken ct,
            out bool          hardMiss)
        {
            hardMiss = false;

            // Fast path: single literal alternative (the common case).
            if (group.IsSingle && !group.IsWildcard && !group.IsFuzzy && !group.IsGrammar)
            {
                var result = new List<string> { group.Pattern };
                // כתיב expansion: add spelling variants as additional OR alternatives.
                // Only for plain literals — wildcards and fuzzy already cover variants.
                if (expandKetiv)
                {
                    foreach (var variant in KetivExpander.Expand(group.Pattern))
                        result.Add(variant);
                }
                return result;
            }

            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            var list = new List<string>();

            // True once any alternative was a genuine constraint (everything except
            // a REJECTED wildcard pattern). Literals count even when absent from the
            // index — the intersection layer's missing-term contract yields the
            // correct empty result for those.
            bool anyRealConstraint = false;

            foreach (var alt in group.Alternatives)
            {
                ct.ThrowIfCancellationRequested();

                List<string> expanded;

                if (alt.IsFuzzy)
                {
                    anyRealConstraint = true;
                    expanded = reader.ExpandFuzzy(alt.Pattern, alt.FuzzyDistance);
                    if (expanded.Count == 0) continue;
                }
                else if (alt.IsWildcard)
                {
                    expanded = reader.ExpandWildcard(alt.Pattern, out bool rejected);
                    if (!rejected) anyRealConstraint = true;
                    if (expanded.Count == 0) continue;
                }
                else if (alt.IsGrammar)
                {
                    anyRealConstraint = true;
                    expanded = reader.ExpandGrammar(alt.Pattern,
                                                    alt.GrammarExpandPrefixes,
                                                    alt.GrammarExpandSuffixes);
                    if (expanded.Count == 0) continue;
                }
                else
                {
                    // Literal alternative — add כתיב variants if requested.
                    anyRealConstraint = true;
                    expanded = new List<string> { alt.Pattern };
                    if (expandKetiv)
                        foreach (var variant in KetivExpander.Expand(alt.Pattern))
                            expanded.Add(variant);
                }

                foreach (var term in expanded)
                    if (seen.Add(term))
                        list.Add(term);
            }

            // A group whose real constraints all matched nothing is unsatisfiable.
            if (list.Count == 0 && anyRealConstraint)
                hardMiss = true;

            return list;
        }
    }
}
