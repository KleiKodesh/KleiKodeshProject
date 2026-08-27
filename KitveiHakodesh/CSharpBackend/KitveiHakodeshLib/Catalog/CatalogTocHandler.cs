using KitveiHakodeshLib.Bridge;
using KitveiHakodeshLib.Settings;
using KitveiHakodeshService.Catalog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodeshLib.Catalog
{
    /// <summary>
    /// Hosted (net48) front end for the catalog TOC Lucene search — the counterpart of
    /// KitveiHakodeshService's CatalogTocSearchService, over the SAME shared
    /// <see cref="CatalogTocIndex"/> engine (linked into this project, see the csproj).
    ///
    /// Why this exists: the catalog search used to be service-only, so the hosted app fell
    /// back to a two-phase in-memory heuristic (book match + TOC keyword rules) that could
    /// not match the Lucene index's ranking. The service is not deployed with the hosted
    /// app, so reaching it was never an option — porting the engine was.
    ///
    /// Index location: "CatalogTocIndex" next to the app binary, matching the FTS index and
    /// the service's own convention.
    ///
    /// Threading mirrors the service: build runs on a background task and the index stays
    /// searchable throughout via Lucene's near-real-time reader, so results appear during
    /// the very first build rather than after it.
    /// </summary>
    public sealed class CatalogTocHandler : IDisposable
    {
        private readonly WebBridge _bridge;
        private readonly string _indexPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CatalogTocIndex");

        private readonly object _lock = new object();
        private CatalogTocIndex _index;
        private Task _buildTask;
        private CancellationTokenSource _buildCts;

        private volatile bool _isReady;      // an index (fresh or stale) is open and serving
        private volatile bool _isIndexing;
        private volatile bool _buildStarted; // latch: build at most once per process

        // Failed build attempts. A failure re-arms the latch so the next Ensure retries,
        // but only up to this many times — otherwise a build that always fails would be
        // restarted by every status call and search keystroke.
        private const int MaxBuildAttempts = 3;
        private int _failedBuilds;

        // True while ResetIndexCore owns the wipe-and-rebuild. A failing build must NOT
        // re-arm the build latch during that window — see the catch in RunBuild.
        private bool _resetInFlight;
        private volatile int _builtBooks;
        private volatile int _totalBooks;

        // A new search SUPERSEDES the previous in-flight one (latest-wins) so an abandoned
        // heavy query stops burning cores as soon as the user keeps typing.
        private CancellationTokenSource _searchCts;

        public CatalogTocHandler(WebBridge bridge)
        {
            _bridge = bridge;
        }

        /// <summary>
        /// The seforim DB is read fresh each time rather than cached: the user can repoint it
        /// from settings mid-session, and the hosted app rebuilds around the new path without
        /// restarting. Reading the setting is cheap.
        /// </summary>
        private static string DbPath
        {
            get
            {
                try { return AppSettings.LoadDbPath(); }
                catch { return null; }
            }
        }

        private static bool HasDb(string path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

        private CatalogTocIndex GetIndex(string dbPath)
        {
            lock (_lock) { return _index ?? (_index = new CatalogTocIndex(_indexPath, dbPath)); }
        }

        /// <summary>
        /// Idempotent. Opens the existing index (even a stale one — no downtime) and, when the
        /// stored DB hash differs from the current one, kicks off a background rebuild that
        /// builds IN PLACE and stays searchable meanwhile.
        /// </summary>
        public void EnsureIndex()
        {
            string dbPath = DbPath;
            if (!HasDb(dbPath)) return;
            if (_buildStarted && _isReady) return;

            lock (_lock)
            {
                if (_buildStarted) return;

                string currentHash;
                try { currentHash = CatalogTocIndex.ComputeDbHash(dbPath); }
                catch (Exception ex)
                {
                    Console.WriteLine("[CatalogToc] could not hash seforim DB: " + ex.Message);
                    return;
                }

                var index = GetIndex(dbPath);
                bool opened = index.TryOpenActive();
                if (opened) _isReady = true; // stale or fresh — either way, keep serving

                if (opened && string.Equals(index.ActiveHash, currentHash, StringComparison.OrdinalIgnoreCase))
                {
                    _buildStarted = true; // up to date — nothing to build
                    return;
                }

                Console.WriteLine(opened
                    ? "[CatalogToc] index is stale (seforim DB changed) — rebuilding in the background"
                    : "[CatalogToc] index missing — building");

                _buildStarted = true;
                _isIndexing = true;
                _buildCts = new CancellationTokenSource();
                var token = _buildCts.Token;
                _buildTask = Task.Run(() => RunBuild(dbPath, currentHash, token));
            }
        }

        private void RunBuild(string dbPath, string dbHash, CancellationToken ct)
        {
            try
            {
                Directory.CreateDirectory(_indexPath);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int docs = GetIndex(dbPath).BuildAndSwitch(
                    dbHash,
                    onProgress: (done, total) => { _builtBooks = done; _totalBooks = total; },
                    ct: ct);
                _isReady = true;
                Console.WriteLine(string.Format(
                    "[CatalogToc] build complete — {0} docs in {1:F1}s", docs, sw.Elapsed.TotalSeconds));
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[CatalogToc] build cancelled");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[CatalogToc] build failed: " + ex);
                // Re-arm so a later Ensure can retry. _buildStarted is set BEFORE the build
                // and was never cleared on failure, so one transient error (the seforim DB
                // locked by the FTS rebuild, a full disk) left EnsureIndex a permanent no-op
                // and catalog search dead until the app restarted. Bounded, so a
                // deterministically failing build cannot spin on every keystroke.
                //
                // NOT during a reset: ResetIndexCore cancels this build, then deletes the
                // index directory, then re-arms and rebuilds itself. Re-arming here would let
                // a status poll start a build into the directory that reset is about to
                // delete — and that build's failure would burn another attempt, so one
                // unlucky reset could exhaust the budget and kill catalog search outright.
                // Interlocked: two builds can be in flight across a re-arm, and a lost
                // increment would turn a bounded retry into an unbounded one.
                int failures = Interlocked.Increment(ref _failedBuilds);
                bool resetOwnsTheRebuild;
                lock (_lock) { resetOwnsTheRebuild = _resetInFlight; }
                if (resetOwnsTheRebuild)
                {
                    Console.WriteLine("[CatalogToc] build failed during a reset — the reset will rebuild");
                }
                else if (failures < MaxBuildAttempts)
                {
                    lock (_lock) { _buildStarted = false; }
                }
                else
                {
                    Console.WriteLine(
                        "[CatalogToc] giving up after " + failures + " failed build attempts");
                }
            }
            finally
            {
                _isIndexing = false;
            }
        }

        // ── Bridge actions ──────────────────────────────────────────────────────────────

        /// <summary>
        /// catalogTocSearch — runs on a background task, NOT the UI thread. A catalog query
        /// walks the whole Lucene index and is easily tens of milliseconds; doing that inline
        /// would block the WebView2 message pump and stutter typing, since this fires on every
        /// keystroke.
        /// </summary>
        public void HandleSearch(JsonElement root, string id)
        {
            string query = root.TryGetProperty("query", out var q) ? q.GetString() : null;
            Task.Run(() =>
            {
                try { _bridge.Reply(id, Search(query)); }
                catch (Exception ex)
                {
                    _bridge.Reply(id, new { ready = false, results = new object[0], error = ex.Message });
                }
            });
        }

        private object Search(string query)
        {
            EnsureIndex();
            string dbPath = DbPath;

            // Ready as soon as a reader exists — during a build the near-real-time reader
            // serves partial results, so this does not gate on the build finishing.
            bool ready = _isReady || (HasDb(dbPath) && GetIndex(dbPath).TryOpenActive());
            if (!ready) return new { ready = false, results = new object[0] };
            _isReady = true;

            if (string.IsNullOrWhiteSpace(query))
                return new { ready = true, results = new object[0] };

            using (var cts = new CancellationTokenSource())
            {
                var prev = Interlocked.Exchange(ref _searchCts, cts);
                // Cancel the superseded search so it stops burning cores. Deliberately NOT
                // disposed here: its own Search call is still reading that token on another
                // thread and would take an ObjectDisposedException mid-flight. Each search
                // disposes its own CTS via this `using` once it unwinds.
                if (prev != null)
                {
                    try { prev.Cancel(); } catch (ObjectDisposedException) { }
                }

                try
                {
                    var hits = GetIndex(dbPath).Search(query, cts.Token);
                    return new { ready = true, results = ToWire(hits) };
                }
                catch (OperationCanceledException)
                {
                    // A newer search took over — the caller discards this response.
                    return new { ready = true, results = new object[0], superseded = true };
                }
                catch (Exception ex) when (ex is ObjectDisposedException
                                              || ex is DirectoryNotFoundException
                                              || ex is FileNotFoundException)
                {
                    // A reset ran under us: it disposes the index (searches are cancelled first,
                    // but the last stretch of a query is not inside a lock) and deletes the
                    // directory the reader's files live in. Neither is a search failure — same
                    // contract as a supersede: the caller discards this and the rebuild serves
                    // the next one. Reported as an error instead, the UI showed "no results" for
                    // a perfectly good query.
                    return new { ready = true, results = new object[0], superseded = true };
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[CatalogToc] search failed: " + ex);
                    return new { ready = true, results = new object[0], error = ex.Message };
                }
                finally
                {
                    // MUST clear the field, and only if it still points at THIS search's source.
                    // The `using` above disposes cts on the way out; leaving _searchCts pointing
                    // at a disposed object means every later supersede attempt hits an already-
                    // disposed CTS, swallows the ObjectDisposedException, and silently cancels
                    // nothing — which disables supersession entirely and stops ResetIndexCore
                    // from cancelling a genuinely in-flight search before disposing the index.
                    Interlocked.CompareExchange(ref _searchCts, null, cts);
                }
            }
        }

        /// <summary>
        /// Project the wire shape BY HAND rather than serialising CatalogTocHit directly.
        /// The hit carries internal ranking fields (IsLiteral, StructureId, QueryInOrder,
        /// WordsFound, WordSpan) that the service keeps off the wire with [IgnoreMember];
        /// System.Text.Json does not honour that attribute, so serialising the object would
        /// silently start shipping them. Listing the four public fields keeps both legs
        /// sending the same payload.
        /// </summary>
        private static List<object> ToWire(List<CatalogTocHit> hits)
        {
            var wire = new List<object>(hits.Count);
            foreach (var h in hits)
            {
                wire.Add(new
                {
                    bookId = h.BookId,
                    lineIndex = h.LineIndex,
                    fullTocPath = h.FullTocPath,
                    level = h.Level,
                    treeOrder = h.TreeOrder,
                });
            }
            return wire;
        }

        /// <summary>catalogTocStatus — cheap, so it answers inline.</summary>
        public void HandleStatus(string id)
        {
            EnsureIndex();
            string dbPath = DbPath;
            bool ready = _isReady || (HasDb(dbPath) && GetIndex(dbPath).TryOpenActive());
            if (ready) _isReady = true;
            _bridge.Reply(id, new
            {
                ready,
                indexing = _isIndexing,
                builtBooks = _builtBooks,
                totalBooks = _totalBooks,
                dbMissing = !HasDb(dbPath),
            });
        }

        /// <summary>
        /// catalogTocResetIndex — wipe and rebuild from scratch. Replies immediately and does
        /// the work in the background: the wipe waits for an in-flight build to unwind, which
        /// is far too slow to make the caller watch (that stall is exactly what made the app
        /// reset look broken).
        /// </summary>
        public void HandleResetIndex(string id)
        {
            if (id != null) _bridge.Reply(id, new { });
            Task.Run(() =>
            {
                try { ResetIndexCore(); }
                catch (Exception ex)
                {
                    // Without this catch a failure is an unobserved task exception and the
                    // reset silently half-completes (index disposed, nothing rebuilding).
                    Console.WriteLine("[CatalogToc] reset failed: " + ex);
                }
            });
        }

        private void ResetIndexCore()
        {
            Task build;
            lock (_lock)
            {
                // Claimed before the cancel: from here a failing build defers to this reset
                // instead of re-arming the latch and racing a build into the directory that
                // is about to be deleted.
                _resetInFlight = true;
                if (_buildCts != null) _buildCts.Cancel();
                build = _buildTask;
            }
            try
            {
                // Wait until the build has actually unwound — the old 30s cap was discarded
                // (return value ignored, timeout swallowed) and the code deleted the index
                // directory anyway, while BuildAndSwitch could still be writing through the
                // very CatalogTocIndex disposed just below. Now that the app reset runs this,
                // the FTS rebuild, and the file-search crawl in parallel, a catalog build
                // exceeding 30s is ordinary. This runs on a background task, so waiting
                // blocks no caller.
                if (build != null)
                {
                    if (!build.Wait(TimeSpan.FromSeconds(30)))
                        Console.WriteLine(
                            "[CatalogToc] reset: build still unwinding after 30s — waiting before deleting the index");
                    try { build.Wait(); } catch { /* cancelled or faulted */ }
                }

                // Searches run outside _lock, so cancel the in-flight one BEFORE disposing
                // the index it is reading; a query landing mid-dispose comes back superseded.
                try
                {
                    var s = Interlocked.Exchange(ref _searchCts, null);
                    if (s != null) s.Cancel();
                }
                catch (ObjectDisposedException) { /* that search already finished */ }

                lock (_lock)
                {
                    if (_index != null) _index.Dispose();
                    _index = null;
                    try { if (Directory.Exists(_indexPath)) Directory.Delete(_indexPath, recursive: true); }
                    catch (Exception ex) { Console.WriteLine("[CatalogToc] reset: delete failed: " + ex.Message); }
                    _isReady = false;
                    _isIndexing = false;
                    _buildStarted = false;
                    _buildTask = null;
                    _buildCts = null;
                }
            }
            finally
            {
                // Released before EnsureIndex so the rebuild it starts is a normal build
                // whose own failure may re-arm the latch again. Cleared even if a step above
                // threw — a stuck flag would silently disable the failure-retry path.
                lock (_lock) { _resetInFlight = false; }
            }
            Console.WriteLine("[CatalogToc] index reset — rebuilding");
            EnsureIndex();
        }

        /// <summary>
        /// Called when the seforim DB path changes. Re-arms the once-per-process build latch
        /// and drops the open index so the next Ensure builds against the new database.
        /// </summary>
        public void OnDbPathChanged()
        {
            lock (_lock)
            {
                if (_buildCts != null) { try { _buildCts.Cancel(); } catch { } }
                if (_index != null) { _index.Dispose(); _index = null; }
                _isReady = false;
                _buildStarted = false;
            }
            EnsureIndex();
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_buildCts != null) { try { _buildCts.Cancel(); } catch { } }
            }
            // Let an in-flight build unwind so Lucene's writer closes cleanly. The ver file is
            // written only on full completion, so an interrupted build is simply rebuilt next run.
            try { if (_buildTask != null) _buildTask.Wait(TimeSpan.FromSeconds(10)); } catch { }
            lock (_lock)
            {
                if (_index != null) { _index.Dispose(); _index = null; }
            }
        }
    }
}
