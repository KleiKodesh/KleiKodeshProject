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
        private const string MutexName = "KitveiHakodesh-SingleInstance-{4A7B2C9E-1F3D-4E8A-B6C0-D2F5A3E7B9C4}";
        private const string PipeName  = "KitveiHakodesh-OpenFile-{4A7B2C9E-1F3D-4E8A-B6C0-D2F5A3E7B9C4}";

        private static MainForm _mainForm;

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string filePath = GetFilePathArgument();

            bool createdNew;
            using (var mutex = new Mutex(initiallyOwned: true, MutexName, out createdNew))
            {
                if (createdNew)
                {
                    // First instance — start the pipe listener and run the app.
                    StartPipeListener();
                    _mainForm = new MainForm(filePath);
                    Application.Run(_mainForm);
                    try { mutex.ReleaseMutex(); } catch { }
                }
                else
                {
                    // An instance is already running. Forward the file path to it and exit.
                    if (!string.IsNullOrEmpty(filePath))
                        SendFilePathToPipe(filePath);

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

        private static string GetFilePathArgument()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length < 2) return null;
            string candidate = args[1];
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
                                string path = reader.ReadToEnd()?.Trim();
                                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                                {
                                    _mainForm?.BeginInvoke(new Action(() =>
                                    {
                                        if (IsIconic(_mainForm.Handle))
                                            ShowWindow(_mainForm.Handle, SW_RESTORE);
                                        _mainForm.OpenFile(path);
                                    }));
                                }
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

        private static void SendFilePathToPipe(string filePath)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(timeout: 3000);
                    using (var writer = new StreamWriter(client) { AutoFlush = true })
                        writer.Write(filePath);
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
