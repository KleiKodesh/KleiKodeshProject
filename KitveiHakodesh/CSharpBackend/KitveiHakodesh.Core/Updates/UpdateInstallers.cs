#nullable enable
using System;
using System.IO;
using Microsoft.Win32;

namespace KitveiHakodesh.Core.Updates
{
    /// <summary>
    /// What an update looks like on disk, and which version is installed: the one rule shared
    /// by everything that shows or offers an update (the service's status reply, the Word
    /// add-in's ribbon button). Nothing here is cached or kept in memory - a downloaded
    /// installer is a complete "KleiKodeshSetup-&lt;version&gt;[-variant].exe" in the updates
    /// folder (a partial one stays ".part"), and the installer stamps its version in the
    /// registry - so every reader gets the same answer, with or without the service running.
    /// </summary>
    public static class UpdateInstallers
    {
        /// <summary>The installer's registry key under the user's hive; its "Version" value is the installed version.</summary>
        public const string RegistryKeyPath = @"SOFTWARE\KleiKodesh";

        /// <summary>The installer's own stamp; null when this is not an installed copy.</summary>
#if NET
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
        public static string? InstalledVersion(RegistryKey hive)
        {
            try
            {
                using (RegistryKey? key = hive.OpenSubKey(RegistryKeyPath))
                    return key?.GetValue("Version")?.ToString();
            }
            catch { return null; }
        }

        /// <summary>
        /// The downloaded installer of the newest release that is newer than
        /// <paramref name="installedVersion"/>, or null (also null when nothing is installed
        /// or the folder does not exist).
        /// </summary>
        public static (string Path, string Version)? ReadyInstaller(string downloadFolder, string? installedVersion)
        {
            if (string.IsNullOrEmpty(installedVersion) || !Directory.Exists(downloadFolder)) return null;

            (string Path, string Version)? best = null;
            foreach (string file in Directory.EnumerateFiles(downloadFolder, "KleiKodeshSetup-*.exe"))
            {
                string tag = System.IO.Path.GetFileNameWithoutExtension(file).Substring("KleiKodeshSetup-".Length);
                int variantDash = tag.IndexOf('-');
                if (variantDash > 0) tag = tag.Substring(0, variantDash);
                if (CompareVersions(tag, installedVersion) <= 0) continue;
                if (best == null || CompareVersions(tag, best.Value.Version) > 0) best = (file, tag);
            }
            return best;
        }

        /// <summary>Compares two release tags ("v1.2.3" or "1.2.3.4"): more components is newer, then the numbers; unparsable tags compare as text.</summary>
        public static int CompareVersions(string? candidate, string? installed)
        {
            string left = candidate?.TrimStart('v') ?? "";
            string right = installed?.TrimStart('v') ?? "";
            if (Version.TryParse(left, out Version? leftVersion) && Version.TryParse(right, out Version? rightVersion))
            {
                int byComponentCount = ComponentCount(leftVersion).CompareTo(ComponentCount(rightVersion));
                return byComponentCount != 0 ? byComponentCount : leftVersion.CompareTo(rightVersion);
            }
            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static int ComponentCount(Version version) =>
            version.Revision >= 0 ? 4 : version.Build >= 0 ? 3 : 2;
    }
}
