using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace KitveiHakodesh.Core.Settings
{
    /// <summary>
    /// WHOSE profile the settings code reads: the registry hive and the AppData folders every
    /// per-user lookup in Core goes through (AppSettingsRegistry, SeforimDbPathResolver,
    /// RegistryValueWatcher, AppFileLocator).
    ///
    /// By default that is the process's own user, exactly as Registry.CurrentUser and
    /// Environment.GetFolderPath would say. When KitveiHakodeshService runs as a Windows
    /// service it is SYSTEM, whose hive holds no seforim DB path and whose AppData has no
    /// library; the service therefore binds this to the user who connects to its pipe
    /// (<see cref="BindToUser"/>) before any of that code runs. One binding per process:
    /// the service is a per-user install serving that user.
    /// </summary>
    public static class UserProfile
    {
        private static RegistryKey _hive = Registry.CurrentUser;
        private static string _roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        private static string _localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        /// <summary>The user's HKEY_CURRENT_USER.</summary>
        public static RegistryKey Hive => _hive;

        /// <summary>The user's %AppData% (roaming).</summary>
        public static string RoamingAppData => _roamingAppData;

        /// <summary>The user's %LocalAppData%.</summary>
        public static string LocalAppData => _localAppData;

        /// <summary>True once <see cref="BindToUser"/> has pointed this at another user.</summary>
        public static bool IsBound { get; private set; }

        /// <summary>
        /// Points every per-user lookup at the user behind <paramref name="userToken"/>: their
        /// loaded registry hive (RegOpenCurrentUser while impersonating) and their known
        /// folders (SHGetKnownFolderPath with the token). The token must be an impersonation
        /// token of a logged-on user - the pipe client's, obtained under RunAsClient.
        /// </summary>
        public static void BindToUser(SafeAccessTokenHandle userToken)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();

            SafeRegistryHandle hiveHandle = System.Security.Principal.WindowsIdentity.RunImpersonated(userToken, () =>
            {
                int error = RegOpenCurrentUser(KeyAllAccess, out SafeRegistryHandle handle);
                if (error != 0) throw new System.ComponentModel.Win32Exception(error, "RegOpenCurrentUser failed");
                return handle;
            });

            _hive = RegistryKey.FromHandle(hiveHandle);
            _roamingAppData = KnownFolder(FolderIdRoamingAppData, userToken) ?? _roamingAppData;
            _localAppData = KnownFolder(FolderIdLocalAppData, userToken) ?? _localAppData;
            IsBound = true;
        }

        private const int KeyAllAccess = 0xF003F;
        private static readonly Guid FolderIdRoamingAppData = new("3EB685DB-65F9-4CF6-A03A-E3EF65729F3D");
        private static readonly Guid FolderIdLocalAppData = new("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");

        private static string? KnownFolder(Guid folderId, SafeAccessTokenHandle token)
        {
            int hr = SHGetKnownFolderPath(ref folderId, 0, token.DangerousGetHandle(), out IntPtr pathPointer);
            if (hr != 0 || pathPointer == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(pathPointer); }
            finally { Marshal.FreeCoTaskMem(pathPointer); }
        }

        [DllImport("advapi32.dll", SetLastError = false)]
        private static extern int RegOpenCurrentUser(int samDesired, out SafeRegistryHandle phkResult);

        [DllImport("shell32.dll", SetLastError = false)]
        private static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
    }
}
