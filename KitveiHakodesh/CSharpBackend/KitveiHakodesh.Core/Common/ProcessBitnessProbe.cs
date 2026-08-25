using System;
using System.Diagnostics;

namespace KitveiHakodesh.Core.Common
{
    /// <summary>
    /// Is this process 32- or 64-bit, and which executable is it?
    ///
    /// Sounds trivial and is the first question worth asking whenever a native library fails
    /// to load: a VSTO add-in runs inside WINWORD.EXE and inherits ITS bitness, so the same
    /// build can be 32-bit in Word and 64-bit in the demo app.
    /// </summary>
    public static class ProcessBitnessProbe
    {
        public static bool Is64Bit => IntPtr.Size == 8;

        public static string Bitness => Is64Bit ? "64-bit" : "32-bit";

        /// <summary>
        /// The running executable's full path, or null when the OS will not say — a protected
        /// process, or a permission the host does not have. Null means unknown, not none.
        /// </summary>
        public static string? ExecutablePath
        {
            get
            {
                try
                {
                    using var process = Process.GetCurrentProcess();
                    return process.MainModule?.FileName;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }
    }
}
