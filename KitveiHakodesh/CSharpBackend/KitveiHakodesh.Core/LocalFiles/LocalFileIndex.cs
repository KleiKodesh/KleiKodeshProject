// Written before nullable annotations (ported from the DocumentLocator library, which built
// with these warnings suppressed); not annotated yet.
#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using KitveiHakodesh.Core.SeforimDbCatalog;

namespace KitveiHakodesh.Core.LocalFiles
{
    /// <summary>
    /// On-disk Lucene index over full file paths from the NTFS MFT.
    ///
    /// Each document stores a single field: the full path lowercased.
    ///
    /// Searching splits the query on whitespace and requires every word to appear
    /// somewhere in the path as a substring — equivalent to:
    ///   paths.Where(p => p.Contains(word1) && p.Contains(word2) && ...)
    /// Each word is looked up by its trigrams (path_trigrams), and candidates are re-checked
    /// against the path from doc-values, so mid-word matches are included without a term scan.
    /// </summary>
    public sealed class LocalFileIndex : IDisposable
    {
        private const LuceneVersion VERSION        = LuceneVersion.LUCENE_48;
        private const string        FLD_PATH      = "path";
        private const string        FLD_DATE      = "date";       // last-write time as Unix ms (stored, not indexed)
        private const string        FLD_ALIAS_TERMS = "alias_terms"; // indexed only: extra search words of a path (Torat Emet Hebrew names), one term each, lower-cased
        private const string        FLD_ADDIN_NAME = "addin_name"; // optional: "תוסף אוצריא: {name}" (indexed + stored, lower-cased)
        private const string        FLD_ADDIN_NAME_DISPLAY = "addin_name_display"; // same value, original case, stored only

        // Ordering fields for the folder-scoped (UserSpace) search - see SearchUnderFolders.
        // Indexed only, never stored; written by MakeDocument, so every writer carries them.
        private const string FLD_NAME_WORDS   = "name_words";    // file-name words (LocalFileNameWords), one term each, DOCS_ONLY
        private const string FLD_FOLDER_WORDS = "folder_words";  // every folder name's words on the path, one term each
        private const string FLD_NAME_SHAPE_DV = "name_shape_dv"; // NumericDocValues: name word count | distinct count << 16
        private const string FLD_PATH_SORT_DV = "path_sort_dv";  // SortedDocValues: the path UPPER-cased (see PathSortKey)
        private const string FLD_DATE_DV      = "date_dv";       // NumericDocValues: FLD_DATE as a number
        private const string FLD_PATH_TRIGRAMS = "path_trigrams"; // every distinct 3-char piece of the lower-cased path, one term each (see PathTrigrams)
        private const string FLD_NAME_SEQ     = "name_seq";       // the file-name words in order, WITH frequencies and positions (repeats, typed order, proximity)
        private const string FLD_QUOTED_KEYS  = "quoted_keys";    // every quoted acronym key the file name carries, one term each (QuotedFormMatch)
        private const long   ShapeStoredRead  = 1L << 32;        // name_shape_dv flag: window reads stored fields

        /// <summary>
        /// The on-disk document format. A different (or missing) stamp in the index folder makes
        /// the constructor start the index over and drop the USN cursors, so the next Build
        /// re-crawls every drive and every document gets the current fields. Bump it whenever
        /// MakeDocument changes what it writes.
        /// v2: name_words / folder_words / name_shape_dv / path_sort_dv / date_dv (Lucene-side
        /// ordering and a doc-values window).
        /// v3: alias_terms (extra search terms: the Hebrew names of Torat Emet files, see ToratEmetNames).
        /// v4: no new fields - more skipped folders (browser storage, dev caches); the re-crawl drops
        /// what the older index still held from them.
        /// v5: path_trigrams (substring search by exact term lookups instead of a *word* scan);
        /// name_seq and quoted_keys, so every ranking key is index data (no re-rank on paths).
        /// </summary>
        internal const string IndexFormatVersion = "5";
        // No "_" in the name: Lucene.Net's writer deletes, as a stale index file, any file whose
        // name CONTAINS "_x.y" (its codec-file pattern is not anchored) - "index_format.txt" went.
        private const string IndexFormatFileName = "format-version.txt";

        /// <summary>
        /// How many folder-scoped hits are MATERIALIZED (stored fields read). A PERFORMANCE
        /// bound and nothing more: matching and ordering run over EVERY match and the total is
        /// the full count; only the ordered top this-many become entries. Same idea and size as
        /// the catalog searcher's MaterializeCap.
        /// </summary>
        internal const int MaterializeCap = 1000;

        /// <summary>
        /// If the USN journal delta for a drive contains more than this fraction
        /// of the current indexed document count, skip incremental update and do
        /// a full MFT rebuild instead — it's cheaper at that point.
        /// 0.20 = 20% of indexed paths changed → full rebuild.
        /// </summary>
        private const double RebuildThresholdFraction = 0.20;

