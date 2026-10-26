using System.IO;
using KitveiHakodesh.Core.Common;

namespace KitveiHakodesh.Core.SeforimLibraryDownload
{
    /// <summary>
    /// Where a library the app downloads itself lives: %LocalAppData%\KleiKodesh\books.
    ///
    /// This is the THIRD default database location, after Otzaria's and Zayit's — see
    /// <see cref="Settings.SeforimDbPathResolver.DefaultLocations"/>. It sits under the
    /// installer's per-user root, so it needs no elevation and goes away with a full uninstall.
    ///
    /// Every in-flight file sits beside the database, on the same drive, so promoting the
    /// finished file is a rename, never a 4 GB copy.
    /// </summary>
    public static class SeforimLibraryDownloadLocations
    {
        public static string Folder => Path.Combine(AppFileLocator.UserInstallRoot, "books");

        /// <summary>The finished database.</summary>
        public static string DatabasePath => Path.Combine(Folder, "seforim.db");

        /// <summary>The compressed download, growing until complete. Kept across restarts.</summary>
        public static string ArchivePartPath => Path.Combine(Folder, "seforim.db.zst.part");

        /// <summary>What <see cref="ArchivePartPath"/> is a part of — read on restart to resume.</summary>
        public static string ResumeRecordPath => Path.Combine(Folder, "seforim.db.zst.part.json");

        /// <summary>The database being unpacked. Renamed to <see cref="DatabasePath"/> once whole.</summary>
        public static string ExtractingPath => Path.Combine(Folder, "seforim.db.extracting");

        /// <summary>A full replacement for a library that is open, swapped in at the next start
        /// (<see cref="SeforimLibraryStagedDatabase"/>).</summary>
        public static string StagedPath => Path.Combine(Folder, "seforim.db.next");

        /// <summary>A downloaded update patch, compressed, growing until complete.</summary>
        public static string PatchPartPath(string assetName) => Path.Combine(Folder, assetName + ".part");

        /// <summary>What <see cref="PatchPartPath"/> is a part of.</summary>
        public static string PatchResumeRecordPath(string assetName) => Path.Combine(Folder, assetName + ".part.json");

        /// <summary>An update patch unpacked: itself a SQLite file, attached while applying.</summary>
        public static string PatchDatabasePath(string assetName) => Path.Combine(Folder, assetName + ".db");
    }
}
