using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KitveiHakodeshService.Hosting;

/// <summary>
/// Starts a program as the user this service serves, on that user's desktop - exactly as if
/// they had double-clicked it: their token (unelevated, as Windows gives it to a logged-on
/// user), their environment, their session.
///
/// Needed because the installed service is SYSTEM in session 0: a plain Process.Start would
/// run the program invisibly and as SYSTEM, which for the per-user installer means installing
/// into SYSTEM's profile. A console instance (dev) already is the user, so it just starts it.
/// </summary>
internal static class UserSessionProcess
{
    /// <summary>The Windows session of the user the service was bound to (the first pipe
    /// client's), or -1 before binding / outside service mode.</summary>
    public static int ServedSessionId { get; set; } = -1;

    public static void Launch(string executable)
    {
        if (!WindowsServiceMode.IsWindowsService)
        {
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true })?.Dispose();
            return;
        }

        uint sessionId = ServedSessionId >= 0 ? (uint)ServedSessionId : WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF) throw new InvalidOperationException("no interactive session to launch into");

        if (!WTSQueryUserToken(sessionId, out IntPtr userToken))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WTSQueryUserToken failed for session " + sessionId);

        IntPtr environment = IntPtr.Zero;
        try
        {
            if (!CreateEnvironmentBlock(out environment, userToken, false)) environment = IntPtr.Zero;

            var startup = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                lpDesktop = @"winsta0\default",
            };
            string commandLine = "\"" + executable + "\"";
            if (!CreateProcessAsUser(
                    userToken, executable, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                    CREATE_UNICODE_ENVIRONMENT, environment,
                    Path.GetDirectoryName(executable), ref startup, out PROCESS_INFORMATION process))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessAsUser failed");

            CloseHandle(process.hThread);
            CloseHandle(process.hProcess);
        }
        finally
        {
            if (environment != IntPtr.Zero) DestroyEnvironmentBlock(environment);
            CloseHandle(userToken);
        }
    }

    /// <summary>The session of a connected pipe client (used at binding time).</summary>
    public static int SessionOfPipeClient(System.IO.Pipes.NamedPipeServerStream pipe) =>
        GetNamedPipeClientSessionId(pipe.SafePipeHandle.DangerousGetHandle(), out uint session) ? (int)session : -1;

    // ── Win32 ─────────────────────────────────────────────────────────────────────

    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr token, string applicationName, string commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment,
        string? currentDirectory, ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientSessionId(IntPtr pipe, out uint sessionId);
}
