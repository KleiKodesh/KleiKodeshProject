using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Zayit.Viewer;

namespace Zayit.Services
{
    /// <summary>
    /// Global PDF Service - Handles general PDF operations (file picker, virtual URLs, temp files).
    /// Non-PDF files are converted via a background task; the UI shows a progress dialog throughout.
    /// Converted PDFs are cached in AppDomain (LRU, max 10 entries) to avoid redundant conversions.
    /// </summary>
    public class PdfService
    {
        private readonly WebView2 _webView;
        private const string PDF_HOST = "zayitHost";

        // ── LRU Cache ────────────────────────────────────────────────────────────
        private const string CacheKey = "PdfService_ConversionCache";
        private const int CacheMax = 10;

        /// <summary>
        /// Per-AppDomain LRU cache: source file path → converted PDF path.
        /// Stored in AppDomain.CurrentDomain.GetData / SetData so it survives
        /// multiple PdfService instances within the same process.
        /// </summary>
        private static LruCache<string, string> ConversionCache
        {
            get
            {
                var cache = AppDomain.CurrentDomain.GetData(CacheKey) as LruCache<string, string>;
                if (cache == null)
                {
                    cache = new LruCache<string, string>(CacheMax);
                    AppDomain.CurrentDomain.SetData(CacheKey, cache);
                }
                return cache;
            }
        }

        public PdfService(WebView2 webView)
        {
            _webView = webView;
        }

        #region PDF Manager Initialization

        public bool InitializePdfManager()
        {
            try
            {
                if (_webView?.CoreWebView2 == null) return false;

                var htmlPath = GetHtmlPath();
                _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    PDF_HOST,
                    htmlPath,
                    Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow
                );

                Console.WriteLine($"[PdfService] Virtual host mapping set: {PDF_HOST} -> {htmlPath}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PdfService] Failed to initialize PDF manager: {ex}");
                return false;
            }
        }

        public bool CheckPdfManagerReady() => _webView?.CoreWebView2 != null;

        #endregion

        #region File Picker

        /// <summary>
        /// Opens a file picker for PDFs or Word/HTML documents.
        /// Non-PDF files are converted asynchronously on a background thread.
        /// A non-blocking progress dialog is shown during conversion.
        /// Results are cached (LRU-10) in the AppDomain to skip repeated conversions.
        /// </summary>
        public async Task<object> OpenPdfOrWordFilePickerAsync()
        {
            try
            {
                var filter =
                    "מסמכים (*.pdf;*.doc;*.docx;*.dot;*.dotx;*.docm;*.dotm;*.rtf;*.odt;*.txt;*.wps;*.xml;*.mht;*.mhtml;*.htm;*.html)" +
                    "|*.pdf;*.doc;*.docx;*.dot;*.dotx;*.docm;*.dotm;*.rtf;*.odt;*.txt;*.wps;*.xml;*.mht;*.mhtml;*.htm;*.html";

                var filePath = await WebViewDialogHelper.ShowOpenFileDialogAsync(
                    _webView, filter, "בחר קובץ PDF או Word");

                if (string.IsNullOrEmpty(filePath))
                    return EmptyResult();

                string finalPath = filePath;

                if (!filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    finalPath = await ConvertWithProgressAsync(filePath);

                    // Conversion failed or was cancelled
                    if (finalPath == null)
                        return EmptyResult();
                }

                var virtualUrl = CreateVirtualUrl(finalPath);
                return new
                {
                    fileName = Path.GetFileName(finalPath),
                    dataUrl = virtualUrl,
                    originalPath = finalPath
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PdfService] Error in file picker: {ex}");
                return EmptyResult();
            }
        }

        /// <summary>
        /// Returns a cached PDF path if available; otherwise converts the file on a
        /// background thread while showing a WinForms progress dialog with a marquee bar.
        /// </summary>
        private async Task<string> ConvertWithProgressAsync(string filePath)
        {
            var cache = ConversionCache;

            // ── Cache hit ────────────────────────────────────────────────────────
            if (cache.TryGet(filePath, out var cachedPdf) && File.Exists(cachedPdf))
            {
                Console.WriteLine($"[PdfService] Cache hit for: {filePath}");
                return cachedPdf;
            }

            // ── Show progress dialog ─────────────────────────────────────────────
            var progressForm = BuildProgressForm(Path.GetFileName(filePath));
            progressForm.Show(_webView.FindForm());

            string pdfPath = null;
            Exception conversionError = null;

            bool isHtmlTxt = filePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                             && WordToPdfConverter.TxtFileContainsHtml(filePath);

            try
            {
                pdfPath = isHtmlTxt
                    ? await WordToPdfConverter.ConvertHtmlToPdfAsync(filePath,
                          Path.ChangeExtension(filePath, ".pdf"))
                    : await WordToPdfConverter.ConvertWordToPdfAsync(filePath,
                          Path.ChangeExtension(filePath, ".pdf"));
            }
            catch (Exception ex)
            {
                conversionError = ex;
            }
            finally
            {
                progressForm.Close();
                progressForm.Dispose();
            }

            if (conversionError != null)
            {
                Console.WriteLine($"[PdfService] Conversion failed: {conversionError}");
                MessageBox.Show(
                    $"המרת הקובץ נכשלה:\n{conversionError.Message}",
                    "שגיאה בהמרה",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1,
                    MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
                return null;
            }

            if (pdfPath != null)
            {
                // ── Cache the result ─────────────────────────────────────────────
                cache.Put(filePath, pdfPath);
                Console.WriteLine($"[PdfService] Cached conversion: {filePath} -> {pdfPath}");
            }

            return pdfPath;
        }

        /// <summary>Builds a borderless RTL progress dialog with a marquee bar.</summary>
        private static Form BuildProgressForm(string fileName)
        {
            var form = new Form
            {
                Text = "ממיר קובץ…",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                Size = new System.Drawing.Size(360, 120),
                MinimizeBox = false,
                MaximizeBox = false,
                ControlBox = false,
                RightToLeft = RightToLeft.Yes,
                RightToLeftLayout = true,
                TopMost = true
            };

            var label = new Label
            {
                Text = $"ממיר את הקובץ \"{fileName}\" ל-PDF…",
                Dock = DockStyle.Top,
                Height = 36,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                Padding = new Padding(8, 8, 8, 0)
            };

            var bar = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                Dock = DockStyle.Bottom,
                Height = 20,
                MarqueeAnimationSpeed = 30
            };

            form.Controls.Add(label);
            form.Controls.Add(bar);
            return form;
        }

