using System;
using System.IO;

namespace KitveiHakodesh.Core.SeforimLibraryDownload
{
    /// <summary>
    /// A full replacement library waiting to be swapped in. A library that is open cannot be
    /// renamed over on Windows, so a full update downloads to <see cref="SeforimLibraryDownloadLocations.StagedPath"/>
    /// and the host swaps it in at its NEXT start, before anything opens the database.
    /// </summary>
    public static class SeforimLibraryStagedDatabase
    {
        public static bool Exists => File.Exists(SeforimLibraryDownloadLocations.StagedPath);

        /// <summary>
        /// Swaps a staged library into place. Call at host start-up, before the database is
        /// opened. Returns whether one was promoted; a library still locked (another host has it
        /// open) is left staged for the next start.
        /// </summary>
        public static bool PromoteIfStaged()
        {
            if (!Exists) return false;
            try
            {
                string database = SeforimLibraryDownloadLocations.DatabasePath;
                File.Move(SeforimLibraryDownloadLocations.StagedPath, database, overwrite: true);
                // SQLite sidecars of the old file describe pages that no longer exist.
                SeforimLibraryDownloadFiles.TryDelete(database + "-wal");
                SeforimLibraryDownloadFiles.TryDelete(database + "-shm");
                SeforimLibraryDownloadFiles.TryDelete(database + "-journal");
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
