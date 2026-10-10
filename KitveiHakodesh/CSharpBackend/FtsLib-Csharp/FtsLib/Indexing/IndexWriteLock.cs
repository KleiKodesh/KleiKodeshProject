using System;
using System.IO;

namespace FtsLib.Indexing
{
    /// <summary>
    /// Thrown when <see cref="IndexWriteLock"/> cannot acquire the exclusive write
    /// lock on an index directory because another process (or another thread in the
    /// same process) is already building or writing to that directory.
    /// </summary>
    public sealed class IndexWriteLockException : Exception
    {
        public IndexWriteLockException(string indexPath)
            : base($"Another process is already writing to the index directory: {indexPath}") { }
    }


    /// <summary>
    /// Exclusive write lock for an index directory, backed by an OS file lock.
    ///
    /// Opens <c>write.lock</c> inside the index directory with
    /// <see cref="FileShare.None"/>. The OS holds the lock for the lifetime of
    /// the <see cref="FileStream"/> — it is released automatically on
    /// <see cref="Dispose"/> or if the process crashes, so no stale lock files
    /// can block a subsequent run.
    ///
    /// Scope: one lock instance per index directory. Two different index paths
    /// never block each other. Multiple readers (Search, GenerateSnippet) never
    /// need this lock — only write operations (BuildIndex, Optimize, Purge) do.
    ///
    /// Usage:
    /// <code>
    /// using (new IndexWriteLock(indexPath))
    /// {
    ///     // safe to write
    /// }
    /// </code>
    ///
    /// Throws <see cref="IndexWriteLockException"/> immediately if the lock is
    /// already held by another process or thread — never blocks waiting.
    /// </summary>
    internal sealed class IndexWriteLock : IDisposable
    {
        private const string LockFileName = "write.lock";

        private readonly FileStream _lockStream;
        private readonly string     _lockFilePath;

        /// <summary>
        /// Acquires the exclusive write lock on <paramref name="indexPath"/>.
        /// Throws <see cref="IndexWriteLockException"/> if the lock is already held.
        /// </summary>
        public IndexWriteLock(string indexPath)
        {
            if (!Directory.Exists(indexPath))
                Directory.CreateDirectory(indexPath);

            _lockFilePath = Path.Combine(indexPath, LockFileName);

            try
            {
                _lockStream = new FileStream(
                    _lockFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);   // no other process or thread may open this file
            }
            catch (IOException)
            {
                throw new IndexWriteLockException(indexPath);
            }
        }

        /// <summary>
        /// Whether something — this process or another — currently holds the write lock on
        /// <paramref name="indexPath"/>, WITHOUT taking it.
        ///
        /// This is the honest answer to "is a build running?", because it asks the lock every
        /// builder actually takes. A host that keeps its own flag only knows about its own
        /// builds, and a host that keeps a named mutex only knows about the hosts that agreed
        /// to use it.
        ///
        /// Advisory, not a reservation: the lock can be taken or dropped the instant after this
        /// returns. Use it to decide what to SHOW, never to decide whether it is safe to build —
        /// for that, take the lock and let it fail.
        /// </summary>
        public static bool IsHeld(string indexPath)
        {
            string lockFilePath = Path.Combine(indexPath, LockFileName);
            if (!File.Exists(lockFilePath)) return false;

            try
            {
                // Opening with FileShare.None succeeds only when nobody else has it open, so a
                // successful open means NOT held. Closing immediately leaves the file in place
                // — an unheld lock file is normal, and deleting it here could race a builder
                // that is between creating it and locking it.
                using (new FileStream(lockFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    return false;
                }
            }
            catch (IOException)
            {
                return true; // someone holds it
            }
            catch (UnauthorizedAccessException)
            {
                // Cannot open it for reasons other than the lock. Not evidence of a build.
                return false;
            }
        }

        public void Dispose()
        {
            try
            {
                _lockStream?.Dispose();
                File.Delete(_lockFilePath);
            }
            catch { /* best-effort — lock is released when the stream is GC'd anyway */ }
        }
    }
}
