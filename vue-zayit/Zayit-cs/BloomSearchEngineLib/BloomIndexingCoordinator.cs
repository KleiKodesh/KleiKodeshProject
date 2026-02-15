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
                Console.WriteLine($"[BloomIndexingCoordinator] Failed to create mutex: {ex}");
            }
        }

        /// <summary>
        /// Check if indexing is currently in progress (across all instances)
        /// </summary>
        public static bool IsIndexing
        {
            get
            {
                lock (_lock)
                {
                    return _isIndexing;
                }
            }
        }

        /// <summary>
        /// Get the last reported progress
        /// </summary>
        public static IndexProgressChangedEventArgs LastProgress
        {
            get
            {
                lock (_lock)
                {
                    return _lastProgress;
                }
            }
        }

        /// <summary>
        /// Try to acquire the indexing lock. Returns true if successful.
        /// Only one instance across all processes can hold the lock.
        /// </summary>
        public static bool TryAcquireIndexingLock(int timeoutMs = 0)
        {
            try
            {
                if (_globalMutex == null)
                {
                    Console.WriteLine("[BloomIndexingCoordinator] Mutex not available");
                    return false;
                }

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
                // Previous process crashed while holding the mutex
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
                Console.WriteLine($"[BloomIndexingCoordinator] Error acquiring lock: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Release the indexing lock
        /// </summary>
        public static void ReleaseIndexingLock()
        {
            try
            {
                lock (_lock)
                {
                    _isIndexing = false;
                }

                if (_globalMutex != null)
                {
                    _globalMutex.ReleaseMutex();
                    Console.WriteLine("[BloomIndexingCoordinator] Indexing lock released");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BloomIndexingCoordinator] Error releasing lock: {ex}");
            }
        }

        /// <summary>
        /// Notify all subscribed instances of indexing progress
        /// </summary>
        public static void NotifyProgress(IndexProgressChangedEventArgs progress)
        {
            lock (_lock)
            {
                _lastProgress = progress;
            }

            // Raise event on background thread to avoid blocking
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    ProgressChanged?.Invoke(null, progress);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BloomIndexingCoordinator] Error notifying progress: {ex}");
                }
            });
        }

        /// <summary>
        /// Check if another instance is currently indexing
        /// </summary>
        public static bool IsAnotherInstanceIndexing()
        {
            try
            {
                if (_globalMutex == null)
                    return false;

                // Try to acquire with 0 timeout - if we can't, someone else has it
                bool canAcquire = _globalMutex.WaitOne(0);
                if (canAcquire)
                {
                    // We got it, so release immediately
                    _globalMutex.ReleaseMutex();
                    return false;
                }
                return true;
            }
            catch (AbandonedMutexException)
            {
                // Previous process crashed - we can take over
                _globalMutex.ReleaseMutex();
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BloomIndexingCoordinator] Error checking indexing status: {ex}");
                return false;
            }
        }
    }
}
