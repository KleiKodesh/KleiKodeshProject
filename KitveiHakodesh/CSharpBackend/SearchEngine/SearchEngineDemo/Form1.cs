using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using SearchEngine.Indexing;
using SearchEngine.Search;
using SearchEngine.SeforimDb;

namespace SearchEngineDemo
{
    public partial class Form1 : Form
    {
        private WebView2 _webView;
        private LuceneSearcher _searcher;
        private ZayitDb _db;
        private readonly string _indexPath;
        private bool _isBuilding;
        private int  _slop         = int.MaxValue;
        private bool _inOrder      = false;
        private int  _fragmentSize = 2000;

        // Cancels the previous search when a new one starts.
        private CancellationTokenSource _searchCts;

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
            else if (msg.StartsWith("slop:"))
            {
                string val = msg.Substring(5);
                _slop = int.TryParse(val, out int s) && s >= 0 ? s : int.MaxValue;
            }
            else if (msg.StartsWith("order:"))
            {
                _inOrder = msg.Substring(6) == "1";
            }
            else if (msg.StartsWith("fragsize:"))
            {
                string val = msg.Substring(9);
                _fragmentSize = int.TryParse(val, out int fs) && fs >= 50 ? fs : 2000;
            }
            else if (msg == "build")
            {
                Task.Run(() => RunBuild());
            }
        }

        // ── Search ────────────────────────────────────────────────────────────

