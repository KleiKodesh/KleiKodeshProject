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
                    // This is the first (and only) instance. Start listening for file-open
                    // requests from any second instances that Windows may spawn when the user
                    // right-clicks "Open With" while the app is already running.
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
                    // Bring the running instance to the foreground.
                    BringExistingInstanceToForeground();
                }
            }
        }

        // ── Command-line argument parsing ─────────────────────────────────────────

        private static string GetFilePathArgument()
        {
            // Environment.GetCommandLineArgs()[0] is the exe path; [1] is the first real arg.
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length < 2) return null;

            string candidate = args[1];
            // Verify the argument is an existing file (not some other flag).
            return File.Exists(candidate) ? candidate : null;
        }

        // ── Named pipe IPC ────────────────────────────────────────────────────────

        private static void StartPipeListener()
        {
            // Run on a background thread — this loops forever accepting one connection at a
            // time, which is the correct pattern for a WinForms single-instance app.
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
                                        _mainForm.BringToFront();
                                        _mainForm.Activate();
                                        _mainForm.OpenFile(path);
                                    }));
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Pipe broke or app is shutting down — stop the loop.
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

        // ── Bring existing window to foreground ───────────────────────────────────

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        private static void BringExistingInstanceToForeground()
        {
            // Find the existing MainForm window by its title (best-effort; the window may not
            // have loaded yet if the machine is slow, but that is acceptable).
            foreach (System.Diagnostics.Process process in System.Diagnostics.Process.GetProcessesByName(
                System.Diagnostics.Process.GetCurrentProcess().ProcessName))
            {
                if (process.Id == System.Diagnostics.Process.GetCurrentProcess().Id) continue;
                if (process.MainWindowHandle == IntPtr.Zero) continue;
                ShowWindow(process.MainWindowHandle, SW_RESTORE);
                SetForegroundWindow(process.MainWindowHandle);
                break;
            }
        }
    }
}
