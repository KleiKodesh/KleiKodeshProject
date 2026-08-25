using System;
using System.Runtime.InteropServices;

namespace KitveiHakodesh.Core.Common
{
    /// <summary>
    /// Which runtime is executing, and from where?
    ///
    /// Both legs of Core answer this, and they answer it differently — that is the point. A
    /// support question of the form "which one of these is running?" is settled here rather
    /// than inferred from behaviour.
    /// </summary>
    public static class DotNetRuntimeProbe
    {
        /// <summary>On .NET Framework this is the CLR version (4.0.30319-era); on the modern
        /// runtime it is the actual .NET version. Useful precisely because it differs.</summary>
        public static string Version => Environment.Version.ToString();

        /// <summary>The framework description, e.g. ".NET Framework 4.8.xxxx" or ".NET 10.x" —
        /// the one string that names the runtime unambiguously.</summary>
        public static string Description => RuntimeInformation.FrameworkDescription;

        /// <summary>Where the runtime itself was loaded from, or null if it will not say.</summary>
        public static string? RuntimeDirectory
        {
            get
            {
                try { return RuntimeEnvironment.GetRuntimeDirectory(); }
                catch (Exception) { return null; }
            }
        }
    }
}
