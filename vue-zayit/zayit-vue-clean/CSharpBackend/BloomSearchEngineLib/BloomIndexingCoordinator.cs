using System;
using System.Threading;

namespace BloomSearchEngineLib
{
    public static class BloomIndexingCoordinator
    {
        private static readonly object _lock = new object();
        private static volatile bool _isIndexing;
        private static IndexProgressChangedEventArgs _lastProgress;
        private static Mutex _mutex;
        private static volatile CancellationTokenSource _cts;
        private const string MutexName = "ZayitBloomIndexing";

        public static event EventHandler<IndexProgressChangedEventArgs> ProgressChanged;

        static BloomIndexingCoordinator()
        {
            try { _mutex = new Mutex(false, MutexName); }
            catch (Exception ex) { Console.WriteLine("[BloomIndexingCoordinator] Mutex error: " + ex.Message); }
        }

        public static bool IsIndexing { get { lock (_lock) return _isIndexing; } }
        public static IndexProgressChangedEventArgs LastProgress { get { lock (_lock) return _lastProgress; } }

        /// <summary>Cancel any in-progress indexing run and wait briefly for it to stop.</summary>
        public static void CancelIndexing()
        {
            CancellationTokenSource old;
            lock (_lock) { old = _cts; }
            if (old != null) { try { old.Cancel(); } catch { } }
            for (int i = 0; i < 30 && IsIndexing; i++) Thread.Sleep(100);
        }

        public static bool TryAcquireIndexingLock(int timeoutMs, out CancellationToken ct)
        {
            ct = CancellationToken.None;
            if (_mutex == null) { ct = CancellationToken.None; return false; }
            try
            {
                bool acquired = _mutex.WaitOne(timeoutMs);
                if (acquired)
                {
                    var cts = new CancellationTokenSource();
                    lock (_lock) { _isIndexing = true; _lastProgress = null; _cts = cts; }
                    ct = cts.Token;
                }
                return acquired;
            }
            catch (AbandonedMutexException)
            {
                var cts = new CancellationTokenSource();
                lock (_lock) { _isIndexing = true; _lastProgress = null; _cts = cts; }
                ct = cts.Token;
                return true;
            }
            catch (Exception ex) { Console.WriteLine("[BloomIndexingCoordinator] Lock error: " + ex.Message); return false; }
        }

        public static void ReleaseIndexingLock()
        {
            try
            {
                lock (_lock) { _isIndexing = false; _cts = null; }
                _mutex?.ReleaseMutex();
            }
            catch (Exception ex) { Console.WriteLine("[BloomIndexingCoordinator] Release error: " + ex.Message); }
        }

        public static void NotifyProgress(IndexProgressChangedEventArgs progress)
        {
            lock (_lock) { _lastProgress = progress; }
            ThreadPool.QueueUserWorkItem(_ => { try { ProgressChanged?.Invoke(null, progress); } catch { } });
        }
    }
}
