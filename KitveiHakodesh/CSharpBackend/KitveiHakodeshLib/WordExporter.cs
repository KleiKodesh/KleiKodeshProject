using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using KitveiHakodeshLib.Pdf;
using Word = Microsoft.Office.Interop.Word;

namespace KitveiHakodeshLib
{
    /// <summary>
    /// Exports book content (provided as an HTML string) to a new Microsoft Word document,
    /// or pastes from the Windows clipboard into the active Word document at the cursor.
    ///
    /// Word detection order for both operations:
    ///   1. Reuse WordToPdfConverter.HostApplication if set (VSTO scenario) — but only after
    ///      PROVING it still answers, see AcquireWordApplication.
    ///   2. Bind to an already-running Word instance via Marshal.GetActiveObject.
    ///   3. Spawn a new Word instance.
    ///
    /// EVERY OPERATION HERE IS ONE-SHOT AND LEAVES NOTHING BEHIND. A copy into Word is a
    /// single user gesture: when it returns — whether it worked or threw — this class holds no
    /// COM reference, no thread, and no lock. The previous version failed both halves of that:
    /// it cached a Word reference that could go dead and never recover (the user's paste then
    /// stayed broken until Word itself was restarted), and it released only the Application
    /// while leaking every Documents/ActiveDocument/ActiveWindow/Selection proxy it walked
    /// through. See ComScope and AcquireWordApplication.
    /// </summary>
    public static class WordExporter
    {
        /// <summary>
        /// One Word automation call at a time. Word is a single-threaded UI application; two
        /// pastes racing into it from thread-pool threads is how a paste gets rejected
        /// mid-keystroke. Callers are UI gestures, so a busy gate REJECTS rather than queues —
        /// a paste that lands a second late, into a cursor the user has since moved, is worse
        /// than one that says it was busy.
        ///
        /// Released in a finally on every path, so a crash inside Word automation can never
        /// leave the gate held and block all later usages.
        /// </summary>
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        private const string BusyPaste = "פעולת הדבקה אחרת מתבצעת כעת.";
        private const string BusyExport = "פעולת ייצוא אחרת מתבצעת כעת.";

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Opens <paramref name="html"/> in Word as a new visible document.
        /// Returns null on success, or a message describing why it failed — NEVER throws and
        /// never leaves a partially-opened document or a stray invisible Word behind.
        /// </summary>
        public static Task<string> ExportAsync(string html, string title = "")
        {
            return RunOnStaAsync("khs-word-export", () => ExportCore(html, title), BusyExport);
        }

        /// <summary>
        /// Pastes the current clipboard content at the cursor position in the active Word
        /// document. The caller is responsible for placing the HTML on the clipboard before
        /// calling this. If no document is open, creates a new blank document first.
        ///
        /// Pastes with "Merge Formatting" so the text adopts the destination document's font
        /// rather than the one Word's HTML importer would otherwise substitute. See
        /// PasteAtCursorCore.
        ///
        /// Returns null on success, or a message describing why it failed — never throws.
        /// </summary>
        public static Task<string> PasteAtCursorAsync()
        {
            return RunOnStaAsync("khs-word-paste", PasteAtCursorCore, BusyPaste);
        }

        // ── Private implementation ────────────────────────────────────────────

