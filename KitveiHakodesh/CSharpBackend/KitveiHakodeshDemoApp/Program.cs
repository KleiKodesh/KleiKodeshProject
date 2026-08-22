using KitveiHakodeshLib;
using KitveiHakodeshLib.Settings;
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows.Forms;

namespace KitveiHakodeshDemoApp
{
    internal static class Program
    {
        // Stable identifiers — never change these; they are the per-user IPC channel.
        // PipeName says "OpenFile" for historical reasons: it now carries any open
        // request, a file path or a deep link. Renaming it would break the channel
        // between an installed instance and a newly launched one mid-upgrade.
        private const string MutexName = "KitveiHakodesh-SingleInstance-{4A7B2C9E-1F3D-4E8A-B6C0-D2F5A3E7B9C4}";
        private const string PipeName  = "KitveiHakodesh-OpenFile-{4A7B2C9E-1F3D-4E8A-B6C0-D2F5A3E7B9C4}";

        private static MainForm _mainForm;

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // A file path or a kitveihakodeshapp:// deep link — see GetOpenRequestArgument.
            string request = GetOpenRequestArgument();

            // Debug harness: `--plain` hosts the viewer in a bare Form with no chrome-tabs
            // strip / mirror, to isolate whether the strip steals web-content focus. Runs
            // standalone (skips the single-instance mutex + its own webcache folder).
            if (Array.Exists(Environment.GetCommandLineArgs(),
                    a => string.Equals(a, "--plain", StringComparison.OrdinalIgnoreCase)))
            {
                // The plain harness hosts a file, so a deep link has nowhere to go here.
                Application.Run(new PlainDebugForm(HostLink.TryParse(request) == null ? request : null));
                return;
            }

            bool createdNew;
            using (var mutex = new Mutex(initiallyOwned: true, MutexName, out createdNew))
            {
                if (createdNew)
                {
                    // First instance — start the pipe listener and run the app.
                    StartPipeListener();
                    _mainForm = new MainForm(request);
                    Application.Run(_mainForm);
                    try { mutex.ReleaseMutex(); } catch { }
                }
                else
                {
                    // An instance is already running. Forward the request to it and exit —
                    // a second window is never opened, for a deep link exactly as for a file:
                    // the running instance turns it into a new tab.
                    if (!string.IsNullOrEmpty(request))
                        SendOpenRequestToPipe(request);

                    // Only restore if minimized — do NOT call SetForegroundWindow cross-process.
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
            if (HostLink.TryParse(candidate) != null) return candidate;
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
                                    if (!string.IsNullOrEmpty(request) &&
                                        _mainForm != null && _mainForm.IsHandleCreated)
                                    {
                                        _mainForm.BeginInvoke(new Action(() =>
                                        {
                                            if (IsIconic(_mainForm.Handle))
                                                ShowWindow(_mainForm.Handle, SW_RESTORE);
                                            // In-process, on our own UI thread, so this is not the
                                            // cross-process SetForegroundWindow the caller avoids.
                                            // Without it a link clicked in Word or a browser opens a
                                            // tab in a window the user never sees.
                                            _mainForm.Activate();
                                            _mainForm.OpenRequest(request);
                                        }));
                                    }
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

        private static void SendOpenRequestToPipe(string request)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(timeout: 3000);
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

        private const int SW_RESTORE = 9;
    }
}
