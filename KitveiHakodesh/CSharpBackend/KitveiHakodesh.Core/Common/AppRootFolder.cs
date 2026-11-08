#nullable disable
using System;
using System.IO;

namespace KitveiHakodesh.Core.Common
{
    /// <summary>
    /// THE APP ROOT: the one folder every file the app writes lives under. The app never saves
    /// files outside it (a portable copy must carry all its data with it). Each kind of file has
    /// its own subfolder, named by the constants below.
    ///
    /// The host (DemoApp / KitveiHakodeshLib, net48, at the top of the app folder) and the service
    /// (the KitveiHakodeshService\ subfolder, AOT net10) run from different exe folders, so the
    /// rule is "the folder that HOLDS KitveiHakodeshService\". In order:
    ///   1. KHS_APP_ROOT, when set (dev rig, tests): taken as is.
    ///   2. Start = KHS_DATA_DIR or the exe folder. Inside a source checkout (an ancestor
    ///      named CSharpBackend): CSharpBackend\bin\AppRoot - gitignored, shared by the dev host
    ///      (DemoApp bin), the dev service (its KHS_DATA_DIR points into the service's bin) and a
    ///      service the dev host spawned from its own bin. Checked first so all of them agree.
    ///   3. Start named KitveiHakodeshService: its parent (the service, installed or portable).
    ///   4. Start holds a KitveiHakodeshService\ folder: start (the host, installed or portable).
    ///   5. %LocalAppData%\KleiKodesh holds KitveiHakodeshService\: that folder (the Word add-in,
    ///      whose exe folder is Office's or a VSTO shadow copy - the installed app is its root).
    ///   6. Otherwise start.
    ///
    /// Dependency-free on purpose: compiled into Core, KitveiHakodeshLib, DocConvertLib and FtsLib
    /// (linked source, internal in each), so all of them answer the same way.
    /// </summary>
    internal static class AppRootFolder
    {
        public const string EnvironmentVariable = "KHS_APP_ROOT";
        public const string ServiceFolderName = "KitveiHakodeshService";

        /// <summary>The library the app downloads itself (seforim.db and its in-flight files).</summary>
        public const string Books = "books";
        /// <summary>The startup-page snapshot shown while the window loads. Not "KitveiHakodesh":
        /// the dev build mirrors the Vue dist over that folder and deletes anything extra.</summary>
        public const string PageSnapshots = "page-snapshots";
        /// <summary>Books exported to Word (one folder per export, old ones purged).</summary>
        public const string WordExports = "word-exports";
        /// <summary>Log files.</summary>
        public const string Logs = "logs";

        private static string _path;

        public static string Path => _path ?? (_path = Resolve());

        /// <summary>A subfolder of the app root, created when missing.</summary>
        public static string Subfolder(string name)
        {
            string dir = System.IO.Path.Combine(Path, name);
            try { Directory.CreateDirectory(dir); } catch { /* the write itself will report it */ }
            return dir;
        }

        private static string Resolve()
        {
            string env = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(env)) return Full(env);

            string dataDir = Environment.GetEnvironmentVariable("KHS_DATA_DIR");
            string start = Full(string.IsNullOrWhiteSpace(dataDir) ? AppDomain.CurrentDomain.BaseDirectory : dataDir);

            var startInfo = new DirectoryInfo(start);
            for (DirectoryInfo d = startInfo; d != null; d = d.Parent)
                if (d.Name.Equals("CSharpBackend", StringComparison.OrdinalIgnoreCase))
                    return System.IO.Path.Combine(d.FullName, "bin", "AppRoot");

            if (startInfo.Name.Equals(ServiceFolderName, StringComparison.OrdinalIgnoreCase) && startInfo.Parent != null)
                return startInfo.Parent.FullName;

            if (HasServiceFolder(start)) return start;

            try
            {
                string installed = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KleiKodesh");
                if (HasServiceFolder(installed)) return installed;
            }
            catch { }

            return start;
        }

        private static bool HasServiceFolder(string dir)
        {
            try { return Directory.Exists(System.IO.Path.Combine(dir, ServiceFolderName)); }
            catch { return false; }
        }

        private static string Full(string path)
        {
            string full = System.IO.Path.GetFullPath(path);
            string root = System.IO.Path.GetPathRoot(full) ?? "";
            return full.Length > root.Length ? full.TrimEnd('\\', '/') : full;
        }
    }
}
