using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FtsLib.SeforimDb;
using KitveiHakodesh.Core.SeforimDb;

namespace KitveiHakodesh.Core.SeforimDbFullTextSearch
{
    /// <summary>
    /// SEARCHES the full-text index and turns the engine's raw matches into finished
    /// <see cref="FtsHit"/>s — snippeted, filtered, embellished and enriched. Feeding the index
    /// is <see cref="SeforimDbFtsIndexer"/>'s job; the match/rank/snippet ALGORITHMS are
    /// FtsLib's, and this class only decides what to do with their output.
    ///
    /// RESULTS ARE NEVER CAPPED. There is deliberately no cap parameter on this class; if a
    /// transport ever needs to stop early, it stops consuming batches — the sequence is lazy,
    /// so unconsumed batches cost nothing.
    ///
    /// The shape is batches, not hits, because the pipeline overlaps two different resources:
    /// content FETCH streams from one SQLite reader (sequential I/O), while SNIPPETING — the
    /// dominant CPU cost — runs per-batch across all cores. Handing finished batches onward is
    /// also what lets a streaming transport write one frame per batch with backpressure: it
    /// simply does not pull the next batch until the previous frame is written.
    /// </summary>
    public sealed class SeforimDbFtsSearcher
    {
        /// <summary>Batch size for parallel snippeting. Large enough to keep every core busy,
        /// small enough that the first results reach the caller almost immediately.</summary>
        private const int SnippetBatchSize = 256;

        /// <summary>
        /// How many lines of context to pull per side when embellishing a short snippet (same
        /// book only). Two fills the snippet's visual space (~4 clamped lines) and reaches
        /// about the requested per-side word context for prose; measured, radius 2 costs
        /// roughly half of radius 3, because it re-tokenizes fewer neighbour lines.
        /// </summary>
        private const int NeighborLineRadius = 2;

        /// <summary>
        /// Words of context a snippet shows per side when the caller does not say. The engine's
        /// default, surfaced here so a host can offer it without referencing the engine.
        /// </summary>
        public const int DefaultContextWords = SeforimIndex.DefaultContextWords;

        private readonly SeforimDbQueries _queries;

        /// <param name="queries">For enrichment — hits leave here carrying their book id and
        /// TOC path, so no consumer makes a second round-trip to display a result.</param>
        public SeforimDbFtsSearcher(SeforimDbQueries queries)
        {
            _queries = queries ?? throw new ArgumentNullException(nameof(queries));
        }

        /// <summary>
        /// Runs a search and yields each finished batch of hits, in engine order, until the
        /// results run out or <paramref name="cancellationToken"/> stops them.
        ///
        /// Query expansion is the caller's step (see SeforimDbFtsRelatedFormExpander) — by the
        /// time a query reaches here it is final.
        /// </summary>
        /// <param name="engine">Core's engine handle. One instance serves searches and the
        /// background build concurrently — see <see cref="SeforimDbFtsEngine"/>, which owns it
        /// so a host never has to reference the engine library.</param>
        /// <param name="maxWordDistance">Hits whose tightest window is looser than this are
        /// dropped — the "how close must my words be" search setting.</param>
        /// <param name="requireOrdered">Only match windows where the query words appear in
        /// query order.</param>
        /// <param name="contextWords">Words of context requested per side of the match.</param>
        /// <param name="expandKetiv">Engine-side ketiv/qere expansion.</param>
        public IEnumerable<List<FtsHit>> SearchInBatches(
            SeforimDbFtsEngine engine,
            string query,
            int maxWordDistance,
            bool requireOrdered,
            int contextWords,
            bool expandKetiv,
            CancellationToken cancellationToken = default)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            var index = engine.Index;
            if (string.IsNullOrWhiteSpace(query)) yield break;

            var batch = new List<SearchResult>(SnippetBatchSize);

            foreach (var hit in index.Search(query, cap: 0, expandKetiv: expandKetiv, ct: cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                batch.Add(hit);
                if (batch.Count < SnippetBatchSize) continue;

                var built = BuildBatch(index, batch, requireOrdered, contextWords, maxWordDistance, cancellationToken);
                batch.Clear();
                if (built.Count > 0) yield return built;
            }

            if (batch.Count > 0)
            {
                var built = BuildBatch(index, batch, requireOrdered, contextWords, maxWordDistance, cancellationToken);
                if (built.Count > 0) yield return built;
            }
        }

