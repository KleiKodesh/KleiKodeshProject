using KitveiHakodeshLib;
using KitveiHakodesh.Core.Settings;
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows.Forms;

namespace KitveiHakodeshDesktopApp
{
    internal static class Program
    {
        // Stable identifiers — never change these; they are the per-user IPC channel.
        // PipeName says "OpenFile" for historical reasons: it now carries any open
        // request, a file path or a deep link. Renaming it would break the channel
        // between an installed instance and a newly launched one mid-upgrade.
        private const string MutexName = "KitveiHakodesh-SingleInstance-{4A7B2C9E-1F3D-4E8A-B6C0-D2F5A3E7B9C4}";
        private const string PipeName  = "KitveiHakodesh-OpenFile-{4A7B2C9E-1F3D-4E8A-B6C0-D2F5A3E7B9C4}";

        // Sent by a launch with nothing to open: the running app opens a new empty window
        // (VS Code's window.openWithoutArgumentsInNewWindow). Never a file or a link, so a
        // running instance older than this drops it in MainForm.OpenRequest.
        private const string NewWindowRequest = "--new-window";

        // The window a forwarded file or link opens in: the last one activated.
        private static MainForm _activeWindow;

        // Main touches ONLY the BCL until it knows it is the first instance. A file opened
        // from Explorer while the app runs launches a second copy of this exe whose only
        // job is to forward the path -- loading KitveiHakodeshLib / WinForms / WebView2 for
        // that (or JIT-compiling a Main that references them) is what made the new tab lag.
        // Everything else lives in methods marked NoInlining so their types load only there.
        [STAThread]
        static void Main()
        {
            string[] args = Environment.GetCommandLineArgs();

            // Debug harness: `--plain` hosts the viewer in a bare Form with no chrome-tabs
            // strip / mirror, to isolate whether the strip steals web-content focus. Runs
            // standalone (skips the single-instance mutex + its own webcache folder).
            if (Array.Exists(args, a => string.Equals(a, "--plain", StringComparison.OrdinalIgnoreCase)))
            {
                RunPlainHarness();
                return;
            }

            // Hosted e2e rig (HostTestMode): a standalone instance that never hands off to,
            // or takes requests from, the user's running app.
            if (IsHostTestMode())
            {
                RunTestInstance();
                return;
            }

            bool createdNew;
            using (var mutex = new Mutex(initiallyOwned: true, MutexName, out createdNew))
            {
                if (createdNew)
                {
                    RunFirstInstance();
                    try { mutex.ReleaseMutex(); } catch { }
                }
                else
                {
                    // An instance is already running. Forward the request to it and exit —
                    // there is only ever one process (the VS Code model). A file or a deep
                    // link becomes a new tab in its last active window; a launch with
                    // nothing to open asks it for a new empty window.
                    // Forwarded RAW: MainForm.OpenRequest validates whatever arrives over the
                    // pipe, so parsing it here would only cost the lib load this path avoids.
                    string request = args.Length > 1 ? args[1] : NewWindowRequest;
                    bool newWindow = request == NewWindowRequest;
                    // A relative path must resolve against OUR working directory, not the
                    // running instance's. Links pass through untouched.
                    if (!newWindow && !string.IsNullOrEmpty(request) && request.IndexOf("://", StringComparison.Ordinal) < 0)
                        try { request = Path.GetFullPath(request); } catch { }
                    if (!string.IsNullOrEmpty(request))
                    {
                        // Hand our foreground rights to the already-running instance BEFORE
                        // it is asked to raise itself. Without this its Activate() is refused
                        // by the foreground lock -- we are the process Windows currently
                        // considers entitled to the foreground -- and the user gets a
                        // flashing taskbar button instead of the window they clicked a link
                        // for. ASFW_ANY because the running instance's pid is not known here
                        // and any of our own instances raising itself is the wanted outcome.
                        AllowSetForegroundWindow(ASFW_ANY);
                        // Pipe only as the fallback for a running instance older than the
                        // WM_COPYDATA receiver (mid-upgrade).
                        if (!SendOpenRequestByCopyData(request))
                            SendOpenRequestToPipe(request);
                    }

                    // A new window shows itself; nothing to restore.
                    if (newWindow) return;

                    // Only restore if minimized — do NOT call SetForegroundWindow on the
                    // OTHER process's window from here. (The running instance calls it on its
                    // own window, which is a different thing and is what AllowSetForegroundWindow
                    // above enables.)
                    // On a maximized WebView2-hosted window, SetForegroundWindow posts a
                    // WM_WINDOWPOSCHANGED/SIZE_RESTORED to the first process's queue, which
                    // visibly flashes the window to Normal. The first process's pipe callback
                    // already runs on its own UI thread and handles focus itself.
                    foreach (System.Diagnostics.Process process in System.Diagnostics.Process.GetProcessesByName(
                        System.Diagnostics.Process.GetCurrentProcess().ProcessName))
                    {
                        if (process.Id == System.Diagnostics.Process.GetCurrentProcess().Id) continue;
                        if (process.MainWindowHandle == IntPtr.Zero) continue;
                        if (IsIconic(process.MainWindowHandle))
                            ShowWindow(process.MainWindowHandle, SW_RESTORE);
                        break;
                    }
                }
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void RunFirstInstance()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            StartServiceEarly();
            // The browser process is the long pole of a cold start: launch it before the form
            // is built, so it comes up while the rest of startup runs. MainForm's AppViewer
            // uses the same folder and picks this environment up.
            AppViewer.StartSharedEnvironment(MainForm.WebCacheFolder);
            KitveiHakodeshLib.LocalFile.WindowsRecentDocuments.Enabled = true;
            // Before the form: the receiver must exist the moment the mutex is ours, so
            // launches that race our startup (a multi-file Explorer open) find it.
            _receiver = new OpenRequestReceiver();
            StartPipeListener();

            // Every window open when the app last quit comes back, each with its own tabs.
            // A file path or kitveihakodeshapp:// deep link we were started with (see
            // GetOpenRequestArgument) opens in the first of them.
            string[] windowIds = AppSettingsRegistry.GetOpenWindowIds();
            var first = CreateWindow(windowIds[0], GetOpenRequestArgument());
            first.HandleCreated += (s, e) => first.BeginInvoke(new Action(DrainEarlyRequests));
            lock (EarlyRequestsLock) _activeWindow = first;
            for (int i = 1; i < windowIds.Length; i++)
                if (Array.IndexOf(windowIds, windowIds[i]) == i) // a damaged list must not open one id twice
                    CreateWindow(windowIds[i], null).Show();
            // Last, so it is the one in front and the one a forwarded file goes to.
            first.Show();
            SaveOpenWindowIds();
            Application.Run(AppContext);
        }

        // ── Windows ───────────────────────────────────────────────────────────────
        // One process, any number of windows (the VS Code model). UI thread only.

        // Quits the message loop when the last window has closed.
        private static readonly ApplicationContext AppContext = new ApplicationContext();
        private static readonly System.Collections.Generic.List<MainForm> OpenWindows = new System.Collections.Generic.List<MainForm>();
        // Windows whose close has started, for any reason. Never a target for a forwarded
        // request, and never "a window that stays open" when another window decides.
        private static readonly System.Collections.Generic.HashSet<MainForm> ClosingWindows = new System.Collections.Generic.HashSet<MainForm>();
        // The closing windows that delete their saved tabs (ClosesForGood).
        private static readonly System.Collections.Generic.HashSet<MainForm> WindowsClosingForGood = new System.Collections.Generic.HashSet<MainForm>();

        // How long a closing window waits, hidden, before it decides its tabs are gone for
        // good. "Close all windows" on the taskbar closes them one after another; within
        // this pause the others start closing too, so all of them are kept, as on a quit.
        private const int CloseAllGraceMs = 1000;

        private static MainForm CreateWindow(string windowId, string initialRequest)
        {
            var form = new MainForm(initialRequest, windowId) { ClosesForGood = ClosesForGood };
            form.Activated += (s, e) => { lock (EarlyRequestsLock) _activeWindow = form; };
            // Every close completes (AppViewer holds the first one only to save), so the
            // window stops being a target the moment it starts.
            form.FormClosing += (s, e) => OnWindowClosing(form);
            form.FormClosed += (s, e) => OnWindowClosed(form);
            OpenWindows.Add(form);
            return form;
        }

        // A new window beside the one it was opened from: empty for a launch with nothing to
        // open, or holding a forwarded request that found no window to go to. Its id is the
        // lowest number no open window has, so ids stay short however often windows come and go.
        private static void OpenNewWindow(MainForm openedFrom, string initialRequest = null)
        {
            var used = new System.Collections.Generic.HashSet<string>();
            foreach (MainForm open in OpenWindows) used.Add(open.WindowId);
            string id;
            int n = 1;
            while (used.Contains(id = n.ToString(System.Globalization.CultureInfo.InvariantCulture))) n++;

            MainForm form = CreateWindow(id, initialRequest);
            form.OpenedFrom = openedFrom;
            form.Show();
            form.Activate();
            SetForegroundWindow(form.Handle);
            SaveOpenWindowIds();
        }

        private static bool AnotherWindowStays(MainForm form)
        {
            foreach (MainForm other in OpenWindows)
                if (other != form && !ClosingWindows.Contains(other)) return true;
            return false;
        }

        // A window closing while another stays open is gone for good, like closing one
        // VS Code window: its saved tabs are deleted and it is not reopened. The last window
        // keeps its tabs for the next start, and so does every window when Windows shuts
        // down, the app is quit from outside, or all windows close together.
        private static async System.Threading.Tasks.Task<bool> ClosesForGood(MainForm form, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing
                || e.CloseReason == CloseReason.ApplicationExitCall)
                return false;
            if (!AnotherWindowStays(form)) return false;
            // Gone from the screen at once; the decision waits for the grace period.
            form.Hide();
            await System.Threading.Tasks.Task.Delay(CloseAllGraceMs);
            if (!AnotherWindowStays(form)) return false;
            WindowsClosingForGood.Add(form);
            SaveOpenWindowIds();
            return true;
        }

