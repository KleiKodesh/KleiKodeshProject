using System;
using System.IO;
using System.Runtime.InteropServices;

namespace KitveiHakodeshLib.Helpers
{
    /// <summary>
    /// Explicitly pre-loads SQLite.Interop.dll from the correct x64 or x86 subfolder
    /// relative to the install directory (AppDomain.CurrentDomain.BaseDirectory).
    ///
    /// Why this is necessary:
    ///   System.Data.SQLite probes for SQLite.Interop.dll using the location of the
    ///   executing assembly. Under VSTO, assemblies are shadow-copied to a temp directory
    ///   before loading — so the probe path becomes %TEMP%\VSTO\...\x64\SQLite.Interop.dll,
    ///   which does not exist. SQLite then falls back to the system PATH, where it may
    ///   find a stale or wrong-bitness copy, producing:
    ///     "Unable to find an entry point named 'sqlite3_open_interop' in DLL 'SQLite.Interop.dll'"
    ///
    ///   By calling LoadLibraryW with the absolute path before any SQLiteConnection is
    ///   opened, we pin the correct DLL into the process. System.Data.SQLite's subsequent
    ///   LoadLibrary call finds it already loaded under the same name and reuses it.
    ///
    /// Call <see cref="EnsureLoaded"/> once at application startup, before any SQLite usage.
    /// Safe to call multiple times — idempotent.
    /// </summary>
    public static class SqliteNativeLoader
    {
        private static bool _loaded;
        private static readonly object _lock = new object();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string lpFileName);

        /// <summary>
        /// Loads SQLite.Interop.dll from the correct arch subfolder under
        /// <paramref name="installDir"/> (or AppDomain.CurrentDomain.BaseDirectory if null).
        /// Must be called before the first <c>SQLiteConnection</c> is opened.
        /// </summary>
        /// <param name="installDir">
        /// Absolute path to the directory that contains the x64\ and x86\ subfolders.
        /// Pass null to use AppDomain.CurrentDomain.BaseDirectory.
        /// </param>
        public static void EnsureLoaded(string installDir = null)
        {
            if (_loaded) return;
            lock (_lock)
            {
                if (_loaded) return;
                _DoLoad(installDir ?? AppDomain.CurrentDomain.BaseDirectory);
                _loaded = true;
            }
        }

        private static void _DoLoad(string baseDir)
        {
            // Pick the subfolder matching the current process bitness.
            string arch = IntPtr.Size == 8 ? "x64" : "x86";
            string path = Path.Combine(baseDir, arch, "SQLite.Interop.dll");

            if (!File.Exists(path))
            {
                // Log but don't throw — the error will surface naturally when SQLite
                // tries to open a connection, with a clearer message.
                System.Diagnostics.Debug.WriteLine(
                    $"[SqliteNativeLoader] SQLite.Interop.dll not found at: {path}");
                return;
            }

            IntPtr handle = LoadLibraryW(path);
            if (handle == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                System.Diagnostics.Debug.WriteLine(
                    $"[SqliteNativeLoader] LoadLibraryW failed for '{path}', Win32 error: {err}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SqliteNativeLoader] Loaded '{path}' (handle=0x{handle:X})");
            }
        }
    }
}