        /// <summary>
        /// One batch, engine matches → finished hits: snippets generated across all cores
        /// (thread-safe — the engine's snippet builder is per-thread), order preserved, the
        /// word-distance filter applied, short snippets embellished, and the survivors
        /// enriched with book id + TOC path.
        /// </summary>
        private List<FtsHit> BuildBatch(
            SeforimIndex index,
            IReadOnlyList<SearchResult> results,
            bool requireOrdered,
            int contextWords,
            int maxWordDistance,
            CancellationToken cancellationToken)
        {
            var built = new FtsHit?[results.Count];
            var windowWords = new int[results.Count];   // 0 = the hit did not pass

            Parallel.For(0, results.Count, new ParallelOptions { CancellationToken = cancellationToken }, i =>
            {
                if (TryBuildHitCore(index, results[i], requireOrdered, contextWords, maxWordDistance,
                        out FtsHit? hit, out int words))
                {
                    built[i] = hit;
                    windowWords[i] = words;
                }
            });

            // The tail — embellish the short snippets, then enrich — is the same work a host
            // that batches on its own rhythm needs, so it lives in one place.
            FinishBatchCore(index, results, built, windowWords, requireOrdered, contextWords,
                            cancellationToken);

            var passing = new List<FtsHit>(results.Count);
            foreach (var hit in built)
                if (hit != null) passing.Add(hit);
            return passing;
        }

        /// <summary>
        /// The engine's matches, with its mid-merge refusal translated into Core's own type so
        /// a host never has to name an engine exception.
        /// </summary>
        private static IEnumerable<SearchResult> Matches(
            SeforimIndex index, string query, bool expandKetiv, CancellationToken cancellationToken)
        {
            IEnumerator<SearchResult> walker;
            try
            {
                walker = index.Search(query, cap: 0, expandKetiv: expandKetiv, ct: cancellationToken)
                              .GetEnumerator();
            }
            catch (FtsLib.Indexing.IndexMergingException ex)
            {
                throw new SeforimDbFtsIndexMergingException(
                    "The full-text index is merging and cannot answer searches right now.", ex);
            }

            using (walker)
            {
                while (true)
                {
                    try
                    {
                        if (!walker.MoveNext()) yield break;
                    }
                    catch (FtsLib.Indexing.IndexMergingException ex)
                    {
                        throw new SeforimDbFtsIndexMergingException(
                            "The full-text index is merging and cannot answer searches right now.", ex);
                    }
                    yield return walker.Current;
                }
            }
        }

        /// <summary>
        /// A hit on its way to a caller that batches on its own rhythm: the finished hit, plus
        /// the two things <see cref="FinishPendingBatch"/> still needs to complete it — the
        /// engine match it came from and its window word count.
        ///
        /// Core's own type, so the engine's result type never crosses into a host.
        /// </summary>
        public readonly struct PendingHit
        {
            internal PendingHit(SearchResult match, FtsHit hit, int windowWords)
            {
                Match = match;
                Hit = hit;
                WindowWords = windowWords;
            }

            /// <summary>Internal: the engine match, needed only to re-render a short snippet.</summary>
            internal SearchResult Match { get; }

            /// <summary>The finished hit. Still missing book id and TOC path until the batch
            /// it belongs to is finished.</summary>
            public FtsHit Hit { get; }

            internal int WindowWords { get; }
        }

        /// <summary>
        /// Streams finished hits ONE AT A TIME, in engine order, for a host that batches on its
        /// own rhythm — the hosted app flushes at 1, 2, 4, 8, 16 results and then on a timer, so
        /// the first result reaches the user immediately.
        ///
        /// Hits that fail the match or word-distance filters are skipped, so a caller only sees
        /// keepers. Each one still needs <see cref="FinishBatch"/> run over whatever batch the
        /// caller assembles — that is where short snippets are re-rendered and every hit gets
        /// its book id and TOC path.
        ///
        /// The engine's own result type never leaves Core — see <see cref="PendingHit"/>.
        /// </summary>
        public IEnumerable<PendingHit> SearchHitByHit(
            SeforimDbFtsEngine engine,
            string query,
            int maxWordDistance,
            bool requireOrdered,
            int contextWords,
            bool expandKetiv,
            CancellationToken cancellationToken = default)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            if (string.IsNullOrWhiteSpace(query)) yield break;