        private static void OnWindowClosing(MainForm form)
        {
            ClosingWindows.Add(form);
            lock (EarlyRequestsLock)
            {
                if (_activeWindow != form) return;
                for (int i = OpenWindows.Count - 1; i >= 0; i--)
                {
                    if (ClosingWindows.Contains(OpenWindows[i])) continue;
                    _activeWindow = OpenWindows[i];
                    return;
                }
            }
        }

        private static void OnWindowClosed(MainForm form)
        {
            OpenWindows.Remove(form);
            ClosingWindows.Remove(form);
            WindowsClosingForGood.Remove(form);
            lock (EarlyRequestsLock)
            {
                if (_activeWindow == form)
                    _activeWindow = OpenWindows.Count > 0 ? OpenWindows[OpenWindows.Count - 1] : null;
            }
            if (OpenWindows.Count == 0) AppContext.ExitThread();
        }

        // The windows the next start reopens: every open one not closing for good.
        private static void SaveOpenWindowIds()
        {
            var ids = new System.Collections.Generic.List<string>();
            foreach (MainForm open in OpenWindows)
                if (!WindowsClosingForGood.Contains(open)) ids.Add(open.WindowId);
            if (ids.Count == 0) return; // the last window keeps the list as it is
            try { AppSettingsRegistry.SetOpenWindowIds(ids); } catch { /* registry unwritable: next start opens one window */ }
        }

