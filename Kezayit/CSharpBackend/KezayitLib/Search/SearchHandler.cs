using FtsLib.Seforim;
using KezayitLib.Bridge;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace KezayitLib.Search
{
    /// <summary>
    /// Thin orchestrator for FTS index lifecycle and search.
    ///
    /// Delegates all index building/merging to FtsIndexBuilder and all search
    /// execution to FtsSearchExecutor. Shared mutable state lives in FtsIndexState.
    ///
    /// State machine (all transitions under FtsIndexState.Lock):
    ///   Idle     → Building : FtsIndexBuilder.StartIndexing()
    ///   Building → Ready    : build completes successfully
    ///   Building → Idle     : build cancelled or failed
    ///   Ready    → Merging  : FtsIndexBuilder.StartBackgroundMerge()
    ///   Merging  → Ready    : merge completes or fails (non-fatal)
    ///   Any      → Idle     : FtsIndexState.StopAll() + DeleteFtsIndex()
    ///
    /// Double-entry guarantee: OnDbReady always calls StopAll() before touching
    /// shared state. When the DB path changes it also deletes the existing index,
    /// so a resumed build can never mix line IDs from two different databases.
    /// </summary>
    public class SearchHandler
    {
        private readonly WebBridge         _bridge;
        private readonly FtsIndexState     _indexState;
        private readonly FtsIndexBuilder   _builder;
        private readonly FtsSearchExecutor _searcher;

        public SearchHandler(WebBridge bridge, WebView2 webView)
        {
            _bridge     = bridge;
            _indexState = new FtsIndexState();
            _builder    = new FtsIndexBuilder(_indexState, bridge);
            _searcher   = new FtsSearchExecutor(_indexState, bridge);
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        public void OnDbReady(string dbPath)
        {
            Console.WriteLine("[SearchHandler] OnDbReady called, dbPath=" + dbPath);
            if (!File.Exists(dbPath))
            {
                Console.WriteLine("[SearchHandler] OnDbReady: file does not exist, aborting");
                return;
            }

            // Always stop any in-flight work before touching shared state.
            // This prevents a concurrent build or merge from writing to the index
            // while we are about to inspect or replace it.
            bool dbPathChanged;
            lock (_indexState.Lock)
            {
                dbPathChanged = !string.Equals(_indexState.DbPath, dbPath,
                                               StringComparison.OrdinalIgnoreCase);
            }

            _indexState.StopAll();

            // If the DB path changed, the existing index belongs to a different database.
            // Delete it so a resume cannot mix line IDs from two different databases.
            if (dbPathChanged)
                FtsIndexState.DeleteFtsIndex();

            lock (_indexState.Lock)
            {
                _indexState.DbPath = dbPath;
                FtsIndexState.DeleteBloomIndexIfPresent();
                _indexState.Index = new SeforimIndex(FtsIndexState.FtsIndexPath, dbPath);
            }

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
                    lock (_indexState.Lock)
                        { _indexState.CurrentState = FtsIndexState.State.Ready; }
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
                lock (_indexState.Lock)
                    { _indexState.CurrentState = FtsIndexState.State.Ready; }
                _builder.PushCurrentProgress();
                _builder.StartBackgroundMergeIfNeeded();
                return;
            }

            // No version stamp — check for an interrupted build to resume.
            SeforimIndex index;
            lock (_indexState.Lock) { index = _indexState.Index; }

            int resumeLineId = index.GetResumeLineId();
            if (resumeLineId > 0)
            {
                Console.WriteLine("[SearchHandler] Interrupted build detected — resuming from line id "
                    + resumeLineId);
            }
            else if (FtsIndexState.ValidateFtsIndex() == null)
            {
                // Segments exist but no progress file — no safe resume point.
                Console.WriteLine("[SearchHandler] Orphaned segments found without progress file — deleting and rebuilding");
                FtsIndexState.DeleteFtsIndex();
            }

            Console.WriteLine("[SearchHandler] Starting FTS index build...");
            _builder.StartIndexing();
        }

        public void ResetAndReindex(string newDbPath)
        {
            Console.WriteLine("[SearchHandler] ResetAndReindex called, newDbPath=" + newDbPath);
            _indexState.StopAll();
            FtsIndexState.DeleteFtsIndex();
            if (!string.IsNullOrEmpty(newDbPath) && File.Exists(newDbPath))
                OnDbReady(newDbPath);
        }

        public void StopIndexing()
        {
            Console.WriteLine("[SearchHandler] StopIndexing called");
            _indexState.StopAll();
        }

        // ── Action handlers ───────────────────────────────────────────────────────

        public void HandleDeleteIndex(string id)
        {
            // Reply immediately so the JS caller is not blocked. StopAll can take
            // up to 45s in the worst case (15s build wait + 30s merge wait).
            if (id != null) _bridge.Reply(id, new { });
            Task.Run(() =>
            {
                _indexState.StopAll();
                FtsIndexState.DeleteFtsIndex();
            });
        }

        public void HandleResetFtsIndex(string id)
        {
            _bridge.Reply(id, new { });
            string dbPath;
            lock (_indexState.Lock) { dbPath = _indexState.DbPath; }
            if (!string.IsNullOrEmpty(dbPath) && File.Exists(dbPath))
                Task.Run(() => ResetAndReindex(dbPath));
        }

        public void HandleConfirmReindex(bool confirm, string id)
        {
            _bridge.Reply(id, new { });
            if (!confirm) return;
            Console.WriteLine("[SearchHandler] User confirmed reindex after app update");
            string dbPath;
            lock (_indexState.Lock) { dbPath = _indexState.DbPath; }
            Task.Run(() => ResetAndReindex(dbPath));
        }

        public void HandleGetProgress(string id)
        {
            bool ready, indexing;
            lock (_indexState.Lock)
                { ready = _indexState.IsReady; indexing = _indexState.IsIndexing; }
            _bridge.Reply(id, new
            {
                isReady         = ready,
                isIndexing      = indexing,
                percentage      = ready ? 100.0 : 0.0,
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