        /// <summary>
        /// Default index location: a "filesystemindex" subfolder of the data root (beside the exe,
        /// or KHS_DATA_DIR — see <see cref="Common.AppFileLocator.DataRoot"/>).
        ///
        /// This is typically the folder where the executing assembly (service exe or library caller)
        /// lives. When the service is deployed alongside the demo, both see the same path.
        /// </summary>
        public static readonly string DefaultIndexPath =
            System.IO.Path.Combine(
                Common.AppFileLocator.DataRoot,
                // the hosted rig's test index (KHS_TEST_INDEX_FOLDER) lives apart: it must never touch a real index
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KHS_TEST_INDEX_FOLDER")) ? "filesystemindex" : "filesystemindex-rig-test");

        private readonly string          _indexPath;
        private readonly FSDirectory     _directory;
        private readonly IndexWriter     _writer;
        private          SearcherManager _searcherManager;

        // ── Live update threads ───────────────────────────────────────────────────
        // One thread per NTFS drive, each blocking on FSCTL_READ_USN_JOURNAL with
        // BytesToWaitFor=1. The kernel parks the thread in kernel-wait (zero CPU,
        // negligible memory) until a new USN record arrives, then the thread wakes,
        // applies the delta to the index, and blocks again. CancellationToken
        // cancellation closes the volume handle, which unblocks the DeviceIoControl
        // immediately and lets the thread exit cleanly.
        private readonly List<Thread> _liveThreads = new List<Thread>();
        private volatile bool _liveUpdatesStopped;

        // Cancelled by Dispose: closes every live thread's volume handle (unblocking its
        // kernel wait) and wakes the rescan loop, so a disposed index leaves no thread
        // behind. Linked with the caller's token in StartLiveUpdates/StartPeriodicRescan.
        private readonly CancellationTokenSource _stopCts = new CancellationTokenSource();

        // Number of background writers (a live USN delta or a periodic rescan) in flight.
        // Read by IsWriting so an idle release never closes the index under them.
        private int _backgroundWrites;

        /// <summary>True while a live USN delta or a periodic rescan is writing the index.</summary>
        public bool IsWriting => Volatile.Read(ref _backgroundWrites) > 0;

        // True while Build runs: searches keep the snapshot from before the build (see Build).
        private volatile bool _searchViewFrozen;

        /// <summary>Refreshes the search view to the latest writes, unless a build is running.</summary>
        private void RefreshSearchView()
        {
            if (!_searchViewFrozen) _searcherManager.MaybeRefresh();
        }

        // Drives indexed WITHOUT a live USN watcher (volume handle refused — not elevated).
        // Guarded by the _liveThreads lock, which already serialises live-update state.
        private readonly HashSet<char> _unwatchedDrives = new HashSet<char>();

        // Folder watchers for the unwatched drives (see LocalFileIndexFolderWatcher).
        // Guarded by the _liveThreads lock; disposed with the index.
        private readonly List<LocalFileIndexFolderWatcher> _folderWatchers = new List<LocalFileIndexFolderWatcher>();

        // Serialises whole-drive rebuilds after startup (periodic rescan, watcher overflow).
        // RebuildDrive deletes then ADDS, so two interleaved rebuilds would index every file twice.
        private readonly object _rebuildLock = new object();

        // Periodic-rescan state (the unelevated staleness mitigation). Guarded by _rescanLock.
        private readonly object _rescanLock = new object();
        private Thread _rescanThread;
        private DateTime _lastRescanUtc = DateTime.MinValue;

        // ── Construction ──────────────────────────────────────────────────────────

        public LocalFileIndex(string indexPath)
        {
            _indexPath = indexPath;
            System.IO.Directory.CreateDirectory(indexPath);
            _directory = FSDirectory.Open(new DirectoryInfo(indexPath));

            // An index written by another document format starts over: CREATE empties it, and
            // without the USN cursors Build crawls every drive in full (see IndexFormatVersion).
            string formatFile = System.IO.Path.Combine(indexPath, IndexFormatFileName);
            bool formatCurrent;
            try { formatCurrent = File.Exists(formatFile) && File.ReadAllText(formatFile).Trim() == IndexFormatVersion; }
            catch { formatCurrent = false; }
            if (!formatCurrent)
            {
                foreach (var cursor in System.IO.Directory.GetFiles(indexPath, "usn_cursor_*.bin"))
                    File.Delete(cursor);
            }

            // PathAnalyzer — each path is one token, stored exactly as-is.
            // Searching uses WildcardQuery so no tokenization is needed at query time.
            var config = new IndexWriterConfig(VERSION, new PathAnalyzer())
            {
                OpenMode = formatCurrent ? OpenMode.CREATE_OR_APPEND : OpenMode.CREATE
            };

            _writer          = new IndexWriter(_directory, config);
            if (!formatCurrent)
            {
                // Stamped once the emptied index is committed. A build interrupted after this
                // point is safe: drives without a saved cursor are re-crawled in full next time.
                _writer.Commit();
                File.WriteAllText(formatFile, IndexFormatVersion);
            }
            _searcherManager = new SearcherManager(_writer, applyAllDeletes: true, searcherFactory: null);
        }

        // ── Build / Update ────────────────────────────────────────────────────────

        /// <summary>KHS_TEST_INDEX_FOLDER: the one folder the hosted rig indexes instead of the drives (null when unset).</summary>
        private static string TestIndexFolder
        {
            get
            {
                string v = Environment.GetEnvironmentVariable("KHS_TEST_INDEX_FOLDER");
                return string.IsNullOrWhiteSpace(v) ? null : v;
            }
        }

        /// <summary>
        /// Smart startup: if a USN cursor exists for every drive, applies only the
        /// delta since the last run (fast — seconds). Falls back to a full MFT
        /// rebuild for any drive whose cursor is missing or stale.
        /// <paramref name="progress"/> receives a status string at each milestone;
        /// <paramref name="fullCrawlReason"/> says, per drive, why it was crawled in full.
        /// <paramref name="progressPercent"/> receives an estimated 0-99 while building and 100
        /// at the end. No pre-count pass: the estimate's total is the entry count the index
        /// already held from its previous run, against the entries crawled so far; drives done
        /// / total is the floor (and the only measure when there is no previous index). The
        /// larger of the two is reported, never decreasing, capped at 99 until done.
        /// </summary>
        public void Build(Action<string> progress = null, CancellationToken ct = default,
                          Action<string> fullCrawlReason = null,
                          Action<int> progressPercent = null)
        {
            // The build runs in the background while searches go on: they keep reading the
            // snapshot the index had when Build started (a drive rebuild deletes then re-adds,
            // so a mid-build refresh would show it half empty). Build's own blocking refresh at
            // the end swaps every search to the finished index at once.
            _searchViewFrozen = true;
            try { BuildCore(progress, ct, fullCrawlReason, progressPercent); }
            finally { _searchViewFrozen = false; }
        }

        private void BuildCore(Action<string> progress, CancellationToken ct,
                               Action<string> fullCrawlReason, Action<int> progressPercent)
        {
            // Test seam (hosted rig): index only this folder tree, never the drives; no live updates then.
            string onlyFolder = TestIndexFolder;
            if (onlyFolder != null && string.Equals(_indexPath, DefaultIndexPath, StringComparison.OrdinalIgnoreCase))
            {
                _writer.DeleteAll();
                foreach (var path in LocalFileIndexDirectoryCrawler.EnumeratePathsUnder(onlyFolder, null, ct))
                    _writer.AddDocument(MakeDocument(path));
                progressPercent?.Invoke(100);
                _writer.Commit();
                _searcherManager.MaybeRefreshBlocking();
                progress?.Invoke($"Index ready - {Count():N0} total paths (test folder)");
                return;
            }

            var drives = LocalFileIndexMftCrawler.GetNtfsDrives();
            if (drives.Count == 0)
            {
                // No NTFS volume detected — index whatever fixed drives exist
                // through the recursive directory walk instead of giving up.
                drives = LocalFileIndexDirectoryCrawler.GetFallbackDrives();
                if (drives.Count == 0)
                    throw new InvalidOperationException("No indexable drives found.");
            }

            int previousEntryCount;
            try { previousEntryCount = Count(); } catch { previousEntryCount = 0; }
            int drivesDone = 0;
            long entriesInFinishedDrives = 0;
            int lastReportedPercent = -1;
            void ReportProgress(long entriesInCurrentDrive)
            {
                if (progressPercent == null) return;
                int byDrives = (int)(drivesDone * 100L / drives.Count);
                int byEntries = previousEntryCount > 0
                    ? (int)Math.Min(100L, (entriesInFinishedDrives + entriesInCurrentDrive) * 100L / previousEntryCount)
                    : 0;
                int percent = Math.Min(99, Math.Max(byDrives, byEntries));
                if (percent <= lastReportedPercent) return;
                lastReportedPercent = percent;
                progressPercent(percent);
            }

            ReportProgress(0);
            foreach (var drive in drives)
            {
                ct.ThrowIfCancellationRequested();
                char letter = drive.Name[0];

                LocalFileIndexUsnJournal.Cursor cursor;
                bool hasCursor = LocalFileIndexUsnJournal.TryLoadCursor(_indexPath, letter, out cursor);

                if (hasCursor)
                {
                    progress?.Invoke($"Updating index for {letter}: via USN journal…");
                    LocalFileIndexUsnJournal.Cursor advancedCursor = default;
                    bool ok;
                    try
                    {
                        ok = UpdateFromJournal(letter, cursor, out advancedCursor, progress, ct);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        // Journal read blocked (e.g. cursor saved by an elevated run,
                        // now running without admin) — fall through to a full rebuild.
                        ok = false;
                        progress?.Invoke($"{letter}: journal read failed ({ex.Message}), rebuilding…");
                    }

                    if (ok)
                    {
                        LocalFileIndexUsnJournal.SaveCursor(_indexPath, letter, advancedCursor);
                        progress?.Invoke($"✓ {letter}: updated incrementally");
                        drivesDone++;
                        ReportProgress(0);
                        continue;
                    }

                    progress?.Invoke($"{letter}: journal cursor stale, rebuilding…");
                    fullCrawlReason?.Invoke($"{letter}: journal cursor stale or unreadable");
                }
                else
                {
                    fullCrawlReason?.Invoke($"{letter}: no saved journal cursor (first build, or an earlier run had no journal access)");
                }
                // Full rebuild for this drive: MFT crawl with automatic fallback to
                // a recursive directory walk when the MFT is blocked or yields nothing.
                var paths = LocalFileIndexDirectoryCrawler.EnumeratePathsWithFallback(
                    letter,
                    msg => progress?.Invoke(msg),
                    count =>
                    {
                        progress?.Invoke($"Crawling {letter}:… {count:N0} entries");
                        ReportProgress(count);
                    },
                    ct);

                ct.ThrowIfCancellationRequested();
                progress?.Invoke($"Writing {paths.Count:N0} paths for {letter}:…");
                RebuildDrive(letter, paths);

                try
                {
                    var newCursor = LocalFileIndexUsnJournal.GetCurrentCursor(letter);
                    LocalFileIndexUsnJournal.SaveCursor(_indexPath, letter, newCursor);
                }
                catch
                {
                    // No USN access (non-admin / non-NTFS) — without a cursor the next
                    // startup simply does another full build via the fallback path.
                }
                progress?.Invoke($"✓ {letter}: full rebuild done ({paths.Count:N0} paths)");
                entriesInFinishedDrives += paths.Count;
                drivesDone++;
                ReportProgress(0);
            }

            progressPercent?.Invoke(100);
            _writer.Commit();
            _searcherManager.MaybeRefreshBlocking();
            progress?.Invoke($"Index ready — {Count():N0} total paths");

            // Post-build: enrich Otzaria addin entry points with their display names.
            // This is a best-effort step — failures are logged but never abort the build.
            try { EnrichOtzariaAddins(progress); }
            catch (Exception ex) { progress?.Invoke($"Addin enrichment skipped: {ex.Message}"); }
        }

        private bool UpdateFromJournal(
            char driveLetter, LocalFileIndexUsnJournal.Cursor cursor,
            out LocalFileIndexUsnJournal.Cursor advancedCursor,
            Action<string> progress, CancellationToken ct)
        {
            var deleted  = new List<LocalFileIndexUsnJournal.ChangeRecord>();
            var upserted = new List<LocalFileIndexUsnJournal.ChangeRecord>();

            LocalFileIndexUsnJournal.ReadChangesResult readResult;
            foreach (var rec in LocalFileIndexUsnJournal.ReadChanges(driveLetter, cursor, out readResult))
            {
                ct.ThrowIfCancellationRequested();
                switch (rec.Reason)
                {
                    case LocalFileIndexUsnJournal.ChangeReason.Deleted:
                    case LocalFileIndexUsnJournal.ChangeReason.RenamedOldName:
                        deleted.Add(rec);  break;
                    case LocalFileIndexUsnJournal.ChangeReason.Created:
                    case LocalFileIndexUsnJournal.ChangeReason.RenamedNewName:
                        upserted.Add(rec); break;
                }
            }

            advancedCursor = readResult.AdvancedCursor;

            if (!readResult.Ok)
                return false;

            if (deleted.Count == 0 && upserted.Count == 0)
                return true;

            // ── Rebuild threshold ─────────────────────────────────────────────────
            // If the delta is large relative to the index size, a full MFT rebuild
            // is cheaper than resolving and applying thousands of individual changes.
            // Counts the work the update really does, not raw journal records: each file once
            // (the same file created, renamed and deleted is one change), folder creations not at
            // all (they are not applied), and folder deletions only outside the skipped folders
            // (nothing under a skipped folder is indexed). Raw records from browser caches and
            // temp-file churn used to push a normal day of use over the threshold.
            var distinctChanges = new HashSet<ulong>();
            foreach (var rec in upserted)
                if (!rec.IsDirectory) distinctChanges.Add(rec.Frn);
            foreach (var rec in deleted)
                if (!rec.IsDirectory || !LocalFileIndexMftCrawler.IsSkippedDirectoryName(rec.Name))
                    distinctChanges.Add(rec.Frn);
            int totalDelta   = distinctChanges.Count;
            int currentCount = Count();
            if (currentCount > 0 &&
                totalDelta > currentCount * RebuildThresholdFraction)
            {
                progress?.Invoke(
                    $"{driveLetter}: {totalDelta:N0} changes ({totalDelta * 100 / currentCount}% of index) " +
                    $"— full rebuild is cheaper than incremental update");
                return false;   // signal caller to do a full rebuild
            }

            progress?.Invoke(
                $"Applying {upserted.Count} addition(s) and {deleted.Count} deletion(s) for {driveLetter}:…");

            foreach (var rec in deleted)
            {
                if (rec.IsDirectory && LocalFileIndexMftCrawler.IsSkippedDirectoryName(rec.Name))
                    continue;   // nothing under a skipped folder is indexed
                if (rec.IsDirectory)
                    RemoveByPathPrefix(char.ToLower(driveLetter) + ":", rec.Name);
                else
                    RemoveByName(char.ToLower(driveLetter) + ":", rec.Name);
            }

            if (upserted.Count > 0)
            {
                var frnSet = new HashSet<ulong>();
                foreach (var rec in upserted) { frnSet.Add(rec.Frn); frnSet.Add(rec.ParentFrn); }

                var snapshot = LocalFileIndexMftCrawler.SnapshotEntries(driveLetter, frnSet, ct);
                string root  = char.ToUpper(driveLetter) + ":";

                foreach (var rec in upserted)
                {
                    if (rec.IsDirectory) continue;
                    string fullPath = LocalFileIndexMftCrawler.ResolvePath(
                        rec.Frn, rec.Name, rec.ParentFrn, snapshot, root);
                    if (fullPath != null)
                        _writer.UpdateDocument(
                            new Term(FLD_PATH, fullPath.ToLowerInvariant()),
                            MakeDocument(fullPath));
                }
            }

            return true;
        }

        // ── Write ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Replaces the entire index with a fresh set of paths.
        /// Used for first-time builds or forced full rebuilds.
        /// </summary>
        public void RebuildFrom(IEnumerable<string> paths)
        {
            _writer.DeleteAll();
            foreach (var path in paths)
                _writer.AddDocument(MakeDocument(path));
            _writer.Commit();
            _searcherManager.MaybeRefreshBlocking();
        }

        /// <summary>
        /// Replaces all documents for a single drive letter with a fresh set of paths.
        /// Used when one drive needs a full rebuild while others are updated incrementally.
        /// </summary>
        private void RebuildDrive(char driveLetter, IEnumerable<string> paths)
        {
            // Delete everything on this drive: paths start with "c:\" etc.
            string prefix = char.ToLower(driveLetter) + ":";
            _writer.DeleteDocuments(new WildcardQuery(new Term(FLD_PATH, prefix + "*")));
            foreach (var path in paths)
                _writer.AddDocument(MakeDocument(path));
            // Caller commits after all drives are processed
        }

        /// <summary>Adds or updates a single path (used by USN journal upsert).</summary>
        public void AddOrUpdate(string fullPath)
        {
            _writer.UpdateDocument(new Term(FLD_PATH, fullPath.ToLowerInvariant()), MakeDocument(fullPath));
            _writer.Commit();
            _searcherManager.MaybeRefreshBlocking();
        }

        /// <summary>Removes a path (used by USN journal delete).</summary>
        public void Remove(string fullPath)
        {
            _writer.DeleteDocuments(new Term(FLD_PATH, fullPath.ToLowerInvariant()));
            _writer.Commit();
            _searcherManager.MaybeRefreshBlocking();
        }

        /// <summary>
        /// Removes all indexed paths on <paramref name="drive"/> whose filename
        /// matches <paramref name="name"/> (case-insensitive).
        /// Used when a file is deleted and we only know its name, not its full path.
        /// </summary>
        private void RemoveByName(string drive, string name)
        {
            // Search for *\name on this drive and delete matching docs.
            // Backslash is Lucene's wildcard ESCAPE character, so every literal
            // separator has to be doubled (see the note in Search).
            string pattern = drive.ToLowerInvariant() + "*\\\\" + EscapeWildcard(name.ToLowerInvariant());
            _writer.DeleteDocuments(new WildcardQuery(new Term(FLD_PATH, pattern)));
        }

        /// <summary>
        /// Removes all indexed paths on <paramref name="drive"/> under a directory
        /// called <paramref name="dirName"/>. Used when a directory is deleted.
        /// </summary>
        private void RemoveByPathPrefix(string drive, string dirName)
        {
            // Match anything containing \dirName\ on this drive.
            // The separators must be doubled — a single trailing "\*" would escape the
            // asterisk into a literal '*' and the delete would silently match nothing,
            // leaving every file under a deleted directory in the index forever.
            string pattern = drive.ToLowerInvariant() + "*\\\\" +
                             EscapeWildcard(dirName.ToLowerInvariant()) + "\\\\*";
            _writer.DeleteDocuments(new WildcardQuery(new Term(FLD_PATH, pattern)));
        }

        /// <summary>
        /// Escapes a literal string for use inside a Lucene <see cref="WildcardQuery"/>
        /// pattern: backslash is the escape character and <c>*</c>/<c>?</c> are wildcards,
        /// so all three must be prefixed with a backslash to match literally.
        /// </summary>
        private static string EscapeWildcard(string literal)
        {
            if (string.IsNullOrEmpty(literal)) return literal;
            var sb = new System.Text.StringBuilder(literal.Length + 8);
            foreach (char c in literal)
            {
                if (c == '\\' || c == '*' || c == '?') sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }

        // Pre-built field types:
        //   PathFieldType      — indexed (DOCS_ONLY) + stored, no norms, no frequencies, no positions.
        //   DateFieldType      — stored only (not indexed); holds the last-write Unix ms as a string.
        //   AddinNameFieldType — indexed (DOCS_ONLY) + stored; same shape as PathFieldType.
        //                        Only added to Otzaria addin documents — Lucene fields are optional
        //                        per-document so non-addin docs simply have no addin_name field.
        //   AddinNameDisplayFieldType — stored only (not indexed); the manifest's name in its
        //                        ORIGINAL case. addin_name itself must stay lower-cased because
        //                        WildcardQuery matches the raw term, but that value then reaches
        //                        the UI as a tab title, where "MyTool" must not read "mytool".
        //                        Paths have the same lower-casing but are a separate concern.
        private static readonly FieldType PathFieldType      = BuildPathFieldType();
        private static readonly FieldType DateFieldType      = BuildDateFieldType();
        private static readonly FieldType AddinNameFieldType = BuildAddinNameFieldType();
        private static readonly FieldType AddinNameDisplayFieldType = BuildAddinNameDisplayFieldType();

        private static FieldType BuildPathFieldType()
        {
            var ft = new FieldType();
            ft.IsIndexed        = true;
            ft.IsStored         = true;
            ft.IsTokenized      = false;
            ft.IndexOptions     = IndexOptions.DOCS_ONLY;  // no TF, no positions
            ft.OmitNorms        = true;
            ft.StoreTermVectors = false;
            ft.Freeze();
            return ft;
        }

        private static FieldType BuildDateFieldType()
        {
            var ft = new FieldType();
            ft.IsIndexed        = false;
            ft.IsStored         = true;
            ft.IsTokenized      = false;
            ft.OmitNorms        = true;
            ft.StoreTermVectors = false;
            ft.Freeze();
            return ft;
        }

        private static FieldType BuildAddinNameDisplayFieldType()
        {
            // Stored only — never searched. addin_name carries the searchable copy.
            var ft = new FieldType();
            ft.IsIndexed        = false;
            ft.IsStored         = true;
            ft.IsTokenized      = false;
            ft.OmitNorms        = true;
            ft.StoreTermVectors = false;
            ft.Freeze();
            return ft;
        }

        private static FieldType BuildAddinNameFieldType()
        {
            // Same as PathFieldType — indexed (DOCS_ONLY) + stored, no norms.
            // The search loop wraps each query word with *word* WildcardQuery against
            // BOTH path and addin_name, so addin names are searched automatically.
            var ft = new FieldType();
            ft.IsIndexed        = true;
            ft.IsStored         = true;
            ft.IsTokenized      = false;
            ft.IndexOptions     = IndexOptions.DOCS_ONLY;
            ft.OmitNorms        = true;
            ft.StoreTermVectors = false;
            ft.Freeze();
            return ft;
        }

        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static string GetLastWriteUnixMs(string fullPath)
        {
            try
            {
                var info = new FileInfo(fullPath);
                if (info.Exists)
                {
                    long ms = (long)(info.LastWriteTimeUtc - UnixEpoch).TotalMilliseconds;
                    return ms.ToString();
                }
            }
            catch { }
            return "0";
        }

        // Indexed-only word terms (name_words / folder_words): untokenized, so the PathAnalyzer
        // never sees them - each Field instance is exactly one term. No norms, no frequencies.
        private static readonly FieldType WordFieldType = BuildWordFieldType();

        // name_seq: tokenized (on spaces, by PathAnalyzer) with frequencies and positions, so the
        // ranking reads repeats, typed order and proximity straight off the postings. No norms.
        private static readonly FieldType PositionedWordsFieldType = BuildPositionedWordsFieldType();

        private static FieldType BuildPositionedWordsFieldType()
        {
            var ft = new FieldType();
            ft.IsIndexed        = true;
            ft.IsStored         = false;
            ft.IsTokenized      = true;
            ft.IndexOptions     = IndexOptions.DOCS_AND_FREQS_AND_POSITIONS;
            ft.OmitNorms        = true;
            ft.StoreTermVectors = false;
            ft.Freeze();
            return ft;
        }

        private static FieldType BuildWordFieldType()
        {
            var ft = new FieldType();
            ft.IsIndexed        = true;
            ft.IsStored         = false;
            ft.IsTokenized      = false;
            ft.IndexOptions     = IndexOptions.DOCS_ONLY;
            ft.OmitNorms        = true;
            ft.StoreTermVectors = false;
            ft.Freeze();
            return ft;
        }

        /// <summary>The path_sort_dv value: the lower-cased path upper-cased, so the doc-values'
        /// byte order is the ranking's OrdinalIgnoreCase path order (which compares upper-cased
        /// chars: "\" sorts AFTER letters there, before them in raw lower-case order).</summary>
        private static BytesRef PathSortKey(string lowerPath) => new BytesRef(lowerPath.ToUpperInvariant());

        /// <summary>Every distinct 3-char piece of <paramref name="text"/>. A path holding a word
        /// of 3+ chars holds all of the word's pieces, so requiring them all finds every match
        /// by exact term lookups (a *word* wildcard walks every path in the index instead).
        /// Words longer than 3 can still give false candidates ("abcd" vs "abc..bcd");
        /// PathContainsCheck drops those.</summary>
        private static HashSet<string> PathTrigrams(string text)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i + 3 <= text.Length; i++) set.Add(text.Substring(i, 3));
            return set;
        }