        // The service stops itself soon after the last window closes, so a normal launch has to
        // start it again. Started here, first thing, it is up (~170 ms) long before the page asks
        // for data; started only after WebView2 is ready, as the viewer does, the page's first
        // requests waited ~1.3 s for it. Never elevates: the viewer's own call, after the window
        // exists, is the one that may show the install prompt, and it returns at once when this
        // start already succeeded (the launcher serialises both).
        private static void StartServiceEarly()
        {
            var launcher = new KitveiHakodeshLib.Service.KitveiHakodeshServiceLauncher();
            System.Threading.Tasks.Task.Run(async () =>
            {
                try { await launcher.EnsureRunningAsync(System.Threading.CancellationToken.None, allowElevation: false); }
                catch { /* the viewer's own attempt reports it */ }
            });
        }

        // BCL only, like the rest of Main before the first-instance check (HostTestMode.IsOn
        // would load KitveiHakodeshLib on every second launch).
        private static bool IsHostTestMode()
            => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KH_TEST_CDP_PORT"));

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void RunTestInstance()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            StartServiceEarly();
            AppViewer.StartSharedEnvironment(MainForm.WebCacheFolder);
            var form = new MainForm(GetOpenRequestArgument());
            lock (EarlyRequestsLock) _activeWindow = form;
            Application.Run(form);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void RunPlainHarness()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string request = GetOpenRequestArgument();
            // The plain harness hosts a file, so a deep link has nowhere to go here.
            Application.Run(new PlainDebugForm(HostLink.TryParse(request) == null && DailyLearningLink.TryParse(request) == null ? request : null));
        }

