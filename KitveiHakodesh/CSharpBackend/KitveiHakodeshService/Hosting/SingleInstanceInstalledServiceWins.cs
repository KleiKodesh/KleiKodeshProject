using System.Diagnostics;
using System.ServiceProcess;
using KitveiHakodeshService.Ipc;

namespace KitveiHakodeshService.Hosting;

/// <summary>
/// One instance per pipe name, and the installed service always wins.
///
///   service  - ends every other copy of this exe serving the shared pipe (a portable app's
///              console copy, a leftover from an earlier install) before it opens the pipe.
///   console  - steps aside while the installed service is running, or when another console
///              copy already serves the shared pipe. A registered service that is stopped,
///              disabled or broken does not stop it: the hosts then repair the service and run
///              a console copy meanwhile (KitveiHakodeshServiceLauncher).
///
/// Only the shared pipe name is guarded: a spawner that passes its own KHS_PIPE_NAME (the
/// Vite dev plugin) asked for a private instance on purpose.
/// </summary>
public static class SingleInstanceInstalledServiceWins
{
    // Held for the life of a console copy; the OS releases it when the process ends.
    private static Mutex? _consoleInstance;

    private static bool IsSharedPipe => PipeServer.ResolvePipeName() == PipeServer.DefaultPipeName;

    /// <summary>
    /// Service start: end every other copy of this exe. Asked to stop first (the pipe op
    /// "shutdown", so an index build releases its write lock cleanly), then ended if still
    /// there. LocalSystem may end any user's process. Called twice: before the pipe opens, and
    /// again once it is open - a copy that slipped in between would otherwise serve beside us
    /// (our DACL lets SYSTEM add instances to its pipe).
    /// </summary>
    public static void EndOtherCopies()
    {
        var others = OtherCopies();
        if (others.Count == 0) return;
        try
        {
            RequestShutdown();
            foreach (var process in others) process.WaitForExit(ShutdownGraceMs);
            foreach (var process in others)
            {
                try { if (!process.HasExited) { process.Kill(); process.WaitForExit(5_000); } }
                catch { /* already gone */ }
            }
        }
        finally { foreach (var process in others) process.Dispose(); }
    }

    private const int ShutdownGraceMs = 10_000;

    private static List<Process> OtherCopies()
    {
        int self = Environment.ProcessId;
        var others = new List<Process>();
        foreach (var process in Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName))
        {
            try
            {
                if (process.Id == self) { process.Dispose(); continue; }
                // A source checkout's build output is a dev instance on its own private pipe.
                // Heuristic: KHS_PIPE_NAME cannot be read from another process; a product copy
                // never lives under a folder named bin.
                string path = process.MainModule?.FileName ?? "";
                if (path.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase)) { process.Dispose(); continue; }
                others.Add(process);
            }
            catch { process.Dispose(); }
        }
        return others;
    }

    /// <summary>One "shutdown" request on the shared pipe, to whichever copy holds it.
    /// Hand-framed like the installer does it: a 1-entry MessagePack map {Op: "shutdown"},
    /// [int32 LE length][payload].</summary>
    private static void RequestShutdown()
    {
        try
        {
            byte[] op = System.Text.Encoding.UTF8.GetBytes("shutdown");
            var frame = new byte[5 + op.Length];
            frame[0] = 0x81; frame[1] = 0xA2; frame[2] = (byte)'O'; frame[3] = (byte)'p';
            frame[4] = (byte)(0xA0 | op.Length);
            op.CopyTo(frame, 5);
            using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", PipeServer.DefaultPipeName, System.IO.Pipes.PipeDirection.InOut);
            pipe.Connect(2_000);
            pipe.Write(BitConverter.GetBytes(frame.Length));
            pipe.Write(frame);
            pipe.Flush();
            pipe.ReadByte(); // reply or EOF once it acts
        }
        catch { /* nobody listening, or it is already going down */ }
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
}
