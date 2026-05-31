using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using LuceneLib.Indexing;
using LuceneLib.Search;
using LuceneLib.SeforimDb;

namespace LuceneDemo
{
    public partial class Form1 : Form
    {
        private WebView2 _webView;
        private LuceneSearcher _searcher;
        private ZayitDb _db;
        private readonly string _indexPath;
        private bool _isBuilding;

        public Form1()
        {
            InitializeComponent();
            _indexPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lucene_index");
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            try
            {
                await _webView.EnsureCoreWebView2Async(null);
                _webView.WebMessageReceived += OnWebMessage;
                LoadShell();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"WebView2 init failed: {ex.Message}\n\nInstall the WebView2 Runtime.");
                Close();
            }
        }

        // ── Message handling ──────────────────────────────────────────────────

        private void OnWebMessage(object sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            string msg = e.TryGetWebMessageAsString();

            if (msg.StartsWith("search:"))
            {
                string query = msg.Substring(7);
                if (!string.IsNullOrWhiteSpace(query))
                    Task.Run(() => RunSearch(query));
            }
            else if (msg == "build")
            {
                Task.Run(() => RunBuild());
            }
        }

        // ── Search ────────────────────────────────────────────────────────────

        private void RunSearch(string query)
        {
            try
            {
                if (_searcher == null) _searcher = new LuceneSearcher(_indexPath);
                if (_db == null)      _db = new ZayitDb(null);
                if (!_db.IsOpen) { Js("setStatus('Database not found.')"); return; }

                Js("clearResults()");
                Js($"setStatus('Searching...')");

                int count = 0;
                foreach (var (rowId, snippet) in _searcher.SearchWithSnippets(
                    query,
                    id => _db.GetLineById(id),
                    preTag: "<mark>",
                    postTag: "</mark>",
                    batchSize: 50))
                {
                    count++;
                    string html = snippet.Html ?? "";
                    
                    // Debug: log first few results to see what's actually in the snippet
                    if (count <= 3)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Result {count}] rowId={rowId}, snippet length={html.Length}");
                        System.Diagnostics.Debug.WriteLine($"  First 200 chars: {html.Substring(0, Math.Min(200, html.Length))}");
                        System.Diagnostics.Debug.WriteLine($"  Mark count: {System.Text.RegularExpressions.Regex.Matches(html, "<mark>").Count}");
                    }
                    
                    string escaped = EscapeJs(html);
                    Js($"addResult({rowId}, '{escaped}')");
                }

                Js($"setStatus('{count:N0} results')");
            }
            catch (Exception ex)
            {
                Js($"setStatus('Error: {EscapeJs(ex.ToString())}')");
            }
        }

        // ── Build ─────────────────────────────────────────────────────────────

        private void RunBuild()
        {
            if (_isBuilding) return;
            _isBuilding = true;

            // Dispose existing searcher so the index directory is not locked
            _searcher?.Dispose();
            _searcher = null;

            try
            {
                using (var db = new ZayitDb(null))
                {
                    if (!db.IsOpen) { Js("setStatus('Database not found.')"); return; }

                    long total = db.CountLines();
                    Js($"setStatus('Building index: 0 / {total:N0}')");

                    using (var writer = new LuceneIndexWriter(_indexPath, deleteExistingIndex: true))
                    {
                        writer.IndexAll(db, total, (current, tot) =>
                        {
                            double pct = tot > 0 ? 100.0 * current / tot : 0;
                            Js($"setStatus('Building: {current:N0} / {tot:N0} ({pct:F0}%)')");
                        });
                    }
                }

                Js("setStatus('Index built.')");
            }
            catch (Exception ex)
            {
                Js($"setStatus('Build error: {EscapeJs(ex.Message)}')");
            }
            finally
            {
                _isBuilding = false;
            }
        }

        // ── UI shell ──────────────────────────────────────────────────────────

        private void LoadShell()
        {
            _webView.NavigateToString(@"<!DOCTYPE html>
<html lang='he' dir='rtl'>
<head>
<meta charset='utf-8'>
<style>
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body { font-family: Segoe UI, sans-serif; background: #fafafa; padding: 12px; }
  #toolbar { display: flex; gap: 8px; align-items: center; margin-bottom: 10px; }
  #q { flex: 1; padding: 8px 10px; font-size: 15px; border: 1px solid #ccc; border-radius: 4px; }
  button { padding: 8px 14px; font-size: 14px; border: none; border-radius: 4px; cursor: pointer; background: #0078d4; color: #fff; white-space: nowrap; }
  button:hover { background: #005fa3; }
  button.sec { background: #6c757d; }
  button.sec:hover { background: #545b62; }
  #status { font-size: 13px; color: #555; margin-bottom: 8px; min-height: 18px; }
  .row { background: #fff; border: 1px solid #e0e0e0; border-radius: 4px; padding: 10px 14px; margin-bottom: 8px; line-height: 1.7; font-size: 14px; }
  .rid { font-size: 11px; color: #999; margin-bottom: 4px; }
  mark { background: #fff176; padding: 0 2px; border-radius: 2px; }
</style>
</head>
<body>
<div id='toolbar'>
  <input id='q' type='text' placeholder='חיפוש...' value='כי ביצחק'>
  <button onclick='search()'>חיפוש</button>
  <button class='sec' onclick='build()'>בנה אינדקס</button>
</div>
<div id='status'></div>
<div id='results'></div>
<script>
  document.getElementById('q').addEventListener('keydown', e => { if (e.key === 'Enter') search(); });

  function search() {
    const q = document.getElementById('q').value.trim();
    if (!q) return;
    document.getElementById('results').innerHTML = '';
    document.getElementById('status').textContent = 'מחפש...';
    window.chrome.webview.postMessage('search:' + q);
  }

  function build() {
    if (!confirm('לבנות מחדש את האינדקס? זה ייקח כ-15 דקות.')) return;
    document.getElementById('results').innerHTML = '';
    window.chrome.webview.postMessage('build');
  }

  function clearResults() {
    document.getElementById('results').innerHTML = '';
  }

  function setStatus(msg) {
    document.getElementById('status').textContent = msg;
  }

  function addResult(rowId, snippet) {
    const div = document.createElement('div');
    div.className = 'row';
    div.innerHTML = '<div class=""rid"">שורה ' + rowId + '</div>' + snippet;
    document.getElementById('results').appendChild(div);
  }
</script>
</body>
</html>");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void Js(string script)
        {
            if (IsDisposed) return;
            try { Invoke((Action)(() => _webView.ExecuteScriptAsync(script))); }
            catch { /* form closing */ }
        }

        private static string EscapeJs(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("'", "\\'")
                    .Replace("\"", "\\\"")
                    .Replace("\r", "\\r")
                    .Replace("\n", "\\n");
        }

        // ── Designer boilerplate ──────────────────────────────────────────────

        private void InitializeComponent()
        {
            _webView = new WebView2();
            SuspendLayout();
            _webView.Dock = DockStyle.Fill;
            Controls.Add(_webView);
            AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(900, 700);
            Text = "Lucene Demo";
            StartPosition = FormStartPosition.CenterScreen;
            Load += Form1_Load;
            ResumeLayout(false);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _searcher?.Dispose();
            _db?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