        // ── Command-line argument parsing ─────────────────────────────────────────

        /// <summary>
        /// The one startup argument this app takes: an existing file to open, or a
        /// deep link into a book — <c>kitveihakodeshapp://book/&lt;id&gt;?index=&lt;n&gt;</c>,
        /// this app's own format, or either of the other two families
        /// <see cref="HostLink"/> parses (<c>otzaria://</c>, <c>zayit://</c>), which cost
        /// nothing to accept and open the same way.
        /// Windows passes the URL as argv[1] when the app is the registered handler for
        /// the scheme — see Build/Installer/README.md, "URL protocol registration".
        /// Anything else returns null, so flags like --plain are never read as a request.
        /// </summary>
        private static string GetOpenRequestArgument()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length < 2) return null;
            string candidate = args[1];
            // Order matters only for clarity: a URL can never be an existing file path.
            if (HostLink.TryParse(candidate) != null || DailyLearningLink.TryParse(candidate) != null) return candidate;
            return File.Exists(candidate) ? candidate : null;
        }

        // ── Named pipe IPC ────────────────────────────────────────────────────────

        private static void StartPipeListener()
        {
            var thread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using (var server = new NamedPipeServerStream(PipeName, PipeDirection.In, maxNumberOfServerInstances: 1))
                        {
                            server.WaitForConnection();
                            using (var reader = new StreamReader(server))
                            {
                                // Validation lives in MainForm.OpenRequest, which is the one
                                // place that knows what an open request may be.
                                string request = reader.ReadToEnd()?.Trim();
                                // Own try/catch: a failed dispatch must not take the listener
                                // down with it, or every later request silently opens nothing.
                                // IsHandleCreated because the listener starts before the form.
                                try
                                {
                                    if (!string.IsNullOrEmpty(request))
                                        EnqueueOrDispatch(request);
                                }
                                catch { /* window went away mid-dispatch; keep listening */ }
                            }
                        }
                    }
                    catch
                    {
                        break;
                    }
                }
            })
            { IsBackground = true, Name = "KitveiHakodesh-PipeListener" };
            thread.Start();
        }

        private static readonly object EarlyRequestsLock = new object();
        private static readonly System.Collections.Generic.List<string> EarlyRequests = new System.Collections.Generic.List<string>();

        // Any thread. Receivers start before the form exists. Explorer launches one copy
        // per selected file, so with several files the others forward theirs while the
        // first is still starting -- park them instead of dropping them.
        private static void EnqueueOrDispatch(string request)
        {
            bool ready;
            MainForm target;
            lock (EarlyRequestsLock)
            {
                target = _activeWindow;
                ready = target != null && target.IsHandleCreated;
                if (!ready) EarlyRequests.Add(request);
            }
            // BeginInvoke even on the UI thread: the sender is blocked in SendMessage
            // until we return, so opening the tab must not happen inside it.
            if (ready)
                target.BeginInvoke(new Action(() => DispatchRequest(request)));
        }

        private static void DrainEarlyRequests()
        {
            string[] requests;
            lock (EarlyRequestsLock)
            {
                requests = EarlyRequests.ToArray();
                EarlyRequests.Clear();
            }
            foreach (string request in requests) DispatchRequest(request);
        }

        // UI thread only.
        private static void DispatchRequest(string request)
        {
            MainForm window;
            lock (EarlyRequestsLock) window = _activeWindow;
            if (window == null || window.IsDisposed) return; // the app is shutting down
            if (request == NewWindowRequest)
            {
                OpenNewWindow(window);
                return;
            }
            // Every window is closing: the request gets a window of its own rather than a
            // tab in one that is about to go.
            if (ClosingWindows.Contains(window))
            {
                OpenNewWindow(null, request);
                return;
            }
            if (IsIconic(window.Handle))
                ShowWindow(window.Handle, SW_RESTORE);
            // In-process, on our own UI thread, so this is not the
            // cross-process SetForegroundWindow the caller avoids.
            // Without it a link clicked in Word or a browser opens a
            // tab in a window the user never sees.
            //
            // Activate() alone is not enough: the launching instance
            // (or Word, or the browser) is the process Windows gives
            // foreground rights to, so our request is refused and only
            // flashes the taskbar button. The launcher calls
            // AllowSetForegroundWindow before sending, which makes the
            // SetForegroundWindow below succeed; Activate() stays as
            // the fallback for callers that cannot do that (a launch
            // arriving over the pipe from anywhere else).
            window.Activate();
            SetForegroundWindow(window.Handle);
            window.OpenRequest(request);
        }

        // ── WM_COPYDATA IPC (the Notepad++ model) ─────────────────────────────────
        // The first instance owns a hidden message-only window with a fixed caption;
        // a later launch finds it and SendMessage's the request. Synchronous, lands on
        // the UI thread in launch order, no listener thread or pipe-instance queue.

        private const string ReceiverCaption = "KitveiHakodesh-OpenRequest-{4A7B2C9E-1F3D-4E8A-B6C0-D2F5A3E7B9C4}";
        private const int WM_COPYDATA = 0x004A;
        private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);
        private static OpenRequestReceiver _receiver; // rooted: the window lives as long as the app

        private sealed class OpenRequestReceiver : NativeWindow
        {
            public OpenRequestReceiver()
            {
                CreateHandle(new CreateParams { Caption = ReceiverCaption, Parent = HWND_MESSAGE });
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_COPYDATA)
                {
                    var data = (COPYDATASTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(COPYDATASTRUCT));
                    string request = data.cbData > 0
                        ? System.Runtime.InteropServices.Marshal.PtrToStringUni(data.lpData, data.cbData / 2)
                        : null;
                    if (!string.IsNullOrEmpty(request)) EnqueueOrDispatch(request);
                    m.Result = new IntPtr(1);
                    return;
                }
                base.WndProc(ref m);
            }
        }

        /// <summary>False when no receiver was found -- the caller falls back to the pipe.</summary>
        private static bool SendOpenRequestByCopyData(string request)
        {
            // Same wait as Notepad++: the mutex owner may not have made its window yet.
            IntPtr target = IntPtr.Zero;
            for (int i = 0; i < 20 && target == IntPtr.Zero; i++)
            {
                if (i > 0) Thread.Sleep(100);
                target = FindWindowEx(HWND_MESSAGE, IntPtr.Zero, null, ReceiverCaption);
            }
            if (target == IntPtr.Zero) return false;

            IntPtr buffer = System.Runtime.InteropServices.Marshal.StringToHGlobalUni(request);
            try
            {
                var data = new COPYDATASTRUCT { dwData = IntPtr.Zero, cbData = request.Length * 2, lpData = buffer };
                IntPtr result;
                // Timeout so a hung instance cannot hang every later launch with it.
                // True even on timeout: the message may still be delivered, and a pipe
                // retry would then open the file twice.
                SendMessageTimeout(target, WM_COPYDATA, IntPtr.Zero, ref data,
                    SMTO_ABORTIFHUNG, 10000, out result);
                return true;
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer); }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct COPYDATASTRUCT
        {
            public IntPtr dwData;
            public int cbData;
            public IntPtr lpData;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, ref COPYDATASTRUCT lParam,
            int flags, int timeout, out IntPtr result);

        private const int SMTO_ABORTIFHUNG = 0x0002;

        private static void SendOpenRequestToPipe(string request)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    // Generous: a multi-file Explorer open queues one launch per file on
                    // this single-instance pipe, behind a first instance still starting.
                    client.Connect(timeout: 10000);
                    using (var writer = new StreamWriter(client) { AutoFlush = true })
                        writer.Write(request);
                }
            }
            catch { /* running instance may not be listening yet; best-effort */ }
        }

        // ── Win32 ─────────────────────────────────────────────────────────────────

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private const int SW_RESTORE = 9;

        /// <summary>ASFW_ANY - any process may take the foreground from us.</summary>
        private const int ASFW_ANY = -1;
    }
}
