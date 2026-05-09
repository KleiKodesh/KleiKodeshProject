using KitveiHakodeshLib.Bridge;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KitveiHakodeshLib.Pdf
{
    /// <summary>
    /// Handles local file picking, Word-to-PDF conversion, virtual host registration,
    /// and session restore for local PDF/Word tabs.
    /// </summary>
    public class PdfHandler
    {
        private static readonly string WordCacheDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KitveiHakodesh", "cache", "word");

        private readonly WebBridge _bridge;
        private readonly WebView2 _webView;
        private readonly Dictionary<string, FolderMapping> _hosts =
            new Dictionary<string, FolderMapping>(StringComparer.OrdinalIgnoreCase);
        private int _hostCounter = 0;

        private struct FolderMapping { public string HostName; public int RefCount; }

        public PdfHandler(WebBridge bridge, WebView2 webView)
        {
            _bridge = bridge;
            _webView = webView;
        }

        public void HandlePickFile(string id, Control owner)
        {
            owner.BeginInvoke(new Action(async () =>
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Title  = "פתח קובץ";
                    dlg.Filter = "קבצים נתמכים (*.pdf;*.doc;*.docx;*.rtf;*.txt;*.htm;*.html)|*.pdf;*.doc;*.docx;*.rtf;*.txt;*.htm;*.html|כל הקבצים (*.*)|*.*";
                    if (dlg.ShowDialog() != DialogResult.OK) { _bridge.Reply(id, new { cancelled = true }); return; }

                    string filePath = dlg.FileName;
                    string ext = Path.GetExtension(filePath).ToLowerInvariant();

                    if (ext == ".pdf")
                    {
                        string url = RegisterFolder(filePath);
                        _bridge.PushEvent(new { @event = "localPdfReady", url, fileName = Path.GetFileName(filePath), filePath });
                        _bridge.Reply(id, new { cancelled = false });
                    }
                    else
                    {
                        string displayName = Path.GetFileNameWithoutExtension(filePath) + ".pdf";
                        string destPath = GetCachePath(filePath);
                        string destFileName = Path.GetFileName(destPath);
                        _bridge.PushEvent(new { @event = "conversionStarted", fileName = displayName, filePath });

                        // Watch for the output PDF to appear — fires as soon as ExportAsFixedFormat
                        // writes the file, before Word has finished closing. This lets the tab
                        // update immediately without waiting for app.Quit() to return.
                        Directory.CreateDirectory(WordCacheDir);
                        FileSystemWatcher watcher = null;
                        if (!File.Exists(destPath))
                        {
                            watcher = new FileSystemWatcher(WordCacheDir, destFileName)
                            {
                                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                                EnableRaisingEvents = true,
                            };
                            FileSystemEventHandler onReady = null;
                            bool fired = false;
                            onReady = (s, e) =>
                            {
                                // Wait until the file is fully written and no longer locked
                                if (!IsFileReady(e.FullPath)) return;
                                if (fired) return;
                                fired = true;
                                watcher.EnableRaisingEvents = false;
                                watcher.Dispose();
                                string url2 = "http://KitveiHakodesh-vue-app/cache/word/" + destFileName;
                                _bridge.PushEvent(new { @event = "conversionReady", url = url2, fileName = displayName, filePath });
                            };
                            watcher.Created += onReady;
                            watcher.Changed += onReady;
                        }

                        string cached = await ConvertToPdfAsync(filePath);

                        if (watcher != null)
                        {
                            watcher.EnableRaisingEvents = false;
                            watcher.Dispose();
                        }

                        if (cached == null) { _bridge.Reply(id, new { error = "לא ניתן להמיר את הקובץ. ודא ש-Microsoft Word מותקן." }); return; }
                        string url = "http://KitveiHakodesh-vue-app/cache/word/" + Path.GetFileName(cached);
                        _bridge.Reply(id, new { cancelled = false, url, fileName = displayName, filePath });
                    }
                }
            }));
        }

        public async Task HandleRestoreLocalPdf(JsonElement root, string id)
        {
            string filePath = root.GetProperty("filePath").GetString();
            if (!File.Exists(filePath)) { _bridge.Reply(id, new { error = "הקובץ לא נמצא" }); return; }

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".pdf")
            {
                _bridge.Reply(id, new { url = RegisterFolder(filePath) });
                return;
            }

            string cached = GetCachePath(filePath);
            if (!File.Exists(cached)) cached = await ConvertToPdfAsync(filePath);
            if (cached == null) { _bridge.Reply(id, new { error = "לא ניתן להמיר את הקובץ" }); return; }
            _bridge.Reply(id, new { url = "http://KitveiHakodesh-vue-app/cache/word/" + Path.GetFileName(cached) });
        }

        public void HandleDisposePdfHost(JsonElement root, string id)
        {
            string filePath = root.GetProperty("filePath").GetString();
            string folder = File.Exists(filePath) ? Path.GetDirectoryName(filePath) : filePath;
            if (_hosts.TryGetValue(folder, out var m))
            {
                m.RefCount--;
                if (m.RefCount <= 0)
                {
                    _hosts.Remove(folder);
                    try { _webView.CoreWebView2.ClearVirtualHostNameToFolderMapping(m.HostName); } catch { }
                }
                else _hosts[folder] = m;
            }
            _bridge.Reply(id, new { ok = true });
        }

        private string RegisterFolder(string filePath)
        {
            string folder = Path.GetDirectoryName(filePath);
            if (!_hosts.TryGetValue(folder, out var m))
            {
                string host = "KitveiHakodesh-pdf-" + (++_hostCounter);
                _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    host, folder, CoreWebView2HostResourceAccessKind.Allow);
                m = new FolderMapping { HostName = host, RefCount = 0 };
                _hosts[folder] = m;
            }
            m.RefCount++;
            _hosts[folder] = m;
            return "http://" + m.HostName + "/" + Path.GetFileName(filePath);
        }

        private static async Task<string> ConvertToPdfAsync(string sourceFilePath)
        {
            Directory.CreateDirectory(WordCacheDir);
            string dest = GetCachePath(sourceFilePath);
            if (File.Exists(dest)) return dest;

            string ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
            bool isHtml = ext == ".htm" || ext == ".html"
                || (ext == ".txt" && WordToPdfConverter.TxtFileContainsHtml(sourceFilePath));

            string result = isHtml
                ? await WordToPdfConverter.ConvertHtmlToPdfAsync(sourceFilePath, dest)
                : await WordToPdfConverter.ConvertWordToPdfAsync(sourceFilePath, dest);

            if (result == sourceFilePath) return null;
            EvictCache(WordCacheDir, 10);
            return dest;
        }

        private static string GetCachePath(string sourceFilePath)
        {
            string key = Path.GetFileNameWithoutExtension(sourceFilePath)
                + "-" + File.GetLastWriteTimeUtc(sourceFilePath).Ticks;
            return Path.Combine(WordCacheDir, MakeSafeFileName(key) + ".pdf");
        }

        private static void EvictCache(string dir, int max)
        {
            if (!Directory.Exists(dir)) return;
            var files = new DirectoryInfo(dir).GetFiles("*.pdf");
            if (files.Length <= max) return;
            Array.Sort(files, (a, b) => a.LastAccessTimeUtc.CompareTo(b.LastAccessTimeUtc));
            for (int i = 0; i < files.Length - max; i++) try { files[i].Delete(); } catch { }
        }

        private static string MakeSafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Length > 80 ? name.Substring(0, 80) : name;
        }

        private static bool IsFileReady(string path)
        {
            try
            {
                using (var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
                    return fs.Length > 0;
            }
            catch { return false; }
        }
    }
}
