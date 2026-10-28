using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FtsLib;
using FtsLib.SeforimDb;
using FtsLib.Snippets;
using FtsLib.Fts5;

namespace KitveiHakodesh.Core.SeforimDbFullTextSearch
{
    /// <summary>
    /// SEARCHES the full-text index and turns the engine's raw matches into finished
    /// <see cref="FtsHit"/>s — snippet windows located once, filtered, short ones flagged.
    /// Feeding the index
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
        // the size the searcher snippets, so a batch is fetched, snippeted and
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
        /// Words of context a snippet shows per side when the caller does not say. The engine's
        /// default, surfaced here so a host can offer it without referencing the engine.
        /// </summary>
        public const int DefaultContextWords = Fts5SeforimIndex.DefaultContextWords;

        /// <summary>
        /// How many leading hits of a search get their snippet texts sent along
        /// (<see cref="SeforimDbFtsHitBatch.SnippetTexts"/>, then <see cref="FtsStreamChunk.SnippetTexts"/>
        /// / <see cref="FtsSearchResult.SnippetTexts"/>) so the first screen renders without a
        /// round-trip. About a screenful or two; the rest is fetched on demand by the page for
        /// the rows it renders. A matched line's text is the content the batch already fetched;
        /// only the short ones' neighbour context is read, in one statement per batch.
        /// </summary>
        public const int SeedSnippetTextHitCount = 100;

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
        /// <param name="afterPosition">Resume: only lines after this index position
        /// (<see cref="SeforimDbFtsEngine.PositionOfLine"/>); 0 = from the start. A resumed
        /// search seeds no snippet texts — the caller already shows its first screen.</param>
        public IEnumerable<SeforimDbFtsHitBatch> SearchInBatches(
            SeforimDbFtsEngine engine,
            string query,
            int maxWordDistance,
            bool requireOrdered,
            int contextWords,
            FtsTermTable terms,
            CancellationToken cancellationToken = default,
            Action<string>? onStage = null,
            long afterPosition = 0)
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
            int seedLeft = afterPosition > 0 ? 0 : SeedSnippetTextHitCount;
            var batch = new List<SearchResult>(FirstBatchSize);

            // The verification fetch reads the matched lines' text only (withPlacement: false):
            // the book id is read afterwards, for the hits that pass, over this one corpus kept
            // for the whole search (opened on first need).
            SeforimDbFtsCorpus? corpus = null;
            SeforimDbFtsCorpus Corpus() => corpus ??= engine.OpenCorpus();
            try
            {
                foreach (var hit in index.Search(query, cap: 0, ct: cancellationToken,
                                                 onStage: stage, batchSizeAt: BatchSizeAt, withPlacement: false,
                                                 afterPosition: afterPosition))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    batch.Add(hit);
                    if (batch.Count < BatchSizeAt(batchIndex)) continue;

                    var built = BuildBatch(index, Corpus, batch, requireOrdered, contextWords, maxWordDistance, terms, seedLeft, cancellationToken, stage);
                    batch.Clear();
                    batchIndex++;
                    seedLeft = Math.Max(0, seedLeft - built.Hits.Count);
                    if (built.Hits.Count > 0) yield return built;
                    stage?.Invoke("fetch");   // engine only reports its first fetch
                }

