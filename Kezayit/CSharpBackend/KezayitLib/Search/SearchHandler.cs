using BloomSearchEngineLib;
using KezayitLib.Bridge;
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
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _searches
            = new ConcurrentDictionary<string, CancellationTokenSource>();
        private int _nextSearchId = 1;

        private static string BloomFilePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BloomFilters", "lines.dat"); }
        }

        private static string IndexingSentinelPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BloomFilters", "indexing.lock"); }
        }

        private static string BloomVersionStampPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BloomFilters", "lines.ver"); }
        }

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
            try { return File.Exists(BloomVersionStampPath) ? File.ReadAllText(BloomVersionStampPath).Trim() : null; }
            catch { return null; }
        }

        private static void WriteVersionStamp(string version)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(BloomVersionStampPath));
                File.WriteAllText(BloomVersionStampPath, version ?? "");
            }
            catch { }
        }

        /// <summary>
        /// Reads the resume state from the sentinel file.
        /// Format: "lastCommittedLineId:chunkCount"
        /// Returns (0, 0) if the file is missing, empty, or unparseable (fresh start).
        /// </summary>
        private static (int lastLineId, int chunkCount) ReadSentinel()
        {
            try
            {
                if (!File.Exists(IndexingSentinelPath)) return (0, 0);
                string text = File.ReadAllText(IndexingSentinelPath).Trim();
                string[] parts = text.Split(':');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out int lineId) &&
                    int.TryParse(parts[1], out int count))
                    return (lineId, count);
            }
            catch { }
            return (0, 0);
        }

        /// <summary>
        /// Writes resume state to the sentinel after each successfully committed chunk.
        /// </summary>
        internal static void UpdateSentinel(int lastCommittedLineId, int chunkCount)
        {
            try { File.WriteAllText(IndexingSentinelPath, lastCommittedLineId + ":" + chunkCount); }
            catch (Exception ex) { Console.WriteLine("[SearchHandler] UpdateSentinel failed: " + ex.Message); }
        }

        public SearchHandler(WebBridge bridge, WebView2 webView)
        {
            _bridge = bridge;
        }

        public void OnDbReady(string dbPath)
        {
            Console.WriteLine("[SearchHandler] OnDbReady called, dbPath=" + dbPath);
            if (!File.Exists(dbPath)) { Console.WriteLine("[SearchHandler] OnDbReady: file does not exist, aborting"); return; }
            _dbPath = dbPath;

            // Read resume state from sentinel (if present)
            var (resumeLineId, resumeChunkCount) = ReadSentinel();
            bool hasResume = resumeChunkCount > 0;

            if (hasResume)
            {
                Console.WriteLine("[SearchHandler] Sentinel found — resuming from lineId=" + resumeLineId + " chunkCount=" + resumeChunkCount);
                // Bloom file must exist and be non-empty to resume; otherwise fall through to fresh start
                var bloomInfo = new FileInfo(BloomFilePath);
                if (!bloomInfo.Exists || bloomInfo.Length <= 64)
                {
                    Console.WriteLine("[SearchHandler] Bloom file missing or empty despite sentinel — forcing fresh start");
                    hasResume = false;
                    resumeLineId = 0;
                    resumeChunkCount = 0;
                    try { File.Delete(IndexingSentinelPath); } catch { }
                }
            }

            if (!hasResume)
            {
                var bloomInfo = new FileInfo(BloomFilePath);
                Console.WriteLine("[SearchHandler] BloomFilePath=" + BloomFilePath + " exists=" + bloomInfo.Exists + " size=" + (bloomInfo.Exists ? bloomInfo.Length : 0));
                if (bloomInfo.Exists &&
                    bloomInfo.Length > 64 &&
                    bloomInfo.LastWriteTimeUtc >= File.GetLastWriteTimeUtc(dbPath))
                {
                    string installedVersion = GetInstalledAppVersion();
                    string stampedVersion = ReadVersionStamp();
                    Console.WriteLine("[SearchHandler] Version check — installed=" + installedVersion + " stamped=" + stampedVersion);

                    if (!string.IsNullOrEmpty(installedVersion) &&
                        !string.IsNullOrEmpty(stampedVersion) &&
                        !string.Equals(installedVersion, stampedVersion, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("[SearchHandler] App version changed — asking user whether to rebuild index");
                        _bridge.PushEvent(new
                        {
                            @event = "bloomIndexVersionMismatch",
                            oldVersion = stampedVersion,
                            newVersion = installedVersion
                        });
                        _isReady = true;
                        PushProgress(true, false, 100, 0, 0, "");
                        return;
                    }

                    Console.WriteLine("[SearchHandler] Bloom file is up-to-date, marking ready");
                    _isReady = true;
                    PushProgress(true, false, 100, 0, 0, "");
                    return;
                }
            }

            Console.WriteLine("[SearchHandler] Starting indexing" + (hasResume ? " (resuming)" : "") + "...");
            StartIndexing(resumeLineId, resumeChunkCount);
        }

        public void ResetAndReindex(string newDbPath)
        {
            Console.WriteLine("[SearchHandler] ResetAndReindex called, newDbPath=" + newDbPath);
            StopIndexing();
            try { if (File.Exists(BloomFilePath)) { File.Delete(BloomFilePath); Console.WriteLine("[SearchHandler] Deleted bloom file"); } } catch (Exception ex) { Console.WriteLine("[SearchHandler] Delete bloom file failed: " + ex.Message); }
            try { if (File.Exists(IndexingSentinelPath)) File.Delete(IndexingSentinelPath); } catch { }
            try { if (File.Exists(BloomVersionStampPath)) File.Delete(BloomVersionStampPath); } catch { }
            _isReady = false;
            if (!string.IsNullOrEmpty(newDbPath) && File.Exists(newDbPath))
                OnDbReady(newDbPath);
        }

        public void StopIndexing()
        {
            Console.WriteLine("[SearchHandler] StopIndexing called, _isIndexing=" + _isIndexing);
            _isIndexing = false;
            BloomIndexingCoordinator.CancelIndexing();
        }

        private void StartIndexing(int resumeAfterLineId = 0, int resumeChunkCount = 0)
        {
            Console.WriteLine("[SearchHandler] StartIndexing called, _isIndexing=" + _isIndexing
                + " resumeAfterLineId=" + resumeAfterLineId + " resumeChunkCount=" + resumeChunkCount);
            if (_isIndexing) { Console.WriteLine("[SearchHandler] Already indexing, skipping"); return; }
            _isIndexing = true;
            _isReady = false;

            bool isFreshStart = resumeChunkCount == 0;
            if (isFreshStart)
            {
                // Remove any stale bloom file, version stamp, and write a blank sentinel so a kill
                // before the first chunk is committed still triggers a fresh start next launch
                try { if (File.Exists(BloomFilePath)) { File.Delete(BloomFilePath); Console.WriteLine("[SearchHandler] Deleted stale bloom file"); } } catch (Exception ex) { Console.WriteLine("[SearchHandler] Failed to delete bloom file: " + ex.Message); }
                try { if (File.Exists(BloomVersionStampPath)) { File.Delete(BloomVersionStampPath); Console.WriteLine("[SearchHandler] Deleted version stamp"); } } catch (Exception ex) { Console.WriteLine("[SearchHandler] Failed to delete version stamp: " + ex.Message); }
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(IndexingSentinelPath));
                    File.WriteAllText(IndexingSentinelPath, "0:0");
                    Console.WriteLine("[SearchHandler] Indexing sentinel initialised");
                }
                catch (Exception ex) { Console.WriteLine("[SearchHandler] Failed to write sentinel: " + ex.Message); }
            }

            Task.Run(() =>
            {
                Console.WriteLine("[SearchHandler] Task.Run started, resumeAfterLineId=" + resumeAfterLineId);
                try
                {
                    var indexer = new BloomFilterIndexer("lines", (short)100, 0.01, _dbPath);
                    indexer.IndexProgressChanged += (s, e) => PushIndexProgress(e);
                    indexer.OnChunkCommitted = (lastLineId, chunkCount) => UpdateSentinel(lastLineId, chunkCount);
                    indexer.CreateBloomFilters(resumeAfterLineId, resumeChunkCount);
                    Console.WriteLine("[SearchHandler] CreateBloomFilters completed");
                }
                catch (Exception ex) { Console.WriteLine("[SearchHandler] EXCEPTION: " + ex); }
                finally
                {
                    _isIndexing = false;
                    _isReady = File.Exists(BloomFilePath);
                    // Delete sentinel only on successful completion
                    if (_isReady)
                    {
                        try { File.Delete(IndexingSentinelPath); Console.WriteLine("[SearchHandler] Indexing sentinel deleted"); } catch { }
                        string ver = GetInstalledAppVersion();
                        if (!string.IsNullOrEmpty(ver)) WriteVersionStamp(ver);
                    }
                    Console.WriteLine("[SearchHandler] Indexing done, _isReady=" + _isReady + " bloom exists=" + File.Exists(BloomFilePath));
                    PushProgress(_isReady, false, 100, 0, 0, "");
                }
            });
        }

        private void PushIndexProgress(IndexProgressChangedEventArgs e)
        {
            bool done = e.TotalChunks > 0 && e.ProcessedChunks >= e.TotalChunks;
            PushProgress(done, !done, e.Percentage, e.ProcessedChunks, e.TotalChunks,
                done ? "" : FormatEta(e.Eta));
        }

        private void PushProgress(bool isReady, bool isIndexing, double pct, int processed, int total, string eta)
        {
            _bridge.PushEvent(new
            {
                @event = "bloomIndexProgress",
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
            try
            {
                if (File.Exists(BloomFilePath)) File.Delete(BloomFilePath);
                if (File.Exists(IndexingSentinelPath)) File.Delete(IndexingSentinelPath);
                if (File.Exists(BloomVersionStampPath)) File.Delete(BloomVersionStampPath);
                _isReady = false;
                _isIndexing = false;
            }
            catch (Exception ex) { Console.WriteLine("[SearchHandler] Delete index error: " + ex.Message); }
            _bridge.Reply(id, new { });
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
            ResetAndReindex(_dbPath);
        }

        public void HandleGetProgress(string id)
        {
            var p = BloomIndexingCoordinator.LastProgress;
            double pct = 0;
            int processed = 0, total = 0;
            string eta = "";
            if (p != null)
            {
                pct = Math.Round(p.Percentage, 1);
                processed = p.ProcessedChunks;
                total = p.TotalChunks;
                eta = FormatEta(p.Eta);
            }
            else if (_isReady)
            {
                pct = 100;
            }
            _bridge.Reply(id, new
            {
                isReady = _isReady,
                isIndexing = _isIndexing,
                percentage = pct,
                processedChunks = processed,
                totalChunks = total,
                eta = eta
            });
        }

        public void HandleSearchStart(JsonElement root, string id)
        {
            string query = root.TryGetProperty("0", out var q) ? q.GetString() : null;
            int skipCount = root.TryGetProperty("1", out var s) ? s.GetInt32() : 0;
            if (!_isReady || string.IsNullOrWhiteSpace(query))
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
                var batch = new System.Collections.Generic.List<object>(20);
                int skipped = 0;
                foreach (var item in new BloomFilterSearcher("lines").Search(query))
                {
                    if (ct.IsCancellationRequested)
                    {
                        PostSearch(new { type = "searchCancelled", searchId = searchId });
                        return;
                    }
                    // Skip results the client already has from cache
                    if (skipped < skipCount) { skipped++; continue; }
                    batch.Add(new
                    {
                        lineId = item.LineId,
                        bookId = item.BookId,
                        bookTitle = item.BookTitle,
                        tocText = item.TocText,
                        score = item.Score,
                        proximityScore = item.ProximityScore,
                        snippet = item.Snippet
                    });
                    if (batch.Count >= 20)
                    {
                        PostSearch(new { type = "searchBatch", searchId = searchId, results = batch.ToArray() });
                        batch.Clear();
                    }
                }
                if (batch.Count > 0)
                    PostSearch(new { type = "searchBatch", searchId = searchId, results = batch.ToArray() });
                PostSearch(new { type = "searchComplete", searchId = searchId });
            }
            catch (Exception ex) { PostSearch(new { type = "searchError", searchId = searchId, error = ex.Message }); }
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
