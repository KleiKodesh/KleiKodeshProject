using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FtsLib;
using FtsLib.SeforimDb;
using FtsLib.Snippets;
// Core's engine is SQLite FTS5 (FtsLib.Fts5): see FtsLib/Fts5/Fts5SeforimIndex.cs.
using SeforimIndex = FtsLib.Fts5.Fts5SeforimIndex;
using KitveiHakodesh.Core.SeforimDb;

namespace KitveiHakodesh.Core.SeforimDbFullTextSearch
{
    /// <summary>
    /// SEARCHES the full-text index and turns the engine's raw matches into finished
    /// <see cref="FtsHit"/>s — snippet windows located, filtered, short windows extended over
    /// neighbour lines. Feeding the index
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
        // ── Batch plan ─────────────────────────────────────────────────────────────
        // ONE plan for the whole pipeline: the engine fetches content in batches of exactly
        // the size the searcher snippets, so a batch is fetched, snippeted, extended and
        // handed on as one unit. Small batches first — the first screen is on the wire
        // after one fetch of 256 lines — then larger ones, because a broad query has
        // hundreds of thousands of batches to go and every batch pays fixed costs (a
        // SQLite round-trip, a Parallel.For spin-up, a frame write on the transport).

        /// <summary>Batch size for the first <see cref="FirstBatchCount"/> batches. Large
        /// enough to keep every core busy, small enough that the first results reach the
        /// caller almost immediately.</summary>
        public const int FirstBatchSize = 256;

        /// <summary>How many batches stay at <see cref="FirstBatchSize"/> before growing —
        /// a few screens' worth, so paging through early results feels the same.</summary>
        public const int FirstBatchCount = 16;

        /// <summary>After the first batches the size doubles per batch up to this.</summary>
        public const int GrownBatchSize = 2048;

        /// <summary>Hard ceiling on any batch the plan can ask for — what the content fetch
        /// and the snippet arrays are sized against.</summary>
        public const int MaxBatchSize = 4096;

        /// <summary>The planned size of batch number <paramref name="batchIndex"/> (0-based).
        /// Shared by the engine's content fetch and this class's snippeting, so both cut the
        /// stream at the same places.</summary>
        public static int BatchSizeAt(int batchIndex)
        {
            if (batchIndex < FirstBatchCount) return FirstBatchSize;
            int size = FirstBatchSize;
            for (int i = FirstBatchCount; i <= batchIndex && size < GrownBatchSize; i++) size *= 2;
            return Math.Min(size, MaxBatchSize);
        }

        /// <summary>
        /// How many lines of context to pull per side when extending a short snippet window
        /// (same book only). Two fills the snippet's visual space (~4 clamped lines) and reaches
        /// about the requested per-side word context for prose; measured, radius 2 costs
        /// roughly half of radius 3, because it re-tokenizes fewer neighbour lines.
        /// </summary>
        private const int NeighborLineRadius = 2;

        /// <summary>
        /// Words of context a snippet shows per side when the caller does not say. The engine's
        /// default, surfaced here so a host can offer it without referencing the engine.
        /// </summary>
        public const int DefaultContextWords = SeforimIndex.DefaultContextWords;

        /// <summary>
        /// How many leading hits of a search get their snippet line texts sent along
        /// (<see cref="FtsStreamChunk.LineTexts"/> / <see cref="FtsSearchResult.LineTexts"/>)
        /// so the first screen renders without a round-trip. About a screenful or two; the
        /// rest is fetched on demand by the page for the rows it renders.
        /// </summary>
        public const int SeedLineTextHitCount = 100;

        private readonly SeforimDbQueries _queries;

