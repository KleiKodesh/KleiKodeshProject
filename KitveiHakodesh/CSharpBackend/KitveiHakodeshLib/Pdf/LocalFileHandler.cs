using KitveiHakodesh.Core.Common;
using KitveiHakodesh.Core.LocalFiles;
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
    /// The WebView2 host's side of local files: the native pickers, Word-to-PDF conversion,
    /// virtual host registration, and session restore for local file tabs.
    ///
    /// The RULES are Core's, shared with the service as linked source (see the csproj): which
    /// types open (LocalFileOpenRules), text decoding (TextEncodingDetector), served content
    /// types (LocalFileContentTypes), the folder picker (NativeFolderPicker). They
    /// run HERE, in the host's process, because the production service is LocalSystem and
    /// refuses desktop-only ops. The WebView2 virtual-host mapping is this host's alone.
    /// </summary>
    public class LocalFileHandler
    {
        private static readonly string WordCacheDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KitveiHakodesh", "word-cache");

        private const string WordCacheUrl = "http://kitveihakodesh-app/word-cache/";

        private readonly WebBridge _bridge;
        private readonly WebView2 _webView;
        private readonly Dictionary<string, FolderMapping> _hosts =
            new Dictionary<string, FolderMapping>(StringComparer.OrdinalIgnoreCase);
        // HTML folders are served by us rather than by SetVirtualHostNameToFolderMapping -
        // see RegisterServedFolder. Separate from _hosts so the same folder can hold both a
        // natively mapped host (opened .pdf) and a served host (opened .html).
        private readonly Dictionary<string, FolderMapping> _servedHosts =
            new Dictionary<string, FolderMapping>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _servedFolderByHost =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private bool _servedHandlerInstalled;
        private int _hostCounter = 0;

        private struct FolderMapping { public string HostName; public int RefCount; }

        public LocalFileHandler(WebBridge bridge, WebView2 webView)
        {
            _bridge = bridge;
            _webView = webView;
        }

        /// <summary>
        /// Opens a file directly by path - the Windows "Open With" context menu or a command-line
        /// argument. Same events as a user pick (see <see cref="OpenAsync"/>), in a new tab.
        /// </summary>
        public Task OpenFileFromPathAsync(string filePath) => OpenAsync(filePath, openInNewTab: true, replyId: null);

        /// <summary>
        /// Opens the native folder picker and replies with the selected folder path, or
        /// { cancelled: true }. Call on the UI thread (it reads the owner's handle).
        /// </summary>
        public void HandlePickFolder(string id, Control owner)
        {
            IntPtr ownerHandle = owner.Handle;
            _ = PickFolderAsync(id, ownerHandle, owner);
        }

        // Core's picker runs on its own STA thread and, in VSTO (no WinForms sync context), the
        // await can resume off the UI thread - every reply hops back via owner.BeginInvoke.
        private async Task PickFolderAsync(string id, IntPtr ownerHandle, Control owner)
        {
            object reply;
            try
            {
                string folder = await NativeFolderPicker.PickAsync("בחר תיקיית ספרים מקומית", ownerHandle);
                reply = folder == null ? (object)new { cancelled = true } : new { folderPath = folder };
            }
            catch (Exception ex) { reply = new { error = ex.Message }; }
            owner.BeginInvoke(new Action(() => _bridge.Reply(id, reply)));
        }

        /// <summary>Show the open-file dialog. <paramref name="root"/> may carry an optional
        /// <c>initialDir</c> (the home page's frequent-folder tiles send one). The dialog and
        /// the open that follows run on the UI thread (BeginInvoke): the open touches CoreWebView2.</summary>
        public void HandlePickFile(JsonElement root, string id, Control owner)
        {
            // Read out of `root` HERE, before any await: the JsonDocument it belongs to is
            // disposed by OnMessageReceivedAsync's `using` as soon as this returns. Touching it
            // later throws ObjectDisposedException and the picker silently never opens.
            string initialDir =
                root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("initialDir", out var initialDirProp)
                && initialDirProp.ValueKind == JsonValueKind.String
                    ? initialDirProp.GetString()
                    : null;
            // Lib's own WinForms dialog, not Core's NativeFilePicker: that one runs on its own STA
            // thread, and in VSTO (no WinForms sync context) the await resumed off the UI thread,
            // so RegisterFolder threw "CoreWebView2 can only be accessed from the UI thread".
            owner.BeginInvoke(new Action(async () =>
            {
                string filePath;
                try
                {
                    using (var dlg = new OpenFileDialog())
                    {
                        dlg.Title  = "פתח קובץ";
                        dlg.Filter = "מסמכים (*.pdf;*.doc;*.docx;*.docm;*.dot;*.dotx;*.dotm;*.htm;*.html;*.odt;*.rtf;*.txt)|*.pdf;*.doc;*.docx;*.docm;*.dot;*.dotx;*.dotm;*.htm;*.html;*.odt;*.rtf;*.txt|כל הקבצים (*.*)|*.*";
                        // A folder that has since been deleted must not sink the pick: the dialog
                        // silently falls back to its default when InitialDirectory does not exist.
                        if (!string.IsNullOrEmpty(initialDir)) dlg.InitialDirectory = initialDir;
                        if (dlg.ShowDialog(owner) != DialogResult.OK) { _bridge.Reply(id, new { cancelled = true }); return; }
                        filePath = dlg.FileName;
                    }
                }
                catch (Exception ex) { _bridge.Reply(id, new { error = ex.Message }); return; }

                // async void: an escaped exception would crash the host, so catch it here.
                try { await OpenAsync(filePath, openInNewTab: false, replyId: id); }
                catch (Exception ex) { _bridge.Reply(id, new { error = ex.Message }); }
            }));
        }

        /// <summary>
        /// The one open path behind a pick (<paramref name="replyId"/> set) and Open-With
        /// (<paramref name="replyId"/> null). Pushes the events the Vue localFileStore handles:
        ///
        ///   TXT          ->  localFileTxtReady { textContent, fileName, filePath }
        ///   PDF / HTML   ->  localFileReady { url, fileName, filePath, isOtzariaAddin }
        ///   Word family  ->  localFileConversionStarted { fileName, filePath }, then
        ///                    localFileConversionReady { url, fileName, filePath } (fast path:
        ///                    a FileSystemWatcher on the cache file, before Word has quit)
        ///
        /// A pick also gets a reply (with the url for PDF/HTML/Word). A failure is the reply's
        /// error for a pick, and a localFileError event for Open-With.
        /// </summary>
        private async Task OpenAsync(string filePath, bool openInNewTab, string replyId)
        {
            void Fail(string message)
            {
                if (replyId != null) _bridge.Reply(replyId, new { error = message });
                else _bridge.PushEvent(new { @event = "localFileError", message, filePath });
            }

            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    Fail("הקובץ לא נמצא: " + filePath);
                    return;
                }

                if (!LocalFileOpenRules.IsSupported(filePath, out bool needsConvert))
                {
                    Fail("סוג קובץ זה אינו נתמך: " + Path.GetExtension(filePath).ToLowerInvariant());
                    return;
                }

                WindowsRecentDocuments.Add(filePath);

                string fileName = Path.GetFileName(filePath);

                if (LocalFileOpenRules.IsText(filePath))
                {
                    string content = TextEncodingDetector.ReadAllText(filePath);
                    _bridge.PushEvent(new { @event = "localFileTxtReady", textContent = content, fileName, filePath, openInNewTab });
                    if (replyId != null) _bridge.Reply(replyId, new { cancelled = false });
                    return;
                }

                if (!needsConvert)
                {
                    string url = RegisterFolder(filePath);
                    bool isOtzariaAddin = LocalFileOpenRules.IsOtzariaAddinFolder(LocalFileOpenRules.AssetFolderOf(filePath));
                    _bridge.PushEvent(new { @event = "localFileReady", url, fileName, filePath, openInNewTab, isOtzariaAddin });
                    if (replyId != null) _bridge.Reply(replyId, new { cancelled = false, url, fileName, filePath });
                    return;
                }

                string displayName = LocalFileOpenRules.ServedFileName(filePath, rendered: true, renderedAsHtml: false);
                _bridge.PushEvent(new { @event = "localFileConversionStarted", fileName = displayName, filePath, openInNewTab });

                bool readyPushed = false;
                string readyUrl = WordCacheUrl + Path.GetFileName(GetCachePath(filePath));
                string cached;
                using (WatchForCachedPdf(filePath, () =>
                {
                    readyPushed = true;
                    _bridge.PushEvent(new { @event = "localFileConversionReady", url = readyUrl, fileName = displayName, filePath });
                }))
                {
                    cached = await ConvertToPdfAsync(filePath);
                }

                if (cached == null) { Fail("לא ניתן להמיר את הקובץ. ודא ש-Microsoft Word מותקן."); return; }

                string cachedUrl = WordCacheUrl + Path.GetFileName(cached);
                if (replyId != null)
                    _bridge.Reply(replyId, new { cancelled = false, url = cachedUrl, fileName = displayName, filePath });
                else if (!readyPushed)
                    _bridge.PushEvent(new { @event = "localFileConversionReady", url = cachedUrl, fileName = displayName, filePath });
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
            }
        }

        /// <summary>
        /// Calls <paramref name="onReady"/> once, the moment Word finishes writing the cached
        /// PDF - before the converter returns (Word's quit takes a while). Null when the PDF is
        /// already cached. Dispose stops watching.
        /// </summary>
        private static IDisposable WatchForCachedPdf(string sourcePath, Action onReady)
        {
            string destPath = GetCachePath(sourcePath);
            if (File.Exists(destPath)) return null;

            Directory.CreateDirectory(WordCacheDir);
            var watcher = new FileSystemWatcher(WordCacheDir, Path.GetFileName(destPath))
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            };
            bool fired = false;
            FileSystemEventHandler handler = (s, e) =>
            {
                if (fired || !IsFileReady(e.FullPath)) return;
                fired = true;
                watcher.EnableRaisingEvents = false;
                onReady();
            };
            watcher.Created += handler;
            watcher.Changed += handler;
            watcher.EnableRaisingEvents = true;
            return watcher;
        }

        public async Task HandleRestoreLocalFile(JsonElement root, string id)
        {
            try
            {
                string filePath = root.GetProperty("filePath").GetString();
                if (!File.Exists(filePath)) { _bridge.Reply(id, new { error = "הקובץ לא נמצא" }); return; }

                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                if (!LocalFileOpenRules.IsSupported(filePath, out _)) { _bridge.Reply(id, new { error = "סוג קובץ זה אינו נתמך" }); return; }
                if (ext == ".txt")
                {
                    // Read + serialize the whole file off the UI thread (see DbHandler.HandleSql).
                    // The branches below stay on the UI thread — RegisterFolder touches CoreWebView2.
                    string content = await Task.Run(() => TextEncodingDetector.ReadAllText(filePath)).ConfigureAwait(false);
                    _bridge.Reply(id, new { textContent = content });
                    return;
                }
                if (ext == ".pdf" || ext == ".htm" || ext == ".html")
                {
                    _bridge.Reply(id, new { url = RegisterFolder(filePath) });
                    return;
                }

                string cached = GetCachePath(filePath);
                if (!File.Exists(cached)) cached = await ConvertToPdfAsync(filePath);
                if (cached == null) { _bridge.Reply(id, new { error = "לא ניתן להמיר את הקובץ" }); return; }
                _bridge.Reply(id, new { url = "http://kitveihakodesh-app/word-cache/" + Path.GetFileName(cached) });
            }
            catch (Exception ex)
            {
                _bridge.Reply(id, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Opens a file in the system's default program for its type - the equivalent of
        /// double-clicking it in Explorer. Any file type is allowed here (unlike the in-app
        /// viewer's allow-list): the user is deliberately handing the file to the OS.
        /// </summary>
        public void HandleOpenInDefaultApp(JsonElement root, string id)
        {
            try
            {
                string filePath = root.GetProperty("filePath").GetString();
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    _bridge.Reply(id, new { error = "הקובץ לא נמצא" });
                    return;
                }

                string error = LocalFileOpenRules.OpenInDefaultApp(filePath);
                _bridge.Reply(id, error == null ? (object)new { ok = true } : new { error });
            }
            catch (Exception ex) { _bridge.Reply(id, new { error = ex.Message }); }
        }

        public async Task HandleReadTxtFileContent(JsonElement root, string id)
        {
            try
            {
                string filePath = root.GetProperty("filePath").GetString();
                if (!File.Exists(filePath)) { _bridge.Reply(id, new { error = "הקובץ לא נמצא" }); return; }
                // Read + decode off the UI thread. Many Hebrew .txt files are legacy
                // Windows-1255, which TextEncodingDetector detects.
                string content = await Task.Run(() => TextEncodingDetector.ReadAllText(filePath)).ConfigureAwait(false);
                _bridge.Reply(id, new { textContent = content });
            }
            catch (Exception ex)
            {
                _bridge.Reply(id, new { error = ex.Message });
            }
        }

        public void HandleDisposeLocalFileHost(JsonElement root, string id)
        {
            string filePath = root.GetProperty("filePath").GetString();
            string folder = File.Exists(filePath) ? Path.GetDirectoryName(filePath) : filePath;
            string ext = Path.GetExtension(filePath);

            // Route the release to the same table RegisterFolder used, so a folder holding both
            // a served (.html) and a natively mapped (.pdf) host releases only the right one.
            if (ext.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".htm", StringComparison.OrdinalIgnoreCase))
            {
                if (_servedHosts.TryGetValue(folder, out var s))
                {
                    s.RefCount--;
                    if (s.RefCount <= 0)
                    {
                        _servedHosts.Remove(folder);
                        _servedFolderByHost.Remove(s.HostName);
                    }
                    else _servedHosts[folder] = s;
                }
            }
            else if (_hosts.TryGetValue(folder, out var m))
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
            // Served hosts are backed by a WebResourceRequested handler rather than a folder
            // mapping, so clearing the dictionaries is not enough — the handler keeps this
            // instance rooted on the long-lived CoreWebView2. Reset the flag too, or a later
            // RegisterServedFolder would skip re-subscribing and silently serve nothing.
            if (_servedHandlerInstalled)
            {
                try { _webView.CoreWebView2.WebResourceRequested -= OnServedResourceRequested; } catch { }
                _servedHandlerInstalled = false;
            }
            _hosts.Clear();
            _servedHosts.Clear();
            _servedFolderByHost.Clear();
        }

        private string RegisterFolder(string filePath)
        {
            string ext = Path.GetExtension(filePath);
            if (ext.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".htm", StringComparison.OrdinalIgnoreCase))
                return RegisterServedFolder(filePath);

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
            
            string filename = Path.GetFileName(filePath);
            return "http://" + m.HostName + "/" + filename;
        }

        /// <summary>
        /// Registers an HTML folder on a virtual host that we serve ourselves from
        /// <see cref="OnServedResourceRequested"/>, instead of handing the folder to
        /// SetVirtualHostNameToFolderMapping.
        ///
        /// Reason: WebView2 serves a mapped folder below the WebResourceRequested layer — the
        /// event does not fire for those URLs at all (verified against runtime 151), so there is
        /// no way to attach a Content-Type to a mapped file. With no Content-Type and no
        /// &lt;meta charset&gt; in the page, Chromium sniffs and falls back to windows-1252, which
        /// renders UTF-8 Hebrew as mojibake — the Otzaria-addin gibberish bug. Addin pages
        /// frequently omit the meta tag, which is why only *some* addins were affected.
        ///
        /// Serving the folder ourselves lets us declare the encoding we actually detected.
        /// PDFs stay on the native mapping (RegisterFolder) — they need no charset and benefit
        /// from WebView2's range-request handling.
        /// </summary>
        private string RegisterServedFolder(string filePath)
        {
            string folder = Path.GetDirectoryName(filePath);

            if (!_servedHosts.TryGetValue(folder, out var m))
            {
                if (!_servedHandlerInstalled)
                {
                    _webView.CoreWebView2.WebResourceRequested += OnServedResourceRequested;
                    _servedHandlerInstalled = true;
                }
                string host = "kitvei-localhtml-" + (++_hostCounter);
                // The 3-arg overload with SourceKinds is REQUIRED: the legacy 2-arg filter
                // only raised WebResourceRequested for the document navigation itself, so
                // every CSS/JS/image subresource of a served addin page fell through to
                // real DNS and died with ERR_NAME_NOT_RESOLVED — pages rendered unstyled,
                // scriptless HTML (diagnosed live 2026-08-27).
                _webView.CoreWebView2.AddWebResourceRequestedFilter(
                    "http://" + host + "/*", CoreWebView2WebResourceContext.All,
                    CoreWebView2WebResourceRequestSourceKinds.All);
                m = new FolderMapping { HostName = host, RefCount = 0 };
                _servedFolderByHost[host] = folder;
            }
            m.RefCount++;
            _servedHosts[folder] = m;

            return "http://" + m.HostName + "/" + Path.GetFileName(filePath);
        }

        /// <summary>
        /// Answers every request under a served host from disk. Requests for paths outside the
        /// registered folder, or for hosts whose last tab has been disposed, are left unhandled
        /// (WebView2 then fails them) rather than served.
        /// </summary>
        private void OnServedResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            try
            {
                var uri = new Uri(e.Request.Uri);
                if (!_servedFolderByHost.TryGetValue(uri.Host, out var folder)) return;
                if (!_servedHosts.ContainsKey(folder)) return;      // released

                string relative = uri.LocalPath.TrimStart('/', '\\').Replace('/', '\\');
                string full = Path.GetFullPath(Path.Combine(folder, relative));
                // Containment check — a page can request "../../secrets.txt".
                string root = Path.GetFullPath(folder);
                if (!root.EndsWith("\\")) root += "\\";
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
                if (!File.Exists(full)) return;

                byte[] bytes = File.ReadAllBytes(full);
                e.Response = _webView.CoreWebView2.Environment.CreateWebResourceResponse(
                    new MemoryStream(bytes), 200, "OK",
                    "Content-Type: " + LocalFileContentTypes.For(full, bytes, truncated: false) + "\r\n" +
                    "Content-Length: " + bytes.Length);
            }
            catch
            {
                // Leave e.Response null — WebView2 fails the request, same as a missing file.
            }
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
