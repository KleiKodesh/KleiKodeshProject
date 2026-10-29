using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodesh.Core.SeforimLibraryDownload
{
    /// <summary>
    /// Downloads the whole library and unpacks it to a given file: find the newest release,
    /// check free space, download (resumable), verify the sha256, unpack beside it, rename.
    /// The caller picks the destination — seforim.db for a first install, a staged file when
    /// replacing a library that is open (<see cref="SeforimLibraryStagedDatabase"/>).
    /// </summary>
    public sealed class SeforimLibraryFullDownload
    {
        /// <summary>Unpacked size over compressed size for the schema-6 library (4.05 GB from
        /// 1.85 GB, measured 2026-10-26), with headroom for the library growing.</summary>
        private const double UnpackedToCompressedRatio = 2.5;

        private const long FreeSpaceMarginBytes = 512L * 1024 * 1024;

        private readonly SeforimLibraryReleaseFeed _feed;
        private readonly SeforimLibraryResumableFileDownload _download;

        public SeforimLibraryFullDownload(HttpClient http)
        {
            _feed = new SeforimLibraryReleaseFeed(http);
            _download = new SeforimLibraryResumableFileDownload(http);
        }

        /// <summary>Downloads into <paramref name="destination"/>. Returns false after reporting
        /// a failure it can name; network and disk exceptions propagate to the caller.</summary>
        public async Task<bool> RunAsync(string destination, SeforimLibraryDownloadProgressReporter progress, CancellationToken cancellationToken)
        {
            progress.Phase(SeforimLibraryDownloadPhase.FindingRelease);
            var found = await _feed.FindNewestDatabaseAsync(cancellationToken).ConfigureAwait(false);
            if (found == null)
            {
                progress.Fail(SeforimLibraryDownloadFailure.NoCompatibleRelease, null);
                return false;
            }
            var (release, asset) = found.Value;
            progress.SetReleaseTag(release.Tag);

            Directory.CreateDirectory(SeforimLibraryDownloadLocations.Folder);
            string part = SeforimLibraryDownloadLocations.ArchivePartPath;
            string record = SeforimLibraryDownloadLocations.ResumeRecordPath;

            long? shortfall = FreeSpaceShortfall(asset.Size, part);
            if (shortfall != null)
            {
                progress.Fail(SeforimLibraryDownloadFailure.NotEnoughDiskSpace, $"{shortfall.Value} more bytes needed");
                return false;
            }

            progress.Phase(SeforimLibraryDownloadPhase.Downloading, asset.Size);
            await _download.DownloadAsync(release.Tag, asset, part, record,
                progress.BytesOf(SeforimLibraryDownloadPhase.Downloading), cancellationToken).ConfigureAwait(false);

            if (asset.Sha256 != null)
            {
                progress.Phase(SeforimLibraryDownloadPhase.Verifying, asset.Size);
                string actual = await SeforimLibraryFileChecksum.Sha256Async(part,
                    progress.BytesOf(SeforimLibraryDownloadPhase.Verifying), cancellationToken).ConfigureAwait(false);
                if (!string.Equals(actual, asset.Sha256, StringComparison.Ordinal))
                {
                    // Corrupt bytes can never resume into a good file.
                    File.Delete(part);
                    File.Delete(record);
                    progress.Fail(SeforimLibraryDownloadFailure.ChecksumMismatch, $"expected {asset.Sha256}, got {actual}");
                    return false;
                }
            }

            string extracting = SeforimLibraryDownloadLocations.ExtractingPath;
            progress.Phase(SeforimLibraryDownloadPhase.Extracting, asset.Size);
            try
            {
                await SeforimLibraryArchiveExtractor.ExtractAsync(part, extracting,
                    progress.BytesOf(SeforimLibraryDownloadPhase.Extracting), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                SeforimLibraryDownloadFiles.TryDelete(extracting);
                throw;
            }

            File.Move(extracting, destination, overwrite: true);
            SeforimLibraryDownloadFiles.TryDelete(part);
            SeforimLibraryDownloadFiles.TryDelete(record);
            return true;
        }

        /// <summary>Bytes missing on the library's drive for the rest of the download plus the
        /// unpacked database, or null when there is room.</summary>
        private static long? FreeSpaceShortfall(long archiveSize, string partPath)
        {
            string? root = Path.GetPathRoot(SeforimLibraryDownloadLocations.Folder);
            if (string.IsNullOrEmpty(root)) return null;
            long available;
            try { available = new DriveInfo(root).AvailableFreeSpace; }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { return null; }

            long alreadyHere = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
            long needed = (archiveSize - alreadyHere) + (long)(archiveSize * UnpackedToCompressedRatio) + FreeSpaceMarginBytes;
            return available >= needed ? null : needed - available;
        }
    }
}