        private void RunSearch(string query)
        {
            // Cancel any previous search still streaming, then start a fresh token.
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

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
                    preTag:       "<mark>",
                    postTag:      "</mark>",
                    batchSize:    50,
                    slop:         _slop,
                    inOrder:      _inOrder,
                    fragmentSize: _fragmentSize,
                    ct:           ct))
                {
                    count++;
                    string html    = snippet.Html ?? "";
                    string escaped = EscapeJs(html);
                    Js($"addResult({rowId}, '{escaped}')");
                }

                if (!ct.IsCancellationRequested)
                    Js($"setStatus('{count:N0} results')");
            }
            catch (OperationCanceledException)
            {
                // Previous search was superseded — no status update needed.
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
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
  body { font-family: Segoe UI, sans-serif; background: #fafafa; padding: 12px; direction: rtl; }

  /* ── toolbar ── */
  #toolbar { display: flex; gap: 8px; align-items: center; margin-bottom: 8px; }
  #q { flex: 1; padding: 8px 10px; font-size: 15px; border: 1px solid #ccc; border-radius: 4px; direction: rtl; }
  button { padding: 8px 14px; font-size: 14px; border: none; border-radius: 4px;
           cursor: pointer; background: #0078d4; color: #fff; white-space: nowrap; }
  button:hover { background: #005fa3; }
  button.sec { background: #6c757d; }
  button.sec:hover { background: #545b62; }

  /* ── proximity bar ── */
  #prox { display: flex; gap: 20px; align-items: center; margin-bottom: 10px;
          padding: 8px 12px; background: #f0f4ff; border: 1px solid #d0d8f0;
          border-radius: 6px; font-size: 13px; color: #333; flex-wrap: wrap; }
  #prox label { display: flex; align-items: center; gap: 6px; cursor: pointer; }
  #slop { width: 60px; padding: 4px 6px; font-size: 13px; border: 1px solid #bbb;
          border-radius: 4px; text-align: center; }
  #slop:disabled { background: #eee; color: #aaa; }
  #prox .hint { font-size: 11px; color: #888; margin-right: auto; }

  /* ── display bar ── */
  #disp { display: flex; gap: 20px; align-items: center; margin-bottom: 10px;
          padding: 8px 12px; background: #f5f5f5; border: 1px solid #ddd;
          border-radius: 6px; font-size: 13px; color: #333; }
  #disp label { display: flex; align-items: center; gap: 6px; }
  #fragsize { width: 70px; padding: 4px 6px; font-size: 13px; border: 1px solid #bbb;
              border-radius: 4px; text-align: center; }

  /* ── status ── */
  #status { font-size: 13px; color: #555; margin-bottom: 8px; min-height: 18px; }

  /* ── results ── */
  .row { background: #fff; border: 1px solid #e0e0e0; border-radius: 4px;
         padding: 10px 14px; margin-bottom: 8px; line-height: 1.8; font-size: 14px; }
  .rid { font-size: 11px; color: #999; margin-bottom: 4px; }
  mark { background: #fff176; padding: 0 2px; border-radius: 2px; font-style: normal; }
</style>
</head>
<body>

<div id='toolbar'>
  <input id='q' type='text' placeholder='חיפוש...' value='כי ביצחק'>
  <button onclick='search()'>חיפוש</button>
  <button class='sec' onclick='build()'>בנה אינדקס</button>
</div>

<div id='prox'>
  <label title='סמן כדי להגביל את המרחק בין מילות החיפוש'>
    <input id='useProx' type='checkbox' onchange='onProxToggle()'>
    קרבה בין מילים
  </label>
  <label id='slopLabel' style='opacity:.4'>
    מרחק מקסימלי:
    <input id='slop' type='number' min='0' value='5' disabled
           title='מספר מילים מקסימלי בין כל זוג מילות חיפוש'>
  </label>
  <label id='orderLabel' style='opacity:.4'>
    <input id='ordered' type='checkbox' disabled>
    סדר מדויק
  </label>
  <span class='hint' id='proxHint'>ללא הגבלת מרחק</span>
</div>

<div id='disp'>
  <label>
    הקשר (תווים):
    <input id='fragsize' type='number' min='50' max='10000' value='2000'
           title='כמות התווים להציג סביב כל תוצאה'>
  </label>
</div>

<div id='status'></div>
<div id='results'></div>

<script>
  // ── proximity toggle ──────────────────────────────────────────────────────
  function onProxToggle() {
    const on = document.getElementById('useProx').checked;
    document.getElementById('slop').disabled    = !on;
    document.getElementById('ordered').disabled = !on;
    document.getElementById('slopLabel').style.opacity  = on ? '1' : '.4';
    document.getElementById('orderLabel').style.opacity = on ? '1' : '.4';
    updateHint();
  }

  function updateHint() {
    const on      = document.getElementById('useProx').checked;
    const slopVal = document.getElementById('slop').value.trim();
    const ordered = document.getElementById('ordered').checked;
    let hint = 'ללא הגבלת מרחק';
    if (on) {
      hint = 'מרחק ≤ ' + (slopVal === '' ? '∞' : slopVal) + ' מילים';
      if (ordered) hint += ' · סדר מדויק';
      else         hint += ' · כל סדר';
    }
    document.getElementById('proxHint').textContent = hint;
  }

  document.getElementById('slop').addEventListener('input', updateHint);
  document.getElementById('ordered').addEventListener('change', updateHint);

  // ── send settings before search ──────────────────────────────────────────
  function sendSettings() {
    const on = document.getElementById('useProx').checked;
    if (on) {
      const v = document.getElementById('slop').value.trim();
      window.chrome.webview.postMessage('slop:' + (v === '' ? '0' : v));
      window.chrome.webview.postMessage('order:' + (document.getElementById('ordered').checked ? '1' : '0'));
    } else {
      window.chrome.webview.postMessage('slop:');   // empty = no limit
      window.chrome.webview.postMessage('order:0');
    }
    const fs = document.getElementById('fragsize').value.trim();
    window.chrome.webview.postMessage('fragsize:' + (fs === '' ? '2000' : fs));
  }

  // ── search ────────────────────────────────────────────────────────────────
  document.getElementById('q').addEventListener('keydown', e => {
    if (e.key === 'Enter') search();
  });

  function search() {
    const q = document.getElementById('q').value.trim();
    if (!q) return;
    sendSettings();
    document.getElementById('results').innerHTML = '';
    document.getElementById('status').textContent = 'מחפש...';
    window.chrome.webview.postMessage('search:' + q);
  }

  // ── build ─────────────────────────────────────────────────────────────────
  function build() {
    if (!confirm('לבנות מחדש את האינדקס? זה ייקח כ-15 דקות.')) return;
    document.getElementById('results').innerHTML = '';
    window.chrome.webview.postMessage('build');
  }

  // ── called from C# ───────────────────────────────────────────────────────
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
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searcher?.Dispose();
            _db?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
