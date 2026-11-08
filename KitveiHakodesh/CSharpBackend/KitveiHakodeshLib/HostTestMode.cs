using System;
using System.IO;

namespace KitveiHakodeshLib
{
    /// <summary>
    /// Opt-in switch for the hosted end-to-end rig (vue-frontend/e2e-hosted/). OFF unless the
    /// rig sets KH_TEST_CDP_PORT, so a normal launch never sees any of this.
    ///
    /// When on:
    ///   - the WebView2 browser listens for DevTools on 127.0.0.1:KH_TEST_CDP_PORT, so
    ///     Playwright can attach with connectOverCDP;
    ///   - the host keeps its per-user files (WebView2 user data, tabs session, splash
    ///     snapshot) under KH_TEST_PROFILE_DIR instead of the user's folders;
    ///   - the demo host skips its single-instance handoff, so a rig run never forwards to
    ///     (or takes requests from) the user's running app.
    /// The service pipe is isolated separately by KHS_PIPE_NAME (a private instance).
    /// </summary>
    public static class HostTestMode
    {
        public const string CdpPortEnvironmentVariable = "KH_TEST_CDP_PORT";
        public const string ProfileDirEnvironmentVariable = "KH_TEST_PROFILE_DIR";

        /// <summary>The DevTools port, or 0 when the rig did not ask for test mode.</summary>
        public static int CdpPort
        {
            get
            {
                string raw = Environment.GetEnvironmentVariable(CdpPortEnvironmentVariable);
                return int.TryParse(raw, out int port) && port > 0 && port < 65536 ? port : 0;
            }
        }

        public static bool IsOn => CdpPort != 0;

        /// <summary>
        /// The rig's own profile folder in test mode; null otherwise (callers keep their
        /// normal location). Test mode without a profile folder falls back to a temp one,
        /// so it can never write into the user's folders.
        /// </summary>
        public static string ProfileDir
        {
            get
            {
                if (!IsOn) return null;
                string dir = Environment.GetEnvironmentVariable(ProfileDirEnvironmentVariable);
                if (string.IsNullOrEmpty(dir))
                    dir = Path.Combine(Path.GetTempPath(), "kh-hosted-rig-" + CdpPort);
                Directory.CreateDirectory(dir);
                return dir;
            }
        }
    }
}
