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
        /// Validates the .dat file format. Returns null if valid, or a reason string if not.
        /// Checks: file exists, minimum header size (10 bytes = new format), header count
        /// is non-negative, and chunkSize is a sane value (> 0).
        /// An old 6-byte header (pre-lastLineId format) will fail the length check.
        /// </summary>
        private static string ValidateDatFile()
        {
            try
            {
                if (!File.Exists(BloomFilePath)) return "file missing";
                using (var fs = new FileStream(BloomFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var r = new BinaryReader(fs))
                {
                    if (fs.Length < 10) return "header too short (" + fs.Length + " bytes, expected ≥10) — old format";
                    int count = r.ReadInt32();       // offset 0
                    short chunkSize = r.ReadInt16(); // offset 4
                    r.ReadInt32();                   // offset 6: lastLineId
                    if (count < 0) return "negative chunk count (" + count + ")";
                    if (chunkSize <= 0) return "invalid chunkSize (" + chunkSize + ")";
                    return null; // valid
                }
            }
            catch (Exception ex) { return "read error: " + ex.Message; }
        }

        /// <summary>
        /// Reads count and lastLineId from the .dat file header in a single seek.
        /// The header is patched on every flush so both values are always authoritative.
        /// Returns (0, 0) if the file is missing or unreadable.
        /// </summary>
        private static (int count, int lastLineId) ReadDatHeader()
        {
            try
            {
                if (!File.Exists(BloomFilePath)) return (0, 0);
                using (var fs = new FileStream(BloomFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var r = new BinaryReader(fs))
                {
                    if (fs.Length < 10) return (0, 0);
                    int count = r.ReadInt32();      // offset 0
                    r.ReadInt16();                  // offset 4: chunkSize (skip)
                    int lastLineId = r.ReadInt32(); // offset 6
                    return (count, lastLineId);
                }
            }
            catch { return (0, 0); }
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

            // lines.ver present → indexing completed in a previous session.
            // Confirm the .dat is also present and valid before marking ready.
            string stampedVersion = ReadVersionStamp();
            if (stampedVersion != null)
            {
                string validationError = ValidateDatFile();
                if (validationError != null)
                {
                    Console.WriteLine("[SearchHandler] lines.ver present but .dat invalid (" + validationError + ") — deleting and rebuilding");
                    try { if (File.Exists(BloomFilePath)) File.Delete(BloomFilePath); } catch { }
                    try { File.Delete(BloomVersionStampPath); } catch { }
                    _bridge.PushEvent(new { @event = "bloomIndexInvalidated", reason = validationError });
                    StartIndexing(0, 0);
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
                        @event = "bloomIndexVersionMismatch",
                        oldVersion = stampedVersion,
                        newVersion = installedVersion
                    });
                    _isReady = true;
                    PushProgress(true, false, 100, 0, 0, "");
                    return;
                }

                Console.WriteLine("[SearchHandler] Bloom index complete and up-to-date, marking ready");
                _isReady = true;
                PushProgress(true, false, 100, 0, 0, "");
                return;
            }

            // lines.ver absent — check if a partial index exists to resume from.
            // Validate format before resuming; an old-format partial file must be discarded.
            if (File.Exists(BloomFilePath))
            {
                string validationError = ValidateDatFile();
                if (validationError != null)
                {
                    Console.WriteLine("[SearchHandler] Partial .dat invalid (" + validationError + ") — deleting and rebuilding");
                    try { File.Delete(BloomFilePath); } catch { }
                    _bridge.PushEvent(new { @event = "bloomIndexInvalidated", reason = validationError });
                    StartIndexing(0, 0);
                    return;
                }
            }

            // lines.ver absent — check if a partial index exists to resume from.
            var (headerCount, resumeLineId) = ReadDatHeader();
            if (headerCount > 0)
            {
                Console.WriteLine("[SearchHandler] Resuming — header=" + headerCount + " chunks, lastLineId=" + resumeLineId);
                StartIndexing(resumeLineId, headerCount);
                return;
            }

            Console.WriteLine("[SearchHandler] Starting fresh index build...");
            StartIndexing(0, 0);
        }

        public void ResetAndReindex(string newDbPath)
        {
            Console.WriteLine("[SearchHandler] ResetAndReindex called, newDbPath=" + newDbPath);
            StopIndexing();
            try { if (File.Exists(BloomFilePath)) { File.Delete(BloomFilePath); Console.WriteLine("[SearchHandler] Deleted bloom file"); } } catch (Exception ex) { Console.WriteLine("[SearchHandler] Delete bloom file failed: " + ex.Message); }
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

        private void StartIndexing(int resumeAfterLineId, int resumeChunkCount)
        {
            Console.WriteLine("[SearchHandler] StartIndexing called, _isIndexing=" + _isIndexing
                + " resumeAfterLineId=" + resumeAfterLineId + " resumeChunkCount=" + resumeChunkCount);
            if (_isIndexing) { Console.WriteLine("[SearchHandler] Already indexing, skipping"); return; }
            _isIndexing = true;
            _isReady = false;

            if (resumeChunkCount == 0)
            {
                // Fresh start — delete any stale bloom file and version stamp
                try { if (File.Exists(BloomFilePath)) { File.Delete(BloomFilePath); Console.WriteLine("[SearchHandler] Deleted stale bloom file"); } } catch (Exception ex) { Console.WriteLine("[SearchHandler] Failed to delete bloom file: " + ex.Message); }
                try { if (File.Exists(BloomVersionStampPath)) { File.Delete(BloomVersionStampPath); Console.WriteLine("[SearchHandler] Deleted version stamp"); } } catch (Exception ex) { Console.WriteLine("[SearchHandler] Failed to delete version stamp: " + ex.Message); }
            }

            Task.Run(() =>
            {
                Console.WriteLine("[SearchHandler] Task.Run started, resumeAfterLineId=" + resumeAfterLineId);
                try
                {
                    var indexer = new BloomFilterIndexer("lines", (short)100, 0.01, _dbPath);
                    indexer.IndexProgressChanged += (s, e) => PushIndexProgress(e);
                    indexer.CreateBloomFilters(resumeAfterLineId, resumeChunkCount);
                    Console.WriteLine("[SearchHandler] CreateBloomFilters completed");
                }
                catch (Exception ex) { Console.WriteLine("[SearchHandler] EXCEPTION: " + ex); }
                finally
                {
                    _isIndexing = false;
                    _isReady = File.Exists(BloomFilePath);
                    if (_isReady)
                    {
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
