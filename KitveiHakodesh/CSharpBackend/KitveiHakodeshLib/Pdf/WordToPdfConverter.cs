using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Word = Microsoft.Office.Interop.Word;

namespace KitveiHakodeshLib.Pdf
{
    /// <summary>
    /// Converts Word documents (.doc, .docx, .rtf) to PDF using the Microsoft Word
    /// COM interop. HTML and plain-text files are served directly via the virtual host
    /// and never pass through this converter.
    /// </summary>
    public static class WordToPdfConverter
    {
        /// <summary>
        /// Set this from VSTO startup (e.g. Globals.ThisAddIn.Application) to reuse
        /// the existing Word instance instead of spawning a new one.
        /// Leave null for standalone usage.
        /// </summary>
        public static Word.Application HostApplication { get; set; }

        private static CancellationTokenSource _hostCts = new CancellationTokenSource();

        /// <summary>
        /// Call from VSTO ThisAddIn_Shutdown to cancel any in-flight conversions.
        /// </summary>
        public static void CancelHostConversions()
        {
            _hostCts.Cancel();
            HostApplication = null;
            Log("HostApplication cleared, in-flight conversions cancelled");
        }

        /// <summary>The cancellation of each conversion in flight, by source path (the frontend's cancel button).</summary>
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> _conversions =
            new ConcurrentDictionary<string, CancellationTokenSource>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Cancels the conversion of <paramref name="sourcePath"/> if one runs. Documents.Open / SaveAs2 of a big
        /// file block for minutes and cannot be interrupted over COM, so the Word instance THIS converter started
        /// is ended - found as in DocConvertLib's AotWordConverter: a process that did not exist before the
        /// converter created Word, carrying a unique caption. A Word the user had open is never touched.
        /// True when a conversion was cancelled.
        /// </summary>
        public static bool Cancel(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath) || !_conversions.TryGetValue(NormalizeKey(sourcePath), out var cts)) return false;
            try { cts.Cancel(); } catch (ObjectDisposedException) { return false; }
            return true;
        }

        private static string NormalizeKey(string path)
        {
            try { return Path.GetFullPath(path); } catch { return path; }
        }

        /// <summary>
        /// Converts to <paramref name="outputPath"/> through a private file renamed on success, so a cancelled
        /// conversion never leaves a cut-off PDF under the cache name.
        /// </summary>
        public static async Task<string> ConvertWordToPdfAsync(string sourcePath, string outputPath)
        {
            string key = NormalizeKey(sourcePath);
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(_hostCts.Token))
            {
                _conversions[key] = linked;
                string partial = Path.ChangeExtension(outputPath, "." + Guid.NewGuid().ToString("N") + ".part.pdf");
                try
                {
                    await Task.Run(() => { using (var conv = new WordConversion()) conv.Convert(sourcePath, partial, linked.Token); }, linked.Token).ConfigureAwait(false);
                    linked.Token.ThrowIfCancellationRequested();
                    if (File.Exists(outputPath)) File.Delete(outputPath);
                    File.Move(partial, outputPath);
                    return outputPath;
                }
                catch (Exception ex) when (!(ex is OperationCanceledException) && linked.IsCancellationRequested)
                {
                    // The blocked COM call failed because the cancel ended our Word.
                    throw new OperationCanceledException("Word conversion cancelled", ex, linked.Token);
                }
                finally
                {
                    ((ICollection<KeyValuePair<string, CancellationTokenSource>>)_conversions)
                        .Remove(new KeyValuePair<string, CancellationTokenSource>(key, linked));
                    try { if (File.Exists(partial)) File.Delete(partial); } catch { }
                }
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowW(string lpClassName, string lpWindowName);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private static HashSet<int> WordProcessIds()
        {
            var ids = new HashSet<int>();
            foreach (var process in Process.GetProcessesByName("WINWORD"))
                using (process) ids.Add(process.Id);
            return ids;
        }

        private static void Log(string msg) => System.Diagnostics.Debug.WriteLine("[Word] " + msg);

        // ── dedicated conversion class ────────────────────────────────────────────
        // Always instantiate with `using` — Dispose() guarantees the Word process
        // is fully released even if an exception is thrown mid-conversion.

        private sealed class WordConversion : IDisposable
        {
            private Word.Application _app;
            private Word.Document _doc;
            private bool _disposed;
            private bool _ownsApp;
            private Process _ownProcess;                         // the WINWORD this conversion started (for a cancel)
            private CancellationTokenRegistration _cancelRegistration;

            public string Convert(string sourcePath, string outputPath, CancellationToken ct = default)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();

                if (HostApplication != null)
                {
                    Log("VSTO host detected — using dedicated Word instance for conversion");
                }
                var wordsBefore = WordProcessIds();
                _app = new Word.Application { Visible = false, ScreenUpdating = false };
                _app.DisplayAlerts = Word.WdAlertLevel.wdAlertsNone;
                _ownsApp = true;
                Log("App ready: " + sw.ElapsedMilliseconds + "ms");
                _ownProcess = OwnWordProcess(wordsBefore);
                if (_ownProcess != null)
                {
                    var own = _ownProcess;
                    _cancelRegistration = ct.Register(() => { try { own.Kill(); } catch { } });
                }

                ct.ThrowIfCancellationRequested();

                _app.Options.UpdateLinksAtOpen = false;
                _app.Options.CheckSpellingAsYouType = false;
                _app.Options.CheckGrammarAsYouType = false;
                _doc = _app.Documents.Open(sourcePath, ConfirmConversions: false,
                    ReadOnly: true, AddToRecentFiles: false, Visible: false, NoEncodingDialog: true);
                Log("Open: " + sw.ElapsedMilliseconds + "ms");
                _doc.Fields.Unlink();
                Log("Fields.Unlink: " + sw.ElapsedMilliseconds + "ms");
                foreach (Word.Hyperlink hl in _doc.Hyperlinks) try { hl.Delete(); } catch { }
                Log("Links cleared: " + sw.ElapsedMilliseconds + "ms");

                ct.ThrowIfCancellationRequested();

                _doc.SaveAs2(outputPath, Word.WdSaveFormat.wdFormatPDF);
                Log("Export: " + sw.ElapsedMilliseconds + "ms");
                Log("Total: " + sw.ElapsedMilliseconds + "ms");

                return outputPath;
            }

            /// <summary>The WINWORD process behind _app: new since the converter created Word, found by a unique
            /// caption on its hidden main window ("OpusApp"). Null when it cannot be told apart (nothing is touched).</summary>
            private Process OwnWordProcess(HashSet<int> wordsBefore)
            {
                var started = WordProcessIds();
                started.ExceptWith(wordsBefore);
                if (started.Count == 0) return null;
                string caption = "KitveiHakodesh conversion " + Guid.NewGuid().ToString("N");
                try { _app.Caption = caption; } catch { return null; }
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    IntPtr window = FindWindowW("OpusApp", caption);
                    if (window != IntPtr.Zero)
                    {
                        GetWindowThreadProcessId(window, out uint processId);
                        if (processId == 0 || !started.Contains((int)processId)) return null;
                        try
                        {
                            var process = Process.GetProcessById((int)processId);
                            _ = process.Handle;            // hold the handle now: a later Kill reaches THIS process, never a reused id
                            return process;
                        }
                        catch { return null; }
                    }
                    Thread.Sleep(25);
                }
                return null;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _cancelRegistration.Dispose();
                // A Word the cancel ended cannot answer: skip Close / Quit.
                bool ended = _ownProcess != null && _ownProcess.HasExited;

                if (_doc != null)
                {
                    if (!ended) try { _doc.Close(false); } catch { }
                    Marshal.ReleaseComObject(_doc);
                    _doc = null;
                }

                if (_app != null)
                {
                    if (_ownsApp)
                    {
                        if (!ended) try { _app.Quit(); } catch { }
                        Marshal.ReleaseComObject(_app);
                    }
                    _app = null;
                }
                _ownProcess?.Dispose();
            }
        }
    }
}
