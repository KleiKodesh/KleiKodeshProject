using System;
using FtsLib.SeforimDb;

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

        private SeforimIndex _index;
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
        internal SeforimIndex Index
        {
            get
            {
                lock (_lock)
                {
                    if (_disposed) throw new ObjectDisposedException(nameof(SeforimDbFtsEngine));
                    return _index ?? (_index = new SeforimIndex(_indexPath, _seforimDbPath));
                }
            }
        }

        /// <summary>
        /// Whether an engine is currently open. False before the first use and after a
        /// <see cref="Invalidate"/>; asking does not open one.
        /// </summary>
        public bool IsOpen { get { lock (_lock) return _index != null; } }

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
        /// Drops the open engine so the next caller opens a fresh one.
        ///
        /// Call this BEFORE deleting the index directory. The engine holds file handles under
        /// it, so wiping underneath a live instance leaves it answering over deleted files —
        /// and on Windows the delete itself can fail against the open handles.
        /// </summary>
        public void Invalidate()
        {
            lock (_lock) _index = null;
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