        private static Document MakeDocument(string fullPath, string addinName = null)
        {
            var doc = new Document();
            string lowerPath = fullPath.ToLowerInvariant();
            doc.Add(new Field(FLD_PATH, lowerPath, PathFieldType));
            string dateMs = GetLastWriteUnixMs(fullPath);
            doc.Add(new Field(FLD_DATE, dateMs, DateFieldType));

            // Ordering and window data for SearchUnderFolders (doc-values: read without
            // decompressing stored fields). The words are the SAME ones the UserSpace ranking
            // measures (LocalFileNameWords), from the same lower-cased path it is handed.
            var nameTokens = LocalFileNameWords.NameTokens(lowerPath);
            var distinctName = new HashSet<string>(nameTokens, StringComparer.Ordinal);
            foreach (var word in distinctName)
                doc.Add(new Field(FLD_NAME_WORDS, word, WordFieldType));
            int lastSep = lowerPath.LastIndexOf('\\');
            if (lastSep > 0)
                foreach (var word in new HashSet<string>(LocalFileNameWords.FolderTokens(lowerPath.Substring(0, lastSep)), StringComparer.Ordinal))
                    doc.Add(new Field(FLD_FOLDER_WORDS, word, WordFieldType));
            foreach (var term in ToratEmet.ToratEmetNames.SearchTerms(fullPath))
                doc.Add(new Field(FLD_ALIAS_TERMS, term, WordFieldType));
            foreach (var trigram in PathTrigrams(lowerPath))
                doc.Add(new Field(FLD_PATH_TRIGRAMS, trigram, WordFieldType));
            // The same name words once more as ONE positioned sequence (the analyzer splits it
            // on spaces): word counts, typed order and proximity are read off its postings.
            if (nameTokens.Count > 0)
                doc.Add(new Field(FLD_NAME_SEQ, string.Join(" ", nameTokens), PositionedWordsFieldType));
            // The quoted acronyms the ORIGINAL file name carries (the indexed words have the
            // quote glyphs stripped), one term each.
            var quotedKeys = SeforimDbCatalogTextNormalizer.GetQuotedAbbreviationKeys(
                LocalFileNameWords.SeparatorsToSpaces(System.IO.Path.GetFileNameWithoutExtension(fullPath)));
            if (quotedKeys != null)
                foreach (var key in quotedKeys)
                    doc.Add(new Field(FLD_QUOTED_KEYS, key, WordFieldType));
            var sortKey = PathSortKey(lowerPath);
            // The window reads the path back from the sort key (lower-casing it again) unless
            // that would not give this exact path back, or the doc has an add-in name: then it
            // reads the stored fields (ShapeStoredRead).
            bool storedRead = !string.IsNullOrEmpty(addinName)
                              || sortKey.Utf8ToString().ToLowerInvariant() != lowerPath;
            doc.Add(new NumericDocValuesField(FLD_NAME_SHAPE_DV,
                (long)Math.Min(nameTokens.Count, 0xFFFF)
                | ((long)Math.Min(distinctName.Count, 0xFFFF) << 16)
                | (storedRead ? ShapeStoredRead : 0)));
            doc.Add(new SortedDocValuesField(FLD_PATH_SORT_DV, sortKey));
            doc.Add(new NumericDocValuesField(FLD_DATE_DV, long.TryParse(dateMs, out long ms) ? ms : 0));
            // addin_name is only added for Otzaria addin entry-point files.
            // Non-addin documents simply have no addin_name field — Lucene fields are
            // per-document optional so this costs nothing for the vast majority of docs.
            if (!string.IsNullOrEmpty(addinName))
            {
                doc.Add(new Field(FLD_ADDIN_NAME, addinName.ToLowerInvariant(), AddinNameFieldType));
                doc.Add(new Field(FLD_ADDIN_NAME_DISPLAY, addinName, AddinNameDisplayFieldType));
            }
            return doc;
        }

        // ── Search ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Files whose path holds EVERY word of <paramref name="input"/> as a substring —
        /// equivalent to paths.Where(p => p.Contains(word1) &amp;&amp; p.Contains(word2) &amp;&amp; ...) — or
        /// matches it through the add-in name / alias terms, optionally on one drive. Ordered by
        /// the catalog's ranking keys (<see cref="FolderRankCollector"/>), then the path.
        ///
        /// <paramref name="limit"/> caps the entries returned (0 = every match); <paramref name="total"/>
        /// is always the full match count, so callers can say "showing X of Y".
        /// </summary>
        public List<PathEntry> Search(string input, char? drive, int limit, out int total)
            => Search(input, drive, folders: null, limit, out total);

