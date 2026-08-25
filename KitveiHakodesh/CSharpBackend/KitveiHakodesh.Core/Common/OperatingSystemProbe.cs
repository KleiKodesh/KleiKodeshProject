using System;

namespace KitveiHakodesh.Core.Common
{
    /// <summary>
    /// Which Windows is this, and is it 64-bit?
    ///
    /// Separate from <see cref="ProcessBitnessProbe"/> on purpose: a 32-bit process on 64-bit
    /// Windows is the common case, and conflating the two hides exactly the mismatch these
    /// probes exist to expose.
    /// </summary>
    public static class OperatingSystemProbe
    {
        public static bool Is64Bit => Environment.Is64BitOperatingSystem;

        public static string Bitness => Is64Bit ? "64-bit" : "32-bit";

        public static string Version => Environment.OSVersion.VersionString;

        public static string Platform => Environment.OSVersion.Platform.ToString();
    }
}
