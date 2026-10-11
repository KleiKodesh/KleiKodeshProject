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
    /// Each word becomes a *word* WildcardQuery so mid-word matches are included.
    /// </summary>
    public sealed class LocalFileIndex : IDisposable
    {
        private const LuceneVersion VERSION        = LuceneVersion.LUCENE_48;
        private const string        FLD_PATH      = "path";
        private const string        FLD_DATE      = "date";       // last-write time as Unix ms (stored, not indexed)
        private const string        FLD_ADDIN_NAME = "addin_name"; // optional: "תוסף אוצריא: {name}" (indexed + stored, lower-cased)
        private const string        FLD_ADDIN_NAME_DISPLAY = "addin_name_display"; // same value, original case, stored only

        /// <summary>
        /// If the USN journal delta for a drive contains more than this fraction
        /// of the current indexed document count, skip incremental update and do
        /// a full MFT rebuild instead — it's cheaper at that point.
        /// 0.20 = 20% of indexed paths changed → full rebuild.
        /// </summary>
        private const double RebuildThresholdFraction = 0.20;

        /// <summary>
        /// Default index location: an "index" subfolder in the current AppDomain's base directory.
        ///
        /// This is typically the folder where the executing assembly (service exe or library caller)
        /// lives. When the service is deployed alongside the demo, both see the same path.
        /// </summary>
        public static readonly string DefaultIndexPath =
            System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "filesystemindex");

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

        // Drives indexed WITHOUT a live USN watcher (volume handle refused — not elevated).
        // Guarded by the _liveThreads lock, which already serialises live-update state.
        private readonly List<char> _unwatchedDrives = new List<char>();

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

            // PathAnalyzer — each path is one token, stored exactly as-is.
            // Searching uses WildcardQuery so no tokenization is needed at query time.
            var config = new IndexWriterConfig(VERSION, new PathAnalyzer())
            {
                OpenMode = OpenMode.CREATE_OR_APPEND
            };

            _writer          = new IndexWriter(_directory, config);
            _searcherManager = new SearcherManager(_writer, applyAllDeletes: true, searcherFactory: null);
        }

        // ── Build / Update ────────────────────────────────────────────────────────

        /// <summary>
        /// Smart startup: if a USN cursor exists for every drive, applies only the
        /// delta since the last run (fast — seconds). Falls back to a full MFT
        /// rebuild for any drive whose cursor is missing or stale.
        /// <paramref name="progress"/> receives a status string at each milestone.
        /// </summary>
        public void Build(Action<string> progress = null, CancellationToken ct = default)
        {
            var drives = LocalFileIndexMftCrawler.GetNtfsDrives();
            if (drives.Count == 0)
            {
                // No NTFS volume detected — index whatever fixed drives exist
                // through the recursive directory walk instead of giving up.
                drives = LocalFileIndexDirectoryCrawler.GetFallbackDrives();
                if (drives.Count == 0)
                    throw new InvalidOperationException("No indexable drives found.");
            }

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
                        continue;
                    }

                    progress?.Invoke($"{letter}: journal cursor stale, rebuilding…");
                }
                // Full rebuild for this drive: MFT crawl with automatic fallback to
                // a recursive directory walk when the MFT is blocked or yields nothing.
                var paths = LocalFileIndexDirectoryCrawler.EnumeratePathsWithFallback(
                    letter,
                    msg => progress?.Invoke(msg),
                    count => progress?.Invoke($"Crawling {letter}:… {count:N0} entries"),
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
            }

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
            int totalDelta   = deleted.Count + upserted.Count;
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

        private static Document MakeDocument(string fullPath, string addinName = null)
        {
            var doc = new Document();
            doc.Add(new Field(FLD_PATH, fullPath.ToLowerInvariant(), PathFieldType));
            doc.Add(new Field(FLD_DATE, GetLastWriteUnixMs(fullPath), DateFieldType));
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
        /// Returns path+date pairs where ALL words in <paramref name="input"/> appear as
        /// substrings — equivalent to:
        ///   paths.Where(p => p.Contains(word1) && p.Contains(word2) && ...)
        ///
        /// Each word is wrapped as *word* so mid-word matches are included.
        /// Optionally scope to a single drive letter (e.g. 'C').
        ///
        /// <paramref name="limit"/> caps the number of entries returned.
        /// 0 (default) means unlimited — every matching document is returned.
        /// When a limit is active, <paramref name="total"/> receives the full
        /// match count so callers can report "showing X of Y results".
        /// </summary>
        public List<PathEntry> Search(string input, char? drive, int limit, out int total)
        {
            total = 0;
            var results = new List<PathEntry>();
            if (string.IsNullOrWhiteSpace(input)) return results;

            string[] words = input.Trim().Split(
                new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            var boolQuery = new BooleanQuery();

            foreach (var word in words)
            {
                // Backslash is the ESCAPE character in Lucene wildcard syntax, so a literal
                // path separator has to be doubled. Without this, a query like
                // "otzaria\plugins" escapes the 'p' and silently matches nothing — even
                // though both "otzaria" and "plugins" match on their own.
                string w = EscapeWildcard(word.ToLowerInvariant());
                // Each word must appear somewhere in EITHER the path OR the addin_name.
                // A sub-query ORing both fields ensures addin names are searched without
                // requiring the word to also appear in the file path.
                var wordPattern = "*" + w + "*";
                var perWord = new BooleanQuery();
                perWord.Add(new WildcardQuery(new Term(FLD_PATH, wordPattern)),      Occur.SHOULD);
                perWord.Add(new WildcardQuery(new Term(FLD_ADDIN_NAME, wordPattern)), Occur.SHOULD);
                boolQuery.Add(perWord, Occur.MUST);
            }

            // Scope to drive: require path to start with "c:\" etc.
            if (drive.HasValue)
            {
                boolQuery.Add(
                    new WildcardQuery(new Term(FLD_PATH, char.ToLower(drive.Value) + ":*")),
                    Occur.MUST);
            }

            _searcherManager.MaybeRefresh();
            var searcher = _searcherManager.Acquire();
            try
            {
                var topDocs = searcher.Search(boolQuery, int.MaxValue);
                total = topDocs.TotalHits;

                int take = (limit > 0 && limit != int.MaxValue)
                    ? Math.Min(limit, topDocs.ScoreDocs.Length)
                    : topDocs.ScoreDocs.Length;
                for (int i = 0; i < take; i++)
                {
                    var doc       = searcher.Doc(topDocs.ScoreDocs[i].Doc);
                    string path      = doc.Get(FLD_PATH);
                    string date      = doc.Get(FLD_DATE) ?? "0";
                    // Prefer the original-case copy; fall back to the lower-cased searchable
                    // one so an index built before that field existed still returns a name.
                    string addinName = doc.Get(FLD_ADDIN_NAME_DISPLAY)
                                    ?? doc.Get(FLD_ADDIN_NAME)
                                    ?? "";
                    results.Add(new PathEntry(path, date, addinName));
                }
            }
            finally
            {
                _searcherManager.Release(searcher);
            }

            return results;
        }

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
            _searcherManager.MaybeRefresh();
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
                try { json = ReadTextDetectEncoding(manifestPath); }
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
        /// Reads a text file, detecting its encoding rather than assuming UTF-8. Honors a BOM if
        /// present; otherwise validates the bytes as UTF-8 and falls back to Windows-1255
        /// (Hebrew ANSI), the common legacy encoding for Hebrew text files on Windows.
        /// </summary>
        private static string ReadTextDetectEncoding(string filePath)
        {
            byte[] b = System.IO.File.ReadAllBytes(filePath);

            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
                return new System.Text.UTF8Encoding(false).GetString(b, 3, b.Length - 3);
            if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xFE && b[2] == 0x00 && b[3] == 0x00)
                return new System.Text.UTF32Encoding(false, false).GetString(b, 4, b.Length - 4);
            if (b.Length >= 4 && b[0] == 0x00 && b[1] == 0x00 && b[2] == 0xFE && b[3] == 0xFF)
                return new System.Text.UTF32Encoding(true, false).GetString(b, 4, b.Length - 4);
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
                return System.Text.Encoding.Unicode.GetString(b, 2, b.Length - 2);
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
                return System.Text.Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);

            return IsValidUtf8(b)
                ? new System.Text.UTF8Encoding(false).GetString(b)
                : Windows1255.GetString(b);
        }

        /// <summary>
        /// The Windows-1255 (Hebrew ANSI) encoding. On .NET Framework the codepage is built in;
        /// on .NET it lives in CodePagesEncodingProvider and <c>Encoding.GetEncoding(1255)</c>
        /// throws NotSupportedException unless something registered that provider process-wide.
        /// Nothing in the service does, so take the encoding straight from the provider instead
        /// of depending on global state we do not own.
        /// </summary>
        private static readonly System.Text.Encoding Windows1255 =
