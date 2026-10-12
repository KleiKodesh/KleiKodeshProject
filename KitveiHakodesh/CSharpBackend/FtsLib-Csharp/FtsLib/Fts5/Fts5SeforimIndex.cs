#if NET6_0_OR_GREATER
using FtsLib.Indexing;
using FtsLib.SeforimDb;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace FtsLib.Fts5
{
    /// <summary>
    /// TEMPORARY (FTS5 trial in the service): a drop-in for <see cref="SeforimIndex"/> — the
    /// same public members Core calls, with SQLite FTS5 doing the search. Core selects it with a
    /// `using SeforimIndex = FtsLib.Fts5.Fts5SeforimIndex;` alias; removing those aliases
    /// restores FtsLib's own engine.
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
        /// <summary>The file whose presence means a searchable index — what the "segments
        /// exist" checks look for while this engine is wired in.</summary>
        public const string IndexFileName = Fts5Index.LinesDbName;

        public const int DefaultContextWords = SeforimIndex.DefaultContextWords;

        readonly string _indexPath;
        readonly Func<IFtsCorpus> _openCorpus;

        public Fts5SeforimIndex(string indexPath, Func<IFtsCorpus> openCorpus)
        {
            _indexPath = indexPath ?? throw new ArgumentNullException(nameof(indexPath));
            _openCorpus = openCorpus ?? throw new ArgumentNullException(nameof(openCorpus));
        }

        bool IsBuilt => File.Exists(Path.Combine(_indexPath, IndexFileName));

        // ── Search ────────────────────────────────────────────────────

        public IEnumerable<SearchResult> Search(string query, int cap = 0, bool expandKetiv = false,
                                                IEnumerable<int> filterIds = null, CancellationToken ct = default)
        {
            if (!IsBuilt) yield break;
            using var engine = new Fts5SearchEngine(_indexPath, _openCorpus);
            foreach (var result in engine.Search(query, cap, expandKetiv, filterIds, ct))
                yield return result;
        }

        public IEnumerable<int> SearchIds(string query, bool expandKetiv = false,
                                          IEnumerable<int> filterIds = null, CancellationToken ct = default)
        {
            if (!IsBuilt) yield break;
            using var engine = new Fts5SearchEngine(_indexPath, _openCorpus);
            foreach (int id in engine.SearchIds(query, expandKetiv, filterIds, ct))
                yield return id;
        }

        // ── Snippets (FtsLib's own, unchanged) ───────────────────────

        public SnippetResult GenerateSnippet(SearchResult result, bool requireOrdered = false,
                                             int contextWords = DefaultContextWords)
            => Fts5SearchEngine.GenerateSnippet(result, requireOrdered, contextWords);

        public SnippetResult GenerateSnippetWithNeighbors(SearchResult result, string prevContent, string nextContent,
                                                          bool requireOrdered = false, int contextWords = DefaultContextWords)
        {
            if (result == null || result.MatchedGroups.Count == 0) return SnippetResult.NoMatch;
            var prepared = result.Prepared ?? FtsLib.Snippets.PreparedQueryGroups.FromGroups(result.MatchedGroups);
            return SnippetPipeline.GenerateWithNeighbors(prevContent, result.Content, nextContent, prepared,
                                                         requireOrdered, result.OriginalGroupCount, contextWords);
        }

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
        /// <paramref name="onFlush"/> fires after every committed batch. The FTS5 'optimize'
        /// and the term dictionary run at the end; <paramref name="onMergeProgress"/> gets the
        /// (0, 0) phase-start signal and (1, 1) when done. <paramref name="resumeOffset"/> and
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
                Fts5Index.BuildLines(_openCorpus, _indexPath, limit, totalLines, onProgress, onFlush, ct, backgroundPriority);
                ct.ThrowIfCancellationRequested();
                onMergeProgress?.Invoke(0, 0);
                Fts5Index.BuildDictionary(_indexPath);
                Fts5Index.MarkComplete(_indexPath);
                onMergeProgress?.Invoke(1, 1);
            }
            return true;
        }
    }
}
#endif