                if (batch.Count > 0)
                {
                    var built = BuildBatch(index, Corpus, batch, requireOrdered, contextWords, maxWordDistance, terms, seedLeft, cancellationToken, stage);
                    if (built.Hits.Count > 0) yield return built;
                }
            }
            finally { corpus?.Dispose(); }
        }

        /// <summary>
        /// One batch, engine matches → finished hits: snippet windows located across all cores
        /// (thread-safe — the engine's window locator is per-thread), order preserved, the
        /// word-distance filter applied. The located window IS the hit's snippet descriptor,
        /// kept for every hit — nothing is located twice. The engine match carries the line's
        /// text only (all the locate reads); the book id is read after the locate, in one query
        /// for the hits that PASSED, so a line the filters drop costs no read.
        /// The first <paramref name="seedSnippetTextHitCount"/> passing hits also get their
        /// snippet texts attached (<see cref="SeedSnippetTexts"/>).
        /// </summary>
        private static SeforimDbFtsHitBatch BuildBatch(
            Fts5SeforimIndex index,
            Func<SeforimDbFtsCorpus> corpus,
            IReadOnlyList<SearchResult> results,
            bool requireOrdered,
            int contextWords,
            int maxWordDistance,
            FtsTermTable terms,
            int seedSnippetTextHitCount,
            CancellationToken cancellationToken,
            Action<string>? onStage = null)
        {
            onStage?.Invoke("snippets");   // before Parallel.For: never call onStage from a worker
            var built = new FtsHit?[results.Count];

            Parallel.For(0, results.Count, new ParallelOptions { CancellationToken = cancellationToken }, i =>
            {
                if (TryBuildHit(index, results[i], requireOrdered, contextWords, maxWordDistance, terms, out FtsHit? hit))
                    built[i] = hit;
            });

            onStage?.Invoke("enrich");
            FetchPassingBookIds(corpus, results, built);

            var passing = new List<FtsHit>(results.Count);
            var seeded = new List<int>();   // indexes into results of the hits to seed
            for (int i = 0; i < built.Length; i++)
            {
                if (built[i] is not { } hit) continue;
                passing.Add(hit);
                if (passing.Count <= seedSnippetTextHitCount) seeded.Add(i);
            }
            var snippetTexts = seeded.Count == 0
                ? new List<FtsSnippetText>()
                : SeedSnippetTexts(corpus, results, built, seeded);
            return new SeforimDbFtsHitBatch(passing, snippetTexts);
        }

        /// <summary>
        /// The book id of every hit of the batch that passed the locate, read in one query
        /// (<see cref="SeforimDbFtsCorpus.FetchPlacements"/>) and set on the hits. A line with
        /// no `line` row keeps book id 0.
        /// </summary>
        private static void FetchPassingBookIds(
            Func<SeforimDbFtsCorpus> corpus, IReadOnlyList<SearchResult> results, FtsHit?[] built)
        {
            var passingIds = new List<int>(results.Count);
            for (int i = 0; i < built.Length; i++)
                if (built[i] != null) passingIds.Add(results[i].LineId);
            if (passingIds.Count == 0) return;

            var byId = corpus().FetchPlacements(passingIds);
            foreach (var hit in built)
                if (hit != null && byId.TryGetValue(hit.LineId, out var at)) hit.BookId = at.BookId;
        }

        /// <summary>
        /// Snippet texts for the seeded hits: a plain hit's text is the content the batch
        /// already fetched (no read); the SHORT ones are read with their neighbour context in
        /// one statement (<see cref="SeforimDbFtsCorpus.FetchSnippetTexts"/>) — the same read
        /// the page makes for every later row.
        /// </summary>
        private static List<FtsSnippetText> SeedSnippetTexts(
            Func<SeforimDbFtsCorpus> corpus, IReadOnlyList<SearchResult> results, FtsHit?[] built, List<int> seeded)
        {
            var texts = new List<FtsSnippetText>(seeded.Count);
            List<int>? shortIds = null;
            foreach (int i in seeded)
            {
                var hit = built[i]!;
                if ((hit.SnippetFlags & FtsHit.SnippetFlagShort) != 0) (shortIds ??= new List<int>()).Add(hit.LineId);
                else texts.Add(new FtsSnippetText { LineId = hit.LineId, Text = results[i].Content });
            }
            if (shortIds != null) texts.AddRange(corpus().FetchSnippetTexts(Array.Empty<int>(), shortIds));
            return texts;
        }

        /// <summary>
        /// One engine match → one finished hit with its snippet descriptor, or false when it
        /// does not pass the match and word-distance filters. A window holding fewer words than
        /// the requested context is flagged <see cref="FtsHit.SnippetFlagShort"/>: the page shows
        /// it with its neighbour lines as plain context.
        /// </summary>
        private static bool TryBuildHit(
            Fts5SeforimIndex index,
            SearchResult hit,
            bool requireOrdered,
            int contextWords,
            int maxWordDistance,
            FtsTermTable terms,
            out FtsHit? built)
        {
            built = null;

            var window = index.LocateSnippetWindow(hit, requireOrdered, contextWords);
            if (!window.IsMatch) return false;
            if (window.WordDistance > maxWordDistance) return false;

            built = new FtsHit
            {
                LineId = hit.LineId,   // BookId: set once the placements are read
                Score = window.Score,
                WordDistance = window.WordDistance,
            };
            CopySnippetDescriptor(built, in window, terms);
            if (window.WindowWordCount < contextWords) built.SnippetFlags |= FtsHit.SnippetFlagShort;
            return true;
        }

        /// <summary>
        /// Copies the located window onto the hit — offsets and highlight ranges over the
        /// matched line's text. The page rebuilds the HTML from that text only for the rows it
        /// renders. Copied, not referenced: the engine's mark ranges view per-thread scratch
        /// that the next locate overwrites. One exactly-sized array per hit.
        ///
        /// The engine's marks name their term by the query's own ordinal; here that becomes
        /// the search-wide <see cref="FtsTermTable"/> id (the page's term table) — so the
        /// marks also say which 1-3 concrete terms occur in this line, and nothing else
        /// has to.
        /// </summary>
        private static void CopySnippetDescriptor(FtsHit hit, in SnippetDescriptor window, FtsTermTable terms)
        {
            hit.SnippetStart = window.SnapStart;
            hit.SnippetEnd = window.SnapEnd;
            var marks = window.MarkRanges;
            int tripleCount = marks.Count / SnippetDescriptor.MarkStride;
            var copied = tripleCount == 0 ? Array.Empty<int>() : new int[tripleCount * SnippetDescriptor.MarkStride];
            for (int t = 0; t < tripleCount; t++)
            {
                int i = t * SnippetDescriptor.MarkStride;
                copied[i] = marks[i];
                copied[i + 1] = marks[i + 1];
                copied[i + 2] = terms.IdOf(window.MatchedTermAt(marks[i + 2]));
            }
            hit.SnippetMarks = copied;
            hit.SnippetFlags = (window.LeadingEllipsis ? FtsHit.SnippetFlagLeadingEllipsis : 0)
                             | (window.TrailingEllipsis ? FtsHit.SnippetFlagTrailingEllipsis : 0);
        }
    }
}
