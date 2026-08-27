using System;
using System.IO;
using System.Threading;
using FtsLib.Indexing;
using FtsLib.SeforimDb;
using KitveiHakodesh.Core.SeforimDb;

namespace KitveiHakodesh.Core.SeforimDbFullTextSearch
{
    /// <summary>
    /// FEEDS the full-text index and owns its build PROVENANCE — the proof of which seforim
    /// database, and which index format, the on-disk index came from. Searching is
    /// <see cref="SeforimDbFtsSearcher"/>'s job; the engine algorithms are FtsLib's.
    ///
    /// PROVENANCE IS PART OF BUILDING, which is why there is no separate stamps class. Two
    /// stamp files, written at two moments:
    ///
    ///   fts.src   at build START — so even an INTERRUPTED build records which database it was
    ///             reading. Without it, a database swapped under an incomplete build leaves a
    ///             resume watermark that silently skips every line below it in the NEW database
    ///             (the 2026-07-28 incident: an entire corpus section unsearchable, no error).
    ///   fts.ver   at build COMPLETION — the "this index is finished and belongs to this
    ///             database" claim later launches check before trusting the segments.
    ///
    /// Both hold <see cref="SeforimDbContentFingerprint"/> values prefixed with
    /// <see cref="IndexFormatVersion"/> — the CONTENT fingerprint, deliberately not the file
    /// one: rebuilding must follow changed ROWS, not journal churn (see that class).
    ///
    /// THE INDEX IS THE ONLY RESUME RECORD. This class keeps no counters, no state files of
    /// its own; the resume point is read back from the engine's progress bookkeeping, so there
    /// is no second copy of state to disagree with the segments.
    ///
    /// Threading, latches, status flags, progress streams and logging all belong to the
    /// orchestrator — this class does the work synchronously and reports through callbacks and
    /// return values.
    /// </summary>
    public sealed class SeforimDbFtsIndexer
    {
        /// <summary>
        /// Prefix for both stamps. BUMP THIS whenever the engine's on-disk segment format or
        /// the indexing pipeline changes what gets indexed — the stamp mismatch is what forces
        /// existing indexes to rebuild even though their source database is unchanged.
        /// </summary>
        // fts2 (2026-07-22): tokenizer change — inline tags (<b>, <small>, …) became
        // word-transparent and HTML entities word separators, so terms indexed under fts1
        // differ and a clean rebuild is required.
        public const string IndexFormatVersion = "fts2";

        private const string CompletedStampFileName = "fts.ver";
        private const string StartedStampFileName = "fts.src";
        private const string ResumeWatermarkFileName = "build.progress";
        private const string SegmentFilePattern = "seg_*.dat";

        /// <summary>Progress is reported every this-many lines — often enough for a live
        /// percentage, rare enough that the callback never becomes the build's cost.</summary>
        private const int ProgressReportInterval = 5000;

        /// <summary>
        /// A fast restart can begin the new build before the previous process has fully exited
        /// and released the OS write lock; the stale lock clears the moment that process dies.
        /// So: wait and retry briefly instead of abandoning a build that would have worked a
        /// second later.
        /// </summary>
        private const int WriteLockRetries = 8;
        private static readonly TimeSpan WriteLockRetryDelay = TimeSpan.FromMilliseconds(500);

        private readonly string _indexPath;
        private readonly string _seforimDbPath;

        public SeforimDbFtsIndexer(string indexPath, string seforimDbPath)
        {
            if (string.IsNullOrWhiteSpace(indexPath))
                throw new ArgumentException("indexPath is required", nameof(indexPath));
            if (string.IsNullOrWhiteSpace(seforimDbPath))
                throw new ArgumentException("seforimDbPath is required", nameof(seforimDbPath));

            _indexPath = indexPath;
            _seforimDbPath = seforimDbPath;
        }

        public string IndexPath => _indexPath;

        /// <summary>Whether any searchable segment exists yet — what "results before the build
        /// finishes" is gated on.</summary>
        public bool SegmentsExist()
        {
            try
            {
                return Directory.Exists(_indexPath)
                    && Directory.GetFiles(_indexPath, SegmentFilePattern).Length > 0;
            }
            catch (Exception)
            {
                return false;   // an unreadable directory has no searchable segments
            }
        }

