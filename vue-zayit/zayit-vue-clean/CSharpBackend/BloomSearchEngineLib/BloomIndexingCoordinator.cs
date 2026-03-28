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
        private const string MutexName = "Global\\ZayitBloomIndexing";

        public static event EventHandler<IndexProgressChangedEventArgs> ProgressChanged;

        static BloomIndexingCoordinator()
        {
            try { _mutex = new Mutex(false, MutexName); }
            catch (Exception ex) { Console.WriteLine("[BloomIndexingCoordinator] Mutex error: " + ex.Message); }
        }

        public static bool IsIndexing { get { lock (_lock) return _isIndexing; } }
        public static IndexProgressChangedEventArgs LastProgress { get { lock (_lock) return _lastProgress; } }

        public static bool TryAcquireIndexingLock(int timeoutMs = 0)
        {
            if (_mutex == null) return false;
            try
            {
                bool acquired = _mutex.WaitOne(timeoutMs);
                if (acquired) lock (_lock) { _isIndexing = true; _lastProgress = null; }
                return acquired;
            }
            catch (AbandonedMutexException)
            {
                lock (_lock) { _isIndexing = true; _lastProgress = null; }
                return true;
            }
            catch (Exception ex) { Console.WriteLine("[BloomIndexingCoordinator] Lock error: " + ex.Message); return false; }
        }

        public static void ReleaseIndexingLock()
        {
            try { lock (_lock) { _isIndexing = false; } _mutex?.ReleaseMutex(); }
            catch (Exception ex) { Console.WriteLine("[BloomIndexingCoordinator] Release error: " + ex.Message); }
        }

        public static void NotifyProgress(IndexProgressChangedEventArgs progress)
        {
            lock (_lock) { _lastProgress = progress; }
            ThreadPool.QueueUserWorkItem(_ => { try { ProgressChanged?.Invoke(null, progress); } catch { } });
        }

        public static bool IsAnotherInstanceIndexing()
        {
            if (_mutex == null) return false;
            try
            {
                bool can = _mutex.WaitOne(0);
                if (can) { _mutex.ReleaseMutex(); return false; }
                return true;
            }
            catch (AbandonedMutexException) { _mutex.ReleaseMutex(); return false; }
            catch { return false; }
        }
    }
}
