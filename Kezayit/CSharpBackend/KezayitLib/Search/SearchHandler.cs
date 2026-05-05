using FtsLib.Seforim;
using KezayitLib.Bridge;
using KezayitLib.Settings;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KezayitLib.Search
{
    public class SearchHandler
    {
        private readonly WebBridge _bridge;
        private volatile bool _isReady = false;
        private volatile bool _isIndexing = false;
        private string _dbPath;
        private SeforimIndex _index;
        private Task _indexingTask;
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _searches
            = new ConcurrentDictionary<string, CancellationTokenSource>();
        private int _nextSearchId = 1;

        // ── Paths ─────────────────────────────────────────────────────────────────

        private static string FtsIndexPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FtsIndex"); }
        }

        private static string FtsVersionStampPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FtsIndex", "fts.ver"); }
        }

        // Legacy Bloom index paths — detected on startup and deleted during migration.
        private static string BloomFilePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BloomFilters", "lines.dat"); }
        }

        private static string BloomVersionStampPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BloomFilters", "lines.ver"); }
        }

        private static string BloomFolderPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BloomFilters"); }
        }

        // ── Version stamp ─────────────────────────────────────────────────────────

        private static string GetInstalledAppVersion()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\KleiKodesh"))
                    return key?.GetValue("Version")?.ToString();
            }
            catch { return null; }
        }

        private static string ReadVersionStamp()
        {
            try { return File.Exists(FtsVersionStampPath) ? File.ReadAllText(FtsVersionStampPath).Trim() : null; }
            catch { return null; }
        }

        private static void WriteVersionStamp(string version)
        {
            try
            {
                Directory.CreateDirectory(FtsIndexPath);
                File.WriteAllText(FtsVersionStampPath, version ?? "");
            }
            catch { }
        }

        // ── FTS index validation ──────────────────────────────────────────────────

        /// <summary>
        /// Returns null if the FTS index directory looks valid (exists and contains segment files),
        /// or a reason string if it is missing or empty.
        /// </summary>
        private static string ValidateFtsIndex()
        {
            try
            {
                if (!Directory.Exists(FtsIndexPath)) return "index directory missing";
                // A valid FTS index contains at least one segment file.
                var files = Directory.GetFiles(FtsIndexPath, "*.seg");
                if (files.Length == 0) return "no segment files found";
                return null;
            }
            catch (Exception ex) { return "validation error: " + ex.Message; }
        }

        // ── Bloom migration ───────────────────────────────────────────────────────

        /// <summary>
        /// Deletes the legacy Bloom index folder if it exists.
        /// Called once on startup before the FTS index is checked.
        /// </summary>
        private static void DeleteBloomIndexIfPresent()
        {
            try
            {
                if (Directory.Exists(BloomFolderPath))
                {
                    Directory.Delete(BloomFolderPath, recursive: true);
                    Console.WriteLine("[SearchHandler] Deleted legacy Bloom index folder: " + BloomFolderPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SearchHandler] Failed to delete Bloom folder: " + ex.Message);
            }
        }

        // ── Constructor ───────────────────────────────────────────────────────────

        public SearchHandler(WebBridge bridge, WebView2 webView)
        {
            _bridge = bridge;
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        public void OnDbReady(string dbPath)
        {
            Console.WriteLine("[SearchHandler] OnDbReady called, dbPath=" + dbPath);
            if (!File.Exists(dbPath)) { Console.WriteLine("[SearchHandler] OnDbReady: file does not exist, aborting"); return; }
            _dbPath = dbPath;

            // Migration: delete legacy Bloom index before checking FTS state.
            DeleteBloomIndexIfPresent();

            _index?.Dispose();
            _index = new SeforimIndex(FtsIndexPath, dbPath);

            string stampedVersion = ReadVersionStamp();
            if (stampedVersion != null)
            {
                // Version stamp present — FTS index was completed in a previous session.
                // Validate that segment files are still present.
                string validationError = ValidateFtsIndex();
                if (validationError != null)
                {
                    Console.WriteLine("[SearchHandler] fts.ver present but index invalid (" + validationError + ") — deleting and rebuilding");
                    DeleteFtsIndex();
                    _bridge.PushEvent(new { @event = "ftsIndexInvalidated", reason = validationError });
                    StartIndexing();
                    return;
                }

                string installedVersion = GetInstalledAppVersion();
                Console.WriteLine("[SearchHandler] Version check — installed=" + installedVersion + " stamped=" + stampedVersion);

                if (!string.IsNullOrEmpty(installedVersion) &&
                    !string.Equals(installedVersion, stampedVersion, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("[SearchHandler] App version changed — asking user whether to rebuild index");
                    _bridge.PushEvent(new
                    {
                        @event = "ftsIndexVersionMismatch",
                        oldVersion = stampedVersion,
                        newVersion = installedVersion
                    });
                    _isReady = true;
                    PushProgress(true, false, 100, 0, 0, "");
                    return;
                }

                Console.WriteLine("[SearchHandler] FTS index complete and up-to-date, marking ready");
                _isReady = true;
                PushProgress(true, false, 100, 0, 0, "");
                return;
            }

            // No version stamp — check if a partial index exists (segment files without stamp).
            string partialError = ValidateFtsIndex();
            if (partialError == null)
            {
                // Partial index present — resume is not supported by FtsLib's BuildIndex
                // (it rebuilds from scratch). Delete and start fresh.
                Console.WriteLine("[SearchHandler] Partial FTS index found without version stamp — deleting and rebuilding");
                DeleteFtsIndex();
            }

            Console.WriteLine("[SearchHandler] Starting fresh FTS index build...");
            StartIndexing();
        }

        public void ResetAndReindex(string newDbPath)
        {
            Console.WriteLine("[SearchHandler] ResetAndReindex called, newDbPath=" + newDbPath);

            // Cancel any running indexer and wait for it to fully stop.
            var cts = _indexingCts;
            if (cts != null) cts.Cancel();
            var task = _indexingTask;
            if (task != null)
            {
                try { task.Wait(10000); }
                catch { }
                _indexingTask = null;
            }
            _isIndexing = false;
            _isReady = false;

            DeleteFtsIndex();

            if (!string.IsNullOrEmpty(newDbPath) && File.Exists(newDbPath))
                OnDbReady(newDbPath);
        }

        public void StopIndexing()
        {
            Console.WriteLine("[SearchHandler] StopIndexing called, _isIndexing=" + _isIndexing);
            var cts = _indexingCts;
            if (cts != null) cts.Cancel();
            var task = _indexingTask;
            if (task != null)
            {
                try { task.Wait(10000); }
                catch { }
                _indexingTask = null;
            }
            _isIndexing = false;
        }

        private void DeleteFtsIndex()
        {
            try
            {
                if (Directory.Exists(FtsIndexPath))
                {
                    Directory.Delete(FtsIndexPath, recursive: true);
                    Console.WriteLine("[SearchHandler] Deleted FTS index directory");
                }
            }
            catch (Exception ex) { Console.WriteLine("[SearchHandler] Failed to delete FTS index: " + ex.Message); }
        }

        // ── Indexing ──────────────────────────────────────────────────────────────

        private CancellationTokenSource _indexingCts;

        private void StartIndexing()
        {
            Console.WriteLine("[SearchHandler] StartIndexing called, _isIndexing=" + _isIndexing);
            if (_isIndexing) { Console.WriteLine("[SearchHandler] Already indexing, skipping"); return; }
            _isIndexing = true;
            _isReady = false;

            // Delete any stale index directory before a fresh build.
            DeleteFtsIndex();

            var cts = new CancellationTokenSource();
            _indexingCts = cts;

            long totalLines = 0;
            try { totalLines = _index.CountLines(); } catch { }

            _indexingTask = Task.Run(() =>
            {
                Console.WriteLine("[SearchHandler] FTS index build started");
                try
                {
                    long processed = 0;
                    _index.BuildIndex(limit: 0, onProgress: (count) =>
                    {
                        if (cts.IsCancellationRequested) return;
                        processed = count;
                        if (totalLines > 0 && count % 5000 == 0)
                        {
                            double pct = Math.Min(99.9, count * 100.0 / totalLines);
                            PushProgress(false, true, pct, (int)count, (int)totalLines, "");
                        }
                    });
                    Console.WriteLine("[SearchHandler] FTS index build completed");
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("[SearchHandler] FTS index build cancelled");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[SearchHandler] FTS index build EXCEPTION: " + ex);
                }
                finally
                {
                    _isIndexing = false;
                    _indexingCts = null;

                    string validationError = ValidateFtsIndex();
                    _isReady = validationError == null;

                    if (_isReady)
                    {
                        string ver = GetInstalledAppVersion();
                        if (!string.IsNullOrEmpty(ver)) WriteVersionStamp(ver);
                        Console.WriteLine("[SearchHandler] FTS index ready");
                    }
                    else
                    {
                        Console.WriteLine("[SearchHandler] FTS index build finished but index invalid: " + validationError);
                    }

                    PushProgress(_isReady, false, 100, 0, 0, "");
                }
            });
        }

        private void PushProgress(bool isReady, bool isIndexing, double pct, int processed, int total, string eta)
        {
            _bridge.PushEvent(new
            {
                @event = "ftsIndexProgress",
                isReady = isReady,
                isIndexing = isIndexing,
                percentage = Math.Round(pct, 1),
                processedChunks = processed,
                totalChunks = total,
                eta = eta
            });
        }

        private static string FormatEta(TimeSpan eta)
        {
            if (eta.TotalSeconds < 1) return "";
            if (eta.TotalMinutes < 1) return (int)eta.TotalSeconds + "s";
            return (int)eta.TotalMinutes + "m " + eta.Seconds + "s";
        }

        // ── Action handlers ───────────────────────────────────────────────────────

        public void HandleDeleteIndex(string id)
        {
            var cts = _indexingCts;
            if (cts != null) cts.Cancel();
            var task = _indexingTask;
            if (task != null)
            {
                try { task.Wait(10000); }
                catch { }
                _indexingTask = null;
            }
            _isIndexing = false;
            _isReady = false;
            DeleteFtsIndex();
            if (id != null) _bridge.Reply(id, new { });
        }

        public void HandleResetFtsIndex(string id)
        {
            _bridge.Reply(id, new { });
            if (!string.IsNullOrEmpty(_dbPath) && File.Exists(_dbPath))
                Task.Run(() => ResetAndReindex(_dbPath));
        }

        /// <summary>
        /// Called when the user responds to the version-mismatch prompt.
        /// confirm=true → delete the old index and rebuild; confirm=false → keep the existing index.
        /// </summary>
        public void HandleConfirmReindex(bool confirm, string id)
        {
            _bridge.Reply(id, new { });
            if (!confirm) return;
            Console.WriteLine("[SearchHandler] User confirmed reindex after app update");
            Task.Run(() => ResetAndReindex(_dbPath));
        }

        public void HandleGetProgress(string id)
        {
            _bridge.Reply(id, new
            {
                isReady = _isReady,
                isIndexing = _isIndexing,
                percentage = _isReady ? 100.0 : 0.0,
                processedChunks = 0,
                totalChunks = 0,
                eta = ""
            });
        }

        // ── Search ────────────────────────────────────────────────────────────────

        public void HandleSearchStart(JsonElement root, string id)
        {
            string query = root.TryGetProperty("0", out var q) ? q.GetString() : null;
            int skipCount = root.TryGetProperty("1", out var s) ? s.GetInt32() : 0;
            if (!_isReady || _index == null || string.IsNullOrWhiteSpace(query))
            {
                _bridge.Reply(id, new { searchId = (string)null });
                return;
            }

            string searchId = "s" + Interlocked.Increment(ref _nextSearchId);
            var cts = new CancellationTokenSource();
            _searches[searchId] = cts;
            _bridge.Reply(id, new { searchId = searchId });
            Task.Run(() => RunSearch(searchId, query, skipCount, cts.Token));
        }

        public void HandleSearchCancel(JsonElement root, string id)
        {
            string searchId = root.TryGetProperty("0", out var s) ? s.GetString() : null;
            if (searchId != null && _searches.TryRemove(searchId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
            _bridge.Reply(id, new { });
        }

        private void RunSearch(string searchId, string query, int skipCount, CancellationToken ct)
        {
            try
            {
                const int InitialBatchSize = 1;
                const int SwitchToTimerThreshold = 16;
                const int FlushTimeoutMs = 150;
                const int MemorySafetyCap = 200;

                var batch = new System.Collections.Generic.List<object>(50);
                int currentThreshold = InitialBatchSize;
                int skipped = 0;
                var batchTimer = new System.Diagnostics.Stopwatch();
                batchTimer.Start();
                bool useTimerOnly = false;

                foreach (var result in _index.Search(query, cap: 0, ct))
                {
                    if (ct.IsCancellationRequested)
                    {
                        PostSearch(new { type = "searchCancelled", searchId = searchId });
                        return;
                    }

                    // Generate snippet — filters out false positives (IsMatch == false)
                    var snippet = _index.GenerateSnippet(result);
                    if (!snippet.IsMatch) continue;

                    // Skip results the client already has from cache
                    if (skipped < skipCount) { skipped++; continue; }

                    // Flatten MatchedGroups into a single list of concrete terms for the frontend
                    var matchedTerms = new System.Collections.Generic.List<string>();
                    foreach (var group in result.MatchedGroups)
                        foreach (var term in group)
                            if (!matchedTerms.Contains(term))
                                matchedTerms.Add(term);

                    batch.Add(new
                    {
                        lineId = result.LineId,
                        bookId = 0,          // frontend fetches bookId via GET_LINE_INDEX_FROM_LINE_ID
                        bookTitle = result.BookTitle,
                        tocText = "",        // frontend enriches via GET_TOC_PATHS_FOR_LINES
                        score = snippet.Score,
                        snippet = snippet.Html,
                        matchedTerms = matchedTerms.ToArray()
                    });

                    bool shouldFlush = false;
                    if (useTimerOnly)
                    {
                        shouldFlush = batch.Count > 0 && (batchTimer.ElapsedMilliseconds >= FlushTimeoutMs || batch.Count >= MemorySafetyCap);
                    }
                    else
                    {
                        bool reachedThreshold = batch.Count >= currentThreshold;
                        bool timedOut = batch.Count > 0 && batchTimer.ElapsedMilliseconds >= FlushTimeoutMs;
                        bool memoryCapReached = batch.Count >= MemorySafetyCap;
                        shouldFlush = reachedThreshold || timedOut || memoryCapReached;

                        if (shouldFlush && reachedThreshold && currentThreshold >= SwitchToTimerThreshold)
                            useTimerOnly = true;
                    }

                    if (shouldFlush)
                    {
                        PostSearch(new { type = "searchBatch", searchId = searchId, results = batch.ToArray() });
                        batch.Clear();
                        batchTimer.Restart();

                        if (!useTimerOnly && currentThreshold < SwitchToTimerThreshold)
                            currentThreshold = Math.Min(currentThreshold * 2, SwitchToTimerThreshold);
                    }
                }

                if (batch.Count > 0)
                    PostSearch(new { type = "searchBatch", searchId = searchId, results = batch.ToArray() });
                PostSearch(new { type = "searchComplete", searchId = searchId });
            }
            catch (OperationCanceledException)
            {
                PostSearch(new { type = "searchCancelled", searchId = searchId });
            }
            catch (Exception ex)
            {
                PostSearch(new { type = "searchError", searchId = searchId, error = ex.Message });
            }
            finally
            {
                if (_searches.TryRemove(searchId, out var cts)) cts.Dispose();
            }
        }

        private void PostSearch(object payload)
        {
            _bridge.PushEvent(payload);
        }
    }
}