            var index = engine.Index;
            // IndexMergingException is translated below, at the one place a host would see it.
            foreach (var match in Matches(index, query, expandKetiv, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryBuildHitCore(index, match, requireOrdered, contextWords, maxWordDistance,
                                    out FtsHit? hit, out int windowWords))
                {
                    yield return new PendingHit(match, hit!, windowWords);
                }
            }
        }

        /// <summary>
        /// One engine match → one finished hit, or false when it does not pass the match and
        /// word-distance filters.
        ///
        /// Public because a host may batch on a different rhythm than
        /// <see cref="SearchInBatches"/>: the hosted app flushes at 1, 2, 4, 8, 16 results and
        /// then on a timer, so the user sees the first result immediately. The per-hit WORK is
        /// the same either way, and that is what must not exist twice.
        ///
        /// <paramref name="windowWordCount"/> is what decides whether the snippet is "short"
        /// and worth embellishing — see <see cref="EmbellishSnippets"/>.
        /// </summary>
        public static bool TryBuildHit(
            SeforimDbFtsEngine engine,
            SearchResult hit,
            bool requireOrdered,
            int contextWords,
            int maxWordDistance,
            out FtsHit? built,
            out int windowWordCount)
            => TryBuildHitCore(engine.Index, hit, requireOrdered, contextWords, maxWordDistance,
                               out built, out windowWordCount);

        private static bool TryBuildHitCore(
            SeforimIndex index,
            SearchResult hit,
            bool requireOrdered,
            int contextWords,
            int maxWordDistance,
            out FtsHit? built,
            out int windowWordCount)
        {
            built = null;
            windowWordCount = 0;

            var snippet = index.GenerateSnippet(hit, requireOrdered, contextWords);
            if (!snippet.IsMatch) return false;
            if (snippet.WordDistance > maxWordDistance) return false;
            windowWordCount = snippet.WindowWordCount;

            var matchedTerms = new List<string>();
            foreach (var group in hit.MatchedGroups)
                foreach (string term in group)
                    if (!matchedTerms.Contains(term)) matchedTerms.Add(term);

            built = new FtsHit
            {
                LineId = hit.LineId,
                BookId = 0,                          // enrichment fills this
                BookTitle = hit.BookTitle ?? "",
                TocText = "",                        // enrichment fills this
                Score = snippet.Score,
                WordDistance = snippet.WordDistance,
                Snippet = snippet.Html ?? "",
                MatchedTerms = matchedTerms,
            };
            return true;
        }

        /// <summary>
        /// Finishes a batch a caller assembled itself — the hosted app's progressive flush
        /// does. Exactly the tail of <see cref="SearchInBatches"/>: the batch's short snippets
        /// are re-rendered over their neighbouring lines, then every hit is enriched with its
        /// book id and TOC path, so hits leave Core COMPLETE however they were batched.
        ///
        /// The arrays are parallel, one entry per hit, and <paramref name="windowWords"/> holds
        /// what <see cref="TryBuildHit"/> reported.
        /// </summary>
        public void FinishBatch(
            SeforimDbFtsEngine engine,
            IReadOnlyList<SearchResult> results,
            FtsHit?[] built,
            int[] windowWords,
            bool requireOrdered,
            int contextWords,
            CancellationToken cancellationToken = default)
            => FinishBatchCore(engine.Index, results, built, windowWords, requireOrdered,
                               contextWords, cancellationToken);

        /// <summary>
        /// Finishes a batch a host assembled from <see cref="SearchHitByHit"/>: re-renders the
        /// short snippets over their neighbouring lines, then gives every hit its book id and
        /// TOC path. The hits are completed IN PLACE.
        /// </summary>
        public void FinishPendingBatch(
            SeforimDbFtsEngine engine,
            IReadOnlyList<PendingHit> pending,
            bool requireOrdered,
            int contextWords,
            CancellationToken cancellationToken = default)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            if (pending == null || pending.Count == 0) return;

            var matches = new SearchResult[pending.Count];
            var built = new FtsHit?[pending.Count];
            var windowWords = new int[pending.Count];
            for (int i = 0; i < pending.Count; i++)
            {
                matches[i] = pending[i].Match;
                built[i] = pending[i].Hit;
                windowWords[i] = pending[i].WindowWords;
            }

