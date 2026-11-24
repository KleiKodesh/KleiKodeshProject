using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using KitveiHakodeshService.Ipc;

namespace KitveiHakodeshService.Hosting;

/// <summary>
/// One instance per pipe name, and the installed service always wins.
///
///   service  - ends every other copy of this exe that serves, or tries to serve, the shared
///              pipe (a portable app's console copy, a leftover from an earlier install)
///              before it opens the pipe, and again once it is open.
///   console  - steps aside while the installed service is running, or when another console
///              copy already serves the shared pipe. A registered service that is merely
///              stopped is STARTED instead (Authenticated Users hold SERVICE_START) and the
///              console copy steps aside for it: the installed version serves whenever it can.
///              Only a disabled or broken registration lets a console copy serve: the hosts
///              then repair the service and run that copy meanwhile (KitveiHakodeshServiceLauncher).
///
/// A copy on a private pipe (KHS_PIPE_NAME, the Vite dev plugin's "KitveiHakodesh.&lt;pid&gt;")
/// asked for an instance of its own and is left alone. Which process holds which pipe is
/// read from the pipe itself (GetNamedPipeServerProcessId), not guessed from paths.
///
/// One user at a time, by design: the service binds to the first user that connects, and
/// a console copy serves the user that spawned it.
/// </summary>
public static class SingleInstanceInstalledServiceWins
{
    // Held for the life of a console copy; the OS releases it when the process ends.
    private static Mutex? _consoleInstance;

    private const int ShutdownGraceMs = 10_000;

    private static bool IsSharedPipe => PipeServer.ResolvePipeName() == PipeServer.DefaultPipeName;

    /// <summary>
    /// Service start: end every other copy of this exe except those on a private pipe.
    /// Whoever holds the shared pipe is asked to stop first (the "shutdown" op, so an index
    /// build releases its write lock cleanly), then ended if still there. A copy that holds no
    /// pipe at all is an older build looping on "access denied", or one about to step aside
    /// anyway - ended outright. LocalSystem may end any user's process.
    ///
    /// Called twice: before the pipe opens, and again once it is open - a copy that slipped
    /// in between would otherwise serve beside us (our DACL lets SYSTEM add instances).
    /// </summary>
    public static void EndOtherCopies()
    {
        var spared = new HashSet<int>();
        var sharedOwners = new HashSet<int>();
        foreach (string name in PipeNames())
        {
            int pid = ServerProcessOf(name, shutdown: name == PipeServer.DefaultPipeName);
            if (pid <= 0) continue;
            if (name == PipeServer.DefaultPipeName) sharedOwners.Add(pid); else spared.Add(pid);
        }

        var victims = new List<Process>();
        foreach (var process in Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName))
        {
            if (process.Id == Environment.ProcessId || spared.Contains(process.Id)) { process.Dispose(); continue; }
            victims.Add(process);
        }
        if (victims.Count == 0) return;

        try
        {
            foreach (var process in victims)
            {
                try
                {
                    if (sharedOwners.Contains(process.Id)) process.WaitForExit(ShutdownGraceMs);
                    if (!process.HasExited) { process.Kill(); process.WaitForExit(5_000); }
                }
                catch { /* already gone */ }
            }
        }
        finally { foreach (var process in victims) process.Dispose(); }
    }

    /// <summary>Console start: false when this copy must not run.</summary>
    public static bool ConsoleMayRun(out string reason)
    {
        reason = "";
        if (!IsSharedPipe) return true;

        if (IsInstalledServiceRunning())
        {
            reason = "the installed service " + WindowsServiceMode.ServiceName + " is running";
            return false;
        }
        if (TryStartInstalledService())
        {
            reason = "the installed service " + WindowsServiceMode.ServiceName + " is registered; started it instead";
            return false;
        }

        _consoleInstance = new Mutex(true, @"Global\KitveiHakodeshService-console-" + PipeServer.DefaultPipeName, out bool createdNew);
        if (!createdNew)
        {
            _consoleInstance.Dispose();
            _consoleInstance = null;
            reason = "another copy already serves the pipe";
            return false;
        }
        return true;
    }

    /// <summary>The installed service is running or on its way up (false when it is not
    /// registered, or its state cannot be read).</summary>
    private static bool IsInstalledServiceRunning()
    {
        try
        {
            using var controller = new ServiceController(WindowsServiceMode.ServiceName);
            return controller.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending;
        }
        catch { return false; }
    }

    /// <summary>A registered, stopped service is started (SERVICE_START is granted to every
    /// user by ServiceRegistration). False when it is not registered, disabled, or Windows
    /// refused - then a console copy may serve.</summary>
    private static bool TryStartInstalledService()
    {
        try
        {
            using var controller = new ServiceController(WindowsServiceMode.ServiceName);
            if (controller.Status != ServiceControllerStatus.Stopped) return controller.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending;
            controller.Start();
            return true;
        }
        catch { return false; } // not registered, disabled (start refused), or no access
    }

    /// <summary>Our pipes on this machine: the shared one and every private one. Listing the
    /// pipe directory connects to nothing.</summary>
    private static IEnumerable<string> PipeNames()
    {
        string[] all;
        try { all = Directory.GetFiles(@"\\.\pipe\"); }
        catch { yield break; }
        foreach (string path in all)
        {
            string name = Path.GetFileName(path);
            if (name == PipeServer.DefaultPipeName || name.StartsWith(PipeServer.DefaultPipeName + ".", StringComparison.Ordinal))
                yield return name;
        }
    }

    /// <summary>
    /// The process serving one instance of a pipe, read from a connection to it; 0 when nobody
    /// answers. On the shared pipe the connection also carries a "shutdown" request - the
    /// server stops after replying. On a private pipe it is an empty probe, which every build
    /// of this exe closes without binding to it.
    /// </summary>
    private static int ServerProcessOf(string pipeName, bool shutdown)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
            pipe.Connect(2_000);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out uint pid)) pid = 0;
            // The second sweep runs with our own acceptors armed, so this connection can land on
            // US. A shutdown frame written before looking at the pid told the service to stop
            // itself a second after it came up (event log: start/stop pairs 1-2 s apart). An
            // empty probe is harmless: a connection without a request binds to nothing.
            if (shutdown && pid != 0 && pid != Environment.ProcessId)
            {
                byte[] frame = ShutdownFrame;
                pipe.Write(BitConverter.GetBytes(frame.Length));
                pipe.Write(frame);
                pipe.Flush();
                pipe.ReadByte(); // the reply, or EOF once it acts
            }
            return (int)pid;
        }
        catch { return 0; } // nobody listening, or it is already going down
    }

    /// <summary>{Op: "shutdown"} hand-framed as the installer does it: a 1-entry MessagePack
    /// map (0x81), fixstr key "Op", fixstr value.</summary>
    private static readonly byte[] ShutdownFrame = BuildShutdownFrame();

    private static byte[] BuildShutdownFrame()
    {
        byte[] op = System.Text.Encoding.UTF8.GetBytes("shutdown");
        var frame = new byte[5 + op.Length];
        frame[0] = 0x81; frame[1] = 0xA2; frame[2] = (byte)'O'; frame[3] = (byte)'p';
        frame[4] = (byte)(0xA0 | op.Length);
        op.CopyTo(frame, 5);
        return frame;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint serverProcessId);
}