        /// <summary><see cref="Search(string, char?, int, out int)"/> scoped to files under any of
        /// <paramref name="folders"/> (null = unscoped; an empty list matches nothing).</summary>
        public List<PathEntry> Search(string input, char? drive, IReadOnlyCollection<string> folders, int limit, out int total)
            => SearchUnderFolders(input, drive, folders, excludedFolders: null, limit, out total);

        /// <summary>
        /// The search: files under any of <paramref name="folders"/> (null = unscoped; an empty
        /// list matches nothing) whose path holds every word of <paramref name="input"/> as a
        /// substring, minus files under <paramref name="excludedFolders"/> (applied in the query,
        /// so the total and the window below are already exclusion-free).
        ///
        /// Matching is index-level throughout: each folder is a PrefixQuery on "folder\" (the
        /// path field is the whole lower-cased path as ONE term), each word a trigram lookup on
        /// the path (<see cref="PathSubstringQuery"/>) or a wildcard over the small add-in-name
        /// / alias-term fields. The word may sit anywhere on the path, the folder's own name and
        /// its parents included — as a catalog category's whole chain matches a scoped catalog
        /// search.
        ///
        /// UNCAPPED matching and ordering: <see cref="FolderRankCollector"/> visits EVERY match
        /// (dropping the trigram false candidates) and orders it by the catalog's ranking keys,
        /// read from index data alone: literal (every query token a whole word of the name or of
        /// a folder on the path), every token in the name, name covered by the query (then more
        /// distinct query words, then the length gap), more query words in the name (with
        /// repeats), the quoted acronym as typed, and for a 2+ word query the words found, in
        /// typed order, closest together; then the first matching folder in the caller's order,
        /// then the path (OrdinalIgnoreCase). <paramref name="total"/> is the FULL match count.
        ///
        /// Only MATERIALIZATION is bounded: the ordered top <paramref name="limit"/> (0 = every
        /// match for the unscoped search; the folder-scoped UserSpace search passes 0 and takes
        /// <see cref="MaterializeCap"/>) are returned, best first, each with its keys.
        /// </summary>
        public List<PathEntry> SearchUnderFolders(
            string input, char? drive, IReadOnlyCollection<string> folders, IList<string> excludedFolders,
            int limit, out int total)
        {
            total = 0;
            var results = new List<PathEntry>();
            if (string.IsNullOrWhiteSpace(input) || (folders != null && folders.Count == 0)) return results;

            string[] words = input.Trim().Split(
                new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            var query = new BooleanQuery();
            // Words of 4+ chars: their trigrams can match a path that lacks the word itself,
            // so PathContainsCheck re-checks those candidates.
            var checkWords = new List<string>();
            foreach (var word in words)
            {
                string lower = word.ToLowerInvariant();
                // Backslash is the ESCAPE character in Lucene wildcard syntax, so a literal
                // path separator has to be doubled. Without this, a query like
                // "otzaria\plugins" escapes the 'p' and silently matches nothing — even
                // though both "otzaria" and "plugins" match on their own.
                var wordPattern = "*" + EscapeWildcard(lower) + "*";
                // Each word must appear somewhere in the path, the addin_name OR an alias term.
                // addin_name / alias_terms hold few terms, so a wildcard over them stays cheap;
                // the path uses its trigrams (a *word* wildcard there walks every path).
                var perWord = new BooleanQuery();
                perWord.Add(PathSubstringQuery(lower), Occur.SHOULD);
                perWord.Add(new WildcardQuery(new Term(FLD_ADDIN_NAME, wordPattern)), Occur.SHOULD);
                perWord.Add(new WildcardQuery(new Term(FLD_ALIAS_TERMS, wordPattern)), Occur.SHOULD);
                query.Add(perWord, Occur.MUST);
                if (lower.Length > 3) checkWords.Add(lower);
            }

            var prefixes = new List<string>();
            if (folders != null)
            {
                // The folder any-of is ONE required clause: beside a MUST, bare SHOULDs turn optional.
                var anyFolder = new BooleanQuery();
                foreach (var folder in folders)
                {
                    prefixes.Add(FolderPrefix(folder));
                    anyFolder.Add(new PrefixQuery(new Term(FLD_PATH, FolderPrefix(folder))), Occur.SHOULD);
                }
                query.Add(anyFolder, Occur.MUST);
            }
            // Scope to drive: require path to start with "c:\" etc.
            if (drive.HasValue)
                query.Add(new PrefixQuery(new Term(FLD_PATH, char.ToLower(drive.Value) + ":")), Occur.MUST);
            if (excludedFolders != null)
                foreach (var excluded in excludedFolders)
                {
                    if (string.IsNullOrWhiteSpace(excluded)) continue;
                    string lower = excluded.ToLowerInvariant().Replace('/', '\\').TrimEnd('\\');
                    if (lower.Length == 0) continue;
                    // Same boundary as LocalFileIndexExcludedFolders.IsExcluded: the folder itself
                    // and what lies under "folder\", never a sibling "folderBar".
                    query.Add(new TermQuery(new Term(FLD_PATH, lower)), Occur.MUST_NOT);
                    query.Add(new PrefixQuery(new Term(FLD_PATH, lower + "\\")), Occur.MUST_NOT);
                }

            int cap = limit > 0 ? limit : folders != null ? MaterializeCap : int.MaxValue;

            RefreshSearchView();
            var searcher = _searcherManager.Acquire();
            try
            {
                // Per check word: the docs the word reaches through addin_name / alias_terms.
                // Those pass the re-check whatever their path holds. Few docs have either field.
                var otherFieldDocs = new List<HashSet<int>>(checkWords.Count);
                foreach (var lower in checkWords)
                {
                    var pattern = "*" + EscapeWildcard(lower) + "*";
                    var other = new BooleanQuery
                    {
                        { new WildcardQuery(new Term(FLD_ADDIN_NAME, pattern)), Occur.SHOULD },
                        { new WildcardQuery(new Term(FLD_ALIAS_TERMS, pattern)), Occur.SHOULD },
                    };
                    var docs = new DocSetCollector();
                    searcher.Search(other, docs);
                    otherFieldDocs.Add(docs.Docs);
                }

                var collector = new FolderRankCollector(input,
                    SeforimDbCatalog.SeforimDbCatalogTextNormalizer.TokenizeQuery(input), prefixes,
                    checkWords.Count > 0 ? new PathContainsCheck(checkWords, otherFieldDocs) : null);
                searcher.Search(query, collector);
                total = collector.Count;
                Materialize(searcher, collector.Top(cap, searcher.IndexReader.Leaves), results);
            }
            finally
            {
                _searcherManager.Release(searcher);
            }
            return results;
        }


        /// <summary>Reads <paramref name="top"/> into entries, in order, each carrying its ranking
        /// keys: from doc-values, and from stored fields only for flagged docs (ShapeStoredRead:
        /// add-ins, odd casing).</summary>
        private static void Materialize(IndexSearcher searcher, List<(int Doc, RankKeys Keys)> top, List<PathEntry> results)
        {
            var leaves = searcher.IndexReader.Leaves;
            var scratch = new BytesRef();
            foreach (var (docId, keys) in top)
            {
                int leaf = ReaderUtil.SubIndex(docId, leaves);
                var reader = leaves[leaf].AtomicReader;
                int local = docId - leaves[leaf].DocBase;
                var shape = reader.GetNumericDocValues(FLD_NAME_SHAPE_DV);
                var pathSort = reader.GetSortedDocValues(FLD_PATH_SORT_DV);
                var date = reader.GetNumericDocValues(FLD_DATE_DV);
                if (shape == null || pathSort == null || date == null || (shape.Get(local) & ShapeStoredRead) != 0)
                {
                    var doc = searcher.Doc(docId);
                    // Prefer the original-case add-in name; the lower-cased searchable copy is the fallback.
                    results.Add(new PathEntry(doc.Get(FLD_PATH), doc.Get(FLD_DATE) ?? "0",
                        doc.Get(FLD_ADDIN_NAME_DISPLAY) ?? doc.Get(FLD_ADDIN_NAME) ?? "") { Keys = keys });
                    continue;
                }
                pathSort.Get(local, scratch);
                results.Add(new PathEntry(scratch.Utf8ToString().ToLowerInvariant(),
                    date.Get(local).ToString(System.Globalization.CultureInfo.InvariantCulture)) { Keys = keys });
            }
        }

        /// <summary>Docs whose path holds <paramref name="lowerWord"/>: words of 3+ chars by
        /// their trigrams (exact term lookups), shorter ones by a *word* wildcard (no trigram to
        /// look up). For 4+ chars this over-matches; PathContainsCheck re-checks.</summary>
        private static Query PathSubstringQuery(string lowerWord)
        {
            if (lowerWord.Length < 3)
                return new WildcardQuery(new Term(FLD_PATH, "*" + EscapeWildcard(lowerWord) + "*"));
            var all = new BooleanQuery();
            foreach (var trigram in PathTrigrams(lowerWord))
                all.Add(new TermQuery(new Term(FLD_PATH_TRIGRAMS, trigram)), Occur.MUST);
            return all;
        }

        /// <summary>Collects the global doc ids of every match (scores ignored).</summary>
        private sealed class DocSetCollector : ICollector
        {
            public readonly HashSet<int> Docs = new HashSet<int>();
            private int _docBase;
            public void SetScorer(Scorer scorer) { }
            public bool AcceptsDocsOutOfOrder => true;
            public void SetNextReader(AtomicReaderContext context) => _docBase = context.DocBase;
            public void Collect(int doc) => Docs.Add(_docBase + doc);
        }

        /// <summary>
        /// Drops trigram false candidates of the unscoped search: a doc passes when every check
        /// word is in its path, or reaches it through addin_name / alias_terms. The path comes
        /// from path_sort_dv (no stored-field read), or from the stored path when the doc-values
        /// copy does not lower-case back to it (ShapeStoredRead).
        /// </summary>
        private sealed class PathContainsCheck
        {
            private readonly List<string> _words;
            private readonly List<HashSet<int>> _otherFieldDocs;
            private readonly BytesRef _scratch = new BytesRef();
            private AtomicReader _reader;
            private SortedDocValues _pathSort;
            private NumericDocValues _shape;
            private int _docBase;

            public PathContainsCheck(List<string> words, List<HashSet<int>> otherFieldDocs)
            {
                _words = words;
                _otherFieldDocs = otherFieldDocs;
                _upperWords = new byte[words.Count][];
                for (int i = 0; i < words.Count; i++)
                    _upperWords[i] = System.Text.Encoding.UTF8.GetBytes(words[i].ToUpperInvariant());
            }

            private readonly byte[][] _upperWords;   // each word as the sort key spells it (see PathSortKey)

            public void SetNextReader(AtomicReaderContext context)
            {
                _reader = context.AtomicReader;
                _docBase = context.DocBase;
                _pathSort = _reader.GetSortedDocValues(FLD_PATH_SORT_DV);
                _shape = _reader.GetNumericDocValues(FLD_NAME_SHAPE_DV);
            }

            public bool Matches(int doc)
            {
                // The doc-values path is the UPPER-cased path's UTF-8: the words are compared as
                // upper-cased bytes, with no string decoded per candidate. A doc whose sort key
                // does not lower-case back to its path (ShapeStoredRead) is checked on the
                // stored path instead.
                bool fromDocValues = _pathSort != null && _shape != null && (_shape.Get(doc) & ShapeStoredRead) == 0;
                string storedPath = null;
                if (fromDocValues) _pathSort.Get(doc, _scratch);
                for (int i = 0; i < _words.Count; i++)
                {
                    if (_otherFieldDocs[i].Contains(_docBase + doc)) continue;
                    if (fromDocValues)
                    {
                        if (!ContainsBytes(_scratch, _upperWords[i])) return false;
                        continue;
                    }
                    storedPath ??= _reader.Document(doc).Get(FLD_PATH);
                    if (storedPath == null || storedPath.IndexOf(_words[i], StringComparison.Ordinal) < 0) return false;
                }
                return true;
            }

            private static bool ContainsBytes(BytesRef hay, byte[] needle)
            {
                int last = hay.Offset + hay.Length - needle.Length;
                for (int start = hay.Offset; start <= last; start++)
                {
                    int k = 0;
                    while (k < needle.Length && hay.Bytes[start + k] == needle[k]) k++;
                    if (k == needle.Length) return true;
                }
                return false;
            }
        }
        /// <summary>
        /// Collects every match with its FULL catalog ranking keys (<see cref="RankKeys"/>),
        /// read from index data alone: per-segment word bit sets (name_words / folder_words
        /// postings of the query's words), the name_seq postings (word counts and positions:
        /// repeats, typed order, proximity), the quoted_keys postings and the doc-values.
        /// Nothing is capped here; <see cref="Top"/> orders every match - keys, then the first
        /// matching folder in the caller's order, then the path - and hands back the best few.
        /// </summary>
        private sealed class FolderRankCollector : ICollector
        {
            private struct Entry
            {
                public int Doc;        // global doc id
                public int Root;       // first matching folder's index in the caller's order (0xFF = none)
                public int Ord;        // path_sort_dv ord in the segment: path order there
                public RankKeys Keys;
            }

            private const int MaxWords = 64;

            private readonly List<string> _words = new List<string>();  // distinct, index = bit
            private readonly Dictionary<string, int> _wordIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly int[][][] _tokenAlternatives;              // per token, per alt: word bits
            private readonly ulong _queryWordMask;                      // QueryWordSet
            private readonly int[] _countWord;                          // per QueryWordCounts entry: the word's index
            private readonly int[] _countWanted;                        //   ...and how many times it was typed
            private readonly int _queryWordCount;
            private readonly int[] _orderWord;                          // per order word: the word's index
            private readonly List<string> _quotedKeys;
            private readonly SeforimDbCatalog.SeforimDbCatalogRankingKeys.ProximityScratch _proximity;
            private readonly bool _measureProximity;
            private readonly List<string> _prefixes;
            private readonly PathContainsCheck _check;   // null: every match is real (no trigram query)

            private readonly List<List<Entry>> _segments = new List<List<Entry>>();
            private readonly List<int> _segmentLeaves = new List<int>();   // leaf ord of each segment

            // Per segment.
            private int _docBase;
            private List<Entry> _entries;
            private FixedBitSet[] _nameBits;
            private FixedBitSet[] _folderBits;
            private DocsEnum[] _nameSeq;     // per word: name_seq postings (freq + positions)
            private DocsEnum[] _quoted;      // per quoted key: quoted_keys postings
            private NumericDocValues _shape;
            private SortedDocValues _pathSort;
            private int[] _prefixLo, _prefixHi;

            // Per doc.
            private readonly int[] _nameFreq;
            private readonly List<int>[] _positions;
            private readonly Func<int, List<int>> _positionsOfOrderWord;

            public int Count { get; private set; }

            public FolderRankCollector(string query, List<SeforimDbCatalog.SeforimDbCatalogTextNormalizer.QueryToken> tokens, List<string> prefixes,
                PathContainsCheck check = null)
            {
                _prefixes = prefixes;
                _check = check;
                _tokenAlternatives = new int[tokens.Count][][];
                for (int t = 0; t < tokens.Count; t++)
                {
                    var alts = tokens[t].Alternatives;
                    _tokenAlternatives[t] = new int[alts.Length][];
                    for (int a = 0; a < alts.Length; a++)
                    {
                        _tokenAlternatives[t][a] = new int[alts[a].Length];
                        for (int w = 0; w < alts[a].Length; w++) _tokenAlternatives[t][a][w] = WordIndex(alts[a][w]);
                    }
                }
                // No VariantLookup: it reads the catalog index's term dictionary, which knows
                // nothing of file names - file keys match words literally.
                foreach (var word in SeforimDbCatalog.SeforimDbCatalogRankingKeys.QueryWordSet(tokens, null))
                    _queryWordMask |= Bit(WordIndex(word));
                var counts = SeforimDbCatalog.SeforimDbCatalogRankingKeys.QueryWordCounts(tokens);
                _countWord = new int[counts.Count];
                _countWanted = new int[counts.Count];
                int c = 0;
                foreach (var pair in counts)
                {
                    _countWord[c] = WordIndex(pair.Key);
                    _countWanted[c++] = pair.Value;
                    _queryWordCount += pair.Value;
                }
                var orderWords = SeforimDbCatalog.SeforimDbCatalogRankingKeys.OrderWords(tokens);
                _orderWord = new int[orderWords.Count];
                for (int i = 0; i < orderWords.Count; i++) _orderWord[i] = WordIndex(orderWords[i]);
                _measureProximity = orderWords.Count >= 2;
                _proximity = new SeforimDbCatalog.SeforimDbCatalogRankingKeys.ProximityScratch(orderWords);
                var quoted = SeforimDbCatalog.SeforimDbCatalogTextNormalizer.GetQuotedAbbreviationKeys(query);
                _quotedKeys = quoted == null ? new List<string>() : new List<string>(quoted);

                _nameFreq = new int[_words.Count];
                _positions = new List<int>[_words.Count];
                for (int i = 0; i < _positions.Length; i++) _positions[i] = new List<int>();
                _positionsOfOrderWord = i => _positions[_orderWord[i]];
            }

            private int WordIndex(string word)
            {
                int i;
                if (_wordIndex.TryGetValue(word, out i)) return i;
                _wordIndex[word] = _words.Count;
                _words.Add(word);
                return _words.Count - 1;
            }

            // A word past the 64th never sets a bit: treated as absent (a 65-word query).
            private static ulong Bit(int index) => index < MaxWords ? 1UL << index : 0;

            public void SetScorer(Scorer scorer) { /* scores are ignored by design */ }

            /// <summary>In doc order: the name_seq / quoted_keys postings enums are forward-only.</summary>
            public bool AcceptsDocsOutOfOrder => false;

            public void SetNextReader(AtomicReaderContext context)
            {
                var reader = context.AtomicReader;
                _docBase = context.DocBase;
                _check?.SetNextReader(context);
                _entries = new List<Entry>();
                _segments.Add(_entries);
                _segmentLeaves.Add(context.Ord);
                int n = Math.Min(_words.Count, MaxWords);
                _nameBits = new FixedBitSet[n];
                _folderBits = new FixedBitSet[n];
                for (int i = 0; i < n; i++)
                {
                    _nameBits[i] = TermBits(reader, FLD_NAME_WORDS, _words[i]);
                    _folderBits[i] = TermBits(reader, FLD_FOLDER_WORDS, _words[i]);
                }
                _nameSeq = Common.LucenePostings.Open(reader, FLD_NAME_SEQ, _words, positions: true);
                _quoted = Common.LucenePostings.Open(reader, FLD_QUOTED_KEYS, _quotedKeys, positions: false);
                _shape = reader.GetNumericDocValues(FLD_NAME_SHAPE_DV);
                _pathSort = reader.GetSortedDocValues(FLD_PATH_SORT_DV);

                // Each folder is one contiguous ord range of the sorted path keys.
                _prefixLo = new int[_prefixes.Count];
                _prefixHi = new int[_prefixes.Count];
                for (int i = 0; i < _prefixes.Count && _pathSort != null; i++)
                {
                    var lo = PathSortKey(_prefixes[i]);
                    var hi = BytesRef.DeepCopyOf(lo);
                    hi.Bytes[hi.Offset + hi.Length - 1]++;   // the prefix ends in '\' - never 0xFF
                    _prefixLo[i] = InsertionPoint(_pathSort.LookupTerm(lo));
                    _prefixHi[i] = InsertionPoint(_pathSort.LookupTerm(hi));
                }
            }

            private static int InsertionPoint(int lookup) => lookup >= 0 ? lookup : -lookup - 1;

            private static FixedBitSet TermBits(AtomicReader reader, string field, string word)
            {
                var docs = reader.GetTermDocsEnum(new Term(field, word));
                if (docs == null) return null;
                var bits = new FixedBitSet(reader.MaxDoc);
                int d;
                while ((d = docs.NextDoc()) != DocIdSetIterator.NO_MORE_DOCS) bits.Set(d);
                return bits;
            }

            public void Collect(int doc)
            {
                if (_check != null && !_check.Matches(doc)) return;
                ulong inName = 0, inFolders = 0;
                for (int i = 0; i < _nameBits.Length; i++)
                {
                    if (_nameBits[i] != null && _nameBits[i].Get(doc)) inName |= 1UL << i;
                    if (_folderBits[i] != null && _folderBits[i].Get(doc)) inFolders |= 1UL << i;
                }
                // name_seq holds the same words as name_words: no query word in the name (a
                // substring-only match, the common case) means nothing to read there.
                for (int i = 0; i < _words.Count; i++)
                    _nameFreq[i] = inName == 0 ? ClearPositions(i) : Common.LucenePostings.FreqAndPositions(_nameSeq[i], doc, _positions[i]);

                // No query word in the name (a substring-only match, the common case): the name
                // shape cannot matter, so its doc-value is not read.
                long shape = inName == 0 ? 0 : _shape?.Get(doc) ?? 0;
                int titleWords = (int)(shape & 0xFFFF);
                int titleDistinct = (int)((shape >> 16) & 0xFFFF);
                int distinctFound = PopCount(inName & _queryWordMask);
                // Covered: every distinct name word is a query word.
                bool covered = titleDistinct > 0 && distinctFound == titleDistinct;

                var keys = new RankKeys
                {
                    // Literal = every query word is a whole word of the name or of a folder on the
                    // path; a file matched only by substrings is a non-literal match.
                    IsLiteral = EveryToken(inName | inFolders),
                    // The file's name plays the title; there is no author.
                    MatchesTitleOrAuthor = EveryToken(inName),
                    TitleCovered = covered,
                    TitleDistinctWords = distinctFound,
                    TitleLengthGap = Math.Abs(titleWords - _queryWordCount),
                    LeafCovered = covered,   // a file is a level-0 hit: the leaf IS the title
                    WordsMatched = WordsMatched(),
                };
                foreach (var q in _quoted)
                    if (Common.LucenePostings.At(q, doc)) { keys.QuotedFormMatch = true; break; }
                if (_measureProximity) _proximity.Measure(_positionsOfOrderWord, ref keys);

                int root = 0xFF, ord = -1;
                if (_pathSort != null)
                {
                    ord = _pathSort.GetOrd(doc);
                    for (int i = 0; i < _prefixLo.Length; i++)
                        if (ord >= _prefixLo[i] && ord < _prefixHi[i]) { root = Math.Min(i, 0xFF); break; }
                }

                _entries.Add(new Entry { Doc = _docBase + doc, Root = root, Ord = ord, Keys = keys });
                Count++;
            }

            private int ClearPositions(int word)
            {
                _positions[word].Clear();
                return 0;
            }

            /// <summary>Per typed word, min(times typed, times in the name).</summary>
            private int WordsMatched()
            {
                int matched = 0;
                for (int c = 0; c < _countWord.Length; c++)
                    matched += Math.Min(_nameFreq[_countWord[c]], _countWanted[c]);
                return matched;
            }

            /// <summary>Per token, SOME alternative has all its words in <paramref name="present"/>
            /// (SeforimDbCatalogRankingKeys.EveryTokenPresent, on bits).</summary>
            private bool EveryToken(ulong present)
            {
                if (_tokenAlternatives.Length == 0) return false;
                foreach (var alts in _tokenAlternatives)
                {
                    bool any = false;
                    foreach (var alt in alts)
                    {
                        bool all = true;
                        foreach (int w in alt)
                            if ((present & Bit(w)) == 0 || w >= MaxWords) { all = false; break; }
                        if (all) { any = true; break; }
                    }
                    if (!any) return false;
                }
                return true;
            }

            private static int PopCount(ulong x)
            {
                int c = 0;
                while (x != 0) { x &= x - 1; c++; }
                return c;
            }

            /// <summary>The ranking keys, then the first matching folder in the caller's order.
            /// 0 = tie, which the path order decides.</summary>
            private int CompareRank(Entry a, Entry b)
            {
                int c = SeforimDbCatalog.SeforimDbCatalogRankingKeys.Compare(a.Keys, b.Keys, _measureProximity);
                return c != 0 ? c : a.Root.CompareTo(b.Root);
            }

            /// <summary>The best <paramref name="cap"/> matches, best first, each with its keys.
            /// Each segment's entries are sorted by rank, then by path ord (path order there);
            /// the segments are then merged on rank and, on a tie, the path bytes - read only
            /// for the current head of each segment, so about cap + segments path reads however
            /// many matches tie.</summary>
            public List<(int Doc, RankKeys Keys)> Top(int cap, IList<AtomicReaderContext> leaves)
            {
                int segments = _segments.Count;
                var heads = new int[segments];
                var sortValues = new SortedDocValues[segments];
                var headPaths = new BytesRef[segments];
                int n = 0;
                for (int s = 0; s < segments; s++)
                {
                    _segments[s].Sort((a, b) =>
                    {
                        int c = CompareRank(a, b);
                        return c != 0 ? c : a.Ord.CompareTo(b.Ord);
                    });
                    n += _segments[s].Count;
                    sortValues[s] = leaves[_segmentLeaves[s]].AtomicReader.GetSortedDocValues(FLD_PATH_SORT_DV);
                    headPaths[s] = new BytesRef();
                    ReadHeadPath(s);
                }

                void ReadHeadPath(int s)
                {
                    if (segments < 2 || heads[s] >= _segments[s].Count) return;   // one segment: never compared
                    int ord = _segments[s][heads[s]].Ord;
                    if (ord < 0 || sortValues[s] == null) headPaths[s].Length = 0;
                    else sortValues[s].LookupOrd(ord, headPaths[s]);
                }

                var top = new List<(int Doc, RankKeys Keys)>(Math.Min(cap, n));
                while (top.Count < cap)
                {
                    int best = -1;
                    for (int s = 0; s < segments; s++)
                    {
                        if (heads[s] >= _segments[s].Count) continue;
                        if (best < 0) { best = s; continue; }
                        int c = CompareRank(_segments[s][heads[s]], _segments[best][heads[best]]);
                        if (c == 0) c = headPaths[s].CompareTo(headPaths[best]);
                        if (c < 0) best = s;
                    }
                    if (best < 0) break;
                    var e = _segments[best][heads[best]];
                    top.Add((e.Doc, e.Keys));
                    heads[best]++;
                    ReadHeadPath(best);
                }
                return top;
            }
        }
        /// lower-cased, one trailing backslash.</summary>
        internal static string FolderPrefix(string folder)
            => folder.ToLowerInvariant().Replace('/', '\\').TrimEnd('\\') + "\\";

        /// <summary>
        /// Convenience overload — no limit, no total output.
        /// Keeps existing callers (tests, USN update code) compiling unchanged.
        /// </summary>
        public List<string> Search(string input, char? drive = null)
        {
            int ignored;
            var entries = Search(input, drive, limit: int.MaxValue, out ignored);
            var paths = new List<string>(entries.Count);
            foreach (var entry in entries)
                paths.Add(entry.Path);
            return paths;
        }

        /// <summary>Returns all ready NTFS drives on the system.</summary>
        public static List<DriveInfo> GetNtfsDrives() => LocalFileIndexMftCrawler.GetNtfsDrives();

        /// <summary>Returns the number of documents currently in the index.</summary>
        public int Count()
        {
            RefreshSearchView();
            var searcher = _searcherManager.Acquire();
            try   { return searcher.IndexReader.NumDocs; }
            finally { _searcherManager.Release(searcher); }
        }

        // ── Otzaria addin enrichment ──────────────────────────────────────────────

        /// <summary>
        /// After the normal index build, finds all Otzaria plugin entry-point files and
        /// stamps them with an <c>addin_name</c> field so users can search by display name.
        ///
        /// Strategy:
        ///   1. Search the just-built index for "otzaria\plugins" — returns all already-indexed
        ///      files (html, txt, …) that live inside the plugins folder tree.
        ///   2. For each hit, walk UP from the file's own directory towards "plugins\" looking
        ///      for a manifest.json. Plugin folders are NOT necessarily direct children of
        ///      "plugins\" — the real Otzaria layout nests them as
        ///      "plugins\installed\{id}\current\", so a fixed depth would never find them.
        ///   3. Parse the manifest's "name" and entry-point fields (simple hand-rolled parse —
        ///      no System.Text.Json to keep the DocumentLocator library dependency-free).
        ///   4. Resolve the entry point: "entrypoint" (the key Otzaria actually emits), with
        ///      "main" accepted as a fallback, defaulting to "index.html" when neither is set.
        ///   5. Re-index that document (UpdateDocument) with addin_name =
        ///      "תוסף אוצריא: {name}" so the prefix is baked into the indexed value and
        ///      users can find addins by searching any word in that string.
        ///   6. Commit once after all updates so the enriched documents are visible.
        /// </summary>
        private void EnrichOtzariaAddins(Action<string> progress)
        {
            // The index stores paths lower-cased; search for the plugins folder segment.
            int ignored;
            var hits = Search("otzaria\\plugins", null, int.MaxValue, out ignored);
            if (hits.Count == 0) return;

            // Collect the unique folders that actually contain a manifest.json.
            //
            // The plugin folder is NOT at a predictable depth below "plugins\": the real
            // Otzaria layout is "plugins\installed\{plugin-id}\current\manifest.json", while
            // a hand-dropped plugin may sit directly in "plugins\{name}\". So instead of
            // slicing a fixed number of segments, walk up from each indexed file towards
            // "plugins\" and take the first directory that has a manifest.json in it.
            var pluginFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var hit in hits)
            {
                // hit.Path is the full lowercased path of an indexed file inside the tree.
                string fullPath = hit.Path;
                int pluginsIdx = fullPath.IndexOf("\\plugins\\", StringComparison.OrdinalIgnoreCase);
                if (pluginsIdx < 0) continue;

                // Everything at or below "plugins\" is fair game; stop once we walk past it.
                string stopAt = fullPath.Substring(0, pluginsIdx + "\\plugins".Length);

                string dir;
                try { dir = System.IO.Path.GetDirectoryName(fullPath); }
                catch { continue; }

                while (!string.IsNullOrEmpty(dir) &&
                       dir.Length > stopAt.Length &&
                       dir.StartsWith(stopAt, StringComparison.OrdinalIgnoreCase))
                {
                    if (System.IO.File.Exists(System.IO.Path.Combine(dir, "manifest.json")))
                    {
                        pluginFolders.Add(dir);
                        break;
                    }
                    try { dir = System.IO.Path.GetDirectoryName(dir); }
                    catch { break; }
                }
            }

            int enriched = 0;
            foreach (var pluginFolder in pluginFolders)
            {
                string manifestPath = System.IO.Path.Combine(pluginFolder, "manifest.json");
                if (!System.IO.File.Exists(manifestPath)) continue;

                string json;
                // Detect the encoding instead of assuming UTF-8: a Windows-1255 manifest read
                // as UTF-8 turns the Hebrew addin name into U+FFFD replacement chars, and that
                // garbage is what gets indexed and shown in search results.
                try { json = KitveiHakodesh.Core.Common.TextEncodingDetector.ReadAllText(manifestPath); }
                catch { continue; }

                string name = ReadJsonStringField(json, "name");
                // Otzaria manifests declare the entry point as "entrypoint"; accept "main"
                // as a fallback for plugins following the more common JS convention.
                string entry = ReadJsonStringField(json, "entrypoint")
                            ?? ReadJsonStringField(json, "main");

                if (string.IsNullOrWhiteSpace(name)) continue;

                // Resolve the entry-point file. The value may be a relative path like
                // "dist/app.html" or just "index.html". Normalise separators.
                string entryPointRelative = string.IsNullOrWhiteSpace(entry)
                    ? "index.html"
                    : entry.Replace('/', '\\').TrimStart('\\');

                string entryPointPath = System.IO.Path.Combine(pluginFolder, entryPointRelative)
                                                       .ToLowerInvariant();

                // The addin_name value is baked with the prefix so every word is searchable.
                string addinName = "תוסף אוצריא: " + name;

                // UpdateDocument replaces the existing document for this path (keyed on the
                // path field term) — adding the addin_name field to it. If the entry point
                // was not previously indexed (rare: extension not in AllowedExtensions) we
                // skip rather than adding an unreachable document.
                if (!System.IO.File.Exists(entryPointPath) &&
                    !System.IO.File.Exists(entryPointPath.Replace('/', '\\')))
                    continue;

                _writer.UpdateDocument(
                    new Term(FLD_PATH, entryPointPath),
                    MakeDocument(entryPointPath, addinName));

                enriched++;
            }

            if (enriched > 0)
            {
                _writer.Commit();
                _searcherManager.MaybeRefreshBlocking();
                progress?.Invoke($"Otzaria addins enriched: {enriched}");
            }
        }

