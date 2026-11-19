using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodesh.Core.SeforimLibraryDownload
{
    /// <summary>
    /// Downloads the whole library and unpacks it to a given file: find the newest release (any
    /// schema, one file or split into parts), check free space, download every part
    /// (each resumable on its own), verify each part's sha256, unpack the parts as one stream
    /// beside the destination, rename. The caller picks the destination - seforim.db for a first
    /// install, a staged file when replacing a library that is open (<see cref="SeforimLibraryStagedDatabase"/>).
    /// </summary>
    public sealed class SeforimLibraryFullDownload
    {
        /// <summary>Unpacked size over compressed size (schema 6: 4.05 GB from 1.85 GB, measured
        /// 2026-10-26), with headroom for the library growing.</summary>
        internal const double UnpackedToCompressedRatio = 2.5;

        internal const long FreeSpaceMarginBytes = 512L * 1024 * 1024;

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
            var archive = await _feed.FindNewestDatabaseAsync(cancellationToken).ConfigureAwait(false);
            if (archive == null)
            {
                progress.Fail(SeforimLibraryDownloadFailure.NoCompatibleRelease, null);
                return false;
            }
            progress.SetReleaseTag(archive.ReleaseTag);

            Directory.CreateDirectory(SeforimLibraryDownloadLocations.Folder);
            DeleteStaleDownloads(archive);

            long onDisk = archive.Parts.Sum(p => LengthOf(SeforimLibraryDownloadLocations.DownloadPath(p.Name)));
            long? shortfall = FreeSpaceShortfall(archive.Size - onDisk, archive.Size);
            if (shortfall != null)
            {
                progress.Fail(SeforimLibraryDownloadFailure.NotEnoughDiskSpace, $"{shortfall.Value} more bytes needed");
                return false;
            }

            // Every part downloads before any is unpacked; progress counts all parts as one file.
            progress.Phase(SeforimLibraryDownloadPhase.Downloading, archive.Size);
            var report = progress.BytesOf(SeforimLibraryDownloadPhase.Downloading);
            long done = 0;
            foreach (var part in archive.Parts)
            {
                long before = done;
                await _download.DownloadAsync(archive.ReleaseTag, part,
                    SeforimLibraryDownloadLocations.DownloadPath(part.Name),
                    SeforimLibraryDownloadLocations.DownloadResumeRecordPath(part.Name),
                    (received, _) => report(before + received, archive.Size), cancellationToken).ConfigureAwait(false);
                done += part.Size;
            }

            // Each part's sha256 is checked as it is read while unpacking (one pass, not two).
            // A part that fails is deleted - corrupt bytes can never resume into a good file.
            var parts = archive.Parts
                .Select(p => new SeforimLibraryArchivePart(SeforimLibraryDownloadLocations.DownloadPath(p.Name), p.Sha256))
                .ToList();
            try
            {
                await UnpackAsync(parts, destination, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (SeforimLibraryPartChecksumException ex)
            {
                // Only the damaged part is fetched again; the good ones (up to 2 GB each) stay.
                var bad = archive.Parts.First(p => string.Equals(
                    SeforimLibraryDownloadLocations.DownloadPath(p.Name), ex.PartPath, StringComparison.OrdinalIgnoreCase));
                DeleteDownload(bad.Name);
                progress.Fail(SeforimLibraryDownloadFailure.ChecksumMismatch, ex.Message);
                return false;
            }

            foreach (var part in archive.Parts) DeleteDownload(part.Name);
            return true;
        }

        /// <summary>Unpacks <paramref name="parts"/> beside the library folder and renames the
        /// result to <paramref name="destination"/>. Shared by the download and the import.</summary>
        internal static async Task UnpackAsync(
            IReadOnlyList<SeforimLibraryArchivePart> parts, string destination,
            SeforimLibraryDownloadProgressReporter progress, CancellationToken cancellationToken)
        {
            string extracting = SeforimLibraryDownloadLocations.ExtractingPath;
            long total = parts.Sum(p => LengthOf(p.Path));
            progress.Phase(SeforimLibraryDownloadPhase.Extracting, total);
            try
            {
                await SeforimLibraryArchiveExtractor.ExtractAsync(parts, extracting,
                    progress.BytesOf(SeforimLibraryDownloadPhase.Extracting), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                SeforimLibraryDownloadFiles.TryDelete(extracting);
                throw;
            }
            File.Move(extracting, destination, overwrite: true);
        }

        /// <summary>Parts of another release (or the single-file name used before split
        /// libraries) can never resume into this one; they only take disk space.</summary>
        private static void DeleteStaleDownloads(SeforimLibraryDatabaseArchive archive)
        {
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in archive.Parts)
            {
                keep.Add(Path.GetFileName(SeforimLibraryDownloadLocations.DownloadPath(part.Name)));
                keep.Add(Path.GetFileName(SeforimLibraryDownloadLocations.DownloadResumeRecordPath(part.Name)));
            }
            foreach (string pattern in new[] { "seforim-schema*.part", "seforim-schema*.part.json", "seforim.db.zst.part", "seforim.db.zst.part.json" })
                foreach (string file in Directory.GetFiles(SeforimLibraryDownloadLocations.Folder, pattern))
                    if (!keep.Contains(Path.GetFileName(file)) && !Path.GetFileName(file).StartsWith("patch-", StringComparison.Ordinal))
                        SeforimLibraryDownloadFiles.TryDelete(file);
        }

        private static void DeleteDownload(string assetName)
        {
            SeforimLibraryDownloadFiles.TryDelete(SeforimLibraryDownloadLocations.DownloadPath(assetName));
            SeforimLibraryDownloadFiles.TryDelete(SeforimLibraryDownloadLocations.DownloadResumeRecordPath(assetName));
        }

        private static long LengthOf(string path)
        {
            var file = new FileInfo(path);
            return file.Exists ? file.Length : 0;
        }

        /// <summary>Bytes missing on the library's drive for <paramref name="toDownload"/> more
        /// bytes plus the unpacked database of a <paramref name="archiveSize"/>-byte archive, or
        /// null when there is room.</summary>
        internal static long? FreeSpaceShortfall(long toDownload, long archiveSize)
        {
            string? root = Path.GetPathRoot(SeforimLibraryDownloadLocations.Folder);
            if (string.IsNullOrEmpty(root)) return null;
            long available;
            try { available = new DriveInfo(root).AvailableFreeSpace; }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { return null; }

            long needed = Math.Max(0, toDownload) + (long)(archiveSize * UnpackedToCompressedRatio) + FreeSpaceMarginBytes;
            return available >= needed ? null : needed - available;
        }
    }
}
