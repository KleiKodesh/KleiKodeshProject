using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodesh.Core.SeforimLibraryDownload
{
    /// <summary>How an update run ended.</summary>
    public enum SeforimLibraryUpdateOutcome
    {
        /// <summary>Already the newest release.</summary>
        UpToDate,

        /// <summary>Patched in place to the newest release.</summary>
        Patched,

        /// <summary>No patch route, or a patch failed: the caller downloads the whole library.</summary>
        NeedsFullDownload,

        /// <summary>The library has no version to update from; it is left alone.</summary>
        Unversioned,
    }

    /// <summary>
    /// Brings the library this app downloaded up to the newest release with update patches,
    /// the way Otzaria updates its own: plan the shortest chain, then per patch download
    /// (resumable), verify both sha256s, unpack, apply in one verified transaction.
    ///
    /// Any failure that means "patching cannot get there" — a malformed patch, a database not
    /// at the patch's starting version, a content-hash mismatch after applying — ends in
    /// <see cref="SeforimLibraryUpdateOutcome.NeedsFullDownload"/>; the database is unchanged,
    /// since every apply is all-or-nothing.
    /// </summary>
    public sealed class SeforimLibraryPatchUpdate
    {
        private readonly SeforimLibraryUpdatePlanner _planner;
        private readonly SeforimLibraryResumableFileDownload _download;

        public SeforimLibraryPatchUpdate(HttpClient http)
        {
            _planner = new SeforimLibraryUpdatePlanner(http);
            _download = new SeforimLibraryResumableFileDownload(http);
        }

        public async Task<SeforimLibraryUpdateOutcome> RunAsync(string dbPath, SeforimLibraryDownloadProgressReporter progress, CancellationToken cancellationToken)
        {
            var local = SeforimLibraryLocalVersion.Read(dbPath);
            if (local == null) return SeforimLibraryUpdateOutcome.Unversioned;

            progress.Phase(SeforimLibraryDownloadPhase.FindingRelease);
            var plan = await _planner.PlanAsync(local.Value.DbVersion, cancellationToken).ConfigureAwait(false);
            if (plan.IsUpToDate) return SeforimLibraryUpdateOutcome.UpToDate;
            if (plan.NeedsFullDownload) return SeforimLibraryUpdateOutcome.NeedsFullDownload;

            foreach (var step in plan.Patches)
            {
                progress.SetReleaseTag(step.ReleaseTag);
                if (!await ApplyStepAsync(dbPath, step, progress, cancellationToken).ConfigureAwait(false))
                    return SeforimLibraryUpdateOutcome.NeedsFullDownload;
            }
            return SeforimLibraryUpdateOutcome.Patched;
        }

        private async Task<bool> ApplyStepAsync(string dbPath, SeforimLibraryPatchStep step, SeforimLibraryDownloadProgressReporter progress, CancellationToken cancellationToken)
        {
            var file = step.Manifest.PatchFiles[0];
            var asset = step.Patch with { Sha256 = file.Sha256 };
            string part = SeforimLibraryDownloadLocations.PatchPartPath(asset.Name);
            string record = SeforimLibraryDownloadLocations.PatchResumeRecordPath(asset.Name);
            string patchDb = SeforimLibraryDownloadLocations.PatchDatabasePath(asset.Name);
            // True once this patch has a definite answer — applied, or never will be. Until
            // then (offline, cancelled, host stopping) its downloaded bytes stay for a resume.
            bool settled = false;
            try
            {
                progress.Phase(SeforimLibraryDownloadPhase.Downloading, asset.Size);
                await _download.DownloadAsync(step.ReleaseTag, asset, part, record,
                    progress.BytesOf(SeforimLibraryDownloadPhase.Downloading), cancellationToken).ConfigureAwait(false);

                progress.Phase(SeforimLibraryDownloadPhase.Verifying, asset.Size);
                string compressed = await SeforimLibraryFileChecksum.Sha256Async(part,
                    progress.BytesOf(SeforimLibraryDownloadPhase.Verifying), cancellationToken).ConfigureAwait(false);
                if (!string.Equals(compressed, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    settled = true;
                    return false;
                }

                progress.Phase(SeforimLibraryDownloadPhase.Extracting, asset.Size);
                await SeforimLibraryArchiveExtractor.ExtractAsync(part, patchDb,
                    progress.BytesOf(SeforimLibraryDownloadPhase.Extracting), cancellationToken).ConfigureAwait(false);
                string uncompressed = await SeforimLibraryFileChecksum.Sha256Async(patchDb, (_, _) => { }, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(uncompressed, file.UncompressedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    settled = true;
                    return false;
                }

                progress.Phase(SeforimLibraryDownloadPhase.Patching);
                await Task.Run(() => SeforimLibraryPatchApplier.Apply(dbPath, patchDb, step.Manifest), cancellationToken).ConfigureAwait(false);
                settled = true;
                return true;
            }
            catch (InvalidDataException)
            {
                // The patch cannot apply to this library — permanent. A cancel, host stop or
                // transient disk/SQLite error is not, and keeps the verified download.
                settled = true;
                return false;
            }
            finally
            {
                if (settled)
                {
                    SeforimLibraryDownloadFiles.TryDelete(part);
                    SeforimLibraryDownloadFiles.TryDelete(record);
                }
                SeforimLibraryDownloadFiles.TryDelete(patchDb);
            }
        }
    }
}
