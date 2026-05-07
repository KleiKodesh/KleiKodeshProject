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

        /// <summary>
        /// Acquires the exclusive write lock on <paramref name="indexPath"/>.
        /// Throws <see cref="IndexWriteLockException"/> if the lock is already held.
        /// </summary>
        public IndexWriteLock(string indexPath)
        {
            if (!Directory.Exists(indexPath))
                Directory.CreateDirectory(indexPath);

            string lockFilePath = Path.Combine(indexPath, LockFileName);

            try
            {
                _lockStream = new FileStream(
                    lockFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);   // no other process or thread may open this file
            }
            catch (IOException)
            {
                throw new IndexWriteLockException(indexPath);
            }
        }

        public void Dispose()
        {
            try
            {
                _lockStream?.Dispose();
            }
            catch { /* best-effort — lock is released when the stream is GC'd anyway */ }
        }
    }
}
