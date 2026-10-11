using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace KitveiHakodeshService.Hosting;

/// <summary>
/// Registers and unregisters this exe with the Service Control Manager (<c>--install</c> /
/// <c>--uninstall</c>). The installer runs these verbs elevated; the service itself is
/// LocalSystem, demand-start, and any authenticated user may START it, so the per-user
/// hosts bring it up without a prompt. Ported from DocumentLocator.Service, which this
/// service replaces.
/// </summary>
internal static class ServiceRegistration
{
    public static int Install()
    {
        string exePath = Environment.ProcessPath ?? throw new InvalidOperationException("ProcessPath unavailable");

        IntPtr scm = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
        if (scm == IntPtr.Zero) return Fail("OpenSCManager", "Run as Administrator.");
        try
        {
            IntPtr svc = CreateKitveiHakodeshService(scm, exePath);
            if (svc == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                if (error != ERROR_SERVICE_EXISTS) return Fail("CreateService", "error " + error);

                // Already registered (an older path, or a repair): stop, delete, recreate.
                // DeleteService only MARKS a running service; CreateService then fails with
                // ERROR_SERVICE_MARKED_FOR_DELETE, so wait until it has actually stopped.
                Console.WriteLine("Service already registered - re-registering at " + exePath);
                Stop(scm);
                WaitUntilStopped(scm, TimeSpan.FromSeconds(30));
                if (!DeleteByName(scm)) return Fail("DeleteService", "error " + Marshal.GetLastWin32Error());
                Thread.Sleep(500);
                svc = CreateKitveiHakodeshService(scm, exePath);
                if (svc == IntPtr.Zero) return Fail("CreateService (retry)", "error " + Marshal.GetLastWin32Error());
            }

            try
            {
                var description = new ServiceDescription
                {
                    lpDescription = "Kitvei Hakodesh data service: seforim library, dictionary, full-text and " +
                                    "catalog search, HebrewBooks, and the NTFS file index. Started on demand.",
                };
                ChangeServiceConfig2(svc, SERVICE_CONFIG_DESCRIPTION, ref description);
                GrantStartToAuthenticatedUsers(svc);
            }
            finally { CloseServiceHandle(svc); }

            Console.WriteLine("Service installed: " + WindowsServiceMode.ServiceName);
            return 0;
        }
        finally { CloseServiceHandle(scm); }
    }