        /// <summary>
        /// Minimal hand-rolled JSON string-field reader — avoids adding a JSON library
        /// dependency to the DocumentLocator library. Reads the first occurrence of
        /// <c>"fieldName": "value"</c> (with or without spaces around the colon).
        /// Returns null when the field is absent.
        /// </summary>
        private static string ReadJsonStringField(string json, string fieldName)
        {
            string key = "\"" + fieldName + "\"";
            int keyIdx = json.IndexOf(key, StringComparison.Ordinal);
            if (keyIdx < 0) return null;

            int colonIdx = json.IndexOf(':', keyIdx + key.Length);
            if (colonIdx < 0) return null;

            int quoteStart = json.IndexOf('"', colonIdx + 1);
            if (quoteStart < 0) return null;

            var sb = new System.Text.StringBuilder();
            int i = quoteStart + 1;
            while (i < json.Length)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    char next = json[i + 1];
                    switch (next)
                    {
                        case '"':  sb.Append('"');  i += 2; continue;
                        case '\\': sb.Append('\\'); i += 2; continue;
                        case '/':  sb.Append('/');  i += 2; continue;
                        case 'n':  sb.Append('\n'); i += 2; continue;
                        case 'r':  sb.Append('\r'); i += 2; continue;
                        case 't':  sb.Append('\t'); i += 2; continue;
                        default:   sb.Append(next); i += 2; continue;
                    }
                }
                if (c == '"') break;
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        // ── Live USN journal updates ──────────────────────────────────────────────

