using KitveiHakodeshLib.Bridge;
using KitveiHakodeshLib.Settings;
using CatalogTocIndex = KitveiHakodesh.Core.SeforimDbCatalog.SeforimDbCatalogIndex;
using CatalogTocHit = KitveiHakodesh.Core.SeforimDbCatalog.CatalogTocHit;
using CatalogIndexCoordinator = KitveiHakodesh.Core.SeforimDbCatalog.SeforimDbCatalogIndexCoordinator;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodeshLib.Catalog
{
    /// <summary>
    /// Hosted (net48) front end for the catalog TOC Lucene search — the counterpart of
    /// KitveiHakodeshService's CatalogTocSearchService, over the SAME shared
    /// <see cref="CatalogTocIndex"/> engine — KitveiHakodesh.Core's, referenced as a library.
    ///
    /// Why this exists: the catalog search used to be service-only, so the hosted app fell
    /// back to a two-phase in-memory heuristic (book match + TOC keyword rules) that could
    /// not match the Lucene index's ranking. The service is not deployed with the hosted
    /// app, so reaching it was never an option — porting the engine was.
    ///
    /// Index location: "CatalogTocIndex" next to the app binary, matching the FTS index and
    /// the service's own convention.
    ///
    /// Threading is no longer merely LIKE the service's — it IS the service's: both hosts
    /// drive one shared coordinator, so the build runs on a background task and the index
    /// stays searchable throughout via Lucene's near-real-time reader, and results appear
    /// during the very first build rather than after it.
    /// </summary>
    public sealed class CatalogTocHandler : IDisposable
    {
        private readonly WebBridge _bridge;

        // The LIFECYCLE is Core's: when to open, when to build, the bounded retry on a failed
        // build, and the claim-before-cancel reset ordering. All of it used to live here AND in
        // the service as the same two hundred lines, explanatory comments included. What stays
        // on this side is what only the hosted app can answer — where the database is (the user
        // can switch it mid-session), how to log, and the superseded-search guard.
        private readonly CatalogIndexCoordinator _coordinator;

        // A new search SUPERSEDES the previous in-flight one (latest-wins) so an abandoned
        // heavy query stops burning cores as soon as the user keeps typing.
        private CancellationTokenSource _searchCts;

        public CatalogTocHandler(WebBridge bridge)
        {
            _bridge = bridge;
            _coordinator = new CatalogIndexCoordinator(
                // Read EACH time, not captured: the user can point the app at a different
                // database from settings mid-session and the index follows without a restart.
                resolveDatabasePath: () =>
                {
                    try { return AppSettings.LoadDbPath(); }
                    catch { return null; }
                },
                indexPath: Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CatalogTocIndex"),
                log: (level, message, ex) => Console.WriteLine(
                    "[CatalogToc] " + message + (ex != null ? ": " + ex : "")),
                // The reset ORDERING is Core's; the search source stays ours.
                takeActiveSearchCts: () => Interlocked.Exchange(ref _searchCts, null));
        }


        /// <summary>
        /// Idempotent. Opens the existing index (even a stale one — no downtime) and, when the
        /// stored DB hash differs from the current one, kicks off a background rebuild that
        /// builds IN PLACE and stays searchable meanwhile.
        /// </summary>
        public void EnsureIndex() => _coordinator.EnsureIndex();
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

            // Ready as soon as a reader exists — during a build the near-real-time reader
            // serves partial results, so this does not gate on the build finishing.
            if (!_coordinator.TryGetSearchableIndex(out var index))
                return new { ready = false, results = new object[0] };

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
                    // A reset racing in disposes this index, which is the
                    // ObjectDisposedException handled below.
                    var hits = index.Search(query, cts.Token);
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
        /// WordsFound, WordSpan, QuotedFormMatch, MatchesTitleOrAuthor) that the service keeps
        /// off the wire with [IgnoreMember]; System.Text.Json does not honour that attribute, so
        /// serialising the object would silently start shipping them. Listing the public fields
        /// keeps both legs sending the same payload — and a ranking key added to Core lands here
        /// as nothing at all, which is what it should be.
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
            _bridge.Reply(id, new
            {
                ready = _coordinator.IsReadyToServe(),
                indexing = _coordinator.IsIndexing,
                builtBooks = _coordinator.BuiltBooks,
                totalBooks = _coordinator.TotalBooks,
                dbMissing = _coordinator.DatabasePath == null,
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
            // Returns immediately; the coordinator backgrounds the work and reports its own
            // failures, so there is nothing to wrap here.
            _coordinator.ResetIndex();
        }
        /// <summary>
        /// The user repointed the app at a different database: cancels any build in flight and
        /// drops the open index so the next Ensure builds against the new one.
        /// </summary>
        public void OnDbPathChanged() => _coordinator.OnDatabasePathChanged();

        public void Dispose()
        {
            // 10s TOTAL, not the service's 25: this runs while the desktop app is closing,
            // where a long stall is visible to the user. The ver file is written only on full
            // completion, so an interrupted build is simply rebuilt next run.
            _coordinator.Dispose(TimeSpan.FromSeconds(10));
        }
    }
}
