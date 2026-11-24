#nullable enable
using System;
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
        // Null off Windows (portable build): there is no registry; callers guard with
        // OperatingSystem.IsWindows() before touching Hive.
#if NET
        private static RegistryKey _hive = OperatingSystem.IsWindows() ? Registry.CurrentUser : null!;
#else
        private static RegistryKey _hive = Registry.CurrentUser; // net48 is Windows-only
#endif
        private static string _roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        private static string _localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        private static string _documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        /// <summary>The user's HKEY_CURRENT_USER.</summary>
#if NET
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
        public static RegistryKey Hive => _hive;

        /// <summary>The user's %AppData% (roaming).</summary>
        public static string RoamingAppData => _roamingAppData;

        /// <summary>The user's %LocalAppData%.</summary>
        public static string LocalAppData => _localAppData;

        /// <summary>The user's Documents folder (a redirected one included). Under the service this is the bound user's, never SYSTEM's.</summary>
        public static string Documents => _documents;

        /// <summary>True once <see cref="BindToUser"/> has pointed this at another user.</summary>
        public static bool IsBound { get; private set; }

        /// <summary>
        /// Points every per-user lookup at the user behind <paramref name="userToken"/>: their
        /// loaded registry hive (RegOpenCurrentUser while impersonating) and their known
        /// folders (SHGetKnownFolderPath with the token). The token must be an impersonation
        /// token of a logged-on user - the pipe client's, obtained under RunAsClient.
        /// </summary>
#if NET
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
        public static void BindToUser(SafeAccessTokenHandle userToken)
        {
            SafeRegistryHandle hiveHandle = System.Security.Principal.WindowsIdentity.RunImpersonated(userToken, () =>
            {
                int error = RegOpenCurrentUser(KeyAllAccess, out SafeRegistryHandle handle);
                if (error != 0) throw new System.ComponentModel.Win32Exception(error, "RegOpenCurrentUser failed");
                return handle;
            });

            _hive = RegistryKey.FromHandle(hiveHandle);
            _roamingAppData = KnownFolder(FolderIdRoamingAppData, userToken) ?? _roamingAppData;
            _localAppData = KnownFolder(FolderIdLocalAppData, userToken) ?? _localAppData;
            _documents = DocumentsOf(userToken) ?? _documents;
            IsBound = true;
        }

        /// <summary>The Documents folder of the user behind a token (null when Windows cannot say).</summary>
#if NET
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
        public static string? DocumentsOf(SafeAccessTokenHandle userToken) => KnownFolder(FolderIdDocuments, userToken);

        private const int KeyAllAccess = 0xF003F;
        private static readonly Guid FolderIdRoamingAppData = new("3EB685DB-65F9-4CF6-A03A-E3EF65729F3D");
        private static readonly Guid FolderIdLocalAppData = new("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");

        private static readonly Guid FolderIdDocuments = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7");

        private static string? KnownFolder(Guid folderId, SafeAccessTokenHandle token)
        {
            // The handle is held for the duration of the call, so a token disposed by another thread
            // cannot be used after free (DocumentsOf is public).
            bool added = false;
            try
            {
                token.DangerousAddRef(ref added);
                int hr = SHGetKnownFolderPath(ref folderId, 0, token.DangerousGetHandle(), out IntPtr pathPointer);
                if (hr != 0 || pathPointer == IntPtr.Zero) return null;
                try { return Marshal.PtrToStringUni(pathPointer); }
                finally { Marshal.FreeCoTaskMem(pathPointer); }
            }
            finally { if (added) token.DangerousRelease(); }
        }

        [DllImport("advapi32.dll", SetLastError = false)]
        private static extern int RegOpenCurrentUser(int samDesired, out SafeRegistryHandle phkResult);

        [DllImport("shell32.dll", SetLastError = false)]
        private static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
    }
}