        /// <summary>
        /// Validates the on-disk index against the CURRENT database and clears anything that
        /// cannot be trusted. Call BEFORE constructing the engine over this directory — never
        /// with one open, since clearing stale state deletes the files under it.
        ///
        /// The rules, in order:
        ///   • A recorded stamp in an older STAMP FORMAT cannot be compared at all. For a
        ///     COMPLETED build that is not evidence of a different database, so the index is
        ///     kept (it re-stamps on its next build). Anything RESUMABLE is still wiped —
        ///     resuming needs positive proof the watermark belongs to this database.
        ///     This covers only an unreadable format, never a bumped
        ///     <see cref="IndexFormatVersion"/>: a bump means the segment format changed and
        ///     MUST rebuild, so it falls through to the mismatch below.
        ///   • Any index state whose stamp does not match the current database — or that has
        ///     no stamp at all — is wiped. Resuming it would permanently skip every line below
        ///     a watermark that belongs to some other database.
        ///   • A completed build whose stamp matches is ready as it stands.
        ///
        /// A failed wipe THROWS rather than continuing: building over unverifiable state is
        /// exactly the poisoned-resume this method exists to prevent.
        /// </summary>
        public SeforimDbFtsIndexPlan Prepare()
        {
            var plan = new SeforimDbFtsIndexPlan();

            string completedStampPath = Path.Combine(_indexPath, CompletedStampFileName);
            string currentStamp = SeforimDbContentFingerprint.Compute(_seforimDbPath, IndexFormatVersion);
            string? recordedStamp = ReadStamp(completedStampPath)
                                 ?? ReadStamp(Path.Combine(_indexPath, StartedStampFileName));

            bool hasResumeWatermark = File.Exists(Path.Combine(_indexPath, ResumeWatermarkFileName));
            bool hasIndexState = SegmentsExist() || hasResumeWatermark;
            bool completedBuild = File.Exists(completedStampPath) && SegmentsExist() && !hasResumeWatermark;

            if (SeforimDbContentFingerprint.IsLegacy(recordedStamp) && completedBuild)
            {
                plan.KeptUnverifiableCompletedIndex = true;
                recordedStamp = currentStamp;   // unverifiable — but not evidence of a different DB
            }

            if (hasIndexState && !string.Equals(recordedStamp, currentStamp, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(_indexPath, recursive: true);
                plan.WipedStaleState = true;
            }

            plan.IsReady = File.Exists(completedStampPath) && SegmentsExist();
            return plan;
        }

        /// <summary>
        /// Builds — or resumes — the index, SYNCHRONOUSLY, until done or cancelled. The caller
        /// decides what thread this occupies.
        ///
        /// Resume comes from the engine's own progress bookkeeping; when a prior session was
        /// interrupted, this continues from its watermark without rescanning the database.
        /// <see cref="Prepare"/> must have validated that watermark's provenance first.
        ///
        /// Returns true when the build completed (and stamped fts.ver), false when the engine
        /// stopped early — cancellation surfaces as <see cref="OperationCanceledException"/>
        /// instead. <see cref="IndexWriteLockException"/> propagates once the retries are
        /// exhausted; whether to re-arm and try again later is host policy.
        /// </summary>
        /// <param name="index">The engine over <see cref="IndexPath"/>. Passed in, not owned:
        /// one engine instance serves both this build and concurrent searches, and its owner
        /// is the orchestrator.</param>
        /// <param name="onProgress">(linesIndexedSoFar, totalLines) — already resume-adjusted,
        /// reported every <see cref="ProgressReportInterval"/> lines.</param>
        /// <param name="onSegmentFlushed">A segment just reached disk — from the first of
        /// these on, the index answers searches.</param>
        public bool Build(
            SeforimIndex index,
            Action<long, long>? onProgress = null,
            Action? onSegmentFlushed = null,
            CancellationToken cancellationToken = default)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));

            Directory.CreateDirectory(_indexPath);

            // The at-START stamp — before any segment or watermark exists, so an interrupted
            // build is attributable to its database on the next launch. A resume session
            // rewrites the same value; Prepare already proved it matches.
            File.WriteAllText(
                Path.Combine(_indexPath, StartedStampFileName),
                SeforimDbContentFingerprint.Compute(_seforimDbPath, IndexFormatVersion));

