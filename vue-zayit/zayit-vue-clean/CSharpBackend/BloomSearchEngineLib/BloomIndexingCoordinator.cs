using System;
using System.IO;
using System.Threading;

namespace BloomSearchEngineLib
{
    /// <summary>
    /// Cross-instance coordinator for bloom filter indexing.
    /// Ensures only one indexing operation runs at a time across all VSTO instances.
    /// Provides progress notifications to all subscribed instances.
    /// </summary>
    public static class BloomIndexingCoordinator
    {
        private static readonly object _lock = new object();
        private static volatile bool _isIndexing = false;
        private static IndexProgressChangedEventArgs _lastProgress;
        private static Mutex _globalMutex;
        private const string MUTEX_NAME = "Global\\ZayitBloomIndexing";

        // Global progress event that all instances can subscribe to
        public static event EventHandler<IndexProgressChangedEventArgs> ProgressChanged;

        static BloomIndexingCoordinator()
        {
            try
            {
                // Create or open global mutex for cross-process coordination
                _globalMutex = new Mutex(false, MUTEX_NAME);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[BloomIndexingCoordinator] Failed to create mutex: " + ex.Message);
            }
        }

        public static bool IsIndexing
        {
            get { lock (_lock) { return _isIndexing; } }
        }

        public static IndexProgressChangedEventArgs LastProgress
        {
            get { lock (_lock) { return _lastProgress; } }
        }

        public static bool TryAcquireIndexingLock(int timeoutMs = 0)
        {
            try
            {
                if (_globalMutex == null)
                    return false;

                bool acquired = _globalMutex.WaitOne(timeoutMs);
                if (acquired)
                {
                    lock (_lock)
                    {
                        _isIndexing = true;
                        _lastProgress = null;
                    }
                    Console.WriteLine("[BloomIndexingCoordinator] Indexing lock acquired");
                }
                else
                {
                    Console.WriteLine("[BloomIndexingCoordinator] Indexing already in progress in another instance");
                }
                return acquired;
            }
            catch (AbandonedMutexException)
            {
                Console.WriteLine("[BloomIndexingCoordinator] Recovered abandoned mutex");
                lock (_lock)
                {
                    _isIndexing = true;
                    _lastProgress = null;
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[BloomIndexingCoordinator] Error acquiring lock: " + ex.Message);
                return false;
            }
        }

        public static void ReleaseIndexingLock()
        {
            try
            {
                lock (_lock) { _isIndexing = false; }

                if (_globalMutex != null)
                {
                    _globalMutex.ReleaseMutex();
                    Console.WriteLine("[BloomIndexingCoordinator] Indexing lock released");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[BloomIndexingCoordinator] Error releasing lock: " + ex.Message);
            }
        }

        public static void NotifyProgress(IndexProgressChangedEventArgs progress)
        {
            lock (_lock) { _lastProgress = progress; }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { ProgressChanged?.Invoke(null, progress); }
                catch (Exception ex)
                {
                    Console.WriteLine("[BloomIndexingCoordinator] Error notifying progress: " + ex.Message);
                }
            });
        }

        public static bool IsAnotherInstanceIndexing()
        {
            try
            {
                if (_globalMutex == null)
                    return false;

                bool canAcquire = _globalMutex.WaitOne(0);
                if (canAcquire)
                {
                    _globalMutex.ReleaseMutex();
                    return false;
                }
                return true;
            }
            catch (AbandonedMutexException)
            {
                _globalMutex.ReleaseMutex();
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[BloomIndexingCoordinator] Error checking indexing status: " + ex.Message);
                return false;
            }
        }
    }
}
