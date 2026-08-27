using FolderBrowserEx;
using KitveiHakodeshLib.Bridge;
using KitveiHakodeshLib.Pdf;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KitveiHakodesh", "word-cache");

        // Document types we deliberately DO NOT support (dropped): MS Works, MHTML web
        // archives, XPS. Rejected at every open entry point (pick / Open-With / restore) so
        // they can't reach the Word converter even via the picker's "All files" option or a
        // stale restored tab. Also removed from the picker filter and the DocumentLocator index.
        private static readonly HashSet<string> UnsupportedExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".wps", ".mht", ".mhtml", ".xps" };

        private readonly WebBridge _bridge;
        private readonly WebView2 _webView;
        private readonly Dictionary<string, FolderMapping> _hosts =
            new Dictionary<string, FolderMapping>(StringComparer.OrdinalIgnoreCase);
        // HTML folders are served by us rather than by SetVirtualHostNameToFolderMapping —
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
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    _bridge.PushEvent(new { @event = "localFileError", message = "הקובץ לא נמצא: " + filePath });
                    return;
                }

                string ext = Path.GetExtension(filePath).ToLowerInvariant();

                if (UnsupportedExtensions.Contains(ext))
                {
                    _bridge.PushEvent(new { @event = "localFileError", message = "סוג קובץ זה אינו נתמך: " + ext, filePath });
                    return;
                }

                if (ext == ".txt")
                {
                    string content = ReadTextDetectEncoding(filePath);
                    _bridge.PushEvent(new
                    {
                        @event = "localFileTxtReady",
                        textContent = content,
                        fileName = Path.GetFileName(filePath),
                        filePath,
                        openInNewTab = true,
                    });
                }
                else if (ext == ".pdf" || ext == ".htm" || ext == ".html")
                {
                    string url = RegisterFolder(filePath);
                    bool isOtzariaAddin = File.Exists(Path.Combine(Path.GetDirectoryName(filePath) ?? "", "manifest.json"));
                    _bridge.PushEvent(new { @event = "localFileReady", url, fileName = Path.GetFileName(filePath), filePath, openInNewTab = true, isOtzariaAddin });
                }
                else
                {
                    string displayName  = Path.GetFileNameWithoutExtension(filePath) + ".pdf";
                    string destPath     = GetCachePath(filePath);
                    string destFileName = Path.GetFileName(destPath);

                    _bridge.PushEvent(new { @event = "localFileConversionStarted", fileName = displayName, filePath, openInNewTab = true });

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
                            string url2 = "http://KitveiHakodesh-vue-app/word-cache/" + destFileName;
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
                        _bridge.PushEvent(new { @event = "localFileError", message = "לא ניתן להמיר את הקובץ. ודא ש-Microsoft Word מותקן.", filePath });
                        return;
                    }

                    if (!watcherFired)
                    {
                        string url = "http://KitveiHakodesh-vue-app/word-cache/" + Path.GetFileName(cached);
                        _bridge.PushEvent(new { @event = "localFileConversionReady", url, fileName = displayName, filePath });
                    }
                }
            }
            catch (Exception ex)
            {
                _bridge.PushEvent(new { @event = "localFileError", message = ex.Message, filePath });
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
                var dlg = new FolderBrowserEx.FolderBrowserDialog();
                try
                {
                    dlg.Title = "בחר תיקיית ספרים מקומית";
                    if (dlg.ShowDialog() != DialogResult.OK)
                    {
                        _bridge.Reply(id, new { cancelled = true });
                        return;
                    }
                    _bridge.Reply(id, new { folderPath = dlg.SelectedFolder });
                }
                finally
                {
                    dlg.Dispose();
                }
            }));
        }

        /// <summary>Show the open-file dialog. <paramref name="root"/> may carry an optional
        /// <c>initialDir</c> (the home page's frequent-folder tiles send one); anything else
        /// leaves the dialog wherever the shell would put it.</summary>
        public void HandlePickFile(JsonElement root, string id, Control owner)
        {
            // Read out of `root` HERE, not inside the queued action below.
            //
            // The JsonDocument this element belongs to is owned by a `using` in
            // OnMessageReceivedAsync, and BeginInvoke returns the instant it has queued the
            // action - so that `using` disposes the document before the action ever runs.
            // Touching `root` from in there hits a disposed document: ValueKind throws
            // ObjectDisposedException, the catch turns it into an error reply, and the
            // frontend maps an error reply to null and returns quietly. The picker simply
            // never opened, with nothing to say why. A plain string captured up here has no
            // such lifetime.
            string initialDir =
                root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("initialDir", out var initialDirProp)
                && initialDirProp.ValueKind == JsonValueKind.String
                    ? initialDirProp.GetString()
                    : null;

            owner.BeginInvoke(new Action(async () =>
            {
                try
                {
                    using (var dlg = new OpenFileDialog())
                    {
                        dlg.Title  = "פתח קובץ";
                        dlg.Filter = "מסמכים (*.pdf;*.doc;*.docx;*.docm;*.dot;*.dotx;*.dotm;*.htm;*.html;*.odt;*.rtf;*.txt)|*.pdf;*.doc;*.docx;*.docm;*.dot;*.dotx;*.dotm;*.htm;*.html;*.odt;*.rtf;*.txt|כל הקבצים (*.*)|*.*";
                        // A folder that has since been deleted must not sink the pick: the dialog
                        // silently falls back to its default when InitialDirectory does not exist.
                        if (!string.IsNullOrEmpty(initialDir)) dlg.InitialDirectory = initialDir;

                        if (dlg.ShowDialog() != DialogResult.OK) { _bridge.Reply(id, new { cancelled = true }); return; }

                        string filePath = dlg.FileName;
                        string ext = Path.GetExtension(filePath).ToLowerInvariant();

                        if (UnsupportedExtensions.Contains(ext))
                        {
                            _bridge.Reply(id, new { error = "סוג קובץ זה אינו נתמך: " + ext });
                            return;
                        }

                        if (ext == ".txt")
                        {
                            string content = ReadTextDetectEncoding(filePath);
                            _bridge.PushEvent(new { @event = "localFileTxtReady", textContent = content, fileName = Path.GetFileName(filePath), filePath, openInNewTab = false });
                            _bridge.Reply(id, new { cancelled = false });
                        }
                        else if (ext == ".pdf" || ext == ".htm" || ext == ".html")
                        {
                            string url = RegisterFolder(filePath);
                            bool isOtzariaAddin = File.Exists(Path.Combine(Path.GetDirectoryName(filePath) ?? "", "manifest.json"));
                            _bridge.PushEvent(new { @event = "localFileReady", url, fileName = Path.GetFileName(filePath), filePath, isOtzariaAddin });
                            _bridge.Reply(id, new { cancelled = false, url, fileName = Path.GetFileName(filePath), filePath });
                        }
                        else
                        {
                            string displayName = Path.GetFileNameWithoutExtension(filePath) + ".pdf";
                            string destPath = GetCachePath(filePath);
                            string destFileName = Path.GetFileName(destPath);
                            _bridge.PushEvent(new { @event = "localFileConversionStarted", fileName = displayName, filePath });

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
                                    if (!IsFileReady(e.FullPath)) return;
                                    if (fired) return;
                                    fired = true;
                                    watcher.EnableRaisingEvents = false;
                                    watcher.Dispose();
                                    string url2 = "http://KitveiHakodesh-vue-app/word-cache/" + destFileName;
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
                            string url = "http://KitveiHakodesh-vue-app/word-cache/" + Path.GetFileName(cached);
                            _bridge.Reply(id, new { cancelled = false, url, fileName = displayName, filePath });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _bridge.Reply(id, new { error = ex.Message });
                }
            }));
        }

        public async Task HandleRestoreLocalFile(JsonElement root, string id)
        {
            try
            {
                string filePath = root.GetProperty("filePath").GetString();
                if (!File.Exists(filePath)) { _bridge.Reply(id, new { error = "הקובץ לא נמצא" }); return; }

                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                if (UnsupportedExtensions.Contains(ext)) { _bridge.Reply(id, new { error = "סוג קובץ זה אינו נתמך" }); return; }
                if (ext == ".txt")
                {
                    // Read + serialize the whole file off the UI thread (see DbHandler.HandleSql).
                    // The branches below stay on the UI thread — RegisterFolder touches CoreWebView2.
                    string content = await Task.Run(() => ReadTextDetectEncoding(filePath)).ConfigureAwait(false);
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
                _bridge.Reply(id, new { url = "http://KitveiHakodesh-vue-app/word-cache/" + Path.GetFileName(cached) });
            }
            catch (Exception ex)
            {
                _bridge.Reply(id, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Opens a file in the system's default program for its type (Word for .docx,
        /// Acrobat for .pdf, etc.) — the equivalent of double-clicking it in Explorer.
        /// UseShellExecute = true so the shell resolves the registered handler.
        /// Any file type is allowed here (unlike the in-app viewer's allow-list): the user
        /// is deliberately handing the file off to whatever program the OS associates with it.
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

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true,
                });
                _bridge.Reply(id, new { ok = true });
            }
            catch (Exception ex) { _bridge.Reply(id, new { error = ex.Message }); }
        }

        public async Task HandleReadTxtFileContent(JsonElement root, string id)
        {
            try
            {
                string filePath = root.GetProperty("filePath").GetString();
                if (!File.Exists(filePath)) { _bridge.Reply(id, new { error = "הקובץ לא נמצא" }); return; }
                // Read + serialize the whole file off the UI thread (see DbHandler.HandleSql).
                // Detect the encoding — many Hebrew .txt files are legacy Windows-1255, not UTF-8;
                // decoding those as UTF-8 fills the view with U+FFFD replacement chars (◇?).
                string content = await Task.Run(() => ReadTextDetectEncoding(filePath)).ConfigureAwait(false);
                _bridge.Reply(id, new { textContent = content });
            }
            catch (Exception ex)
            {
                _bridge.Reply(id, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Reads a text file, detecting its encoding rather than assuming UTF-8.
        /// Honors a BOM if present (UTF-8 / UTF-16 LE / UTF-16 BE / UTF-32); otherwise
        /// validates the bytes as UTF-8 and, if they are not valid UTF-8, falls back to
        /// Windows-1255 (Hebrew ANSI) — the common legacy encoding for Hebrew .txt files.
        /// </summary>
        internal static string ReadTextDetectEncoding(string filePath)
        {
            byte[] bytes = File.ReadAllBytes(filePath);

            // 1. BOM sniffing — a BOM is authoritative.
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
                return new UTF32Encoding(false, false).GetString(bytes, 4, bytes.Length - 4);
            if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
                return new UTF32Encoding(true, false).GetString(bytes, 4, bytes.Length - 4);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);       // UTF-16 LE
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2); // UTF-16 BE

            // 2. No BOM: if the bytes are valid UTF-8, decode as UTF-8; else Windows-1255.
            return IsValidUtf8(bytes)
                ? new UTF8Encoding(false).GetString(bytes)
                : Encoding.GetEncoding(1255).GetString(bytes);
        }

        /// <summary>
        /// True if <paramref name="bytes"/> is a well-formed UTF-8 sequence (pure ASCII
        /// counts as valid). Used to distinguish UTF-8 without a BOM from single-byte
        /// legacy codepages such as Windows-1255.
        /// </summary>
        private static bool IsValidUtf8(byte[] bytes)
        {
            int i = 0;
            while (i < bytes.Length)
            {
                byte b = bytes[i];
                int extra;          // continuation bytes expected after b
                int min;            // lowest code point legal for this length (reject overlong)
                if (b <= 0x7F) { i++; continue; }
                else if ((b & 0xE0) == 0xC0) { extra = 1; min = 0x80; }
                else if ((b & 0xF0) == 0xE0) { extra = 2; min = 0x800; }
                else if ((b & 0xF8) == 0xF0) { extra = 3; min = 0x10000; }
                else return false;  // 0x80-0xBF lead or 0xF8+ — not valid UTF-8

                if (i + extra >= bytes.Length) return false;
                int cp = b & (0x7F >> (extra + 1));
                for (int k = 1; k <= extra; k++)
                {
                    byte c = bytes[i + k];
                    if ((c & 0xC0) != 0x80) return false; // not a continuation byte
                    cp = (cp << 6) | (c & 0x3F);
                }
                if (cp < min) return false;               // overlong encoding
                if (cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF)) return false; // out of range / surrogate
                i += extra + 1;
            }
            return true;
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
                    "Content-Type: " + ContentTypeFor(full, bytes) + "\r\n" +
                    "Content-Length: " + bytes.Length);
            }
            catch
            {
                // Leave e.Response null — WebView2 fails the request, same as a missing file.
            }
        }

        /// <summary>
        /// Content type for a served file. Text types carry the charset we detected from the
        /// bytes (UTF-8 with or without BOM, else Windows-1255) so the browser never has to
        /// guess; a declared charset in the response wins over sniffing.
        /// </summary>
        private static string ContentTypeFor(string path, byte[] bytes)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".html":
                case ".htm":  return "text/html; charset=" + DetectCharset(bytes);
                case ".css":  return "text/css; charset=" + DetectCharset(bytes);
                case ".js":
                case ".mjs":  return "text/javascript; charset=" + DetectCharset(bytes);
                case ".json": return "application/json; charset=" + DetectCharset(bytes);
                case ".txt":  return "text/plain; charset=" + DetectCharset(bytes);
                case ".svg":  return "image/svg+xml; charset=" + DetectCharset(bytes);
                case ".png":  return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".gif":  return "image/gif";
                case ".webp": return "image/webp";
                case ".ico":  return "image/x-icon";
                case ".woff": return "font/woff";
                case ".woff2":return "font/woff2";
                case ".ttf":  return "font/ttf";
                case ".pdf":  return "application/pdf";
                default:      return "application/octet-stream";
            }
        }

        /// <summary>
        /// Charset label for text bytes, using the same UTF-8-or-Windows-1255 rule as
        /// <see cref="ReadTextDetectEncoding"/>. UTF-16/32 BOMs are labelled so the browser
        /// decodes them correctly too.
        /// </summary>
        private static string DetectCharset(byte[] b)
        {
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return "utf-8";
            if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xFE && b[2] == 0x00 && b[3] == 0x00) return "utf-32le";
            if (b.Length >= 4 && b[0] == 0x00 && b[1] == 0x00 && b[2] == 0xFE && b[3] == 0xFF) return "utf-32be";
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return "utf-16le";
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return "utf-16be";
            return IsValidUtf8(b) ? "utf-8" : "windows-1255";
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
