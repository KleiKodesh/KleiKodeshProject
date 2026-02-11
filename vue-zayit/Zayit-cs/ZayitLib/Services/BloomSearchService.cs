using BloomSearchEngineLib;
using Microsoft.VisualBasic;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Zayit.Services
{
    /// <summary>
    /// Bloom Filter Search Service - Manages search index lifecycle with log-based state tracking
    /// </summary>
    public class BloomSearchService
    {
        private readonly object _lock = new object();
        private bool _isReady = false;
        private bool _isIndexing = false;
        private IndexingProgress _progress;
        private BloomFilterIndexer _indexer;
        private CancellationTokenSource _indexingCts;
        private readonly ConcurrentDictionary<string, SearchSession> _activeSessions = new ConcurrentDictionary<string, SearchSession>();

        // Event callbacks for streaming results to Vue
        private Action<string, List<SearchResultItem>> _onSearchBatch;
        private Action<string> _onSearchComplete;
        private Action<string> _onSearchCancelled;
        private Action<string, string> _onSearchError;

        private const string VB_APP = "ZayitApp";
        private const string VB_SECTION = "BloomIndex";
        private const string VB_KEY_DB_HASH = "LastDbHash";
        private const string LOCK_FILE_NAME = "indexing.lock";

        public BloomSearchService()
        {
            _progress = new IndexingProgress
            {
                IsReady = false,
                IsIndexing = false,
                ProcessedChunks = 0,
                TotalChunks = 0,
                Percentage = 0,
                Eta = ""
            };
        }

        /// <summary>
        /// Set callbacks for streaming search results
        /// </summary>
        public void SetSearchCallbacks(
            Action<string, List<SearchResultItem>> onBatch,
            Action<string> onComplete,
            Action<string> onCancelled,
            Action<string, string> onError)
        {
            _onSearchBatch = onBatch;
            _onSearchComplete = onComplete;
            _onSearchCancelled = onCancelled;
            _onSearchError = onError;
        }

        /// <summary>
        /// Initialize service - check index validity and start/resume indexing if needed
        /// </summary>
        public void Initialize()
        {
            try
            {
                Console.WriteLine("[BloomSearchService] Initializing...");

                // Check if database is available first
                if (!DbQueries.IsDatabaseAvailable())
                {
                    Console.WriteLine("[BloomSearchService] Database not available - skipping initialization");
                    lock (_lock)
                    {
                        _isReady = false;
                        _isIndexing = false;
                        _progress.IsReady = false;
                        _progress.IsIndexing = false;
                    }
                    return;
                }

                // Check if indexing is in progress (lock file exists)
                if (IsIndexingInProgress())
                {
                    Console.WriteLine("[BloomSearchService] Found incomplete indexing, restarting from scratch...");
                    StartIndexingAsync();
                    return;
                }

                // Check if index needs rebuild
                if (NeedsReindex())
                {
                    Console.WriteLine("[BloomSearchService] Index needs rebuild - starting indexing");
                    StartIndexingAsync();
                }
                else
                {
                    Console.WriteLine("[BloomSearchService] Index is valid and ready");
                    lock (_lock)
                    {
                        _isReady = true;
                        _progress.IsReady = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BloomSearchService] Initialization error: {ex}");
            }
        }

        /// <summary>
        /// Check if indexing is currently in progress by checking for lock file
        /// </summary>
        private bool IsIndexingInProgress()
        {
            var lockPath = GetLockFilePath();
            return File.Exists(lockPath);
        }

        /// <summary>
        /// Get the lock file path
        /// </summary>
        private string GetLockFilePath()
        {
            var indexPath = GetIndexPath();
            return Path.Combine(indexPath, LOCK_FILE_NAME);
        }

        /// <summary>
        /// Create lock file to indicate indexing is in progress
        /// </summary>
        private void CreateLockFile()
        {
            try
            {
                var lockPath = GetLockFilePath();
                File.WriteAllText(lockPath, DateTime.UtcNow.ToString("O"));
                Console.WriteLine("[BloomSearchService] Lock file created");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BloomSearchService] Error creating lock file: {ex}");
            }
        }

        /// <summary>
        /// Delete lock file when indexing completes
        /// </summary>
        private void DeleteLockFile()
        {
            try
            {
                var lockPath = GetLockFilePath();
                if (File.Exists(lockPath))
                {
                    File.Delete(lockPath);
                    Console.WriteLine("[BloomSearchService] Lock file deleted");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BloomSearchService] Error deleting lock file: {ex}");
            }
        }

        /// <summary>
        /// Check if index needs to be rebuilt
        /// </summary>
        private bool NeedsReindex()
        {
            try
            {
                // Check if index files exist
                if (!IndexExists())
                {
                    Console.WriteLine("[BloomSearchService] Index files not found");
                    return true;
                }

                // Check if database has been modified since last index using hash
                var currentDbHash = GetDatabaseHash();
                var lastIndexedDbHash = GetLastIndexedDbHash();

                if (currentDbHash != lastIndexedDbHash)
                {
                    Console.WriteLine($"[BloomSearchService] Database modified - Current hash: {currentDbHash}, Last indexed hash: {lastIndexedDbHash}");
                    return true;
                }

                Console.WriteLine("[BloomSearchService] Index is up to date");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BloomSearchService] Error checking reindex: {ex}");
                return true; // Rebuild on error
            }
        }

        /// <summary>
        /// Get index directory path
        /// </summary>
        private string GetIndexPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var indexPath = Path.Combine(baseDir, "BloomFilters");
            Directory.CreateDirectory(indexPath);
            return indexPath;
        }

        /// <summary>
        /// Check if index files exist
        /// </summary>
        private bool IndexExists()
        {
            var indexPath = GetIndexPath();
            var bloomFile = Path.Combine(indexPath, "lines.dat");
            return File.Exists(bloomFile);
        }

        /// <summary>
        /// Get database file hash (using file size + last write time as simple hash)
        /// </summary>
        private string GetDatabaseHash()
        {
            var dbPath = DbQueries.CurrentDbPath;
            if (File.Exists(dbPath))
            {
                var fileInfo = new FileInfo(dbPath);
                // Combine file size and last write time (rounded to seconds) for a simple but effective hash
                var lastWrite = fileInfo.LastWriteTimeUtc;
                var roundedTime = new DateTime(lastWrite.Year, lastWrite.Month, lastWrite.Day,
                                              lastWrite.Hour, lastWrite.Minute, lastWrite.Second, DateTimeKind.Utc);
                return $"{fileInfo.Length}_{roundedTime:yyyyMMddHHmmss}";
            }
            return string.Empty;
        }

        /// <summary>
        /// Get last indexed database hash from settings
        /// </summary>
        private string GetLastIndexedDbHash()
        {
            return Interaction.GetSetting(VB_APP, VB_SECTION, VB_KEY_DB_HASH, "");
        }

        /// <summary>
        /// Start indexing asynchronously
        /// </summary>
        private void StartIndexingAsync()
        {
            lock (_lock)
            {
                if (_isIndexing)
                {
                    Console.WriteLine("[BloomSearchService] Indexing already in progress");
                    return;
                }

                _isIndexing = true;
                _isReady = false;
                _progress.IsIndexing = true;
                _progress.IsReady = false;
                _indexingCts = new CancellationTokenSource();
            }

            // Create lock file immediately
            CreateLockFile();

            Task.Run(() => PerformIndexing(_indexingCts.Token));
        }

        /// <summary>
        /// Perform the actual indexing
        /// </summary>
        private void PerformIndexing(CancellationToken cancellationToken)
        {
            try
            {
                Console.WriteLine("[BloomSearchService] Starting indexing process");

                // Clean up old index files
                CleanupIndexFiles();

                // Create indexer
                _indexer = new BloomFilterIndexer("lines", 100, 0.01);

                // Subscribe to progress events
                _indexer.IndexProgressChanged += OnIndexingProgress;

                // Start indexing
                _indexer.CreateBloomFilters();

                if (!cancellationToken.IsCancellationRequested)
                {
                    // Save completion state with database hash
                    var dbHash = GetDatabaseHash();
                    Interaction.SaveSetting(VB_APP, VB_SECTION, VB_KEY_DB_HASH, dbHash);
                    Console.WriteLine($"[BloomSearchService] Saved database hash: {dbHash}");

                    // Delete lock file to indicate completion
                    DeleteLockFile();

                    lock (_lock)
                    {
                        _isReady = true;
                        _isIndexing = false;
                        _progress.IsReady = true;
                        _progress.IsIndexing = false;
                        _progress.Percentage = 100;
                    }

                    Console.WriteLine("[BloomSearchService] Indexing completed successfully");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BloomSearchService] Indexing error: {ex}");
                lock (_lock)
                {
                    _isIndexing = false;
                    _progress.IsIndexing = false;
                }
                // Leave lock file in place so we know to restart on next launch
            }
            finally
            {
                if (_indexer != null)
                {
                    _indexer.IndexProgressChanged -= OnIndexingProgress;
                }
            }
        }

        /// <summary>
        /// Handle indexing progress updates
        /// </summary>
        private void OnIndexingProgress(object sender, IndexProgressChangedEventArgs e)
        {
            lock (_lock)
            {
                _progress.ProcessedChunks = e.ProcessedChunks;
                _progress.TotalChunks = e.TotalChunks;
                _progress.Percentage = e.Percentage;
                _progress.Eta = FormatTimeSpan(e.Eta);
            }

            Console.WriteLine($"[BloomSearchService] Indexing progress: {e.Percentage:F1}% ({e.ProcessedChunks}/{e.TotalChunks})");
        }

        /// <summary>
        /// Format TimeSpan for display
        /// </summary>
        private string FormatTimeSpan(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            else if (ts.TotalMinutes >= 1)
                return $"{ts.Minutes}:{ts.Seconds:D2}";
            else
                return $"{ts.Seconds}s";
        }

        /// <summary>
        /// Clean up old index files
        /// </summary>
        private void CleanupIndexFiles()
        {
            try
            {
                var indexPath = GetIndexPath();
                var bloomFile = Path.Combine(indexPath, "lines.dat");

                if (File.Exists(bloomFile))
                    File.Delete(bloomFile);

                Console.WriteLine("[BloomSearchService] Old index files cleaned up");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BloomSearchService] Error cleaning up index files: {ex}");
            }
        }

        /// <summary>
        /// Check if search is ready
        /// </summary>
        public bool IsReady()
        {
            lock (_lock)
            {
                return _isReady;
            }
        }

        /// <summary>
        /// Get current indexing progress
        /// </summary>
        public IndexingProgress GetIndexingProgress()
        {
            lock (_lock)
            {
                var progress = new IndexingProgress
                {
                    IsReady = _progress.IsReady,
                    IsIndexing = _progress.IsIndexing,
                    ProcessedChunks = _progress.ProcessedChunks,
                    TotalChunks = _progress.TotalChunks,
                    Percentage = _progress.Percentage,
                    Eta = _progress.Eta
                };

                Console.WriteLine($"[BloomSearchService] GetIndexingProgress - IsReady: {progress.IsReady}, IsIndexing: {progress.IsIndexing}, Progress: {progress.Percentage:F1}%");
                return progress;
            }
        }

        /// <summary>
        /// Start a new search session and stream results as they come
        /// </summary>
        public string StartSearch(string query)
        {
            if (!IsReady())
            {
                Console.WriteLine("[BloomSearchService] Search called but index not ready");
                return null;
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return null;
            }

            // Cancel and dispose ALL existing searches before starting new one
            var existingSessions = _activeSessions.Keys.ToList();
            if (existingSessions.Count > 0)
            {
                Console.WriteLine($"[BloomSearchService] Cancelling {existingSessions.Count} existing search(es) before starting new search");
                foreach (var existingId in existingSessions)
                {
                    if (_activeSessions.TryRemove(existingId, out var existingSession))
                    {
                        existingSession.CancellationToken?.Cancel();
                        existingSession.Dispose();
                        Console.WriteLine($"[BloomSearchService] Cancelled and disposed search: {existingId}");
                    }
                }

                // Force GC after cancelling previous searches
                GC.Collect(1, GCCollectionMode.Optimized, false);
            }

            var searchId = Guid.NewGuid().ToString();
            Console.WriteLine($"[BloomSearchService] Starting search session {searchId}: {query}");

            // Create new search session
            var session = new SearchSession
            {
                SearchId = searchId,
                Query = query,
                CurrentIndex = 0,
                IsComplete = false,
                CancellationToken = new CancellationTokenSource()
            };

            _activeSessions[searchId] = session;

            // Execute search on background thread and stream results
            Task.Run(() => ExecuteSearchWithStreaming(session));

            return searchId;
        }

        /// <summary>
        /// Execute search and stream results in batches as they're verified
        /// </summary>
        private void ExecuteSearchWithStreaming(SearchSession session)
        {
            BloomFilterSearcher searcher = null;
            IEnumerable<SearchResultItem> results = null;

            try
            {
                searcher = new BloomFilterSearcher("lines");
                results = searcher.Search(session.Query);

                var batch = new List<SearchResultItem>();
                const int batchSize = 100;
                int totalSent = 0;

                foreach (var result in results)
                {
                    if (session.CancellationToken.IsCancellationRequested)
                    {
                        Console.WriteLine($"[BloomSearchService] Search {session.SearchId} cancelled during execution");
                        // Don't send cancellation message here - CancelSearch() already sent it
                        _activeSessions.TryRemove(session.SearchId, out _);
                        session.Dispose();

                        // Force GC on cancellation
                        batch?.Clear();
                        batch = null;
                        results = null;
                        searcher = null;
                        GC.Collect(2, GCCollectionMode.Forced, true, true);
                        GC.WaitForPendingFinalizers();
                        GC.Collect(2, GCCollectionMode.Forced, true, true);

                        return;
                    }

                    batch.Add(result);

                    // Send batch when it reaches batchSize
                    if (batch.Count >= batchSize)
                    {
                        SendSearchBatch(session.SearchId, batch);
                        totalSent += batch.Count;
                        Console.WriteLine($"[BloomSearchService] Sent batch {totalSent / batchSize}, total results sent: {totalSent}");

                        // Clear batch immediately after sending to allow GC
                        batch.Clear();
                        batch = null;

                        // Force GC every 3 batches (300 results)
                        if (totalSent % 300 == 0)
                        {
                            GC.Collect(1, GCCollectionMode.Optimized, false);
                        }

                        batch = new List<SearchResultItem>();
                    }
                }

                // Send remaining results
                if (batch != null && batch.Count > 0)
                {
                    SendSearchBatch(session.SearchId, batch);
                    totalSent += batch.Count;
                    Console.WriteLine($"[BloomSearchService] Sent final batch, total results: {totalSent}");
                    batch.Clear();
                    batch = null;
                }

                // Send completion message
                session.IsComplete = true;
                SendSearchComplete(session.SearchId);
                _activeSessions.TryRemove(session.SearchId, out _);
                session.Dispose();

                Console.WriteLine($"[BloomSearchService] Search {session.SearchId} completed with {totalSent} results");

                // Force aggressive GC after search completes
                results = null;
                searcher = null;
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                Console.WriteLine($"[BloomSearchService] Forced GC after search completion");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BloomSearchService] Search {session.SearchId} error: {ex}");
                SendSearchError(session.SearchId, ex.Message);
                _activeSessions.TryRemove(session.SearchId, out _);
                session.Dispose();

                // Force GC on error
                results = null;
                searcher = null;
                GC.Collect(2, GCCollectionMode.Forced, true, true);
            }
        }

        /// <summary>
        /// Send search batch to Vue frontend
        /// </summary>
        private void SendSearchBatch(string searchId, List<SearchResultItem> batch)
        {
            if (_onSearchBatch != null)
            {
                _onSearchBatch(searchId, batch);
            }
        }

        /// <summary>
        /// Send search complete message to Vue frontend
        /// </summary>
        private void SendSearchComplete(string searchId)
        {
            if (_onSearchComplete != null)
            {
                _onSearchComplete(searchId);
            }
        }

        /// <summary>
        /// Send search cancelled message to Vue frontend
        /// </summary>
        private void SendSearchCancelled(string searchId)
        {
            if (_onSearchCancelled != null)
            {
                _onSearchCancelled(searchId);
            }
        }

        /// <summary>
        /// Send search error message to Vue frontend
        /// </summary>
        private void SendSearchError(string searchId, string error)
        {
            if (_onSearchError != null)
            {
                _onSearchError(searchId, error);
            }
        }

        /// <summary>
        /// Cancel an active search session
        /// </summary>
        public void CancelSearch(string searchId)
        {
            if (_activeSessions.TryRemove(searchId, out var session))
            {
                session.CancellationToken?.Cancel();

                // Send cancellation message to Vue
                SendSearchCancelled(searchId);

                // Dispose session
                session.Dispose();

                Console.WriteLine($"[BloomSearchService] Search session {searchId} cancelled and disposed");
            }
            else
            {
                Console.WriteLine($"[BloomSearchService] Search session {searchId} not found (may have already completed)");
            }
        }

        /// <summary>
        /// Get line index from line ID
        /// </summary>
        public (int lineIndex, int bookId) GetLineIndexFromLineId(int lineId)
        {
            try
            {
                using (var db = new ZayitDbManager())
                {
                    return db.GetLineIndexFromLineId(lineId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BloomSearchService] Error getting line index: {ex}");
            }

            return (-1, -1);
        }
    }

    /// <summary>
    /// Indexing progress information
    /// </summary>
    public class IndexingProgress
    {
        public bool IsReady { get; set; }
        public bool IsIndexing { get; set; }
        public int ProcessedChunks { get; set; }
        public int TotalChunks { get; set; }
        public double Percentage { get; set; }
        public string Eta { get; set; }
    }

    /// <summary>
    /// Active search session
    /// </summary>
    internal class SearchSession : IDisposable
    {
        public string SearchId { get; set; }
        public string Query { get; set; }
        public int CurrentIndex { get; set; }
        public bool IsComplete { get; set; }
        public CancellationTokenSource CancellationToken { get; set; }

        public void Dispose()
        {
            CancellationToken?.Dispose();
        }
    }

    /// <summary>
    /// Search batch result
    /// </summary>
    public class SearchBatchResult
    {
        public List<SearchResultItem> Results { get; set; }
        public bool HasMore { get; set; }
        public bool IsComplete { get; set; }
    }
}