        private static object EmptyResult() =>
            new { fileName = (string)null, dataUrl = (string)null, originalPath = (string)null };

        #endregion

        #region Virtual URL / Temp Files

        public string CreateVirtualUrl(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return null;

                var htmlPath = GetHtmlPath();
                var tempDir = Path.Combine(htmlPath, "temp");
                Directory.CreateDirectory(tempDir);

                var uniqueName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(filePath);
                var tempPath = Path.Combine(tempDir, uniqueName);

                File.Copy(filePath, tempPath, true);

                var virtualUrl = $"https://{PDF_HOST}/temp/{uniqueName}";
                Console.WriteLine($"[PdfService] Created virtual URL: {filePath} -> {virtualUrl}");
                return virtualUrl;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PdfService] Error creating virtual URL: {ex}");
                return null;
            }
        }

        public string RecreateVirtualUrlFromPath(string originalPath)
        {
            try
            {
                if (!File.Exists(originalPath))
                {
                    Console.WriteLine($"[PdfService] Original file not found: {originalPath}");
                    return null;
                }
                return CreateVirtualUrl(originalPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PdfService] Error recreating virtual URL: {ex}");
                return null;
            }
        }

        public void CleanupTempFile(string fileName)
        {
            try
            {
                var tempPath = Path.Combine(GetHtmlPath(), "temp", fileName);
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                    Console.WriteLine($"[PdfService] Cleaned up temp file: {fileName}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PdfService] Error cleaning up temp file: {ex}");
            }
        }

        public object GetTempFileStats()
        {
            try
            {
                var tempDir = Path.Combine(GetHtmlPath(), "temp");
                if (!Directory.Exists(tempDir))
                    return new { fileCount = 0, totalSize = 0 };

                var files = Directory.GetFiles(tempDir);
                long totalSize = 0;
                foreach (var file in files)
                    totalSize += new FileInfo(file).Length;

                return new { fileCount = files.Length, totalSize };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PdfService] Error getting temp file stats: {ex}");
                return new { fileCount = 0, totalSize = 0 };
            }
        }

        public void ClearTempFiles()
        {
            try
            {
                var tempDir = Path.Combine(GetHtmlPath(), "temp");
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                    Directory.CreateDirectory(tempDir);
                    Console.WriteLine("[PdfService] All temp files cleared");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PdfService] Error clearing temp files: {ex}");
            }
        }

        private string GetHtmlPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var standardPath = Path.Combine(baseDir, "zayit-vue-app");
            if (Directory.Exists(standardPath))
                return Path.GetFullPath(standardPath);

            var assemblyPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            var clickOncePath = Path.Combine(assemblyPath, "zayit-vue-app");
            if (Directory.Exists(clickOncePath))
                return Path.GetFullPath(clickOncePath);

            Console.WriteLine($"[PdfService] WARNING: zayit-vue-app folder not found! Tried:");
            Console.WriteLine($"  - {standardPath}");
            Console.WriteLine($"  - {clickOncePath}");
            return Path.GetFullPath(standardPath);
        }

        #endregion
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Simple LRU Cache (not thread-safe — all calls come from the UI thread)
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Least-Recently-Used cache with a fixed capacity.
    /// When full, the least recently accessed entry is evicted.
    /// </summary>
    internal sealed class LruCache<TKey, TValue>
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map;
        private readonly LinkedList<(TKey Key, TValue Value)> _order;

        public LruCache(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
            _map = new Dictionary<TKey, LinkedListNode<(TKey, TValue)>>(capacity);
            _order = new LinkedList<(TKey, TValue)>();
        }

        /// <summary>Returns true and sets <paramref name="value"/> if the key exists; promotes it to MRU.</summary>
        public bool TryGet(TKey key, out TValue value)
        {
            if (_map.TryGetValue(key, out var node))
            {
                // Move to front (most recently used)
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
            value = default;
            return false;
        }

        /// <summary>Inserts or updates a key/value pair; evicts LRU entry when at capacity.</summary>
        public void Put(TKey key, TValue value)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                _map.Remove(key);
            }
            else if (_map.Count >= _capacity)
            {
                // Evict least recently used (tail)
                var lru = _order.Last;
                _order.RemoveLast();
                _map.Remove(lru.Value.Key);
            }

            var node = _order.AddFirst((key, value));
            _map[key] = node;
        }

        public int Count => _map.Count;
    }
}