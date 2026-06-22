using KitveiHakodeshLib.Bridge;
using KitveiHakodeshLib.Pdf;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KitveiHakodeshLib.LocalFile
{
    /// <summary>
    /// Handles local file picking, Word-to-PDF conversion, virtual host registration,
    /// and session restore for local file tabs.
    /// </summary>
    public class LocalFileHandler
    {
        private static readonly string WordCacheDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KitveiHakodesh", "cache", "word");

        private readonly WebBridge _bridge;
        private readonly WebView2 _webView;
        private readonly Dictionary<string, FolderMapping> _hosts =
            new Dictionary<string, FolderMapping>(StringComparer.OrdinalIgnoreCase);
        private int _hostCounter = 0;

        private struct FolderMapping { public string HostName; public int RefCount; }

        public LocalFileHandler(WebBridge bridge, WebView2 webView)
        {
            _bridge = bridge;
            _webView = webView;
        }

        /// <summary>
        /// Opens a file directly by path — used when the app is launched via the Windows
        /// "Open With" context menu or via a command-line argument.
        ///
        /// Pushes exactly the same events as HandlePickFile so the Vue localFileStore
        /// handles this identically to a user-initiated file pick:
        ///
        ///   PDF / HTML / TXT  →  localFileReady { url, fileName, filePath }
        ///                         Vue: updateActiveTab with correct route + all persisted fields
        ///
        ///   Word / RTF        →  localFileConversionStarted { fileName, filePath }
        ///                         Vue: shows converting placeholder in /pdf-view
        ///                       localFileConversionReady { url, fileName, filePath }  (fast path via FileSystemWatcher)
        ///                         Vue: finishLocalFileConversion → sets localFilePath to original
        ///                              source path (not cache path) so session restore works
        ///
        /// No extra events are pushed — the final state is reached via the same event
        /// sequence that HandlePickFile produces.
        /// </summary>
        public async Task OpenFileFromPathAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                _bridge.PushEvent(new { @event = "localFileError", message = "הקובץ לא נמצא: " + filePath });
                return;
            }

            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            if (ext == ".pdf" || ext == ".htm" || ext == ".html" || ext == ".txt")
            {
                // Directly hostable — register the folder as a virtual host and fire
                // localFileReady. Vue routes to /pdf-view or /html-view based on extension,
                // and sets localFilePath for session restore.
                // openInNewTab: true tells the Vue store to open in a new tab rather than
                // replacing the current active tab (this path is only taken from OpenFileFromPathAsync,
                // i.e. "Open With" / command-line — not from the in-app file picker).
                string url = RegisterFolder(filePath);
                _bridge.PushEvent(new
                {
                    @event = "localFileReady",
                    url,
                    fileName = Path.GetFileName(filePath),
                    filePath,
                    openInNewTab = true,
                });
            }
            else
            {
                // Word / RTF — needs conversion.
                // Push localFileConversionStarted so Vue opens a new tab with the converting
                // placeholder (openInNewTab: true, same reason as above).
                string displayName = Path.GetFileNameWithoutExtension(filePath) + ".pdf";
                string destPath    = GetCachePath(filePath);
                string destFileName = Path.GetFileName(destPath);

                _bridge.PushEvent(new { @event = "localFileConversionStarted", fileName = displayName, filePath, openInNewTab = true });

                // Watch for the output PDF to appear — fires as soon as ExportAsFixedFormat
                // writes the file, before Word has finished closing. This lets the tab update
                // immediately without waiting for app.Quit() to return.
                // localFileConversionReady is the terminal event for Word files: Vue calls
                // finishLocalFileConversion which sets localFilePath to the *original* source
                // path so session restore can reconvert if the cache is evicted.
                Directory.CreateDirectory(WordCacheDir);
                FileSystemWatcher watcher = null;
                bool watcherFired = false;
                if (!File.Exists(destPath))
                {
                    watcher = new FileSystemWatcher(WordCacheDir, destFileName)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                        EnableRaisingEvents = true,
                    };
                    FileSystemEventHandler onReady = null;
                    onReady = (s, e) =>
                    {
                        if (!IsFileReady(e.FullPath)) return;
                        if (watcherFired) return;
                        watcherFired = true;
                        watcher.EnableRaisingEvents = false;
                        watcher.Dispose();
                        string url2 = "http://KitveiHakodesh-vue-app/cache/word/" + destFileName;
                        _bridge.PushEvent(new { @event = "localFileConversionReady", url = url2, fileName = displayName, filePath });
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

                if (cached == null)
                {
                    // Conversion failed — tab is still showing the converting placeholder.
                    // Push localFileError so Vue's localFileStore resets it to home.
                    _bridge.PushEvent(new { @event = "localFileError", message = "לא ניתן להמיר את הקובץ. ודא ש-Microsoft Word מותקן.", filePath });
                    return;
                }

                // Push localFileConversionReady if it hasn't been pushed yet:
                //   — watcher == null  → file was already cached; watcher was never set up
                //   — !watcherFired    → watcher was set up but the callback lost the race
                //                        with ConvertToPdfAsync returning (very rare)
                // HandlePickFile has the same fallback via _bridge.Reply on the RPC path;
                // the Vue hosted frontend ignores that reply but arrives at the same state
                // because finishLocalFileConversion is idempotent (bails if !localFileConverting).
                if (!watcherFired)
                {
                    string url = "http://KitveiHakodesh-vue-app/cache/word/" + Path.GetFileName(cached);
                    _bridge.PushEvent(new { @event = "localFileConversionReady", url, fileName = displayName, filePath });
                }
            }
        }

        /// <summary>
        /// Opens a native folder picker dialog and replies with the selected folder path.
        /// Replies { cancelled: true } if the user cancels.
        /// Must be called from within a BeginInvoke because it shows a dialog.
        /// </summary>
        public void HandlePickFolder(string id, Control owner)
        {
            owner.BeginInvoke(new Action(() =>
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "בחר תיקיית ספרים מקומית";
                    dlg.ShowNewFolderButton = false;
                    if (dlg.ShowDialog() != DialogResult.OK)
                    {
                        _bridge.Reply(id, new { cancelled = true });
                        return;
                    }
                    _bridge.Reply(id, new { folderPath = dlg.SelectedPath });
                }
            }));
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

                    // Handle already-webview-hostable files (PDF, HTML, and plain text) by
                    // registering the parent folder as a virtual host and returning the URL
                    // immediately. Plain text files are served as-is and rendered by the
                    // browser inside the html-view iframe — no conversion needed.
                    if (ext == ".pdf" || ext == ".htm" || ext == ".html" || ext == ".txt")
                    {
                        string url = RegisterFolder(filePath);
                        _bridge.PushEvent(new { @event = "localFileReady", url, fileName = Path.GetFileName(filePath), filePath });
                        _bridge.Reply(id, new { cancelled = false, url, fileName = Path.GetFileName(filePath), filePath });
                    }
                    else
                    {
                        string displayName = Path.GetFileNameWithoutExtension(filePath) + ".pdf";
                        string destPath = GetCachePath(filePath);
                        string destFileName = Path.GetFileName(destPath);
                        _bridge.PushEvent(new { @event = "localFileConversionStarted", fileName = displayName, filePath });

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
                                _bridge.PushEvent(new { @event = "localFileConversionReady", url = url2, fileName = displayName, filePath });
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

        public async Task HandleRestoreLocalFile(JsonElement root, string id)
        {
            string filePath = root.GetProperty("filePath").GetString();
            if (!File.Exists(filePath)) { _bridge.Reply(id, new { error = "הקובץ לא נמצא" }); return; }

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".pdf" || ext == ".htm" || ext == ".html" || ext == ".txt")
            {
                _bridge.Reply(id, new { url = RegisterFolder(filePath) });
                return;
            }

            string cached = GetCachePath(filePath);
            if (!File.Exists(cached)) cached = await ConvertToPdfAsync(filePath);
            if (cached == null) { _bridge.Reply(id, new { error = "לא ניתן להמיר את הקובץ" }); return; }
            _bridge.Reply(id, new { url = "http://KitveiHakodesh-vue-app/cache/word/" + Path.GetFileName(cached) });
        }

        public void HandleDisposeLocalFileHost(JsonElement root, string id)
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

        /// <summary>
        /// Releases all remaining virtual host mappings. Call on app shutdown so WebView2
        /// does not hold folder handles after the process exits.
        /// </summary>
        public void DisposeAllHosts()
        {
            foreach (var kvp in _hosts)
            {
                try { _webView.CoreWebView2?.ClearVirtualHostNameToFolderMapping(kvp.Value.HostName); } catch { }
            }
            _hosts.Clear();
        }

        private string RegisterFolder(string filePath)
        {
            string folder = Path.GetDirectoryName(filePath);
            if (!_hosts.TryGetValue(folder, out var m))
            {
                string host = "kitvei-localfile-" + (++_hostCounter);
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
            string result = await WordToPdfConverter.ConvertWordToPdfAsync(sourceFilePath, dest);
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
