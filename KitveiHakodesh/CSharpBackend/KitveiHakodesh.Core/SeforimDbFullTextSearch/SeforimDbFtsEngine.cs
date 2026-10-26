using System;
using FtsLib.SeforimDb;
using FtsLib.Fts5;

namespace KitveiHakodesh.Core.SeforimDbFullTextSearch
{
    /// <summary>
    /// Owns the search engine instance, so a host never has to.
    ///
    /// Both apps used to construct FtsLib's <c>SeforimIndex</c> themselves and pass it into every
    /// Core call — which meant both had to REFERENCE FtsLib, and both hand-rolled the same
    /// lock-guarded lazy singleton to hold it. Core is the layer that knows the engine; a host
    /// should only know that full-text search exists.
    ///
    /// ONE instance per index directory is the rule, not an optimisation: a build and the
    /// searches running beside it must share the engine, because the build publishes new
    /// segments through it and a second instance would not see them. That is why this hands out
    /// the same object until <see cref="Invalidate"/> is called.
    ///
    /// <see cref="Invalidate"/> is what a wipe needs. Deleting the index directory under a live
    /// engine leaves an object answering over files that no longer exist, so the owner drops the
    /// instance first and the next caller gets a fresh one over the rebuilt directory.
    ///
    /// Thread-safe. Callers may be the UI thread, a background build, and several concurrent
    /// searches at once.
    /// </summary>
    public sealed class SeforimDbFtsEngine : IDisposable
    {
        private readonly object _lock = new object();
        private readonly string _indexPath;
        private readonly string _seforimDbPath;

        private Fts5SeforimIndex? _index;
        private bool _disposed;

        /// <param name="indexPath">The index directory.</param>
        /// <param name="seforimDbPath">The library the index is built from.</param>
        public SeforimDbFtsEngine(string indexPath, string seforimDbPath)
        {
            if (string.IsNullOrWhiteSpace(indexPath))
                throw new ArgumentException("indexPath is required", nameof(indexPath));
            if (string.IsNullOrWhiteSpace(seforimDbPath))
                throw new ArgumentException("seforimDbPath is required", nameof(seforimDbPath));

            _indexPath = indexPath;
            _seforimDbPath = seforimDbPath;
        }

        public string IndexPath => _indexPath;

        /// <summary>
        /// The engine, opened on first use.
        ///
        /// Internal: this is the seam between Core and FtsLib, and it stays on Core's side of it.
        /// A host that could reach the engine would be back to passing it around, which is the
        /// coupling this type exists to remove.
        /// </summary>
        internal Fts5SeforimIndex Index
        {
            get
            {
                lock (_lock)
                {
                    if (_disposed) throw new ObjectDisposedException(nameof(SeforimDbFtsEngine));
                    // Core supplies the corpus; the engine opens no database of its own. The
                    // factory (not an instance) is what IFtsCorpus requires: a search returns a
                    // LAZY sequence that must outlive this call, and concurrent searches cannot
                    // share one connection — so the engine opens one per operation and disposes it.
                    return _index ?? (_index = new Fts5SeforimIndex(
                        _indexPath,
                        () => new SeforimDbFtsCorpus(_seforimDbPath)));
                }
            }
        }

        /// <summary>
        /// A fresh corpus over the same library the index reads — for Core-side reads the
        /// engine's own API does not carry (snippet descriptors need neighbour line IDS, not
        /// just their text). One per operation; the caller disposes it.
        /// </summary>
        internal SeforimDbFtsCorpus OpenCorpus() => new SeforimDbFtsCorpus(_seforimDbPath);

        /// <summary>
        /// Whether an engine is currently open. False before the first use and after a
        /// <see cref="Invalidate"/>; asking does not open one.
        /// </summary>
        public bool IsOpen { get { lock (_lock) return _index != null; } }

        /// <summary>
        /// A fragment contained in more dictionary terms than this is too common for the
        /// index to be the cheap answer: the caller reads the book instead.
        /// </summary>
        public const int MaxTermsPerSubstringFragment = 4000;

        // Each book's block of index positions — read once per engine (one index build), as
        // it takes a pass over the whole line table. Dropped with the engine on Invalidate.
        private Dictionary<int, (long First, long Count)>? _bookPositions;

