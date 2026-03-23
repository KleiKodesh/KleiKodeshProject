using BloomSearchEngineLib;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Zayit.Services;

namespace Zayit.Viewer
{
    public class ZayitViewer : WebView2
    {
        private static CoreWebView2Environment _sharedEnv;
        private static readonly object _envLock = new object();

        private readonly string _htmlPath;
        private bool _coreInitialized;

        // Search state
        private readonly BloomSearchService _search = new BloomSearchService();

        private static readonly JsonSerializerOptions _jsonOpts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public ZayitViewer()
        {
            _htmlPath = ResolveHtmlPath();
            Dock = DockStyle.Fill;
            DefaultBackgroundColor = System.Drawing.Color.White;

            _search.SetSearchCallbacks(
                onBatch: (id, batch) => _ = SendAsync($"window.__searchBatch&&window.__searchBatch({Serialize(new {{ searchId = id, results = batch }})})"),
                onComplete: (id) => _ = SendAsync($"window.__searchComplete&&window.__searchComplete({Serialize(id)})"),
                onCancelled: (id) => _ = SendAsync($"window.__searchCancelled&&window.__searchCancelled({Serialize(id)})"),
                onError: (id, err) => _ = SendAsync($"window.__searchError&&window.__searchError({Serialize(new {{ searchId = id, error = err }})})"));

            CoreWebView2InitializationCompleted += OnCoreReady;
            _ = InitAsync();
        }

        public void SetPopOutToggleAction(Action action) { /* handled by host */ }

        // ── Initialization ────────────────────────────────────────────────────

        private async Task InitAsync()
        {
            try
            {
                if (_sharedEnv == null)
                {
                    var cachePath = Path.Combine(_htmlPath, "ZayitWebView2SharedCache");
                    var env = await CoreWebView2Environment.CreateAsync(null, cachePath, new CoreWebView2EnvironmentOptions());
                    lock (_envLock) { _sharedEnv = _sharedEnv ?? env; }
                }
                await EnsureCoreWebView2Async(_sharedEnv);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"WebView2 init failed:\n{ex.Message}", "WebView2 Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnCoreReady(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (_coreInitialized || !e.IsSuccess) return;
            _coreInitialized = true;

            var s = CoreWebView2.Settings;
            s.AreDevToolsEnabled = true;
            s.AreHostObjectsAllowed = true;
            s.IsWebMessageEnabled = true;
            s.IsStatusBarEnabled = false;
            s.IsSwipeNavigationEnabled = false;

            float dpi = DeviceDpi / 96f;
            ZoomFactor = 1.0 / dpi;

            CoreWebView2.SetVirtualHostNameToFolderMapping("zayitHost", _htmlPath, CoreWebView2HostResourceAccessKind.DenyCors);

            // Initialize PDF virtual host
            PdfService.Initialize(CoreWebView2, _htmlPath);

            // Initialize search
            _search.Initialize();

            WebMessageReceived += OnMessage;
            Source = new Uri("https://zayitHost/index.html");
        }

        // ── Message dispatch ──────────────────────────────────────────────────

        private void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.WebMessageAsJson)) return;
            _ = HandleMessageAsync(e.WebMessageAsJson);
        }

        private async Task HandleMessageAsync(string json)
        {
            string callbackId = null;
            string type = null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
                callbackId = root.TryGetProperty("callbackId", out var cb) ? cb.GetString() : null;
                var payload = root.TryGetProperty("payload", out var p) ? p : (JsonElement?)null;

                object result = await DispatchAsync(type, payload);

                if (callbackId != null)
                    await SendAsync($"window.__bridgeCallback&&window.__bridgeCallback({Serialize(callbackId)},{Serialize(result)})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ZayitViewer] Message error ({type}): {ex.Message}");
                if (callbackId != null)
                    await SendAsync($"window.__bridgeCallback&&window.__bridgeCallback({Serialize(callbackId)},{Serialize(new {{ error = ex.Message }})})");
            }
        }

        private async Task<object> DispatchAsync(string type, JsonElement? p)
        {
            switch (type)
            {
                // ── DB ────────────────────────────────────────────────────────
                case "query":
                    return DbService.Query(Str(p, "sql"), BuildParam(p));

                case "setDbPath":
                    DbService.DbPath = Str(p, "path");
                    _search.Initialize(); // re-index on DB change
                    return new { ok = true };

                // ── PDF / Documents ───────────────────────────────────────────
                case "openFile":
                    return await PdfService.OpenFileAsync(this);

                case "recreateUrl":
                    return new { url = PdfService.RecreateUrl(Str(p, "originalPath")) };

                case "cleanupTemp":
                    PdfService.CleanupTemp(Str(p, "fileName"));
                    return new { ok = true };

                // ── Hebrew Books ──────────────────────────────────────────────
                case "prepareHebrewBook":
                    return await HebrewBooksService.PrepareForViewingAsync(this, Str(p, "bookId"), Str(p, "title"));

                case "downloadHebrewBook":
                    return await HebrewBooksService.PrepareForDownloadAsync(this, Str(p, "bookId"), Str(p, "title"));

                case "checkHebrewBookCache":
                    return HebrewBooksService.CheckCache(Str(p, "bookId"), Str(p, "title"));

                // ── Search ────────────────────────────────────────────────────
                case "startSearch":
                    return new { searchId = _search.StartSearch(Str(p, "query")) };

                case "cancelSearch":
                    _search.CancelSearch(Str(p, "searchId"));
                    return new { ok = true };

                case "getSearchProgress":
                    return _search.GetIndexingProgress();

                default:
                    Console.WriteLine($"[ZayitViewer] Unknown message: {type}");
                    return null;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private async Task SendAsync(string script)
        {
            try { await ExecuteScriptAsync(script); }
            catch (Exception ex) { Console.WriteLine($"[ZayitViewer] SendAsync error: {ex.Message}"); }
        }

        private static string Serialize(object obj) => JsonSerializer.Serialize(obj);

        private static string Str(JsonElement? p, string key)
        {
            if (p == null) return null;
            return p.Value.TryGetProperty(key, out var el) && el.ValueKind != JsonValueKind.Null
                ? el.GetString() : null;
        }

        /// <summary>Builds a Dapper-compatible anonymous param object from payload keys (excluding "sql").</summary>
        private static object BuildParam(JsonElement? p)
        {
            if (p == null) return null;
            var dict = new Dictionary<string, object>();
            foreach (var prop in p.Value.EnumerateObject())
            {
                if (prop.Name == "sql") continue;
                dict[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? (object)l : prop.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => prop.Value.GetString()
                };
            }
            return dict.Count > 0 ? dict : null;
        }

        private static string ResolveHtmlPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var p1 = Path.Combine(baseDir, "zayit-vue-app");
            if (Directory.Exists(p1) && File.Exists(Path.Combine(p1, "index.html"))) return p1;

            var p2 = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "zayit-vue-app");
            if (Directory.Exists(p2) && File.Exists(Path.Combine(p2, "index.html"))) return p2;

            MessageBox.Show($"zayit-vue-app folder not found.\nLooked in:\n{p1}\n{p2}", "Zayit Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return p1;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) WebMessageReceived -= OnMessage;
            base.Dispose(disposing);
        }
    }
}
