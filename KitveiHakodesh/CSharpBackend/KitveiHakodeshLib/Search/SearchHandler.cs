using SearchEngine.SeforimDb;
using KitveiHakodeshLib.Bridge;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodeshLib.Search
{
    /// <summary>
    /// Thin orchestrator for FTS index lifecycle and search.
    ///
    /// Lifecycle serialization uses the Single Writer / Actor pattern:
    /// a dedicated background thread drains a BlockingCollection of Actions.
    /// OnDbReady, ResetAndReindex, and HandleDeleteIndex post lambdas to this
    /// actor and return immediately — no semaphores, no Wait/Release pairs,
    /// no deadlock risk.
    ///
    /// Background tasks (build, merge) run on separate Task.Run threads and
    /// communicate back only through FtsIndexState's named transition methods.
    ///
    /// State machine (owned entirely by FtsIndexState):
    ///   Idle     → Building : FtsIndexBuilder.StartIndexing()
    ///   Building → Ready    : build completes successfully
    ///   Building → Idle     : build cancelled or failed
    ///   Any      → Idle     : FtsIndexState.StopAll() + DeleteFtsIndex()
    ///
    /// Cross-process coordination:
    ///   When another instance of the app is already building the index, this
    ///   instance does not start a second build. Instead it starts a watcher
    ///   thread that polls the progress file every 2 seconds and pushes
    ///   ftsIndexProgress events to its own frontend so the UI stays live.
    ///   When the other process releases the build lock (finishes or crashes),
    ///   the watcher re-queues ExecuteOnDbReady on the actor thread so this
    ///   instance picks up and completes any interrupted build.
    /// </summary>
    public class SearchHandler
    {
        private readonly WebBridge         _bridge;
        private readonly FtsIndexState     _indexState;
        private readonly FtsIndexBuilder   _builder;
        private readonly FtsSearchExecutor _searcher;

        // ── Actor thread ──────────────────────────────────────────────────────────
        // All lifecycle operations are posted here and executed sequentially on a
        // single dedicated thread. Callers post and return immediately — no blocking.
        private readonly BlockingCollection<Action> _lifecycleQueue
            = new BlockingCollection<Action>();
        private readonly Thread _actorThread;

        // ── Cross-process watcher ─────────────────────────────────────────────────
        // Cancels the watcher thread when this instance takes over the build or shuts down.
        private CancellationTokenSource _watcherCts;

        public SearchHandler(WebBridge bridge, WebView2 webView)
        {
            _bridge     = bridge;
            _indexState = new FtsIndexState();
            _builder    = new FtsIndexBuilder(_indexState, bridge);
            _searcher   = new FtsSearchExecutor(_indexState, bridge);

            _actorThread = new Thread(DrainLifecycleQueue)
            {
                IsBackground = true,
                Name         = "FtsLifecycleActor"
            };
            _actorThread.Start();
        }

        private void DrainLifecycleQueue()
        {
            foreach (var action in _lifecycleQueue.GetConsumingEnumerable())
            {
                try { action(); }
                catch (Exception ex)
                {
                    Console.WriteLine("[SearchHandler] Actor thread exception: " + ex.Message);
                }
            }
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Called when a database path becomes available. Posts the full startup
        /// sequence to the actor thread and returns immediately.
        /// </summary>
        public void OnDbReady(string dbPath)
        {
            Console.WriteLine("[SearchHandler] OnDbReady queued, dbPath=" + dbPath);
            _lifecycleQueue.Add(() => ExecuteOnDbReady(dbPath));
        }

        private void ExecuteOnDbReady(string dbPath)
        {
            Console.WriteLine("[SearchHandler] OnDbReady executing, dbPath=" + dbPath);

            // Cancel any running watcher from a previous cross-process wait.
            StopWatcher();

            if (!File.Exists(dbPath))
            {
                Console.WriteLine("[SearchHandler] OnDbReady: file does not exist — notifying frontend");
                _bridge.PushEvent(new { @event = "ftsDbNotFound", path = dbPath });
                return;
            }

            bool dbPathChanged = !string.IsNullOrEmpty(_indexState.GetDbPath()) &&
                                 !string.Equals(
                                     _indexState.GetDbPath(), dbPath,
                                     StringComparison.OrdinalIgnoreCase);

            // Stop any in-flight work before touching shared state.
            _indexState.StopAll();

            // If the DB path changed the existing index belongs to a different database.
            if (dbPathChanged)
                FtsIndexState.DeleteFtsIndex();

            FtsIndexState.DeleteBloomIndexIfPresent();
            _indexState.SetDatabase(dbPath,
                new SeforimIndex(FtsIndexState.FtsIndexPath, dbPath));

            string stampedVersion = FtsIndexState.ReadVersionStamp();
            if (stampedVersion != null)
            {
                string validationError = FtsIndexState.ValidateFtsIndex();
                if (validationError != null)
                {
                    Console.WriteLine("[SearchHandler] fts.ver present but index invalid ("
                        + validationError + ") — deleting and rebuilding");
                    FtsIndexState.DeleteFtsIndex();
                    _bridge.PushEvent(new { @event = "ftsIndexInvalidated", reason = validationError });
                    StartBuildOrWatch(dbPath);
                    return;
                }

                string installedVersion = FtsIndexState.GetInstalledAppVersion();
                Console.WriteLine("[SearchHandler] Version check — installed="
                    + installedVersion + " stamped=" + stampedVersion);

                if (!string.IsNullOrEmpty(installedVersion) &&
                    !string.Equals(installedVersion, stampedVersion,
                                   StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("[SearchHandler] DB changed (app version " + stampedVersion
                        + " → " + installedVersion + ") — rebuilding index automatically");
                    FtsIndexState.DeleteFtsIndex();
                    _bridge.PushEvent(new { @event = "ftsIndexInvalidated", reason = "db updated" });
                    StartBuildOrWatch(dbPath);
                    return;
                }

                Console.WriteLine("[SearchHandler] FTS index complete and up-to-date, marking ready");
                _indexState.MarkReadyDirect();
                _builder.PushCurrentProgress();
                return;
            }

            // No version stamp — could be an interrupted build or a stale index from
            // a previous install. Decide whether to resume or wipe based on the
            // progress file.
            //
            // Resume is only safe when a progress file exists: it records the exact
            // line ID up to which segments are consistent, so BuildIndex can call
            // ReadLinesFrom and append without duplicating anything.
            //
            // If there is no progress file but segments already exist on disk, we
            // cannot know whether those segments are complete, partial, or from a
            // different database version. Appending would duplicate every line that
            // was already indexed. Wipe and rebuild from scratch.
            _indexState.GetIndex().GetResumeState(out int resumeLineId, out _, out _);
            if (resumeLineId > 0)
            {
                Console.WriteLine("[SearchHandler] Interrupted build detected — resuming from line id "
                    + resumeLineId);
            }
            else
            {
                // No progress file. Check whether stale segments exist.
                bool staleSegmentsExist = false;
                try
                {
                    if (Directory.Exists(FtsIndexState.FtsIndexPath))
                    {
                        foreach (var f in Directory.GetFiles(FtsIndexState.FtsIndexPath))
                        {
                            string name = System.IO.Path.GetFileName(f);
                            if (name.StartsWith("segments_") && name != "segments.gen")
                            { staleSegmentsExist = true; break; }
                        }
                    }
                }
                catch { }

                if (staleSegmentsExist)
                {
                    Console.WriteLine("[SearchHandler] No progress file but stale segments found " +
                        "(reinstall or leftover index) — wiping index for clean rebuild");
                    FtsIndexState.DeleteFtsIndex();
                    _indexState.SetDatabase(_indexState.GetDbPath(),
                        new SeforimIndex(FtsIndexState.FtsIndexPath, dbPath));
                }
                else
                {
                    Console.WriteLine("[SearchHandler] No progress file — starting fresh build");
                }
            }

            Console.WriteLine("[SearchHandler] Starting FTS index build...");
            StartBuildOrWatch(dbPath);
        }

        // ── Cross-process coordination ────────────────────────────────────────────

        /// <summary>
        /// Starts the index build if no other process holds the build lock.
        /// If another process is already building, starts a watcher thread instead:
        /// the watcher polls the progress file every 2 seconds, pushes progress
        /// events to this instance's frontend, and re-queues ExecuteOnDbReady when
        /// the other process releases the lock (finishes or crashes).
        /// </summary>
        private void StartBuildOrWatch(string dbPath)
        {
            if (!FtsIndexState.IsAnotherProcessBuilding())
            {
                // Lock is free — we will acquire it inside FtsIndexBuilder.StartIndexing.
                _builder.StartIndexing();
                return;
            }

            Console.WriteLine("[SearchHandler] Another process is building the FTS index — watching for completion");

            // Push an initial progress event so the frontend shows the indexing overlay.
            PushProgressFromFile(isReady: false);

            // Start the watcher thread.
            var cts = new CancellationTokenSource();
            _watcherCts = cts;

            Thread watcher = new Thread(() => WatchOtherProcessBuild(dbPath, cts.Token))
            {
                IsBackground = true,
                Name         = "FtsIndexWatcher"
            };
            watcher.Start();
        }

        /// <summary>
        /// Runs on a background thread while another process holds the build lock.
        /// Polls the progress file every 2 seconds and pushes ftsIndexProgress events.
        /// When the lock is released, re-queues ExecuteOnDbReady on the actor thread.
        /// </summary>
        private void WatchOtherProcessBuild(string dbPath, CancellationToken ct)
        {
            const int PollIntervalMs = 2000;

            while (!ct.IsCancellationRequested)
            {
                Thread.Sleep(PollIntervalMs);
                if (ct.IsCancellationRequested) break;

                // Push current progress from the file so the frontend stays live.
                PushProgressFromFile(isReady: false);

                // Check whether the other process has released the lock.
                if (!FtsIndexState.IsAnotherProcessBuilding())
                {
                    Console.WriteLine("[SearchHandler] Other process released build lock — taking over");
                    // Re-queue the full startup sequence on the actor thread.
                    // This will resume or complete the interrupted build.
                    _lifecycleQueue.Add(() => ExecuteOnDbReady(dbPath));
                    return;
                }
            }
        }

        private void StopWatcher()
        {
            var cts = _watcherCts;
            _watcherCts = null;
            cts?.Cancel();
        }

        /// <summary>
        /// Reads the progress file and pushes an ftsIndexProgress event to the frontend.
        /// Used by the watcher thread to keep the UI live while another process builds.
        /// </summary>
        private void PushProgressFromFile(bool isReady)
        {
            double pct;
            int processed, total;
            if (FtsIndexState.TryReadProgressFile(out pct, out processed, out total))
            {
                _bridge.PushEvent(new
                {
                    @event           = "ftsIndexProgress",
                    isReady          = isReady,
                    isIndexing       = true,
                    percentage       = Math.Round(pct, 1),
                    processedChunks  = processed,
                    totalChunks      = total,
                    eta              = "",
                    segmentCount     = 0,
                    latestSegmentPct = (double?)null
                });
            }
            else
            {
                // No progress file yet — push a "started but no data" event.
                _bridge.PushEvent(new
                {
                    @event           = "ftsIndexProgress",
                    isReady          = false,
                    isIndexing       = true,
                    percentage       = 0.0,
                    processedChunks  = 0,
                    totalChunks      = 0,
                    eta              = "",
                    segmentCount     = 0,
                    latestSegmentPct = (double?)null
                });
            }
        }

        /// <summary>
        /// Resets the index and starts a fresh build. Posts to the actor thread.
        /// </summary>
        public void ResetAndReindex(string newDbPath)
        {
            Console.WriteLine("[SearchHandler] ResetAndReindex queued, newDbPath=" + newDbPath);
            _lifecycleQueue.Add(() =>
            {
                Console.WriteLine("[SearchHandler] ResetAndReindex executing");
                StopWatcher();
                _indexState.StopAll();
                FtsIndexState.DeleteFtsIndex();
                if (!string.IsNullOrEmpty(newDbPath) && File.Exists(newDbPath))
                    ExecuteOnDbReady(newDbPath);
            });
        }

        public void StopIndexing()
        {
            Console.WriteLine("[SearchHandler] StopIndexing called");
            StopWatcher();
            _indexState.StopAll();
        }

        // ── Action handlers ───────────────────────────────────────────────────────

        public void HandleDeleteIndex(string id)
        {
            // Reply immediately — the delete runs on the actor thread asynchronously.
            if (id != null) _bridge.Reply(id, new { });
            _lifecycleQueue.Add(() =>
            {
                Console.WriteLine("[SearchHandler] HandleDeleteIndex executing");
                StopWatcher();
                _indexState.StopAll();
                // Full reset — wipe all cache folders (FTS index, Bloom, Word PDFs,
                // HebrewBooks downloads, WebView2 webcache).
                FtsIndexState.DeleteAllCaches();
            });
        }

        public void HandleResetFtsIndex(string id)
        {
            _bridge.Reply(id, new { });
            string dbPath = _indexState.GetDbPath();
            if (!string.IsNullOrEmpty(dbPath) && File.Exists(dbPath))
                ResetAndReindex(dbPath);
        }

        public void HandleGetProgress(string id)
        {
            bool ready    = _indexState.IsReady;
            bool indexing = _indexState.IsIndexing;

            // If this instance is not building but another process is, read the
            // progress file so the frontend gets real percentage data on mount.
            if (!ready && !indexing && FtsIndexState.IsAnotherProcessBuilding())
            {
                double pct;
                int processed, total;
                if (FtsIndexState.TryReadProgressFile(out pct, out processed, out total))
                {
                    _bridge.Reply(id, new
                    {
                        isReady         = false,
                        isIndexing      = true,
                        percentage      = Math.Round(pct, 1),
                        processedChunks = processed,
                        totalChunks     = total,
                        eta             = "",
                        segmentCount    = 0,
                        latestSegmentPct = (double?)null
                    });
                    return;
                }

                _bridge.Reply(id, new
                {
                    isReady         = false,
                    isIndexing      = true,
                    percentage      = 0.0,
                    processedChunks = 0,
                    totalChunks     = 0,
                    eta             = "",
                    segmentCount    = 0,
                    latestSegmentPct = (double?)null
                });
                return;
            }

            _bridge.Reply(id, new
            {
                isReady         = ready,
                isIndexing      = indexing,
                percentage      = (ready && !indexing) ? 100.0 : 0.0,
                processedChunks = 0,
                totalChunks     = 0,
                eta             = ""
            });
        }

        // ── Search ────────────────────────────────────────────────────────────────

        public void HandleSearchStart(JsonElement root, string id)
            => _searcher.HandleSearchStart(root, id);

        public void HandleSearchCancel(JsonElement root, string id)
            => _searcher.HandleSearchCancel(root, id);
    }
}