            FinishBatchCore(engine.Index, matches, built, windowWords, requireOrdered,
                            contextWords, cancellationToken);
        }

        /// <summary>The same tail, for callers inside Core that already hold the engine.</summary>
        private void FinishBatchCore(
            SeforimIndex index,
            IReadOnlyList<SearchResult> results,
            FtsHit?[] built,
            int[] windowWords,
            bool requireOrdered,
            int contextWords,
            CancellationToken cancellationToken)
        {
            EmbellishShortSnippets(index, results, built, windowWords, requireOrdered,
                                   contextWords, cancellationToken);

            var passing = new List<FtsHit>(built.Length);
            foreach (var hit in built) if (hit != null) passing.Add(hit);
            EnrichHits(passing);
        }

        /// <summary>
        /// Re-renders the batch's SHORT snippets — those whose window holds fewer words than
        /// the requested per-side context, meaning the matched line itself was too short to
        /// fill it — over their surrounding lines from the same book.
        ///
        /// One batched neighbour fetch covers every short line in the batch, and only those
        /// hits re-render (across cores). A batch with nothing short pays nothing — no query,
        /// no work — and a broad query's typical batch has only about one short line in six.
        /// </summary>
        private static void EmbellishShortSnippets(
            SeforimIndex index,
            IReadOnlyList<SearchResult> results,
            FtsHit?[] built,
            int[] windowWords,
            bool requireOrdered,
            int contextWords,
            CancellationToken cancellationToken)
        {
            List<int>? shortIndexes = null;
            List<int>? shortLineIds = null;
            for (int i = 0; i < built.Length; i++)
            {
                if (built[i] == null || windowWords[i] >= contextWords) continue;
                (shortIndexes ??= new List<int>()).Add(i);
                (shortLineIds ??= new List<int>()).Add(results[i].LineId);
            }
            if (shortLineIds == null) return;

            var neighbours = index.FetchNeighborContext(shortLineIds, NeighborLineRadius);
            if (neighbours.Count == 0) return;

            Parallel.ForEach(shortIndexes!, new ParallelOptions { CancellationToken = cancellationToken }, i =>
            {
                if (!neighbours.TryGetValue(results[i].LineId, out var context)) return;

                var rerendered = index.GenerateSnippetWithNeighbors(
                    results[i], context.Prev, context.Next, requireOrdered, contextWords);

                // Only the snippet HTML is swapped in: score and word distance stay as computed
                // on the matched line itself, because those are the relevance keys and the
                // neighbours are presentation. A failed re-render (should not happen — same
                // terms) keeps the original snippet rather than blanking it.
                if (rerendered.IsMatch && !string.IsNullOrEmpty(rerendered.Html) && built[i] is { } hit)
                    hit.Snippet = rerendered.Html;
            });
        }

        /// <summary>
        /// Fills each hit's book id and TOC path so results leave Core COMPLETE. One batched
        /// query per batch; lines with no TOC entry (custom books) fall back to resolving the
        /// book id straight off the line table, so no hit ships with BookId 0 that the corpus
        /// can resolve.
        /// </summary>
        private void EnrichHits(List<FtsHit> hits)
        {
            if (hits.Count == 0) return;

            var lineIds = new List<int>(hits.Count);
            foreach (var hit in hits) lineIds.Add(hit.LineId);

            var tocPaths = _queries.GetTocPathsForLines(lineIds);
            if (tocPaths.Count > 0)
            {
                var byLine = new Dictionary<int, TocPathRow>(tocPaths.Count);
                foreach (var row in tocPaths) byLine[row.LineId] = row;
                foreach (var hit in hits)
                {
                    if (byLine.TryGetValue(hit.LineId, out var row))
                    {
                        hit.BookId = row.BookId;
                        hit.TocText = row.TocPath;
                    }
                }
            }

            List<int>? withoutToc = null;
            foreach (var hit in hits)
                if (hit.BookId == 0) (withoutToc ??= new List<int>()).Add(hit.LineId);
            if (withoutToc == null) return;

            var books = _queries.GetBookIdsForLines(withoutToc);
            if (books.Count == 0) return;

            var bookByLine = new Dictionary<int, int>(books.Count);
            foreach (var row in books) bookByLine[row.LineId] = row.BookId;
            foreach (var hit in hits)
                if (hit.BookId == 0 && bookByLine.TryGetValue(hit.LineId, out int bookId))
                    hit.BookId = bookId;
        }
    }
}
