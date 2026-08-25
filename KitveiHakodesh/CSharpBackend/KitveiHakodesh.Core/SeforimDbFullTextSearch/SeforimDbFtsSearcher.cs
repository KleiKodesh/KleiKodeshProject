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
        /// <param name="index">The engine. Passed in, not owned: one instance serves searches
        /// and the background build concurrently, and its owner is the orchestrator.</param>
        /// <param name="maxWordDistance">Hits whose tightest window is looser than this are
        /// dropped — the "how close must my words be" search setting.</param>
        /// <param name="requireOrdered">Only match windows where the query words appear in
        /// query order.</param>
        /// <param name="contextWords">Words of context requested per side of the match.</param>
        /// <param name="expandKetiv">Engine-side ketiv/qere expansion.</param>
        public IEnumerable<List<FtsHit>> SearchInBatches(
            SeforimIndex index,
            string query,
            int maxWordDistance,
            bool requireOrdered,
            int contextWords,
            bool expandKetiv,
            CancellationToken cancellationToken = default)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));
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
                if (TryBuildHit(index, results[i], requireOrdered, contextWords, maxWordDistance,
                        out FtsHit? hit, out int words))
                {
                    built[i] = hit;
                    windowWords[i] = words;
                }
            });

            EmbellishShortSnippets(index, results, built, windowWords, requireOrdered, contextWords, cancellationToken);

            var passing = new List<FtsHit>(results.Count);
            foreach (var hit in built)
                if (hit != null) passing.Add(hit);

            EnrichHits(passing);
            return passing;
        }

        /// <summary>Generate the snippet, apply the match and word-distance filters, and shape
        /// the hit. False when the hit does not pass.</summary>
        private static bool TryBuildHit(
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