    public static int Uninstall()
    {
        IntPtr scm = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
        if (scm == IntPtr.Zero) return Fail("OpenSCManager", "Run as Administrator.");
        try
        {
            Stop(scm);
            WaitUntilStopped(scm, TimeSpan.FromSeconds(30));
            if (!DeleteByName(scm))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == ERROR_SERVICE_DOES_NOT_EXIST) { Console.WriteLine("Service not registered."); return 0; }
                return Fail("DeleteService", "error " + error);
            }
            Console.WriteLine("Service uninstalled: " + WindowsServiceMode.ServiceName);
            return 0;
        }
        finally { CloseServiceHandle(scm); }
    }

    private static IntPtr CreateKitveiHakodeshService(IntPtr scm, string exePath) =>
        CreateService(
            scm,
            WindowsServiceMode.ServiceName,
            WindowsServiceMode.DisplayName,
            SERVICE_ALL_ACCESS,
            SERVICE_WIN32_OWN_PROCESS,
            SERVICE_DEMAND_START,
            SERVICE_ERROR_NORMAL,
            "\"" + exePath + "\"",
            null, IntPtr.Zero, null,
            "LocalSystem",   // raw MFT / USN access for the file index
            null);

    private static void Stop(IntPtr scm)
    {
        IntPtr svc = OpenService(scm, WindowsServiceMode.ServiceName, SERVICE_STOP);
        if (svc == IntPtr.Zero) return;
        try
        {
            var status = new SERVICE_STATUS();
            ControlService(svc, SERVICE_CONTROL_STOP, ref status);
        }
        finally { CloseServiceHandle(svc); }
    }

    /// <summary>Polls until the service reports SERVICE_STOPPED (or is not registered), bounded.</summary>
    private static void WaitUntilStopped(IntPtr scm, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            IntPtr svc = OpenService(scm, WindowsServiceMode.ServiceName, SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero) return;
            try
            {
                var status = new SERVICE_STATUS();
                if (!QueryServiceStatus(svc, ref status) || status.dwCurrentState == SERVICE_STOPPED) return;
            }
            finally { CloseServiceHandle(svc); }
            Thread.Sleep(250);
        }
    }

    private static bool DeleteByName(IntPtr scm)
    {
        IntPtr svc = OpenService(scm, WindowsServiceMode.ServiceName, SERVICE_ALL_ACCESS);
        if (svc == IntPtr.Zero) return false;
        try { return DeleteService(svc); }
        finally { CloseServiceHandle(svc); }
    }

    /// <summary>SERVICE_START | SERVICE_QUERY_STATUS for Authenticated Users, so a non-admin
    /// host can start the service without a prompt.</summary>
    private static void GrantStartToAuthenticatedUsers(IntPtr svc)
    {
        QueryServiceObjectSecurity(svc, DACL_SECURITY_INFORMATION, null, 0, out uint needed);
        byte[] descriptorBytes = new byte[needed];
        if (!QueryServiceObjectSecurity(svc, DACL_SECURITY_INFORMATION, descriptorBytes, needed, out needed))
            throw new InvalidOperationException("QueryServiceObjectSecurity failed (error " + Marshal.GetLastWin32Error() + ").");

        var descriptor = new RawSecurityDescriptor(descriptorBytes, 0);
        RawAcl dacl = descriptor.DiscretionaryAcl ?? new RawAcl(RawAcl.AclRevision, 1);
        dacl.InsertAce(dacl.Count, new CommonAce(
            AceFlags.None, AceQualifier.AccessAllowed,
            (int)(SERVICE_START | SERVICE_QUERY_STATUS),
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            false, null));
        descriptor.DiscretionaryAcl = dacl;

        byte[] updated = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(updated, 0);
        if (!SetServiceObjectSecurity(svc, DACL_SECURITY_INFORMATION, updated))
            throw new InvalidOperationException("SetServiceObjectSecurity failed (error " + Marshal.GetLastWin32Error() + ").");
    }

    private static int Fail(string what, string detail)
    {
        Console.Error.WriteLine(what + " failed: " + detail);
        return 1;
    }

    // ── Win32 SCM ─────────────────────────────────────────────────────────────────

    private const uint SC_MANAGER_ALL_ACCESS = 0xF003F;
    private const uint SERVICE_ALL_ACCESS = 0xF01FF;
    private const uint SERVICE_START = 0x0010;
    private const uint SERVICE_STOP = 0x0020;
    private const uint SERVICE_QUERY_STATUS = 0x0004;
    private const uint SERVICE_WIN32_OWN_PROCESS = 0x00000010;
    private const uint SERVICE_DEMAND_START = 0x00000003;
    private const uint SERVICE_ERROR_NORMAL = 0x00000001;
    private const uint SERVICE_CONFIG_DESCRIPTION = 1;
    private const uint SERVICE_CONTROL_STOP = 0x00000001;
    private const uint SERVICE_STOPPED = 0x00000001;
    private const uint DACL_SECURITY_INFORMATION = 4;
    private const int ERROR_SERVICE_EXISTS = 1073;
    private const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType, dwCurrentState, dwControlsAccepted, dwWin32ExitCode,
                    dwServiceSpecificExitCode, dwCheckPoint, dwWaitHint;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ServiceDescription
    {
        public string lpDescription;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateService(
        IntPtr scm, string serviceName, string displayName, uint desiredAccess, uint serviceType,
        uint startType, uint errorControl, string binaryPathName, string? loadOrderGroup, IntPtr tagId,
        string? dependencies, string? serviceStartName, string? password);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr scm, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DeleteService(IntPtr service);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ChangeServiceConfig2(IntPtr service, uint infoLevel, ref ServiceDescription info);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool ControlService(IntPtr service, uint control, ref SERVICE_STATUS status);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatus(IntPtr service, ref SERVICE_STATUS status);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceObjectSecurity(
        IntPtr service, uint securityInformation, byte[]? descriptor, uint bufferSize, out uint bytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetServiceObjectSecurity(IntPtr service, uint securityInformation, byte[] descriptor);
}