        /// <param name="queries">For the snippet line texts seeded onto the first frames.</param>
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
            FtsTermTable terms,
            CancellationToken cancellationToken = default,
            Action<string>? onStage = null)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            if (terms == null) throw new ArgumentNullException(nameof(terms));
            var index = engine.Index;
            if (string.IsNullOrWhiteSpace(query)) yield break;

            // Only stage CHANGES are reported: the label follows the stream back to
            // fetch/snippets/enrich per batch without re-sending a stage twice in a row.
            string? last = null;
            Action<string>? stage = onStage == null ? null : s => { if (s != last) { last = s; onStage(s); } };

            int batchIndex = 0;
            var batch = new List<SearchResult>(FirstBatchSize);

            foreach (var hit in index.Search(query, cap: 0, expandKetiv: expandKetiv, ct: cancellationToken,
                                             onStage: stage, batchSizeAt: BatchSizeAt))
            {
                cancellationToken.ThrowIfCancellationRequested();
                batch.Add(hit);
                if (batch.Count < BatchSizeAt(batchIndex)) continue;

                var built = BuildBatch(engine, batch, requireOrdered, contextWords, maxWordDistance, terms, cancellationToken, stage);
                batch.Clear();
                batchIndex++;
                if (built.Count > 0) yield return built;
                stage?.Invoke("fetch");   // engine only reports its first fetch
            }

            if (batch.Count > 0)
            {
                var built = BuildBatch(engine, batch, requireOrdered, contextWords, maxWordDistance, terms, cancellationToken, stage);
                if (built.Count > 0) yield return built;
            }
        }

        /// <summary>
        /// One batch, engine matches → finished hits: snippet windows located across all cores
        /// (thread-safe — the engine's window locator is per-thread), order preserved, the
        /// word-distance filter applied, short windows extended over neighbour lines. Book id
        /// and TOC entry id come with the engine match (the corpus reads them in the content
        /// fetch — <see cref="FtsDocumentPlacement"/>), so no query is needed to finish a hit.
        /// </summary>
        private List<FtsHit> BuildBatch(
            SeforimDbFtsEngine engine,
            IReadOnlyList<SearchResult> results,
            bool requireOrdered,
            int contextWords,
            int maxWordDistance,
            FtsTermTable terms,
            CancellationToken cancellationToken,
            Action<string>? onStage = null)
        {
            onStage?.Invoke("snippets");   // before Parallel.For: never call onStage from a worker
            var index = engine.Index;
            var built = new FtsHit?[results.Count];
            var windowWords = new int[results.Count];   // 0 = the hit did not pass

            Parallel.For(0, results.Count, new ParallelOptions { CancellationToken = cancellationToken }, i =>
            {
                if (TryBuildHit(index, results[i], requireOrdered, contextWords, maxWordDistance, terms,
                        out FtsHit? hit, out int words))
                {
                    built[i] = hit;
                    windowWords[i] = words;
                }
            });

            onStage?.Invoke("enrich");
            ExtendShortWindowsWithNeighbourLines(engine, results, built, windowWords, requireOrdered,
                                                 contextWords, terms, cancellationToken);

            var passing = new List<FtsHit>(results.Count);
            foreach (var hit in built)
                if (hit != null) passing.Add(hit);
            return passing;
        }

        /// <summary>
        /// One engine match → one finished hit, or false when it does not pass the match and
        /// word-distance filters. <paramref name="windowWordCount"/> is what decides whether
        /// the window is "short" and worth extending — see
        /// <see cref="ExtendShortWindowsWithNeighbourLines"/>.
        /// </summary>
        private static bool TryBuildHit(
            SeforimIndex index,
            SearchResult hit,
            bool requireOrdered,
            int contextWords,
            int maxWordDistance,
            FtsTermTable terms,
            out FtsHit? built,
            out int windowWordCount)
        {
            built = null;
            windowWordCount = 0;

            var window = index.LocateSnippetWindow(hit, requireOrdered, contextWords);
            if (!window.IsMatch) return false;
            if (window.WordDistance > maxWordDistance) return false;
            windowWordCount = window.WindowWordCount;

            built = new FtsHit
            {
                LineId = hit.LineId,
                BookId = hit.Placement.BookId,
                TocEntryId = hit.Placement.TocEntryId,
                Score = window.Score,
                WordDistance = window.WordDistance,
            };
            built.SnippetLineIds.Add(hit.LineId);
            CopySnippetDescriptor(built, in window, terms);
            return true;
        }

        /// <summary>
        /// Copies the located window onto the hit — offsets and highlight ranges over the text
        /// of <see cref="FtsHit.SnippetLineIds"/> joined by single spaces, which the caller has
        /// already set. The page rebuilds the HTML from that text only for the rows it renders.
        /// Copied, not referenced: the engine's mark ranges view per-thread scratch that the
        /// next locate overwrites. Reuses the hit's own lists, so this allocates nothing.
        ///
        /// The engine's marks name their term by the query's own ordinal; here that becomes
        /// the search-wide <see cref="FtsTermTable"/> id (the page's term table), and the
        /// hit's <see cref="FtsHit.TermIds"/> are the distinct ids of ITS marks — the 1-3 terms
        /// that actually occur in this line, never the query's whole expansion.
        /// </summary>
        private static void CopySnippetDescriptor(FtsHit hit, in SnippetDescriptor window, FtsTermTable terms)
        {
            hit.SnippetStart = window.SnapStart;
            hit.SnippetEnd = window.SnapEnd;
            hit.SnippetMarks.Clear();
            hit.TermIds.Clear();
            var marks = window.MarkRanges;
            for (int i = 0; i + 2 < marks.Count; i += SnippetDescriptor.MarkStride)
            {
                int termId = terms.IdOf(window.MatchedTermAt(marks[i + 2]));
                hit.SnippetMarks.Add(marks[i]);
                hit.SnippetMarks.Add(marks[i + 1]);
                hit.SnippetMarks.Add(termId);
                if (!hit.TermIds.Contains(termId)) hit.TermIds.Add(termId);
            }
            hit.SnippetFlags = (window.LeadingEllipsis ? FtsHit.SnippetFlagLeadingEllipsis : 0)
                             | (window.TrailingEllipsis ? FtsHit.SnippetFlagTrailingEllipsis : 0);
        }

        /// <summary>
        /// Re-locates the batch's SHORT windows — those holding fewer words than the requested
        /// per-side context, meaning the matched line itself was too short to fill them — over
        /// the matched line joined with its surrounding lines from the same book, and points
        /// the descriptor at those line ids.
        ///
        /// One batched neighbour fetch covers every short line in the batch, and only those
        /// hits re-locate (across cores). A batch with nothing short pays nothing — no query,
        /// no work — and a broad query's typical batch has only about one short line in six.
        /// </summary>
        private static void ExtendShortWindowsWithNeighbourLines(
            SeforimDbFtsEngine engine,
            IReadOnlyList<SearchResult> results,
            FtsHit?[] built,
            int[] windowWords,
            bool requireOrdered,
            int contextWords,
            FtsTermTable terms,
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

            // Through Core's own corpus rather than the engine's FetchNeighborContext: the
            // descriptor must name the neighbour lines BY ID, which the engine API drops.
            IDictionary<int, SeforimDbFtsNeighbourLines> neighbours;
            using (var corpus = engine.OpenCorpus())
                neighbours = corpus.FetchNeighbourLines(shortLineIds, NeighborLineRadius);
            if (neighbours.Count == 0) return;
            var index = engine.Index;

            Parallel.ForEach(shortIndexes!, new ParallelOptions { CancellationToken = cancellationToken }, i =>
            {
                if (!neighbours.TryGetValue(results[i].LineId, out var context)) return;

                var extended = index.LocateSnippetWindowOverNeighbourLines(
                    results[i], context.Previous, context.Next, requireOrdered, contextWords);

                // Only the snippet descriptor is swapped in: score and word distance stay as
                // computed on the matched line itself, because those are the relevance keys and
                // the neighbours are presentation. A failed re-locate (should not happen — same
                // terms) keeps the original window rather than blanking it.
                if (extended.IsMatch && built[i] is { } hit)
                {
                    // The text the engine joined: previous + ' ' + line + ' ' + next, empty sides
                    // dropped — i.e. exactly these ids' texts joined by single spaces.
                    hit.SnippetLineIds.Clear();
                    hit.SnippetLineIds.AddRange(context.PreviousIds);
                    hit.SnippetLineIds.Add(results[i].LineId);
                    hit.SnippetLineIds.AddRange(context.NextIds);
                    CopySnippetDescriptor(hit, in extended, terms);
                }
            });
        }

        /// <summary>
        /// The texts of the snippet lines of the first <paramref name="count"/> hits, keyed
        /// by line id — what a transport attaches to the frame carrying those hits. One
        /// batched query; an id the DB no longer has is simply absent.
        /// </summary>
        public Dictionary<int, string> FetchSnippetLineTexts(IReadOnlyList<FtsHit> hits, int count)
        {
            var texts = new Dictionary<int, string>();
            if (hits == null || hits.Count == 0 || count <= 0) return texts;
            var ids = new List<int>();
            var seen = new HashSet<int>();
            int n = Math.Min(count, hits.Count);
            for (int i = 0; i < n; i++)
            {
                var hit = hits[i];
                if (hit.SnippetLineIds.Count == 0) { if (seen.Add(hit.LineId)) ids.Add(hit.LineId); continue; }
                foreach (int id in hit.SnippetLineIds) if (seen.Add(id)) ids.Add(id);
            }
            foreach (var row in _queries.GetLineContents(ids))
                texts[row.Id] = row.Content ?? string.Empty;
            return texts;
        }
    }
}