        /// <summary>
        /// Runs one Word automation operation on a dedicated STA thread and reports the outcome
        /// as a string instead of an exception.
        ///
        /// STA because Word automation from a thread-pool (MTA) thread only reaches Word through
        /// a marshaling proxy, which is measurably flaky for UI operations like Paste — the same
        /// reason the AOT leg (DocConvertLib AotWordPaste) spins its own STA thread.
        ///
        /// The thread is IsBackground so a wedged Word automation call can never hold the process
        /// open, and the gate is released in a finally so a crash inside <paramref name="work"/>
        /// leaves nothing blocked for the next attempt.
        /// </summary>
        private static async Task<string> RunOnStaAsync(string threadName, Action work, string busyMessage)
        {
            if (!await Gate.WaitAsync(0).ConfigureAwait(false)) return busyMessage;
            try
            {
                var completion = new TaskCompletionSource<string>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                var thread = new Thread(() =>
                {
                    try { work(); completion.TrySetResult(null); }
                    catch (Exception ex) { completion.TrySetResult(Describe(ex)); }
                })
                { IsBackground = true, Name = threadName };

                // Start inside the try: if SetApartmentState or Start throws (a thread that
                // never runs can never complete the TCS), awaiting would hang FOREVER while
                // holding the gate — turning one failure into a permanently dead feature, the
                // exact behaviour this class exists to prevent. Completing it here guarantees
                // the await always returns.
                try
                {
                    thread.SetApartmentState(ApartmentState.STA);
                    thread.Start();
                }
                catch (Exception ex)
                {
                    completion.TrySetResult(Describe(ex));
                }

                return await completion.Task.ConfigureAwait(false);
            }
            finally { Gate.Release(); }
        }

        /// <summary>
        /// A message a user can act on. A bare COMException message is an HRESULT in hex, which
        /// tells them nothing; the two that actually happen mid-editing get named outright.
        /// </summary>
        private static string Describe(Exception ex)
        {
            var com = ex as COMException;
            if (com != null)
            {
                // RPC_E_CALL_REJECTED / VBA "call was rejected by callee" — Word is busy with a
                // dialog, a drag, or a modal state and is refusing automation right now.
                if (com.HResult == unchecked((int)0x80010001) || com.HResult == unchecked((int)0x8001010A))
                    return "וורד עסוק כעת ואינו מגיב. סגור תיבת דו-שיח פתוחה ונסה שוב.";

                // RPC_E_DISCONNECTED / the object is gone — the instance we were talking to died.
                if (com.HResult == unchecked((int)0x80010108) || com.HResult == unchecked((int)0x800706BA))
                    return "החיבור לוורד אבד. נסה שוב.";
            }
            return ex.Message;
        }