        /// <summary>
        /// Starts one background thread per NTFS drive that blocks on
        /// <c>FSCTL_READ_USN_JOURNAL</c> with <c>BytesToWaitFor=1</c>. The kernel
        /// parks each thread in kernel-wait — zero CPU, negligible RAM — until a new
        /// change record arrives, then the thread applies the delta and blocks again.
        ///
        /// Call once after <see cref="Build"/> completes. Idempotent: a second call
        /// while threads are already running does nothing.
        ///
        /// <paramref name="onError"/> is called on the thread when a non-recoverable
        /// error occurs (journal recreated → cursor stale → need rebuild). The caller
        /// should trigger a full <see cref="Build"/> in response.
        /// </summary>
        public void StartLiveUpdates(CancellationToken ct, Action<string, Exception> onError = null)
        {
            if (_liveUpdatesStopped || (TestIndexFolder != null && string.Equals(_indexPath, DefaultIndexPath, StringComparison.OrdinalIgnoreCase))) return;
            ct = CancellationTokenSource.CreateLinkedTokenSource(ct, _stopCts.Token).Token;
            lock (_liveThreads)
            {
                if (_liveThreads.Count > 0) return; // already running

                var drives = LocalFileIndexMftCrawler.GetNtfsDrives();
                if (drives.Count == 0)
                {
                    // No NTFS volume: Build indexed the fallback drives by directory walk,
                    // and nothing can watch those, so they are all unwatched.
                    foreach (var drive in LocalFileIndexDirectoryCrawler.GetFallbackDrives())
                        _unwatchedDrives.Add(drive.Name[0]);
                    StartFolderWatchers(ct, onError);
                    return;
                }
                foreach (var drive in drives)
                {
                    char letter = drive.Name[0];
                    // Only start a thread for drives whose cursor we saved during Build.
                    // No cursor means Build had no journal access (not elevated): the drive
                    // is unwatched too, or the idle trimmer would close the index and the
                    // next search would crawl the whole drive again.
                    LocalFileIndexUsnJournal.Cursor cursor;
                    if (!LocalFileIndexUsnJournal.TryLoadCursor(_indexPath, letter, out cursor))
                    {
                        _unwatchedDrives.Add(letter);
                        continue;
                    }

                    // Probe here, not just inside the loop, so callers can tell whether
                    // the USN watcher is actually live on this drive. Without this the
                    // thread starts, returns immediately, and the index silently goes
                    // stale with nothing reporting that it happened.
                    if (!LocalFileIndexUsnJournal.CanOpenVolume(letter))
                    {
                        _unwatchedDrives.Add(letter);
                        continue;
                    }

                    var thread = new Thread(() => LiveUpdateLoop(letter, cursor, ct, onError))
                    {
                        IsBackground = true,
                        Name = $"pathindex-live-{char.ToUpper(letter)}",
                    };
                    _liveThreads.Add(thread);
                    thread.Start();
                }
                StartFolderWatchers(ct, onError);
            }
        }

