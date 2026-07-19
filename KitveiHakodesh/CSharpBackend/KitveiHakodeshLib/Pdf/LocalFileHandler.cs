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
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    _bridge.PushEvent(new { @event = "localFileError", message = "הקובץ לא נמצא: " + filePath });
                    return;
                }

                string ext = Path.GetExtension(filePath).ToLowerInvariant();

                if (ext == ".txt")
                {
                    string content = File.ReadAllText(filePath, Encoding.UTF8);
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

        public void HandlePickFile(string id, Control owner)
        {
            owner.BeginInvoke(new Action(async () =>
            {
                try
                {
                    using (var dlg = new OpenFileDialog())
                    {
                        dlg.Title  = "פתח קובץ";
                        dlg.Filter = "מסמכים (*.pdf;*.doc;*.docx;*.docm;*.dot;*.dotx;*.dotm;*.htm;*.html;*.mht;*.mhtml;*.odt;*.rtf;*.txt;*.wps;*.xps)|*.pdf;*.doc;*.docx;*.docm;*.dot;*.dotx;*.dotm;*.htm;*.html;*.mht;*.mhtml;*.odt;*.rtf;*.txt;*.wps;*.xps|כל הקבצים (*.*)|*.*";
                        if (dlg.ShowDialog() != DialogResult.OK) { _bridge.Reply(id, new { cancelled = true }); return; }

                        string filePath = dlg.FileName;
                        string ext = Path.GetExtension(filePath).ToLowerInvariant();

                        if (ext == ".txt")
                        {
                            string content = File.ReadAllText(filePath, Encoding.UTF8);
                            _bridge.PushEvent(new { @event = "localFileTxtReady", textContent = content, fileName = Path.GetFileName(filePath), filePath, openInNewTab = false });
                            _bridge.Reply(id, new { cancelled = false });
                        }
                        else if (ext == ".pdf" || ext == ".htm" || ext == ".html")
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
                if (ext == ".txt")
                {
                    // Read + serialize the whole file off the UI thread (see DbHandler.HandleSql).
                    // The branches below stay on the UI thread — RegisterFolder touches CoreWebView2.
                    string content = await Task.Run(() => File.ReadAllText(filePath, Encoding.UTF8)).ConfigureAwait(false);
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
            
            string filename = Path.GetFileName(filePath);
            return "http://" + m.HostName + "/" + filename;
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
