using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace KitveiHakodeshService.Hosting;

/// <summary>
/// The two shapes this exe runs in, and what differs between them.
///
///   console  - spawned by the Vite dev plugin or by a hosted app that found no installed
///              service: inherits the spawner's user, serves one owner process, may show
///              dialogs and drive Word.
///   service  - registered with the SCM as <see cref="ServiceName"/> under LocalSystem: has
///              the MFT/USN access the file index needs, no desktop, no user of its own.
///              The user it serves is the first one to connect to the pipe
///              (<see cref="UserSessionBinding"/>).
/// </summary>
public static class WindowsServiceMode
{
    public const string ServiceName = "KitveiHakodeshSvc";
    public const string DisplayName = "Kitvei Hakodesh";

    /// <summary>True when the SCM started this process.</summary>
    public static bool IsWindowsService { get; } = OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService();

    /// <summary>Ops that need the user's desktop or the user's Word. The hosts do these
    /// themselves; the service refuses them plainly instead of hanging in session 0.</summary>
    private static readonly HashSet<string> DesktopOnlyOps = new(StringComparer.Ordinal)
    {
        "pickLocalFile", "pickFolder", "pickSeforimDbPath",
        "exportToWord", "pasteIntoWord", "openFileInDefaultApp",
    };

    public static bool IsDesktopOnlyOp(string op) => DesktopOnlyOps.Contains(op);

    public static string DesktopOnlyOpError(string op) =>
        "'" + op + "' needs the user's desktop and is not available from the Windows service; the host application does it.";

    /// <summary>
    /// Who may talk to the pipe. The default DACL of a pipe created by SYSTEM admits nobody
    /// else; every logged-on user must be able to connect, and only SYSTEM may create more
    /// instances. Harmless on a console instance (its own user already had full access).
    /// </summary>
    public static PipeSecurity PipeSecurity()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            ProcessUserSid, PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }

    // Computed once: a pipe instance is created on every accept, and WindowsIdentity.GetCurrent
    // opens a token handle each time.
    private static readonly SecurityIdentifier ProcessUserSid = ReadProcessUserSid();

    private static SecurityIdentifier ReadProcessUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User!;
    }

    public static NamedPipeServerStream CreatePipe(string pipeName) =>
        NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 4096,
            outBufferSize: 4096,
            PipeSecurity());
}
