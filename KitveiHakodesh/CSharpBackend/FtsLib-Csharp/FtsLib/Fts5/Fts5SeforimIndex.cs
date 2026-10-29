#if NET6_0_OR_GREATER
using FtsLib.Indexing;
using FtsLib.SeforimDb;
using FtsLib.Snippets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace FtsLib.Fts5
{
    /// <summary>
    /// The SQLite FTS5 engine behind Core's full-text search — FtsLib's public entry point.
    ///
    /// Index files (<see cref="Fts5Index"/>) live directly in the index directory. The line
    /// table is built in place and committed in batches, so searches see every committed
    /// line while the build runs (wildcard and fuzzy on a slower path until the term
    /// dictionary is published at the end). An interrupted build resumes after the last
    /// committed line.
    ///
    /// Every search opens its own <see cref="Fts5SearchEngine"/> (connections are not shared
    /// across threads) and disposes it when the result stream ends.
    /// </summary>
    public sealed class Fts5SeforimIndex
    {
        /// <summary>The file whose presence means a searchable index.</summary>
        public const string IndexFileName = Fts5Index.LinesDbName;

        public const int DefaultContextWords = SnippetDescriptorPipeline.DefaultContextWords;

        readonly string _indexPath;
        readonly Func<IFtsCorpus> _openCorpus;

        public Fts5SeforimIndex(string indexPath, Func<IFtsCorpus> openCorpus)
        {
            _indexPath = indexPath ?? throw new ArgumentNullException(nameof(indexPath));
            _openCorpus = openCorpus ?? throw new ArgumentNullException(nameof(openCorpus));
        }

        bool IsBuilt => File.Exists(Path.Combine(_indexPath, IndexFileName));

        // ── Search ────────────────────────────────────────────────────

        /// <param name="batchSizeAt">The caller's batch plan — see
        /// <see cref="Fts5SearchEngine.Search"/>.</param>
        /// <param name="withPlacement">See <see cref="Fts5SearchEngine.Search"/>.</param>
        /// <param name="afterPosition">See <see cref="Fts5SearchEngine.Search"/>.</param>
        public IEnumerable<SearchResult> Search(string query, int cap = 0,
                                                IEnumerable<int> filterIds = null, CancellationToken ct = default,
                                                Action<string> onStage = null, Func<int, int> batchSizeAt = null,
                                                bool withPlacement = true, long afterPosition = 0)
        {
            if (!IsBuilt) yield break;
            using var engine = new Fts5SearchEngine(_indexPath, _openCorpus);
            foreach (var result in engine.Search(query, cap, filterIds, ct, onStage, batchSizeAt, withPlacement, afterPosition))
                yield return result;
        }

        public IEnumerable<int> SearchIds(string query,
                                          IEnumerable<int> filterIds = null, CancellationToken ct = default)
        {
            if (!IsBuilt) yield break;
            using var engine = new Fts5SearchEngine(_indexPath, _openCorpus);
            foreach (int id in engine.SearchIds(query, filterIds, ct))
                yield return id;
        }

        /// <summary>
        /// Substring candidates inside blocks of positions — see
        /// <see cref="Fts5SearchEngine.FindSubstringCandidates"/>. Null whenever the index
        /// cannot answer exactly, including while it is still being built: only a complete
        /// index has every committed line's terms in its dictionary.
        /// </summary>
        public List<(int Range, long Position, int LineId)> FindSubstringCandidates(
            string query, IReadOnlyList<(long First, long Last)> ranges, int maxTermsPerFragment, CancellationToken ct = default)
        {
            if (!IsBuilt) return null;
            var state = Fts5Index.ReadBuildState(_indexPath);
            if (state == null || !state.Value.Complete) return null;
            using var engine = new Fts5SearchEngine(_indexPath, _openCorpus);
            return engine.FindSubstringCandidates(query, ranges, maxTermsPerFragment, ct);
        }

        // ── Snippet windows (FtsLib's own, unchanged) ────────────────

        public SnippetDescriptor LocateSnippetWindow(SearchResult result, bool requireOrdered = false,
                                                     int contextWords = DefaultContextWords)
            => Fts5SearchEngine.LocateSnippetWindow(result, requireOrdered, contextWords);

        public Dictionary<int, (string Prev, string Next)> FetchNeighborContext(IReadOnlyList<int> lineIds, int radius)
        {
            var neighbours = new Dictionary<int, (string Prev, string Next)>();
            if (lineIds == null || lineIds.Count == 0 || radius <= 0) return neighbours;
            using var corpus = _openCorpus();
            foreach (var entry in corpus.FetchNeighbourText(lineIds, radius))
                neighbours[entry.Key] = (entry.Value.Previous, entry.Value.Next);
            return neighbours;
        }

        // ── Build ─────────────────────────────────────────────────────

        public long CountLines()
        {
            using var corpus = _openCorpus();
            return corpus.CountDocuments();
        }

        /// <summary>Where an interrupted build stopped: last committed line, the corpus size
        /// it recorded, and how many lines were done. All 0 when there is nothing to resume.</summary>
        public void GetResumeState(out int lineId, out long totalLines, out long resumeOffset)
        {
            lineId = 0; totalLines = 0; resumeOffset = 0;
            var state = Fts5Index.ReadBuildState(_indexPath);
            if (state == null || state.Value.Complete) return;
            lineId = state.Value.LastLineId;
            totalLines = state.Value.TotalLines;
            resumeOffset = state.Value.LinesDone;
        }

        public void DeleteBuildProgressFile() { }

        public static bool IsBuildInProgress(string indexPath) => IndexWriteLock.IsHeld(indexPath);

        /// <summary>
        /// Builds (or resumes) the index under FtsLib's write lock, so
        /// <see cref="IsBuildInProgress"/> and concurrent builders behave as with FtsLib.
        /// <paramref name="onProgress"/> counts lines of THIS session, as FtsLib's does;
        /// <paramref name="onFlush"/> fires after every committed batch. The final phase — FTS5
        /// 'optimize' + VACUUM, then the term dictionary — reports through
        /// <paramref name="onMergeProgress"/> as (0, 2) before it starts, (1, 2), (2, 2), so
        /// the app shows it as its own phase instead of a bar stuck at 99.9%. It runs at
        /// NORMAL priority: a few minutes of single-threaded work that, throttled by
        /// background mode, took 16 minutes live; it holds no lock searches wait on. <paramref name="resumeOffset"/> and
        /// <paramref name="forceMergeOnComplete"/> are accepted for signature compatibility.
        /// </summary>
        public bool BuildIndex(int limit = 0, Action<long> onProgress = null, Action onFlush = null,
                               long totalLines = 0, long resumeOffset = 0, bool forceMergeOnComplete = false,
                               CancellationToken ct = default, bool backgroundPriority = false,
                               Action<int, int> onMergeProgress = null)
        {
            Directory.CreateDirectory(_indexPath);
            using (new IndexWriteLock(_indexPath))
            using (BackgroundPriorityScope.EnterIf(backgroundPriority))
            {
                var state = Fts5Index.ReadBuildState(_indexPath);
                // terms.db is published only after the lines are complete, so a crash after
                // that point needs nothing but the completion flag.
                if (state != null && !state.Value.Complete && File.Exists(Fts5Index.TermsDb(_indexPath)))
                {
                    Fts5Index.MarkComplete(_indexPath);
                    state = Fts5Index.ReadBuildState(_indexPath);
                }
                if (state != null && state.Value.Complete)
                {
                    Fts5TermDictionaries.Clear(_indexPath);   // leftovers a search still held at publish time
                    return true;                              // already complete: let the caller stamp it
                }
                try
                {
                    Fts5Index.BuildLines(_openCorpus, _indexPath, limit, totalLines, onProgress, onFlush, ct, backgroundPriority);
                }
                catch (Fts5Index.IndexNoLongerMatchesCorpusException ex)
                {
                    // Resuming would index some lines twice and skip others. Start over.
                    FtsLog.Write("Fts5SeforimIndex", "rebuilding from scratch: " + ex.Message);
                    Fts5Index.DeleteIndexFiles(_indexPath);
                    Fts5Index.BuildLines(_openCorpus, _indexPath, limit, totalLines, onProgress, onFlush, ct, backgroundPriority);
                }
                ct.ThrowIfCancellationRequested();
                onMergeProgress?.Invoke(0, 2);
                using (BackgroundPriorityScope.Suspend())
                {
                    Fts5Index.CompactLines(_indexPath, ct);
                    onMergeProgress?.Invoke(1, 2);
                    Fts5Index.BuildDictionary(_indexPath);
                }
                Fts5Index.MarkComplete(_indexPath);
                onMergeProgress?.Invoke(2, 2);
            }
            return true;
        }
    }
}
#endif
