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
        public const int DefaultContextWords = Fts5SeforimIndex.DefaultContextWords;

        /// <summary>
        /// How many leading hits of a search are streamed WITH their snippet — descriptor and
        /// line texts — so the first screen renders without a round-trip. Every later hit
        /// streams as numbers only (line, book, TOC entry, score, word distance): its snippet
        /// is built when the page shows it (<see cref="SnippetsFor"/>), so neither its
        /// descriptor nor a neighbour-line read for a short window is paid for hits that are
        /// never seen. Same count as the line texts seeded below.
        /// </summary>
        public const int StreamedSnippetHitCount = SeedLineTextHitCount;

        /// <summary>
        /// How many leading hits of a search get their snippet line texts sent along
        /// (<see cref="SeforimDbFtsHitBatch.LineTexts"/>, then <see cref="FtsStreamChunk.LineTexts"/>
        /// / <see cref="FtsSearchResult.LineTexts"/>) so the first screen renders without a
        /// round-trip. About a screenful or two; the rest is fetched on demand by the page
        /// for the rows it renders. The texts are the ones the batch fetched to snippet — the
        /// matched lines' content and the neighbour lines a short window was extended over —
        /// so seeding reads nothing.
        /// </summary>
        public const int SeedLineTextHitCount = 100;

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
        public IEnumerable<SeforimDbFtsHitBatch> SearchInBatches(
            SeforimDbFtsEngine engine,
            string query,
            int maxWordDistance,
            bool requireOrdered,
            int contextWords,
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
            int seedLeft = SeedLineTextHitCount;
            int snippetLeft = StreamedSnippetHitCount;
            var batch = new List<SearchResult>(FirstBatchSize);

            // The verification fetch reads the matched lines' text only (withPlacement: false):
            // the placement (book, lineIndex, TOC entry) is read afterwards, for the hits that
            // pass, over this one corpus kept for the whole search (opened on first need).
            SeforimDbFtsCorpus? corpus = null;
            SeforimDbFtsCorpus Corpus() => corpus ??= engine.OpenCorpus();
            try
            {
                foreach (var hit in index.Search(query, cap: 0, ct: cancellationToken,
                                                 onStage: stage, batchSizeAt: BatchSizeAt, withPlacement: false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    batch.Add(hit);
                    if (batch.Count < BatchSizeAt(batchIndex)) continue;

                    var built = BuildBatch(engine, Corpus, batch, requireOrdered, contextWords, maxWordDistance, terms, seedLeft, snippetLeft, cancellationToken, stage);
                    batch.Clear();
                    batchIndex++;
                    seedLeft = Math.Max(0, seedLeft - built.Hits.Count);
                    snippetLeft = Math.Max(0, snippetLeft - built.Hits.Count);
                    if (built.Hits.Count > 0) yield return built;
                    stage?.Invoke("fetch");   // engine only reports its first fetch
                }

                if (batch.Count > 0)
                {
                    var built = BuildBatch(engine, Corpus, batch, requireOrdered, contextWords, maxWordDistance, terms, seedLeft, snippetLeft, cancellationToken, stage);
                    if (built.Hits.Count > 0) yield return built;
                }
            }
            finally { corpus?.Dispose(); }
        }

        /// <summary>
        /// One batch, engine matches → finished hits: snippet windows located across all cores
        /// (thread-safe — the engine's window locator is per-thread), order preserved, the
        /// word-distance filter applied, short windows extended over neighbour lines. The
        /// engine match carries the line's text only (all the locate reads); the placement
        /// (book id, lineIndex, TOC entry — <see cref="FtsDocumentPlacement"/>) is read after
        /// the locate, in one query for the hits that PASSED, so a line the filters drop
        /// costs no placement read.
        /// The first <paramref name="seedLineTextHitCount"/> passing hits also get their
        /// snippet line texts attached, from the content in hand.
        /// </summary>
        private SeforimDbFtsHitBatch BuildBatch(
            SeforimDbFtsEngine engine,
            Func<SeforimDbFtsCorpus> corpus,
            IReadOnlyList<SearchResult> results,
            bool requireOrdered,
            int contextWords,
            int maxWordDistance,
            FtsTermTable terms,
            int seedLineTextHitCount,
            int snippetHitCount,
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

            // Only the first snippetHitCount passing hits keep their snippet; the rest stream
            // as numbers only (their snippet is built on demand — SnippetsFor) and are left out
            // of the neighbour-line extension, which reads lines from the database.
            int passed = 0;
            for (int i = 0; i < built.Length; i++)
            {
                if (built[i] is not { } hit) continue;
                if (passed++ < snippetHitCount) continue;
                hit.SnippetLineIds = Array.Empty<int>();
                hit.SnippetStart = 0;
                hit.SnippetEnd = 0;
                hit.SnippetMarks = Array.Empty<int>();
                hit.SnippetFlags = 0;
                windowWords[i] = int.MaxValue;   // never "short": no extension
            }

            onStage?.Invoke("enrich");
            var placements = FetchPassingPlacements(corpus, results, built);
            var neighbourTextsById = ExtendShortWindowsWithNeighbourLines(
                engine, corpus, results, placements, built, windowWords, requireOrdered, contextWords, terms, cancellationToken);

            var passing = new List<FtsHit>(results.Count);
            var lineTexts = new Dictionary<int, string>();
            for (int i = 0; i < built.Length; i++)
            {
                if (built[i] is not { } hit) continue;
                passing.Add(hit);
                if (passing.Count <= seedLineTextHitCount)
                    CollectSnippetLineTexts(hit, results[i].Content, neighbourTextsById, lineTexts);
            }
            return new SeforimDbFtsHitBatch(passing, lineTexts);
        }

        /// <summary>
        /// Snippets for lines a search already returned — the rows a page is about to show:
        /// each line's window located and, when short, extended over its neighbour lines,
        /// exactly as the stream builds the first hits' snippets; with the snippet lines'
        /// texts, so the page renders them without another round-trip. In
        /// <paramref name="lineIds"/>' order; a line that no longer matches (the library
        /// changed) is absent. No full-text MATCH runs: the query is only parsed and
        /// expanded, and only these lines are read.
        /// </summary>
        public SeforimDbFtsHitBatch SnippetsFor(
            SeforimDbFtsEngine engine,
            string query,
            IReadOnlyList<int> lineIds,
            bool requireOrdered,
            int contextWords,
            FtsTermTable terms,
            CancellationToken cancellationToken = default)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            var matches = engine.Index.MatchesForLines(query, lineIds, cancellationToken);
            if (matches.Count == 0) return new SeforimDbFtsHitBatch(new List<FtsHit>(), new Dictionary<int, string>());
            SeforimDbFtsCorpus? corpus = null;
            try
            {
                return BuildBatch(engine, () => corpus ??= engine.OpenCorpus(), matches, requireOrdered, contextWords,
                                  int.MaxValue, terms, matches.Count, matches.Count, cancellationToken);
            }
            finally { corpus?.Dispose(); }
        }

        /// <summary>
        /// The placement of every hit of the batch that passed the locate, read in one query
        /// (<see cref="SeforimDbFtsCorpus.FetchPlacements"/>), and set on the hits (book id,
        /// TOC entry). Indexed like <paramref name="results"/>; a failed hit, or a line with
        /// no `line` row, keeps the default (all zero).
        /// </summary>
        private static FtsDocumentPlacement[] FetchPassingPlacements(
            Func<SeforimDbFtsCorpus> corpus, IReadOnlyList<SearchResult> results, FtsHit?[] built)
        {
            var placements = new FtsDocumentPlacement[results.Count];
            var passingIds = new List<int>(results.Count);
            for (int i = 0; i < built.Length; i++)
                if (built[i] != null) passingIds.Add(results[i].LineId);
            if (passingIds.Count == 0) return placements;

            var byId = corpus().FetchPlacements(passingIds);
            for (int i = 0; i < built.Length; i++)
            {
                if (built[i] is not { } hit || !byId.TryGetValue(hit.LineId, out var at)) continue;
                placements[i] = at;
                hit.BookId = at.BookId;
                hit.TocEntryId = at.TocEntryId;
            }
            return placements;
        }

        /// <summary>
        /// Adds the texts of one hit's snippet lines to <paramref name="lineTexts"/>: the matched
        /// line's own content, and any neighbour line the window was extended over, from the
        /// texts that extension already had in hand. An id neither has (cannot happen for a
        /// descriptor built here) is left out; the page fetches what it lacks on demand.
        /// </summary>
        private static void CollectSnippetLineTexts(
            FtsHit hit, string matchedLineContent,
            Dictionary<int, string>? neighbourTextsById, Dictionary<int, string> lineTexts)
        {
            foreach (int id in hit.SnippetLineIds)
            {
                if (lineTexts.ContainsKey(id)) continue;
                if (id == hit.LineId) lineTexts[id] = matchedLineContent;
                else if (neighbourTextsById != null && neighbourTextsById.TryGetValue(id, out var text)) lineTexts[id] = text;
            }
        }

        /// <summary>
        /// One engine match → one finished hit, or false when it does not pass the match and
        /// word-distance filters. <paramref name="windowWordCount"/> is what decides whether
        /// the window is "short" and worth extending — see
        /// <see cref="ExtendShortWindowsWithNeighbourLines"/>.
        /// </summary>
        private static bool TryBuildHit(
            Fts5SeforimIndex index,
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
                LineId = hit.LineId,   // BookId / TocEntryId: set once the placements are read
                Score = window.Score,
                WordDistance = window.WordDistance,
            };
            built.SnippetLineIds = new[] { hit.LineId };
            CopySnippetDescriptor(built, in window, terms);
            return true;
        }

        /// <summary>
        /// Copies the located window onto the hit — offsets and highlight ranges over the text
        /// of <see cref="FtsHit.SnippetLineIds"/> joined by single spaces, which the caller has
        /// already set. The page rebuilds the HTML from that text only for the rows it renders.
        /// Copied, not referenced: the engine's mark ranges view per-thread scratch that the
        /// next locate overwrites. One exactly-sized array per hit.
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

        /// <summary>
        /// Re-locates the batch's SHORT windows — those holding fewer words than the requested
        /// per-side context, meaning the matched line itself was too short to fill them — over
        /// the matched line joined with its surrounding lines from the same book, and points
        /// the descriptor at those line ids.
        ///
        /// The neighbours are the lines at (bookId, lineIndex ± 1..<see cref="NeighborLineRadius"/>).
        /// Results arrive in catalog order, so most of those lines are other hits of the SAME
        /// batch whose content is already in hand; only the rest are fetched, in one statement
        /// per batch (<see cref="SeforimDbFtsCorpus.FetchLinesAtBookPositions"/>). A batch
        /// with nothing short pays nothing, and a broad query's typical batch has only about
        /// one short line in six.
        ///
        /// Joined exactly as before — each side's non-empty lines in document order, single
        /// spaces between — so a descriptor's offsets land on the same characters the page
        /// re-joins (JoinWithNeighbourLines).
        /// </summary>
        /// <returns>The neighbour lines' texts by line id — what seeding the first hits' line
        /// texts draws on — or null when the batch had nothing short.</returns>
        private static Dictionary<int, string>? ExtendShortWindowsWithNeighbourLines(
            SeforimDbFtsEngine engine,
            Func<SeforimDbFtsCorpus> corpus,
            IReadOnlyList<SearchResult> results,
            FtsDocumentPlacement[] placements,
            FtsHit?[] built,
            int[] windowWords,
            bool requireOrdered,
            int contextWords,
            FtsTermTable terms,
            CancellationToken cancellationToken)
        {
            List<int>? shortIndexes = null;
            for (int i = 0; i < built.Length; i++)
            {
                if (built[i] == null || windowWords[i] >= contextWords) continue;
                if (placements[i].BookId == 0) continue;   // no placement: nothing to extend over
                (shortIndexes ??= new List<int>()).Add(i);
            }
            if (shortIndexes == null) return null;

            var linesByPosition = CollectNeighbourLines(corpus, results, placements, shortIndexes);
            if (linesByPosition.Count == 0) return null;
            var index = engine.Index;

            Parallel.ForEach(shortIndexes, new ParallelOptions { CancellationToken = cancellationToken }, i =>
            {
                var match = results[i];
                var previousIds = new List<int>(NeighborLineRadius);
                var nextIds = new List<int>(NeighborLineRadius);
                string previous = JoinNeighbourSide(linesByPosition, placements[i], -NeighborLineRadius, -1, previousIds);
                string next = JoinNeighbourSide(linesByPosition, placements[i], 1, NeighborLineRadius, nextIds);
                if (previous.Length == 0 && next.Length == 0) return;

                var extended = index.LocateSnippetWindowOverNeighbourLines(
                    match, previous, next, requireOrdered, contextWords);

                // Only the snippet descriptor is swapped in: score and word distance stay as
                // computed on the matched line itself, because those are the relevance keys and
                // the neighbours are presentation. A failed re-locate (should not happen — same
                // terms) keeps the original window rather than blanking it.
                if (extended.IsMatch && built[i] is { } hit)
                {
                    // The text the engine joined: previous + ' ' + line + ' ' + next, empty sides
                    // dropped — i.e. exactly these ids' texts joined by single spaces.
                    var lineIds = new int[previousIds.Count + 1 + nextIds.Count];
                    previousIds.CopyTo(lineIds, 0);
                    lineIds[previousIds.Count] = match.LineId;
                    nextIds.CopyTo(lineIds, previousIds.Count + 1);
                    hit.SnippetLineIds = lineIds;
                    CopySnippetDescriptor(hit, in extended, terms);
                }
            });

            var textsById = new Dictionary<int, string>(linesByPosition.Count);
            foreach (var line in linesByPosition.Values) textsById[line.Id] = line.Text;
            return textsById;
        }

        /// <summary>
        /// The neighbour lines of the batch's short hits, keyed by (bookId, lineIndex): first
        /// whatever the batch already fetched (its passing results, whose placement was read),
        /// then ONE corpus fetch for the positions still missing. Lines of the batch that are
        /// not neighbours of a short hit are not collected.
        /// </summary>
        private static Dictionary<(int BookId, int LineIndex), (int Id, string Text)> CollectNeighbourLines(
            Func<SeforimDbFtsCorpus> corpus, IReadOnlyList<SearchResult> results,
            FtsDocumentPlacement[] placements, List<int> shortIndexes)
        {
            var wanted = new HashSet<(int BookId, int LineIndex)>(shortIndexes.Count * 2 * NeighborLineRadius);
            foreach (int i in shortIndexes)
            {
                var at = placements[i];
                for (int delta = -NeighborLineRadius; delta <= NeighborLineRadius; delta++)
                    if (delta != 0) wanted.Add((at.BookId, at.LineIndex + delta));
            }

            var linesByPosition = new Dictionary<(int BookId, int LineIndex), (int Id, string Text)>(wanted.Count);
            for (int i = 0; i < results.Count; i++)
            {
                if (placements[i].BookId == 0) continue;   // not passing: placement not read
                var key = (placements[i].BookId, placements[i].LineIndex);
                if (wanted.Contains(key)) linesByPosition[key] = (results[i].LineId, results[i].Content);
            }

            if (linesByPosition.Count < wanted.Count)
            {
                var missing = new List<(int BookId, int LineIndex)>(wanted.Count - linesByPosition.Count);
                foreach (var key in wanted)
                    if (!linesByPosition.ContainsKey(key)) missing.Add(key);
                foreach (var line in corpus().FetchLinesAtBookPositions(missing))
                    linesByPosition[(line.BookId, line.LineIndex)] = (line.Id, line.Text);
            }
            return linesByPosition;
        }

        /// <summary>One side of a short hit's neighbours: the non-empty lines at lineIndex +
        /// <paramref name="firstDelta"/>..<paramref name="lastDelta"/> (document order), their
        /// texts joined by single spaces, their ids appended to <paramref name="idsOut"/>.</summary>
        private static string JoinNeighbourSide(
            Dictionary<(int BookId, int LineIndex), (int Id, string Text)> linesByPosition,
            FtsDocumentPlacement at, int firstDelta, int lastDelta, List<int> idsOut)
        {
            string? joined = null;
            for (int delta = firstDelta; delta <= lastDelta; delta++)
            {
                if (!linesByPosition.TryGetValue((at.BookId, at.LineIndex + delta), out var line)) continue;
                if (line.Text.Length == 0) continue;
                idsOut.Add(line.Id);
                joined = joined == null ? line.Text : joined + " " + line.Text;
            }
            return joined ?? string.Empty;
        }
    }
}