        private static void ExportCore(string html, string title)
        {
            // A FRESH directory per export, so the file name can stay the book's title while the
            // path is still unique. BuildSafeFileName is deterministic, so exporting the same
            // book twice used to resolve to the same path — and the second WriteAllText threw a
            // sharing violation, because Word still had the first one open. That turned a
            // successful export into the thing that blocked the next one.
            string exportDir = Path.Combine(Path.GetTempPath(), "KitveiHakodesh", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(exportDir);

            string tempFile = Path.Combine(exportDir, BuildSafeFileName(title) + ".html");
            File.WriteAllText(tempFile, html, new System.Text.UTF8Encoding(false));

            // Yesterday's exports: Word has long since let go of them, and nobody else will ever
            // clean them up. Best-effort, and never allowed to fail the export the user asked for.
            PurgeOldExports(exportDir);

            bool opened = false;

            // ComScope releases every proxy we take, in reverse order, on every exit path.
            using (var com = new ComScope())
            {
                bool ownsApp;
                Word.Application app = com.Track(AcquireWordApplication(out ownsApp), ownsApp);
                try
                {
                    app.Visible = true;

                    Word.Documents documents = com.Track(app.Documents);
                    Word.Document doc = com.Track(documents.Open(
                        tempFile,
                        ConfirmConversions: false,
                        ReadOnly: false,
                        AddToRecentFiles: false,
                        Visible: true));

                    opened = doc != null;

                    // Activate is COSMETIC and must never fail the export. It sits AFTER the
                    // document is open and committed, so letting it throw would drop into the
                    // catch below and Quit a Word we spawned — with DisplayAlerts suppressed,
                    // discarding the user's freshly exported document without a save prompt.
                    // A throw here (Word still finishing the HTML import) is routine.
                    try { app.Activate(); } catch { }
                }
                catch
                {
                    // Nothing half-open left behind: an invisible Word we started would linger as
                    // a process the user cannot see or close. A reused instance is never quit.
                    if (ownsApp) { try { app.Quit(); } catch { } }
                    throw;
                }
                finally
                {
                    // Only clean up if Word never got it open. On success Word holds the file,
                    // and deleting it out from under the user breaks Save — so the successful
                    // export's directory is left for the next run's PurgeOldExports to collect.
                    if (!opened) { try { Directory.Delete(exportDir, true); } catch { } }
                }
            }
        }

        private static void PasteAtCursorCore()
        {
            using (var com = new ComScope())
            {
                bool ownsApp;
                Word.Application app = com.Track(AcquireWordApplication(out ownsApp), ownsApp);
                try
                {
                    app.Visible = true;

                    Word.Documents documents = com.Track(app.Documents);
                    if (documents.Count == 0)
                        com.Track(documents.Add());

                    // The clipboard already contains the formatted HTML set by the frontend.
                    //
                    // Paste with "Merge Formatting" rather than plain Selection.Paste(). Paste()
                    // does a Keep-Source-Formatting web import, and because the copied HTML carries
                    // no font of its own (the reading font lives in a stylesheet the clipboard does
                    // not travel with) Word substitutes its own web default — David / Times New
                    // Roman — as direct character formatting, which then overrides the document's
                    // styles. wdFormatSurroundingFormattingWithEmphasis (20) is Word's own
                    // "Merge Formatting" option: it matches the pasted text to the formatting of
                    // the surrounding text while keeping emphasis, so the text takes the
                    // destination's font instead of an imported one.
                    //
                    // Each hop is tracked separately: ActiveDocument, ActiveWindow and Selection
                    // are three distinct proxies, and the chained one-liner this replaces leaked
                    // the first two on every single paste.
                    Word.Document active = com.Track(app.ActiveDocument);
                    Word.Window window = com.Track(active.ActiveWindow);
                    Word.Selection selection = com.Track(window.Selection);

                    try
                    {
                        selection.PasteAndFormat(Word.WdRecoveryType.wdFormatSurroundingFormattingWithEmphasis);
                    }
                    catch (Exception pasteEx)
                    {
                        // PasteAndFormat can reject a clipboard payload that plain Paste accepts
                        // (the enum is documented against table cells and is format-sensitive), so
                        // fall back rather than silently dropping the user's paste.
                        System.Diagnostics.Debug.WriteLine("[WordExporter] PasteAndFormat failed, falling back to Paste: " + pasteEx.Message);
                        selection.Paste();
                    }

                    // Cosmetic, and AFTER the paste has already committed into the document.
                    // Letting it throw would reach the catch below and Quit a spawned Word,
                    // discarding the content we just pasted. See ExportCore.
                    try { app.Activate(); } catch { }
                }
                catch
                {
                    // An instance we started only to paste into is useless once the paste failed;
                    // leaving it running would strand an invisible Word process.
                    if (ownsApp) { try { app.Quit(); } catch { } }
                    throw;
                }
            }
        }

        /// <summary>
        /// Deletes export directories from previous runs, skipping <paramref name="keep"/> (the
        /// one this export is using) and anything recent enough that Word may still hold it open.
        ///
        /// Needed because a SUCCESSFUL export deliberately leaves its file behind — Word has the
        /// document open, and deleting it would break the user's Save. Something has to collect
        /// them eventually, and the next export is the natural place: no timer, no shutdown hook.
        /// Entirely best-effort; a file Word still holds simply survives to the next sweep.
        /// </summary>
        private static void PurgeOldExports(string keep)
        {
            try
            {
                string root = Path.Combine(Path.GetTempPath(), "KitveiHakodesh");
                if (!Directory.Exists(root)) return;

                DateTime cutoff = DateTime.UtcNow.AddDays(-1);
                foreach (string dir in Directory.GetDirectories(root))
                {
                    if (string.Equals(dir, keep, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        if (Directory.GetCreationTimeUtc(dir) > cutoff) continue;
                        Directory.Delete(dir, true);
                    }
                    catch { /* still open in Word, or not ours to delete */ }
                }
            }
            catch { /* the sweep is never worth failing an export over */ }
        }

        private static string BuildSafeFileName(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return "kitvei_export_" + Guid.NewGuid().ToString("N");

            char[] invalid = Path.GetInvalidFileNameChars();
            var safe = new System.Text.StringBuilder();
            foreach (char c in title.Trim())
            {
                if (Array.IndexOf(invalid, c) < 0)
                    safe.Append(c);
            }

            string result = safe.ToString().Trim();
            if (result.Length == 0)
                return "kitvei_export_" + Guid.NewGuid().ToString("N");

            if (result.Length > 80)
                result = result.Substring(0, 80).TrimEnd();

            return result;
        }

        private static Word.Application AcquireWordApplication(out bool ownsApp)
        {
            ownsApp = false;

            // Reuse the VSTO host application — but only if it still ANSWERS.
            //
            // HostApplication is captured once at add-in startup and cleared only at shutdown.
            // If the underlying proxy is severed while the add-in stays loaded (Word rebuilt its
            // automation server, a crash-recovery cycle, an RPC channel reset), the reference is
            // still non-null but every call on it throws RPC_E_DISCONNECTED forever. Trusting it
            // blindly is what made a broken paste stay broken until the user restarted Word:
            // this path short-circuits the other two, so it never got a chance to rebind.
            //
            // Probing a cheap property proves the channel is alive. A dead one is cleared and we
            // fall through to rebind, which is what makes the failure self-healing.
            Word.Application host = WordToPdfConverter.HostApplication;
            if (host != null)
            {
                if (KitveiHakodeshLib.Common.RunningWordFinder.IsAlive(host)) return host;
                WordToPdfConverter.HostApplication = null;
            }

            // Bind to an already-running Word instance. Also verified: the running-object table
            // can hand back an instance that is already tearing down.
            try
            {
                var running = (Word.Application)Marshal.GetActiveObject("Word.Application");
                if (running != null)
                {
                    if (KitveiHakodeshLib.Common.RunningWordFinder.IsAlive(running)) return running;
                    try { Marshal.ReleaseComObject(running); } catch { }
                }
            }
            catch (COMException) { }

            // Spawn a new Word instance.
            ownsApp = true;
            var app = new Word.Application { Visible = false };
            app.DisplayAlerts = Word.WdAlertLevel.wdAlertsNone;
            return app;
        }

        // IsAlive / IsDisconnected live in Core (Common.RunningWordFinder): the same "alive
        // unless proven dead" rule, and the same audited list of severed-channel HRESULTs, is
        // needed wherever Word is reused — this leg and any other. Keeping a second copy of that
        // list is how one of them ends up listing a BUSY code and starting a second Word beside
        // the user's own.

        private sealed class ComScope : IDisposable
        {
            private readonly System.Collections.Generic.List<object> _tracked =
                new System.Collections.Generic.List<object>();
            private readonly System.Collections.Generic.HashSet<object> _doNotRelease =
                new System.Collections.Generic.HashSet<object>();

            /// <summary>Tracks a proxy for release. Returns it so calls can stay inline.</summary>
            public T Track<T>(T comObject) where T : class
            {
                if (comObject != null) _tracked.Add(comObject);
                return comObject;
            }

            /// <summary>
            /// Tracks an Application. <paramref name="ownsApp"/> false means it came from the VSTO
            /// host or the running-object table; the host's instance must outlive us untouched.
            /// </summary>
            public Word.Application Track(Word.Application app, bool ownsApp)
            {
                if (app == null) return null;
                _tracked.Add(app);
                if (!ownsApp && ReferenceEquals(app, WordToPdfConverter.HostApplication))
                    _doNotRelease.Add(app);
                return app;
            }

            public void Dispose()
            {
                for (int i = _tracked.Count - 1; i >= 0; i--)
                {
                    object comObject = _tracked[i];
                    if (_doNotRelease.Contains(comObject)) continue;
                    try { Marshal.ReleaseComObject(comObject); } catch { }
                }
                _tracked.Clear();
                _doNotRelease.Clear();
            }
        }
    }
}
