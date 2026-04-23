using MinimalIndexer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace BloomSearchEngineLib
{
    public sealed class BloomFilterSearcher
    {
        private readonly string _id;
        private readonly ZayitDbConnectionPool _pool;

        /// <param name="pool">
        /// Shared connection pool. If null a temporary single-use connection is opened
        /// per search (original behaviour — useful for one-off calls and tests).
        /// </param>
        public BloomFilterSearcher(string id = "lines", ZayitDbConnectionPool pool = null)
        {
            _id = id;
            _pool = pool;
        }

        public IEnumerable<SearchResultItem> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) yield break;

            var rawTerms = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var termList = new List<string>(rawTerms.Length);
            foreach (var raw in rawTerms)
            {
                var normalized = TextNormalizer.NormalizeQueryTerm(raw);
                if (normalized != null) termList.Add(normalized);
            }
            var terms = termList.ToArray();
            if (terms.Length == 0) yield break;

            var swTotal = Stopwatch.StartNew();

            using (var reader = new BloomFilterCollectionReader(_id))
            {
                // ── Bloom scan ────────────────────────────────────────────────
                // Enumerate eagerly so we can time the scan separately from DB work.
                var swScan = Stopwatch.StartNew();
                var hits = new List<SearchResult>(reader.Search(terms));
                swScan.Stop();

                int perfectHits  = hits.Count(h => h.Score == terms.Length);
                int partialHits  = hits.Count - perfectHits;
                Console.WriteLine("[Search] query=\"{0}\"  bloom_scan={1}ms  hits={2} (perfect={3} partial={4})",
                    query, swScan.ElapsedMilliseconds, hits.Count, perfectHits, partialHits);

                if (hits.Count == 0) yield break;

                // ── Merge consecutive hit chunks ──────────────────────────────
                // Hits whose line-ID ranges are within GAP lines of each other are
                // merged into one wider range. This turns N small range queries into
                // one larger one, dramatically reducing SQLite round-trips for common
                // words where hits are dense.
                //
                // GAP = 0  → only merge chunks that are literally adjacent (LastLineId+1 == next FirstLineId)
                // GAP = 50 → merge chunks with up to 50 lines of gap between them
                // Balanced gap for optimal chunk size and processing speed.
                const int MergeGap = 50;
                var mergedHits = MergeHits(hits, MergeGap);

                int mergedCount = mergedHits.Count;
                Console.WriteLine("[Search] merged {0} hits → {1} ranges (gap={2})", hits.Count, mergedCount, MergeGap);

                // ── DB verification ───────────────────────────────────────────
                // Phase 1 (per chunk): read lines, run match, collect perfect hits.
                // Phase 2 (flush): one GetLineMetadataBatch call for accumulated hits, yield results.
                //
                // Flush triggers (whichever comes first):
                //   - FLUSH_HIT_THRESHOLD hits accumulated → flush immediately for low first-result latency
                //   - META_BATCH_SIZE chunks processed → flush periodically to bound memory
                // The hit threshold drives first-result latency; the chunk threshold bounds memory
                // for dense queries where almost every chunk has hits.
                const int FirstFlushThreshold = 2;   // flush the first batch after ~2 perfect matches for faster initial results
                const int MetaBatchSize       = 25;  // more frequent flushes for smoother streaming
                const int MaxPendingHits      = 20;  // flush sooner for better responsiveness

                var partialMatches = new TopNPartialMatches(100);
                int perfectCount   = 0;
                int chunksProcessed = 0;

                long msDbRead    = 0;
                long msMatchWork = 0;
                long msMeta      = 0;
                bool firstResult  = true;
                bool firstFlushed = false; // tracks whether the first flush has happened

                var pendingLineIds   = new List<int>(MetaBatchSize * 2);
                var pendingMatchInfo = new List<(int lineId, MatchInfo match, string norm)>(MetaBatchSize * 2);

                bool ownsConnection = _pool == null;
                ZayitDbConnectionPool.Lease lease = _pool != null ? _pool.Acquire() : null;
                ZayitDbManager tempDb = ownsConnection ? new ZayitDbManager() : null;
                ZayitDbManager db = lease != null ? lease.Db : tempDb;
                try
                {
                    foreach (var hit in mergedHits)
                    {
                        CollectChunkMatches(db, hit, terms, terms.Length, pendingLineIds, pendingMatchInfo,
                            partialMatches, ref msDbRead, ref msMatchWork);
                        chunksProcessed++;

                        bool shouldFlush = (!firstFlushed && pendingLineIds.Count >= FirstFlushThreshold)
                            || chunksProcessed % MetaBatchSize == 0
                            || pendingLineIds.Count >= MaxPendingHits;

                        if (shouldFlush && pendingLineIds.Count > 0)
                        {
                            firstFlushed = true;
                            foreach (var item in FlushMetaBatch(db, pendingLineIds, pendingMatchInfo, ref msMeta))
                            {
                                if (firstResult)
                                {
                                    Console.WriteLine("[Search] first_result_latency={0}ms  chunks_before_first={1}",
                                        swTotal.ElapsedMilliseconds, chunksProcessed);
                                    firstResult = false;
                                }
                                perfectCount++;
                                yield return item;
                            }
                        }
                    }

                    // Flush any remaining pending hits.
                    if (pendingLineIds.Count > 0)
                    {
                        foreach (var item in FlushMetaBatch(db, pendingLineIds, pendingMatchInfo, ref msMeta))
                        {
                            if (firstResult)
                            {
                                Console.WriteLine("[Search] first_result_latency={0}ms  chunks_before_first={1}",
                                    swTotal.ElapsedMilliseconds, chunksProcessed);
                                firstResult = false;
                            }
                            perfectCount++;
                            yield return item;
                        }
                    }
                }
                finally
                {
                    if (lease != null) lease.Dispose();
                    if (tempDb != null) tempDb.Dispose();
                }                Console.WriteLine("[Search] db_phase={0}ms  chunks={1}  perfect={2}  partial_candidates={3}",
                    swTotal.ElapsedMilliseconds - swScan.ElapsedMilliseconds,
                    chunksProcessed, perfectCount, partialMatches.Count);
                Console.WriteLine("[Search] db_breakdown  read={0}ms  match={1}ms  meta={2}ms",
                    msDbRead, msMatchWork, msMeta);

                // ── Partial hydration ─────────────────────────────────────────
                var swPartial = Stopwatch.StartNew();
                foreach (var item in HydratePartialMatches(partialMatches, perfectCount, _pool))
                    yield return item;
                swPartial.Stop();

                swTotal.Stop();
                Console.WriteLine("[Search] partial_hydration={0}ms  total={1}ms  RAM={2:F1}MB",
                    swPartial.ElapsedMilliseconds, swTotal.ElapsedMilliseconds,
                    GC.GetTotalMemory(false) / (1024.0 * 1024.0));
            }
        }

        private static IEnumerable<SearchResultItem> HydratePartialMatches(
            TopNPartialMatches partialMatches, int perfectCount, ZayitDbConnectionPool pool)
        {
            if (perfectCount >= 100) yield break;

            int remaining = Math.Min(100 - perfectCount, partialMatches.Count);
            var partials = partialMatches.GetTop(remaining);
            if (partials.Length == 0) yield break;

            ZayitDbConnectionPool.Lease lease = pool != null ? pool.Acquire() : null;
            ZayitDbManager tempDb = pool == null ? new ZayitDbManager() : null;
            ZayitDbManager db = lease != null ? lease.Db : tempDb;
            try
            {
                var lineIds = new List<int>(partials.Length);
                foreach (var p in partials) lineIds.Add(p.LineId);

                var metadata = db.GetLineMetadataBatch(lineIds);

                foreach (var p in partials)
                {
                    var content = db.GetLineContent(p.LineId).NormalizeText();
                    if (!metadata.TryGetValue(p.LineId, out var meta)) continue;
                    yield return new SearchResultItem
                    {
                        LineId = p.LineId,
                        BookId = meta.bookId,
                        BookTitle = meta.bookTitle,
                        TocText = meta.tocText,
                        Score = p.Score,
                        ProximityScore = p.ProximityScore,
                        Snippet = SearchEngineMatcher.ExtractSnippetFromCluster(content, p.ClusterStart, p.ClusterEnd)
                    };
                }
            }
            finally
            {
                if (lease != null) lease.Dispose();
                if (tempDb != null) tempDb.Dispose();
            }
        }

        /// <summary>
        /// Merges Bloom hit ranges that are within <paramref name="gap"/> lines of each other
        /// into a single wider range, reducing the number of SQLite round-trips.
        ///
        /// Hits are sorted by FirstLineId before merging so the output is in DB order.
        /// The merged hit's Score is the minimum of the constituent scores — conservative,
        /// ensures we don't skip lines that only partially matched in the Bloom scan.
        /// </summary>
        private static List<SearchResult> MergeHits(List<SearchResult> hits, int gap)
        {
            if (hits.Count <= 1) return hits;

            // Sort by FirstLineId so we can do a single linear pass.
            hits.Sort((a, b) => a.FirstLineId.CompareTo(b.FirstLineId));

            var merged = new List<SearchResult>(hits.Count);
            var current = hits[0];

            for (int i = 1; i < hits.Count; i++)
            {
                var next = hits[i];
                // Merge if the next range starts within gap lines of where the current ends.
                if (next.FirstLineId <= current.LastLineId + gap)
                {
                    // Extend the current range and take the minimum score.
                    current = new SearchResult
                    {
                        Id           = current.Id,
                        FirstLineId  = current.FirstLineId,
                        LastLineId   = Math.Max(current.LastLineId, next.LastLineId),
                        Score        = Math.Min(current.Score, next.Score)
                    };
                }
                else
                {
                    merged.Add(current);
                    current = next;
                }
            }
            merged.Add(current);
            return merged;
        }

        /// <summary>
        /// Phase 1: read lines for one chunk, run match, accumulate perfect hits into
        /// pending lists. Does NOT touch metadata — that is deferred to FlushMetaBatch.
        /// </summary>
        private static void CollectChunkMatches(
            ZayitDbManager db,
            SearchResult hit,
            string[] terms,
            int maxScore,
            List<int> pendingLineIds,
            List<(int lineId, MatchInfo match, string norm)> pendingMatchInfo,
            TopNPartialMatches partialMatches,
            ref long msDbRead,
            ref long msMatchWork)
        {
            var sw = Stopwatch.StartNew();

            var lines = new List<(int lineId, string content)>();
            foreach (var row in db.GetLineContentsChunk(hit.FirstLineId, hit.LastLineId))
                lines.Add(row);

            sw.Stop();
            msDbRead += sw.ElapsedMilliseconds;
            sw.Restart();

            foreach (var (lineId, content) in lines)
            {
                if (!TextNormalizer.RawContentMightMatch(content, terms)) continue;

                string norm = content.NormalizeText();
                var match = SearchEngineMatcher.Match(norm, terms, hit.Score);
                if (match == null) continue;

                if (match.Words.Length == maxScore)
                {
                    pendingLineIds.Add(lineId);
                    pendingMatchInfo.Add((lineId, match, norm));
                }
                else
                {
                    partialMatches.TryAdd(new PartialMatchData
                    {
                        LineId = lineId,
                        Score = match.Words.Length,
                        ProximityScore = match.ProximityScore,
                        ClusterStart = match.ClusterStart,
                        ClusterEnd = match.ClusterEnd
                    });
                }
            }

            sw.Stop();
            msMatchWork += sw.ElapsedMilliseconds;
        }

        /// <summary>
        /// Phase 2: fetch metadata for all accumulated perfect line IDs in one batch query,
        /// build result items, clear the pending lists, and return the items.
        /// </summary>
        private static IEnumerable<SearchResultItem> FlushMetaBatch(
            ZayitDbManager db,
            List<int> pendingLineIds,
            List<(int lineId, MatchInfo match, string norm)> pendingMatchInfo,
            ref long msMeta)
        {
            var sw = Stopwatch.StartNew();
            var metadata = db.GetLineMetadataBatch(pendingLineIds);
            sw.Stop();
            msMeta += sw.ElapsedMilliseconds;

            var results = new List<SearchResultItem>(pendingMatchInfo.Count);
            foreach (var (lineId, match, norm) in pendingMatchInfo)
            {
                if (!metadata.TryGetValue(lineId, out var meta)) continue;
                results.Add(new SearchResultItem
                {
                    LineId = lineId,
                    BookId = meta.bookId,
                    BookTitle = meta.bookTitle,
                    TocText = meta.tocText,
                    Score = match.Words.Length,
                    ProximityScore = match.ProximityScore,
                    Snippet = match.Snippet(norm)
                });
            }

            pendingLineIds.Clear();
            pendingMatchInfo.Clear();
            return results;
        }

        private struct PartialMatchData
        {
            public int LineId, Score, ClusterStart, ClusterEnd;
            public double ProximityScore;
        }

        private class TopNPartialMatches
        {
            private readonly object _lock = new object();
            private readonly SortedSet<PartialMatchData> _set;
            private readonly int _max;

            public TopNPartialMatches(int max)
            {
                _max = max;
                _set = new SortedSet<PartialMatchData>(Comparer<PartialMatchData>.Create((a, b) =>
                {
                    int c = b.Score.CompareTo(a.Score);
                    if (c != 0) return c;
                    c = b.ProximityScore.CompareTo(a.ProximityScore);
                    return c != 0 ? c : a.LineId.CompareTo(b.LineId);
                }));
            }

            public void TryAdd(PartialMatchData m)
            {
                lock (_lock)
                {
                    if (_set.Count < _max) { _set.Add(m); return; }
                    var worst = _set.Max;
                    if (m.Score > worst.Score || (m.Score == worst.Score && m.ProximityScore > worst.ProximityScore))
                    {
                        _set.Remove(worst);
                        _set.Add(m);
                    }
                }
            }

            public PartialMatchData[] GetTop(int count) { lock (_lock) return _set.Take(count).ToArray(); }
            public int Count { get { lock (_lock) return _set.Count; } }
        }
    }
}