            // Resume state lives in the engine; the total is cached there from the prior
            // session so resuming never rescans the corpus just to size the progress bar.
            index.GetResumeState(out _, out long cachedTotal, out long resumeOffset);
            long total = cachedTotal > 0 ? cachedTotal : index.CountLines();

            bool completed;
            for (int attempt = 0; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    completed = index.BuildIndex(
                        limit: 0,
                        onProgress: sessionCount =>
                        {
                            if (total > 0 && sessionCount % ProgressReportInterval == 0)
                                onProgress?.Invoke(resumeOffset + sessionCount, total);
                        },
                        onFlush: onSegmentFlushed,
                        totalLines: total,
                        resumeOffset: resumeOffset,
                        forceMergeOnComplete: true,
                        ct: cancellationToken,
                        // Lowest CPU + very-low I/O priority for the whole build, so it
                        // never contends with searches or the other index rebuilds that
                        // run alongside it. Same policy as both current hosts.
                        backgroundPriority: true);
                    break;
                }
                catch (IndexWriteLockException) when (attempt < WriteLockRetries)
                {
                    Thread.Sleep(WriteLockRetryDelay);
                }
            }

            if (completed)
            {
                // The at-COMPLETION stamp: from here on, launches trust these segments for
                // this database, and any later database change invalidates them.
                File.WriteAllText(
                    Path.Combine(_indexPath, CompletedStampFileName),
                    SeforimDbContentFingerprint.Compute(_seforimDbPath, IndexFormatVersion));

                // The watermark has served its purpose. If this delete fails the leftover is
                // benign — its stamp matches, so the next launch "resumes" past the last line
                // and finds nothing to do — which is why this is not worth failing a
                // successful build over.
                try { index.DeleteBuildProgressFile(); }
                catch (Exception) { /* see above */ }
            }

            return completed;
        }

        /// <summary>
        /// Whether the index no longer matches the database — the live-change check the DB
        /// watcher calls while the app runs. READ-ONLY and cheap (two file reads and a
        /// fingerprint); acting on a true answer — cancelling a build, wiping, rebuilding —
        /// is the orchestrator's move.
        ///
        /// Checks fts.ver first and falls back to fts.src, because a database swapped
        /// MID-BUILD must count as changed: the interrupted build's segments came from the old
        /// database and appending the new one's lines onto them would interleave two corpora.
        /// No stamp at all means nothing built yet — nothing to be stale. A legacy-format
        /// stamp is not comparable, and calling it "changed" would wipe a live index over a
        /// format change, so it reads as not-stale; the next build re-stamps it.
        /// </summary>
        public bool IsIndexStale()
        {
            string? builtFrom = ReadStamp(Path.Combine(_indexPath, CompletedStampFileName))
                             ?? ReadStamp(Path.Combine(_indexPath, StartedStampFileName));
            if (builtFrom == null) return false;
            if (SeforimDbContentFingerprint.IsLegacy(builtFrom)) return false;

            string current = SeforimDbContentFingerprint.Compute(_seforimDbPath, IndexFormatVersion);
            return !string.Equals(builtFrom, current, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Deletes the whole index — the reset feature. The caller must have stopped the build
        /// and released every engine instance over this directory first; files that are still
        /// open surface here as the exception they cause.
        /// </summary>
        public void Wipe()
        {
            if (Directory.Exists(_indexPath))
                Directory.Delete(_indexPath, recursive: true);
        }

        /// <summary>A stamp file's content, or null when absent or unreadable — for provenance,
        /// "cannot read the claim" and "no claim" both mean unverified.</summary>
        private static string? ReadStamp(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path).Trim() : null; }
            catch (Exception) { return null; }
        }
    }

    /// <summary>What <see cref="SeforimDbFtsIndexer.Prepare"/> found and did — returned as data
    /// so the orchestrator can log it; Core does not write the log line.</summary>
    public sealed class SeforimDbFtsIndexPlan
    {
        /// <summary>A completed, provenance-matching index exists — no build needed.</summary>
        public bool IsReady { get; set; }

        /// <summary>Stale or unverifiable index state was deleted; the next build starts clean.</summary>
        public bool WipedStaleState { get; set; }

        /// <summary>A completed index carried a stamp too old to verify and was kept anyway —
        /// worth a log line, since its provenance is trust rather than proof until it next
        /// rebuilds.</summary>
        public bool KeptUnverifiableCompletedIndex { get; set; }
    }
}
