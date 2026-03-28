using BloomSearchEngineLib;
using Kezayit.Bridge;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Kezayit.Search
{
    public class SearchHandler
    {
        private readonly WebBridge _bridge;
        private readonly WebView2 _webView;
        private volatile bool _isReady = false;
        private volatile bool _isIndexing = false;
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
            _webView = webView;
        }

        public void OnDbReady(string dbPath)
        {
            if (!File.Exists(dbPath)) return;

            if (File.Exists(BloomFilePath) &&
                File.GetLastWriteTimeUtc(BloomFilePath) >= File.GetLastWriteTimeUtc(dbPath))
            {
                _isReady = true;
                PushProgress(true, false, 100, 0, 0, "");
                return;
            }

            StartIndexing();
        }

        private void StartIndexing()
        {
            if (_isIndexing) return;
            _isIndexing = true;
            _isReady = false;

            Task.Run(() =>
            {
                try
                {
                    if (!BloomIndexingCoordinator.IsIndexing)
                    {
                        var indexer = new BloomFilterIndexer("lines", 100, 0.01);
                        indexer.IndexProgressChanged += (s, e) => PushIndexProgress(e);
                        indexer.CreateBloomFilters();
                    }
                    else
                    {
                        while (BloomIndexingCoordinator.IsIndexing)
                        {
                            var p = BloomIndexingCoordinator.LastProgress;
                            if (p != null) PushIndexProgress(p);
                            Thread.Sleep(1000);
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine("[SearchHandler] " + ex.Message); }
                finally
                {
                    _isIndexing = false;
                    _isReady = File.Exists(BloomFilePath);
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
            string json = JsonSerializer.Serialize(payload);
            if (_webView.InvokeRequired)
                _webView.Invoke(new Action(() => _webView.CoreWebView2.PostWebMessageAsString(json)));
            else
                _webView.CoreWebView2.PostWebMessageAsString(json);
        }
    }
}