        /// <summary>
        /// The lines inside the given (book, first lineIndex, last lineIndex) ranges whose
        /// text may hold <paramref name="query"/> as a substring (a superset — the caller
        /// confirms on the text): as (book, lineIndex, line id), in range order then reading
        /// order. Null when the index cannot answer exactly (still building, or a fragment
        /// too common): the caller reads the lines itself. A book the index does not hold
        /// contributes nothing.
        /// </summary>
        public List<(int BookId, int LineIndex, int LineId)>? SubstringCandidates(
            string query, IReadOnlyList<(int BookId, int FirstLineIndex, int LastLineIndex)> lineRanges, CancellationToken ct = default)
        {
            Dictionary<int, (long First, long Count)> books;
            lock (_lock)
            {
                if (_bookPositions == null)
                {
                    using var corpus = OpenCorpus();
                    _bookPositions = corpus.BookPositionRanges();
                }
                books = _bookPositions;
            }
            var ranges = new List<(long First, long Last)>(lineRanges.Count);
            var owners = new List<(int BookId, long First)>(lineRanges.Count);
            foreach (var (bookId, firstLine, lastLine) in lineRanges)
            {
                if (!books.TryGetValue(bookId, out var book) || book.Count == 0) continue;
                long from = Math.Max(0, firstLine), to = Math.Min(book.Count - 1, lastLine);
                if (to < from) continue;
                ranges.Add((book.First + from, book.First + to));
                owners.Add((bookId, book.First));
            }
            if (ranges.Count == 0) return new List<(int, int, int)>();
            var found = Index.FindSubstringCandidates(query, ranges, MaxTermsPerSubstringFragment, ct);
            if (found == null) return null;
            var result = new List<(int, int, int)>(found.Count);
            foreach (var (range, position, lineId) in found)
                result.Add((owners[range].BookId, (int)(position - owners[range].First), lineId));
            return result;
        }

        /// <summary>
        /// Where an interrupted build left off, read from the index's own progress file.
        ///
        /// <paramref name="totalLines"/> is the corpus size cached by the previous session, so
        /// resuming never rescans the library just to size a progress bar; it is 0 when nothing
        /// has been built yet.
        /// </summary>
        public void GetResumeState(out int lineId, out long totalLines, out long resumeOffset)
            => Index.GetResumeState(out lineId, out totalLines, out resumeOffset);

        /// <summary>
        /// Clears the resume watermark. Called once a build has completed, so the next launch
        /// does not try to resume past the end of a finished index.
        /// </summary>
        public void DeleteBuildProgressFile() => Index.DeleteBuildProgressFile();

        /// <summary>
        /// Whether a build is running against this index directory — in THIS process or any
        /// other.
        ///
        /// Asks the lock every builder actually takes, which is the only answer that holds
        /// across processes. A host that tracks its own builds knows nothing about the other
        /// leg's, and a host that keeps a named mutex only learns about hosts that agreed to
        /// use the same mutex — the hosted app had one the service never took, so a service
        /// build looked like no build at all.
        ///
        /// ADVISORY. The lock can change hands the instant after this returns, so use it to
        /// decide what to SHOW — a progress bar for someone else's build — never whether it is
        /// safe to start one. For that, start it and let the lock refuse.
        /// </summary>
        public bool IsBuildInProgress() => IsBuildInProgress(_indexPath);

        /// <summary>
        /// As <see cref="IsBuildInProgress()"/>, for an index directory with no engine open.
        ///
        /// Static because asking must never CONSTRUCT one: opening an engine runs crash
        /// recovery and self-heals sidecars, which is a great deal of work for a caller that
        /// only wants to decide whether to show a progress bar.
        /// </summary>
        public static bool IsBuildInProgress(string indexPath) =>
            Fts5SeforimIndex.IsBuildInProgress(indexPath);

        /// <summary>
        /// Drops the open engine so the next caller opens a fresh one.
        ///
        /// Call this BEFORE deleting the index directory. The engine holds file handles under
        /// it, so wiping underneath a live instance leaves it answering over deleted files —
        /// and on Windows the delete itself can fail against the open handles.
        /// </summary>
        public void Invalidate()
        {
            lock (_lock) { _index = null; _bookPositions = null; }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _index = null;
                _disposed = true;
            }
        }
    }
}