#if NET
            System.Text.CodePagesEncodingProvider.Instance.GetEncoding(1255);
#else
            System.Text.Encoding.GetEncoding(1255);
#endif

        /// <summary>
        /// True if <paramref name="bytes"/> is well-formed UTF-8 (pure ASCII counts), rejecting
        /// overlong encodings, surrogates and out-of-range code points. This is the test that
        /// distinguishes UTF-8 without a BOM from a single-byte codepage such as Windows-1255.
        /// </summary>
        private static bool IsValidUtf8(byte[] bytes)
        {
            int i = 0;
            while (i < bytes.Length)
            {
                byte b = bytes[i];
                int extra;      // continuation bytes expected after b
                int min;        // lowest code point legal for this length (rejects overlong)
                if (b <= 0x7F) { i++; continue; }
                else if ((b & 0xE0) == 0xC0) { extra = 1; min = 0x80; }
                else if ((b & 0xF0) == 0xE0) { extra = 2; min = 0x800; }
                else if ((b & 0xF8) == 0xF0) { extra = 3; min = 0x10000; }
                else return false;

                if (i + extra >= bytes.Length) return false;
                int cp = b & (0x7F >> (extra + 1));
                for (int k = 1; k <= extra; k++)
                {
                    byte c = bytes[i + k];
                    if ((c & 0xC0) != 0x80) return false;
                    cp = (cp << 6) | (c & 0x3F);
                }
                if (cp < min) return false;
                if (cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF)) return false;
                i += extra + 1;
            }
            return true;
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
            if (_liveUpdatesStopped) return;
            lock (_liveThreads)
            {
                if (_liveThreads.Count > 0) return; // already running

                var drives = LocalFileIndexMftCrawler.GetNtfsDrives();
                foreach (var drive in drives)
                {
                    char letter = drive.Name[0];
                    // Only start a thread for drives whose cursor we saved during Build.
                    LocalFileIndexUsnJournal.Cursor cursor;
                    if (!LocalFileIndexUsnJournal.TryLoadCursor(_indexPath, letter, out cursor)) continue;

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
            }
        }

        /// <summary>
        /// NTFS drives that were indexed but have no live USN watcher, because the volume
        /// handle was refused (process not elevated). For these the index is a
        /// point-in-time snapshot — nothing tracks changes after the build, so
        /// <see cref="StartPeriodicRescan"/> is what keeps it current.
        /// </summary>
        private bool HasUnwatchedDrives
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

                    Build(null, ct);
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
                            bool ok = ApplyDelta(driveLetter, changeList, readResult.AdvancedCursor);
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
    }

    // ── PathAnalyzer ──────────────────────────────────────────────────────────
    // Treats each field value as a single token — identical to KeywordAnalyzer
    // but defined here so we don't need Lucene.Net.Analysis.Common (5 MB).

    internal sealed class PathAnalyzer : Analyzer
    {
        protected override TokenStreamComponents CreateComponents(
            string fieldName, System.IO.TextReader reader)
        {
            var tok = new PathTokenizer(Lucene.Net.Util.AttributeSource.AttributeFactory.DEFAULT_ATTRIBUTE_FACTORY, reader);
            return new TokenStreamComponents(tok, tok);
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
