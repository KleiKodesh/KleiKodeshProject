using System;
using System.Threading;

namespace FtsLib.Indexing
{
    /// <summary>
    /// Holds a read lock on <see cref="SegmentStore"/>'s search/merge exclusion lock
    /// for the lifetime of one search.
    ///
    /// While this lease is alive, any merge that needs to acquire the write lock
    /// (to delete source segment files) will block until the lease is disposed.
    /// This guarantees that no segment file an active <see cref="IndexReader"/> has
    /// open is deleted from under it.
    ///
    /// Obtain via <see cref="SegmentStore.AcquireSearchLease"/>.
    /// Dispose as soon as the corresponding <see cref="IndexReader"/> is disposed.
    /// </summary>
    internal sealed class SearchLease : IDisposable
    {
        private readonly ReaderWriterLockSlim _lock;
        private bool _disposed;

        internal SearchLease(ReaderWriterLockSlim rwLock)
        {
            _lock = rwLock;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lock.ExitReadLock();
        }
    }
}
