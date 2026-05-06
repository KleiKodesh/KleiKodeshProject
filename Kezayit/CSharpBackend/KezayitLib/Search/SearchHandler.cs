using FtsLib.Seforim;
using KezayitLib.Bridge;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KezayitLib.Search
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
    ///   Ready    → Merging  : FtsIndexBuilder.StartBackgroundMerge()
    ///   Merging  → Ready    : merge completes or fails (non-fatal)
    ///   Any      → Idle     : FtsIndexState.StopAll() + DeleteFtsIndex()
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
            if (!File.Exists(dbPath))
            {
                Console.WriteLine("[SearchHandler] OnDbReady: file does not exist, aborting");
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
                    _builder.StartIndexing();
                    return;
                }

                string installedVersion = FtsIndexState.GetInstalledAppVersion();
                Console.WriteLine("[SearchHandler] Version check — installed="
                    + installedVersion + " stamped=" + stampedVersion);

                if (!string.IsNullOrEmpty(installedVersion) &&
                    !string.Equals(installedVersion, stampedVersion,
                                   StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("[SearchHandler] App version changed — asking user whether to rebuild index");
                    _indexState.MarkReadyDirect();
                    _bridge.PushEvent(new
                    {
                        @event     = "ftsIndexVersionMismatch",
                        oldVersion = stampedVersion,
                        newVersion = installedVersion
                    });
                    _builder.PushCurrentProgress();
                    _builder.StartBackgroundMergeIfNeeded();
                    return;
                }

                Console.WriteLine("[SearchHandler] FTS index complete and up-to-date, marking ready");
                _indexState.MarkReadyDirect();
                _builder.PushCurrentProgress();
                _builder.StartBackgroundMergeIfNeeded();
                return;
            }

            // No version stamp — interrupted build. Resume from where we left off.
            // IndexWriter handles crash recovery internally (WAL replay).
            // If there is a progress file, BuildIndex will call ReadLinesFrom to skip
            // already-indexed lines. If there is no progress file, it starts from the
            // beginning — which is safe because IndexWriter appends to existing segments.
            int resumeLineId = _indexState.GetIndex().GetResumeLineId();
            if (resumeLineId > 0)
            {
                Console.WriteLine("[SearchHandler] Interrupted build detected — resuming from line id "
                    + resumeLineId);
            }
            else
            {
                Console.WriteLine("[SearchHandler] No progress file — starting fresh build");
            }

            Console.WriteLine("[SearchHandler] Starting FTS index build...");
            _builder.StartIndexing();
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
                _indexState.StopAll();
                FtsIndexState.DeleteFtsIndex();
                if (!string.IsNullOrEmpty(newDbPath) && File.Exists(newDbPath))
                    ExecuteOnDbReady(newDbPath);
            });
        }

        public void StopIndexing()
        {
            Console.WriteLine("[SearchHandler] StopIndexing called");
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
                _indexState.StopAll();
                FtsIndexState.DeleteFtsIndex();
            });
        }

        public void HandleResetFtsIndex(string id)
        {
            _bridge.Reply(id, new { });
            string dbPath = _indexState.GetDbPath();
            if (!string.IsNullOrEmpty(dbPath) && File.Exists(dbPath))
                ResetAndReindex(dbPath);
        }

        public void HandleConfirmReindex(bool confirm, string id)
        {
            _bridge.Reply(id, new { });
            if (!confirm) return;
            Console.WriteLine("[SearchHandler] User confirmed reindex after app update");
            string dbPath = _indexState.GetDbPath();
            ResetAndReindex(dbPath);
        }

        public void HandleGetProgress(string id)
        {
            bool ready    = _indexState.IsReady;
            bool indexing = _indexState.IsIndexing;
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
