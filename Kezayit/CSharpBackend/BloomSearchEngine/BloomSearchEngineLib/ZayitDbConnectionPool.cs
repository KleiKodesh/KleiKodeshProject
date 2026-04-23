using System;
using System.Collections.Generic;
using System.Threading;

namespace BloomSearchEngineLib
{
    /// <summary>
    /// A fixed-size pool of warm ZayitDbManager connections.
    ///
    /// Connections are opened once at construction and reused across searches,
    /// eliminating the ~65ms cold-open cost (file open + WAL recovery + PRAGMA setup)
    /// on every search call.
    ///
    /// Thread safety: a SemaphoreSlim gates access so callers block until a connection
    /// is available. Pool size 1 means searches are serialized (current behaviour).
    /// Raising the pool size to N allows N concurrent searches without any other changes.
    ///
    /// Usage:
    ///   using (var lease = pool.Acquire())
    ///   {
    ///       lease.Db.GetLineContentsChunk(...);
    ///   }  // connection returned to pool automatically
    /// </summary>
    public sealed class ZayitDbConnectionPool : IDisposable
    {
        private readonly Queue<ZayitDbManager> _idle;
        private readonly SemaphoreSlim _semaphore;
        private readonly object _lock = new object();
        private bool _disposed;

        public int PoolSize { get; }

        public ZayitDbConnectionPool(int poolSize = 1, string dbPath = null)
        {
            if (poolSize < 1) throw new ArgumentOutOfRangeException("poolSize");
            PoolSize = poolSize;
            _idle = new Queue<ZayitDbManager>(poolSize);
            _semaphore = new SemaphoreSlim(poolSize, poolSize);

            for (int i = 0; i < poolSize; i++)
                _idle.Enqueue(new ZayitDbManager(dbPath));

            Console.WriteLine("[ZayitDbConnectionPool] Opened {0} connection(s)", poolSize);
        }

        /// <summary>
        /// Acquires a connection from the pool, blocking until one is available.
        /// Dispose the returned lease to return the connection to the pool.
        /// </summary>
        public Lease Acquire()
        {
            _semaphore.Wait();
            ZayitDbManager db;
            lock (_lock)
            {
                db = _idle.Dequeue();
            }
            return new Lease(db, this);
        }

        private void Return(ZayitDbManager db)
        {
            lock (_lock)
            {
                _idle.Enqueue(db);
            }
            _semaphore.Release();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (_lock)
            {
                while (_idle.Count > 0)
                    _idle.Dequeue().Dispose();
            }
            _semaphore.Dispose();
        }

        /// <summary>
        /// RAII lease — holds a connection for the duration of a using block.
        /// </summary>
        public sealed class Lease : IDisposable
        {
            public ZayitDbManager Db { get; }
            private readonly ZayitDbConnectionPool _pool;
            private bool _returned;

            internal Lease(ZayitDbManager db, ZayitDbConnectionPool pool)
            {
                Db = db;
                _pool = pool;
            }

            public void Dispose()
            {
                if (_returned) return;
                _returned = true;
                _pool.Return(Db);
            }
        }
    }
}
