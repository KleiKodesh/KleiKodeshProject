using BloomSearchEngineLib;
using KezayitLib.Bridge;
using Microsoft.Web.WebView2.WinForms;
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

        public SearchHandler(WebBridge bridge, WebView2 webView)
        {
            _bridge = bridge;
        }

        public void OnDbReady(string dbPath)
        {
            Console.WriteLine("[SearchHandler] OnDbReady called, dbPath=" + dbPath);
            if (!File.Exists(dbPath)) { Console.WriteLine("[SearchHandler] OnDbReady: file does not exist, aborting"); return; }
            _dbPath = dbPath;

            var bloomInfo = new FileInfo(BloomFilePath);
            Console.WriteLine("[SearchHandler] BloomFilePath=" + BloomFilePath + " exists=" + bloomInfo.Exists + " size=" + (bloomInfo.Exists ? bloomInfo.Length : 0));
            if (bloomInfo.Exists &&
                bloomInfo.Length > 64 &&
                bloomInfo.LastWriteTimeUtc >= File.GetLastWriteTimeUtc(dbPath))
            {
                Console.WriteLine("[SearchHandler] Bloom file is up-to-date, marking ready");
                _isReady = true;
                PushProgress(true, false, 100, 0, 0, "");
                return;
            }

            Console.WriteLine("[SearchHandler] Starting indexing...");
            StartIndexing();
        }

        /// <summary>Cancel any running indexing, delete the bloom file, optionally restart with a new DB path.</summary>
        public void ResetAndReindex(string newDbPath)
        {
            Console.WriteLine("[SearchHandler] ResetAndReindex called, newDbPath=" + newDbPath);
            StopIndexing();
            try { if (File.Exists(BloomFilePath)) { File.Delete(BloomFilePath); Console.WriteLine("[SearchHandler] Deleted bloom file"); } } catch (Exception ex) { Console.WriteLine("[SearchHandler] Delete bloom file failed: " + ex.Message); }
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

        private void StartIndexing()
        {
            Console.WriteLine("[SearchHandler] StartIndexing called, _isIndexing=" + _isIndexing);
            if (_isIndexing) { Console.WriteLine("[SearchHandler] Already indexing, skipping"); return; }
            _isIndexing = true;
            _isReady = false;

            // Remove any stale/empty bloom file so the writer starts clean
            try { if (File.Exists(BloomFilePath)) { File.Delete(BloomFilePath); Console.WriteLine("[SearchHandler] Deleted stale bloom file"); } } catch (Exception ex) { Console.WriteLine("[SearchHandler] Failed to delete bloom file: " + ex.Message); }

            Task.Run(() =>
            {
                Console.WriteLine("[SearchHandler] Task.Run started, BloomIndexingCoordinator.IsIndexing=" + BloomIndexingCoordinator.IsIndexing);
                try
                {
                    Console.WriteLine("[SearchHandler] Creating BloomFilterIndexer with dbPath=" + _dbPath);
                    var indexer = new BloomFilterIndexer("lines", (short)100, 0.01, _dbPath);
                    indexer.IndexProgressChanged += (s, e) => PushIndexProgress(e);
                    indexer.CreateBloomFilters();
                    Console.WriteLine("[SearchHandler] CreateBloomFilters completed");
                }
                catch (Exception ex) { Console.WriteLine("[SearchHandler] EXCEPTION: " + ex); }
                finally
                {
                    _isIndexing = false;
                    _isReady = File.Exists(BloomFilePath);
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
                if (File.Exists(BloomFilePath))
                    File.Delete(BloomFilePath);
                _isReady = false;
                _isIndexing = false;
            }
            catch (Exception ex) { Console.WriteLine("[SearchHandler] Delete index error: " + ex.Message); }
            _bridge.Reply(id, new { });
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
            if (!_isReady || string.IsNullOrWhiteSpace(query))
            {
                _bridge.Reply(id, new { searchId = (string)null });
                return;
            }

            string searchId = "s" + Interlocked.Increment(ref _nextSearchId);
            var cts = new CancellationTokenSource();
            _searches[searchId] = cts;
            _bridge.Reply(id, new { searchId = searchId });
            Task.Run(() => RunSearch(searchId, query, cts.Token));
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

        private void RunSearch(string searchId, string query, CancellationToken ct)
        {
            try
            {
                var batch = new System.Collections.Generic.List<object>(20);
                foreach (var item in new BloomFilterSearcher("lines").Search(query))
                {
                    if (ct.IsCancellationRequested)
                    {
                        PostSearch(new { type = "searchCancelled", searchId = searchId });
                        return;
                    }
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