        /// <summary>
        /// Watches every unwatched drive for changes the way Everything's "Folder Indexing"
        /// does without its service: no admin rights, changes applied as they happen, a
        /// rescan when the change buffer overflows, and the daily rescan as a safety net.
        /// Caller holds the _liveThreads lock.
        /// </summary>
        private void StartFolderWatchers(CancellationToken ct, Action<string, Exception> onError)
        {
            if (_folderWatchers.Count > 0) return; // already running
            foreach (char letter in _unwatchedDrives)
            {
                try
                {
                    _folderWatchers.Add(new LocalFileIndexFolderWatcher(
                        letter, ApplyFolderChanges, RescanDrive, ct, onError));
                }
                catch (Exception ex)
                {
                    // No watcher on this drive: the daily rescan still bounds staleness.
                    onError?.Invoke($"Folder watcher on {char.ToUpper(letter)}: could not start", ex);
                }
            }
        }

        /// <summary>Applies one batch of folder-watcher changes and commits once.</summary>
        private void ApplyFolderChanges(List<LocalFileIndexFolderWatcher.Change> changes, CancellationToken ct)
        {
            Interlocked.Increment(ref _backgroundWrites);
            try
            {
                foreach (var change in changes)
                {
                    if (change.Kind == LocalFileIndexFolderWatcher.ChangeKind.Removed)
                    {
                        // The watcher cannot tell whether a deleted path was a file or a
                        // folder, so drop both the path itself and anything under it.
                        string lower = change.Path.ToLowerInvariant();
                        _writer.DeleteDocuments(new Term(FLD_PATH, lower));
                        _writer.DeleteDocuments(new WildcardQuery(new Term(FLD_PATH, EscapeWildcard(lower + "\\") + "*")));
                    }
                    else if (System.IO.Directory.Exists(change.Path))
                    {
                        // A folder created or moved in: its files raise no events of their own.
                        foreach (var path in LocalFileIndexDirectoryCrawler.EnumeratePathsUnder(change.Path, ct: ct))
                            _writer.UpdateDocument(new Term(FLD_PATH, path), MakeDocument(path));
                    }
                    else if (File.Exists(change.Path)
                             && LocalFileIndexMftCrawler.IsAllowedExtension(Path.GetFileName(change.Path))
                             && !LocalFileIndexMftCrawler.IsInSkippedDirectory(change.Path))
                    {
                        _writer.UpdateDocument(new Term(FLD_PATH, change.Path.ToLowerInvariant()), MakeDocument(change.Path));
                    }
                }
                _writer.Commit();
                _searcherManager.MaybeRefreshBlocking();
            }
            finally { Interlocked.Decrement(ref _backgroundWrites); }
        }

        /// <summary>Re-crawls one drive in full: the watcher's buffer overflowed, so some
        /// changes were lost.</summary>
        private void RescanDrive(char driveLetter, CancellationToken ct)
        {
            Interlocked.Increment(ref _backgroundWrites);
            try
            {
                lock (_rebuildLock)
                {
                    var paths = LocalFileIndexDirectoryCrawler.EnumeratePathsWithFallback(driveLetter, ct: ct);
                    ct.ThrowIfCancellationRequested();
                    RebuildDrive(driveLetter, paths);
                    _writer.Commit();
                    _searcherManager.MaybeRefreshBlocking();
                }
            }
            finally { Interlocked.Decrement(ref _backgroundWrites); }
        }

        /// <summary>
        /// Drives that were indexed but have no live USN watcher (process not elevated, or
        /// not NTFS). A folder watcher follows their changes instead, and
        /// <see cref="StartPeriodicRescan"/> catches whatever it missed.
        /// </summary>
        public bool HasUnwatchedDrives
        {
            get { lock (_liveThreads) return _unwatchedDrives.Count > 0; }
        }

        /// <summary>
        /// Starts a background timer that periodically rebuilds the index, to recover the
        /// changes a missing USN watcher cannot see.
        ///
        /// This is the mitigation for running unelevated, and it is the same design the
        /// Everything search engine uses for its no-NTFS "Folder Indexing" mode, whose
        /// documented behaviour is worth restating because it sets the honest expectation:
        ///   * "Almost all changes are detected, but changes can be missed if too many
        ///     occur in a short time."
        ///   * It rescans when its change buffer overflows ("Rescan on full buffer").
        ///   * It rescans folder indexes daily (Everything uses 3 AM), and if the machine
        ///     was off at that time, on the next run instead.
        ///
        /// So a rescan is a correctness backstop, not a nicety: without it an unelevated
        /// index is frozen at build time forever. With it, staleness is bounded by
        /// <paramref name="interval"/>.
        ///
        /// No-ops when every indexed drive already has a live USN watcher — an elevated
        /// process gets exact deltas and must not pay for whole-drive rescans. Also
        /// no-ops if called twice.
        ///
        /// Production registers this indexer as a LocalSystem service, so the no-op branch
        /// is the normal one there. This is NOT dead code: it carries the dev rig (spawned
        /// from an ordinary terminal) and any run where elevation was declined or lost.
        /// Note it bounds staleness only — it cannot restore COVERAGE, since an unelevated
        /// crawl never sees ACL-protected trees in the first place.
        ///
        /// <paramref name="interval"/> bounds how stale the index can get. Daily is a
        /// reasonable default: a rescan is a full crawl, so it is far too expensive to run
        /// often. <paramref name="onRescan"/> is invoked (on the rescan thread) before each
        /// pass so callers can log it; exceptions from the rebuild are passed to
        /// <paramref name="onError"/> and the loop continues.
        /// </summary>
        public void StartPeriodicRescan(
            TimeSpan interval,
            CancellationToken ct,
            Action<string> onRescan = null,
            Action<string, Exception> onError = null)
        {
            if (_liveUpdatesStopped) return;

            // Elevated (or nothing indexed) — deltas are exact, a rescan would be waste.
            if (!HasUnwatchedDrives) return;

            if (interval <= TimeSpan.Zero) return;

            ct = CancellationTokenSource.CreateLinkedTokenSource(ct, _stopCts.Token).Token;
            lock (_rescanLock)
            {
                if (_rescanThread != null) return; // already running

                _rescanThread = new Thread(() => RescanLoop(interval, ct, onRescan, onError))
                {
                    IsBackground = true,
                    Name = "pathindex-rescan",
                };
                _rescanThread.Start();
            }
        }

        /// <summary>
        /// Sleeps in short slices so cancellation is responsive, and rebuilds whenever a
        /// full interval has elapsed since the last rescan.
        ///
        /// Deliberately does NOT rescan immediately on start: Build has just run, so the
        /// index is fresh. The first rescan lands one interval later.
        /// </summary>
        private void RescanLoop(
            TimeSpan interval,
            CancellationToken ct,
            Action<string> onRescan,
            Action<string, Exception> onError)
        {
            lock (_rescanLock) _lastRescanUtc = DateTime.UtcNow;

            // Poll in slices rather than one long sleep: a machine that suspends and
            // resumes must not skip a rescan, and cancellation must not wait a full day.
            var slice = TimeSpan.FromMinutes(1);

            while (!ct.IsCancellationRequested && !_liveUpdatesStopped)
            {
                try
                {
                    if (ct.WaitHandle.WaitOne(slice)) return; // cancelled
                }
                catch (ObjectDisposedException) { return; }

                if (ct.IsCancellationRequested || _liveUpdatesStopped) return;

                DateTime last;
                lock (_rescanLock) last = _lastRescanUtc;
                if (DateTime.UtcNow - last < interval) continue;

                try
                {
                    onRescan?.Invoke("Rescanning file index (no live file-system watcher)");

                    Interlocked.Increment(ref _backgroundWrites);
                    try { lock (_rebuildLock) Build(null, ct); }
                    finally { Interlocked.Decrement(ref _backgroundWrites); }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    onError?.Invoke("Periodic index rescan failed", ex);
                }
                finally
                {
                    // Stamp even on failure so a persistently failing rescan retries on
                    // the next interval instead of spinning every minute.
                    lock (_rescanLock) _lastRescanUtc = DateTime.UtcNow;
                }
            }
        }

