using System.IO;
using KitveiHakodesh.Core.Common;

namespace KitveiHakodesh.Core.SeforimLibraryDownload
{
    /// <summary>
    /// Where a library the app downloads itself lives: AppRoot\books
    /// (<see cref="AppFileLocator.AppRoot"/>), so the host and the service see the same file.
    ///
    /// This is the THIRD default database location, after Otzaria's and Zayit's — see
    /// <see cref="Settings.SeforimDbPathResolver.DefaultLocations"/>. It sits inside the
    /// app's own folder, so a portable copy carries it and an uninstall takes it away.
    ///
    /// Every in-flight file sits beside the database, on the same drive, so promoting the
    /// finished file is a rename, never a 4 GB copy.
    /// </summary>
    public static class SeforimLibraryDownloadLocations
    {
        public static string Folder => Path.Combine(AppFileLocator.AppRoot, AppRootFolder.Books);

        /// <summary>The finished database.</summary>
        public static string DatabasePath => Path.Combine(Folder, "seforim.db");

        /// <summary>The database being unpacked. Renamed to <see cref="DatabasePath"/> once whole.</summary>
        public static string ExtractingPath => Path.Combine(Folder, "seforim.db.extracting");

        /// <summary>A full replacement for a library that is open, swapped in at the next start
        /// (<see cref="SeforimLibraryStagedDatabase"/>).</summary>
        public static string StagedPath => Path.Combine(Folder, "seforim.db.next");

        /// <summary>A release file being downloaded (a library part, an update patch), growing
        /// until complete. Kept across restarts; each part of a split library has its own.</summary>
        public static string DownloadPath(string assetName) => Path.Combine(Folder, assetName + ".part");

        /// <summary>What <see cref="DownloadPath"/> is a download of - read on restart to resume.</summary>
        public static string DownloadResumeRecordPath(string assetName) => Path.Combine(Folder, assetName + ".part.json");

        /// <summary>An update patch unpacked: itself a SQLite file, attached while applying.</summary>
        public static string PatchDatabasePath(string assetName) => Path.Combine(Folder, assetName + ".db");
    }
}
