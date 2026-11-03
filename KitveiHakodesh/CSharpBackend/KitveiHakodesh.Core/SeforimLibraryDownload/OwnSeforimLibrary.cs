using System;
using System.IO;
using KitveiHakodesh.Core.Settings;
using MessagePack;

namespace KitveiHakodesh.Core.SeforimLibraryDownload
{
    /// <summary>The library this app downloaded itself: whether it is on disk and how much room it takes.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class OwnSeforimLibraryInfo
    {
        /// <summary>Where it lives, whether or not it exists — the page compares this with the path being left.</summary>
        public string Path { get; set; } = "";

        public bool Exists { get; set; }

        /// <summary>The database plus every leftover of a download or update beside it.</summary>
        public long SizeBytes { get; set; }
    }

    /// <summary>
    /// Frees the disk space of the library this app downloaded, once the user has moved to
    /// Otzaria's or Zayit's copy (or any other file) and no longer needs it.
    ///
    /// Only OUR folder is ever touched. Refuses while that library is the one in use, because
    /// the service holds it open and deleting it would take the whole app down with it.
    /// </summary>
    public static class OwnSeforimLibrary
    {
        public static OwnSeforimLibraryInfo Describe()
        {
            string database = SeforimLibraryDownloadLocations.DatabasePath;
            return new OwnSeforimLibraryInfo
            {
                Path = database,
                Exists = File.Exists(database),
                SizeBytes = SizeOnDisk(),
            };
        }

        /// <summary>Deletes the library and its download leftovers. False when it is the library
        /// in use right now, or when a file could not be removed (still open); nothing is half-reported.</summary>
        public static bool TryDelete()
        {
            if (IsInUse()) return false;

            string folder = SeforimLibraryDownloadLocations.Folder;
            if (!Directory.Exists(folder)) return true;

            bool allDeleted = true;
            foreach (string file in Directory.GetFiles(folder))
            {
                try { File.Delete(file); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { allDeleted = false; }
            }
            return allDeleted;
        }

        private static bool IsInUse()
        {
            string? inUse = SeforimDbPathResolver.Resolve().Path;
            return inUse != null && string.Equals(
                Path.GetFullPath(inUse), Path.GetFullPath(SeforimLibraryDownloadLocations.DatabasePath), StringComparison.OrdinalIgnoreCase);
        }

        private static long SizeOnDisk()
        {
            string folder = SeforimLibraryDownloadLocations.Folder;
            if (!Directory.Exists(folder)) return 0;

            long total = 0;
            foreach (string file in Directory.GetFiles(folder))
            {
                try { total += new FileInfo(file).Length; }
                catch (IOException) { /* vanished while counting */ }
            }
            return total;
        }
    }
}