        /// <summary>
        /// The per-drive live-update loop. Blocks on FSCTL_READ_USN_JOURNAL (kernel
        /// wait, zero CPU) until records arrive, then applies the delta. Exits cleanly
        /// when the CancellationToken is cancelled (which causes the volume handle to
        /// be closed, unblocking the kernel wait immediately).
        ///
        /// Exits silently (no error callback) when the volume cannot be opened — which
        /// happens when the process is not elevated. <see cref="StartLiveUpdates"/>
        /// pre-screens for that and skips starting this loop at all, so reaching the check
        /// here means access was lost after the pre-screen. The re-check is kept because it
        /// is cheap and this loop must never hammer onError on an inaccessible volume.
        ///
        /// Note the index is NOT self-maintaining without this loop: the build still
        /// happens (via LocalFileIndexDirectoryCrawler), but nothing tracks changes afterwards. See
        /// <see cref="StartPeriodicRescan"/> for the fallback that bounds staleness.
        /// </summary>
        private void LiveUpdateLoop(
            char driveLetter, LocalFileIndexUsnJournal.Cursor cursor,
            CancellationToken ct, Action<string, Exception> onError)
        {
            // Probe admin access first. OpenVolume throws when not elevated;
            // exit the loop silently rather than hammering onError repeatedly.
            if (!LocalFileIndexUsnJournal.CanOpenVolume(driveLetter)) return;

            while (!ct.IsCancellationRequested && !_liveUpdatesStopped)
            {
                try
                {
                    // Block until at least one new record is available on this drive.
                    // LocalFileIndexUsnJournal.WaitForChanges parks the thread in the kernel; when
                    // the token fires it closes the volume handle to unblock the call.
                    var waitResult = LocalFileIndexUsnJournal.WaitForChanges(driveLetter, cursor, ct);
                    if (ct.IsCancellationRequested || _liveUpdatesStopped) return;

                    if (!waitResult.Ok)
                    {
                        // Journal was recreated or cursor is too old — need full rebuild.
                        onError?.Invoke(
                            $"Live update on {char.ToUpper(driveLetter)}: USN journal cursor stale — full rebuild needed",
                            null);
                        return;
                    }

                    // Now drain all available records (non-blocking, same as startup).
                    LocalFileIndexUsnJournal.ReadChangesResult readResult;
                    var changes = LocalFileIndexUsnJournal.ReadChanges(driveLetter, cursor, out readResult);

                    if (!readResult.Ok)
                    {
                        onError?.Invoke(
                            $"Live update on {char.ToUpper(driveLetter)}: ReadChanges failed — full rebuild needed",
                            null);
                        return;
                    }

                    if (changes != null)
                    {
                        var changeList = new List<LocalFileIndexUsnJournal.ChangeRecord>(changes);
                        if (changeList.Count > 0)
                        {
                            bool ok;
                            Interlocked.Increment(ref _backgroundWrites);
                            try { ok = ApplyDelta(driveLetter, changeList, readResult.AdvancedCursor); }
                            finally { Interlocked.Decrement(ref _backgroundWrites); }
                            if (!ok)
                            {
                                // Delta too large — full rebuild needed.
                                onError?.Invoke(
                                    $"Live update on {char.ToUpper(driveLetter)}: delta too large — full rebuild needed",
                                    null);
                                return;
                            }
                        }
                    }

                    cursor = readResult.AdvancedCursor;
                    LocalFileIndexUsnJournal.SaveCursor(_indexPath, driveLetter, cursor);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested || _liveUpdatesStopped) return;
                    onError?.Invoke(
                        $"Live update on {char.ToUpper(driveLetter)}: unexpected error", ex);
                    // Back off briefly before retrying so we don't spin on a persistent error.
                    Thread.Sleep(5_000);
                }
            }
        }

        /// <summary>
        /// Apply a delta from the USN journal to the index. Returns false when the
        /// delta exceeds <see cref="RebuildThresholdFraction"/> of the index size
        /// (caller should trigger a full rebuild instead).
        /// </summary>
        private bool ApplyDelta(
            char driveLetter, List<LocalFileIndexUsnJournal.ChangeRecord> changes,
            LocalFileIndexUsnJournal.Cursor advancedCursor)
        {
            int totalDelta   = changes.Count;
            int currentCount = Count();
            if (currentCount > 0 && totalDelta > currentCount * RebuildThresholdFraction)
                return false; // too large — signal caller to full-rebuild

            var deleted  = new List<LocalFileIndexUsnJournal.ChangeRecord>();
            var upserted = new List<LocalFileIndexUsnJournal.ChangeRecord>();
            foreach (var rec in changes)
            {
                switch (rec.Reason)
                {
                    case LocalFileIndexUsnJournal.ChangeReason.Deleted:
                    case LocalFileIndexUsnJournal.ChangeReason.RenamedOldName:
                        deleted.Add(rec);  break;
                    case LocalFileIndexUsnJournal.ChangeReason.Created:
                    case LocalFileIndexUsnJournal.ChangeReason.RenamedNewName:
                        upserted.Add(rec); break;
                }
            }

            foreach (var rec in deleted)
            {
                if (rec.IsDirectory && LocalFileIndexMftCrawler.IsSkippedDirectoryName(rec.Name))
                    continue;   // nothing under a skipped folder is indexed
                if (rec.IsDirectory)
                    RemoveByPathPrefix(char.ToLower(driveLetter) + ":", rec.Name);
                else
                    RemoveByName(char.ToLower(driveLetter) + ":", rec.Name);
            }

            if (upserted.Count > 0)
            {
                var frnSet = new HashSet<ulong>();
                foreach (var rec in upserted) { frnSet.Add(rec.Frn); frnSet.Add(rec.ParentFrn); }

                var snapshot = LocalFileIndexMftCrawler.SnapshotEntries(driveLetter, frnSet);
                string root  = char.ToUpper(driveLetter) + ":";
                foreach (var rec in upserted)
                {
                    if (rec.IsDirectory) continue;
                    string fullPath = LocalFileIndexMftCrawler.ResolvePath(
                        rec.Frn, rec.Name, rec.ParentFrn, snapshot, root);
                    if (fullPath != null)
                        _writer.UpdateDocument(
                            new Term(FLD_PATH, fullPath.ToLowerInvariant()),
                            MakeDocument(fullPath));
                }
            }

            _writer.Commit();
            _searcherManager.MaybeRefreshBlocking();
            return true;
        }

        // ── Dispose ───────────────────────────────────────────────────────────────

        public void Dispose()
        {
            // Also stops the periodic-rescan loop, which polls this flag between slices.
            _liveUpdatesStopped = true;
            try { _stopCts.Cancel(); } catch (ObjectDisposedException) { }

            // Let the live threads see the cancellation and exit before the writer closes, so
            // none is mid-delta against a disposed writer. Their wait is a kernel call that the
            // cancellation unblocks at once; the timeout only bounds a thread stuck applying a
            // large delta (it then fails on the closed writer, and since its cursor was not
            // saved, that delta is simply replayed from the journal by the next Build).
            Thread[] live;
            LocalFileIndexFolderWatcher[] watchers;
            lock (_liveThreads)
            {
                live = _liveThreads.ToArray();
                watchers = _folderWatchers.ToArray();
            }
            foreach (var w in watchers) w.Dispose();
            foreach (var t in live)
            {
                if (t != Thread.CurrentThread) t.Join(TimeSpan.FromSeconds(2));
            }

            _searcherManager?.Dispose();
            _writer?.Dispose();
            _directory?.Dispose();
        }
    }

    // ── PathEntry ─────────────────────────────────────────────────────────────
    // Carries a full path and its last-write time (Unix ms as a string) out of
    // the Lucene index. The string representation avoids a long→string conversion
    // at every layer — callers embed it directly into JSON.

    public sealed class PathEntry
    {
        public string Path { get; }
        /// <summary>Last-write time as Unix milliseconds (string), e.g. "1720000000000". "0" if unknown.</summary>
        public string DateMs { get; }
        /// <summary>
        /// Non-empty only for Otzaria addin entry-point files. Value is the lowercased
        /// form of "תוסף אוצריא: {name from manifest.json}" as stored in the index.
        /// Empty string for all other documents.
        /// </summary>
        public string AddinName { get; }
        public PathEntry(string path, string dateMs, string addinName = "")
        { Path = path; DateMs = dateMs; AddinName = addinName ?? ""; }
        /// <summary>The entry's catalog ranking keys, read from the index while collecting
        /// (the order the search returned it in; the UserSpace merge reuses them).</summary>
        internal RankKeys Keys { get; set; }
    }

    // ── PathAnalyzer ──────────────────────────────────────────────────────────
    // Treats each field value as a single token — identical to KeywordAnalyzer
    // but defined here so we don't need Lucene.Net.Analysis.Common (5 MB).

    internal sealed class PathAnalyzer : Analyzer
    {
        // The one field that is a word SEQUENCE (name_seq: positions) splits on spaces.
        private const string WordSequenceField = "name_seq";

        public PathAnalyzer() : base(PER_FIELD_REUSE_STRATEGY) { }

        protected override TokenStreamComponents CreateComponents(
            string fieldName, System.IO.TextReader reader)
        {
            Tokenizer tok = fieldName == WordSequenceField
                ? new SpaceTokenizer(Lucene.Net.Util.AttributeSource.AttributeFactory.DEFAULT_ATTRIBUTE_FACTORY, reader)
                : new PathTokenizer(Lucene.Net.Util.AttributeSource.AttributeFactory.DEFAULT_ATTRIBUTE_FACTORY, reader);
            return new TokenStreamComponents(tok, tok);
        }
    }

    /// <summary>One token per space-separated word, in order (positions follow). The words
    /// come pre-normalized from LocalFileNameWords, so nothing else is done to them.</summary>
    internal sealed class SpaceTokenizer : Tokenizer
    {
        private readonly ICharTermAttribute _termAtt;
        private string[] _words;
        private int _next;

        public SpaceTokenizer(Lucene.Net.Util.AttributeSource.AttributeFactory factory, System.IO.TextReader input)
            : base(factory, input)
        {
            _termAtt = AddAttribute<ICharTermAttribute>();
        }

        public override bool IncrementToken()
        {
            _words ??= m_input.ReadToEnd().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (_next >= _words.Length) return false;
            ClearAttributes();
            _termAtt.SetEmpty().Append(_words[_next++]);
            return true;
        }

        public override void Reset()
        {
            base.Reset();
            _words = null;
            _next = 0;
        }
    }

    internal sealed class PathTokenizer : Tokenizer
    {
        private readonly ICharTermAttribute _termAtt;
        private bool _done;

        public PathTokenizer(Lucene.Net.Util.AttributeSource.AttributeFactory factory, System.IO.TextReader input)
            : base(factory, input)
        {
            _termAtt = AddAttribute<ICharTermAttribute>();
        }

        public override bool IncrementToken()
        {
            if (_done) return false;
            _done = true;
            ClearAttributes();
            var sb  = new System.Text.StringBuilder();
            var buf = new char[256];
            int n;
            while ((n = m_input.Read(buf, 0, buf.Length)) > 0)
                sb.Append(buf, 0, n);
            _termAtt.SetEmpty().Append(sb);
            return true;
        }

        public override void Reset()
        {
            base.Reset();
            _done = false;
        }
    }
}
