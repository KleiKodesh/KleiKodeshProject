using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodesh.Core.SeforimLibraryDownload
{
    /// <summary>
    /// Installs a library the user downloaded by hand (from the website, or the release page):
    /// a seforim-schemaN.db.zst, or its parts .part-000, .part-001, ... The user picks ANY one of
    /// those files (or the parts' .manifest.json); the rest are found beside it. When the split
    /// manifest is there too, every part is checked against its size and sha256.
    ///
    /// The picked files are only read - the unpacked database goes to the app's own library
    /// folder, exactly like a download.
    /// </summary>
    public static class SeforimLibraryArchiveImport
    {
        private static readonly Regex PartName = new Regex(@"^(.+)\.part-(\d+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private const string ManifestSuffix = ".manifest.json";

        /// <summary>The parts to unpack, in order, for the picked <paramref name="pickedPath"/>.</summary>
        /// <exception cref="FileNotFoundException">The file, or a part the manifest names, is missing.</exception>
        /// <exception cref="InvalidDataException">Not a library archive, a gap in the parts, or a
        /// part that does not match the manifest's size.</exception>
        public static IReadOnlyList<SeforimLibraryArchivePart> ResolveParts(string pickedPath)
        {
            string full = Path.GetFullPath(pickedPath);
            if (!File.Exists(full)) throw new FileNotFoundException("no such file", full);
            string folder = Path.GetDirectoryName(full)!;
            string name = Path.GetFileName(full);

            string archiveName;
            if (name.EndsWith(ManifestSuffix, StringComparison.OrdinalIgnoreCase))
                archiveName = name.Substring(0, name.Length - ManifestSuffix.Length);
            else if (PartName.Match(name) is { Success: true } part)
                archiveName = part.Groups[1].Value;
            else if (name.EndsWith(".zst", StringComparison.OrdinalIgnoreCase))
                return new[] { new SeforimLibraryArchivePart(full, SingleFileSha256(full)) };
            else
                throw new InvalidDataException("not a library archive (.zst or .part-NNN)");

            string manifestPath = Path.Combine(folder, archiveName + ManifestSuffix);
            if (File.Exists(manifestPath))
            {
                var manifest = SeforimLibrarySplitManifest.Parse(File.ReadAllText(manifestPath), null)
                    ?? throw new InvalidDataException("the parts' manifest is not valid");
                var parts = new List<SeforimLibraryArchivePart>();
                foreach (var p in manifest.Parts)
                {
                    string path = Path.Combine(folder, p.Name);
                    if (!File.Exists(path)) throw new FileNotFoundException("a part of the library is missing", path);
                    long size = new FileInfo(path).Length;
                    if (size != p.Size) throw new SeforimLibraryArchiveTruncatedException($"{p.Name} is {size} bytes, the manifest says {p.Size} - incomplete download");
                    parts.Add(new SeforimLibraryArchivePart(path, p.Sha256));
                }
                return parts;
            }

            var found = Directory.GetFiles(folder)
                .Select(f => (Path: f, Match: PartName.Match(Path.GetFileName(f))))
                .Where(f => f.Match.Success && string.Equals(f.Match.Groups[1].Value, archiveName, StringComparison.OrdinalIgnoreCase))
                .Select(f => (f.Path, Index: int.Parse(f.Match.Groups[2].Value, CultureInfo.InvariantCulture)))
                .OrderBy(f => f.Index)
                .ToList();
            for (int i = 0; i < found.Count; i++)
                if (found[i].Index != i)
                    throw new FileNotFoundException($"part {i:000} of the library is missing", Path.Combine(folder, $"{archiveName}.part-{i:000}"));
            if (found.Count == 0) throw new InvalidDataException("no parts found");
            return found.Select(f => new SeforimLibraryArchivePart(f.Path, null)).ToList();
        }

        /// <summary>A single .zst is checked against the whole-archive sha256 of a manifest beside
        /// it (seforim-schemaN.db.zst.manifest.json), when there is one and its size matches.
        /// Without one there is nothing to check against: a damaged file then shows only as a
        /// zstd error or a truncated stream while unpacking.</summary>
        private static string? SingleFileSha256(string path)
        {
            string manifestPath = path + ManifestSuffix;
            if (!File.Exists(manifestPath)) return null;
            var manifest = SeforimLibrarySplitManifest.Parse(File.ReadAllText(manifestPath), null);
            if (manifest == null) throw new InvalidDataException("the archive's manifest is not valid");
            long size = new FileInfo(path).Length;
            if (size != manifest.Size) throw new SeforimLibraryArchiveTruncatedException($"{Path.GetFileName(path)} is {size} bytes, the manifest says {manifest.Size} - incomplete download");
            return manifest.Sha256;
        }

        /// <summary>Unpacks the picked archive into <paramref name="destination"/>. Returns false
        /// after reporting a failure it can name.</summary>
        internal static async Task<bool> RunAsync(string pickedPath, string destination,
            SeforimLibraryDownloadProgressReporter progress, CancellationToken cancellationToken)
        {
            IReadOnlyList<SeforimLibraryArchivePart> parts;
            try
            {
                parts = ResolveParts(pickedPath);
            }
            catch (FileNotFoundException ex)
            {
                progress.Fail(SeforimLibraryDownloadFailure.ImportIncomplete, ex.Message + ": " + ex.FileName);
                return false;
            }
            catch (SeforimLibraryArchiveTruncatedException ex)
            {
                progress.Fail(SeforimLibraryDownloadFailure.ImportIncomplete, ex.Message);
                return false;
            }
            catch (InvalidDataException ex)
            {
                progress.Fail(SeforimLibraryDownloadFailure.ImportNotALibrary, ex.Message);
                return false;
            }

            Directory.CreateDirectory(SeforimLibraryDownloadLocations.Folder);
            long size = parts.Sum(p => new FileInfo(p.Path).Length);
            long? shortfall = SeforimLibraryFullDownload.FreeSpaceShortfall(0, size);
            if (shortfall != null)
            {
                progress.Fail(SeforimLibraryDownloadFailure.NotEnoughDiskSpace, $"{shortfall.Value} more bytes needed");
                return false;
            }

            try
            {
                await SeforimLibraryFullDownload.UnpackAsync(parts, destination, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidDataException or SeforimLibraryArchiveException)
            {
                progress.Fail(ex switch
                {
                    SeforimLibraryPartChecksumException => SeforimLibraryDownloadFailure.ChecksumMismatch,
                    SeforimLibraryArchiveTruncatedException => SeforimLibraryDownloadFailure.ImportIncomplete,
                    _ => SeforimLibraryDownloadFailure.ImportNotALibrary,
                }, ex.Message);
                return false;
            }
            return true;
        }
    }
}
