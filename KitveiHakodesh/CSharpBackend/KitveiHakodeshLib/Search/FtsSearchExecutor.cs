using SearchEngine.SeforimDb;
using KitveiHakodeshLib.Bridge;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodeshLib.Search
{
    /// <summary>
    /// Executes full-text searches against the FTS index.
    ///
    /// Two-phase approach:
    ///   1. FtsSearchIds  — streams line IDs + bookId + bookTitle immediately (no snippet work).
    ///                      Frontend receives the full result set fast and renders placeholders.
    ///   2. FtsGetSnippets — generates snippets on demand for a given list of lineIds.
    ///                       Called by the frontend for the visible viewport window as the user scrolls.
    /// </summary>
    internal sealed class FtsSearchExecutor
    {
        private readonly FtsIndexState _state;
        private readonly WebBridge     _bridge;

        private readonly ConcurrentDictionary<string, CancellationTokenSource> _searches
            = new ConcurrentDictionary<string, CancellationTokenSource>();
        private int _nextSearchId = 1;

        // ── ID-only result row (phase 1) ──────────────────────────────────────────
        // Phase 1 sends only lineIds — no DB touch, no bookTitle.
        // bookTitle and tocText arrive in phase 2 with the snippets.

        // ── Snippet result row (phase 2) ──────────────────────────────────────────
        private struct SnippetPayload
        {
            public int      lineId       { get; set; }
            public int      score        { get; set; }
            public string   snippet      { get; set; }
            public string[] matchedTerms { get; set; }
            public bool     isWeakMatch  { get; set; }
        }

        internal FtsSearchExecutor(FtsIndexState state, WebBridge bridge)
        {
            _state  = state;
            _bridge = bridge;
        }

        // ── Phase 1: ID stream ────────────────────────────────────────────────────

        /// <summary>
        /// Returns all matching lineIds (with bookId + bookTitle) to the frontend.
        /// No snippet generation — fast index-only pass + minimal DB join.
        /// Replies immediately with a searchId, then pushes idsComplete push event.
        ///
        /// Parameters (positional):
        ///   0: query (string)
        ///   1: expandKetiv (bool)
        /// </summary>
        internal void HandleSearchIds(JsonElement root, string id)
        {
            string query       = root.TryGetProperty("0", out var q)  ? q.GetString()  : null;
            bool   expandKetiv = root.TryGetProperty("1", out var ek) && ek.GetBoolean();

            bool         ready = _state.IsReady;
            SeforimIndex index = _state.GetIndex();

            if (string.IsNullOrWhiteSpace(query))
            {
                _bridge.Reply(id, new { searchId = (string)null, failReason = (string)null });
                return;
            }

            if (!ready || index == null)
            {
                string reason = !ready ? "indexNotReady" : "searchFailed";
                _bridge.Reply(id, new { searchId = (string)null, failReason = reason });
                return;
            }

            string searchId = "s" + Interlocked.Increment(ref _nextSearchId);
            var cts = new CancellationTokenSource();
            _searches[searchId] = cts;
            _bridge.Reply(id, new { searchId = searchId, failReason = (string)null });

            Task task = Task.Run(() => RunSearchIds(searchId, query, expandKetiv, index, cts.Token));
            task.ContinueWith(
                t => Console.WriteLine("[FtsSearchExecutor] Unhandled exception in SearchIds: " + t.Exception),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        private void RunSearchIds(string searchId, string query, bool expandKetiv,
                                  SeforimIndex index, CancellationToken ct)
        {
            try
            {
                var ids = new List<int>(512);
                foreach (int lineId in index.SearchIds(query, expandKetiv, ct))
                {
                    if (ct.IsCancellationRequested)
                    {
                        PostSearch(new { @event = "search", type = "idsCancelled", searchId });
                        return;
                    }
                    ids.Add(lineId);
                }

                // NOTE: @event = "search" is required so the JS bridge routes this as a push
                // event (not an RPC reply) — the bridge checks msg.event to decide routing.
                PostSearch(new { @event = "search", type = "idsComplete", searchId, lineIds = ids.ToArray() });
            }
            catch (OperationCanceledException)
            {
                PostSearch(new { @event = "search", type = "idsCancelled", searchId });
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FtsSearchExecutor] SearchIds error: " + ex);
                PostSearch(new { @event = "search", type = "idsError", searchId, failReason = "searchFailed", error = ex.Message });
            }
            finally
            {
                if (_searches.TryRemove(searchId, out var cts)) cts.Dispose();
            }
        }

        // ── Phase 2: on-demand snippets ───────────────────────────────────────────

        /// <summary>
        /// Generates snippets for a specific list of lineIds (the visible viewport).
        /// Synchronous RPC — replies directly with the snippet array.
        /// False positives are skipped; word-distance violations are included but flagged as isWeakMatch.
        ///
        /// Parameters (positional):
        ///   0: lineIds           (int[])
        ///   1: query             (string)
        ///   2: maxCharDistance   (int, default 50)
        ///   3: requireOrdered    (bool, default false)
        ///   4: contextChars      (int, default 200)
        ///   5: expandKetiv       (bool, default false)
        /// </summary>
        internal void HandleGetSnippets(JsonElement root, string id)
        {
            var lineIds = new List<int>();
            if (root.TryGetProperty("0", out var lids) && lids.ValueKind == JsonValueKind.Array)
                foreach (var el in lids.EnumerateArray())
                    lineIds.Add(el.GetInt32());

            string query           = root.TryGetProperty("1", out var q)  ? q.GetString()  : null;
            int    maxCharDistance = root.TryGetProperty("2", out var d)   ? d.GetInt32()   : 50;
            bool   reqOrdered      = root.TryGetProperty("3", out var o)   && o.GetBoolean();
            int    contextChars    = root.TryGetProperty("4", out var cw)  ? cw.GetInt32()  : 200;
            bool   expandKetiv     = root.TryGetProperty("5", out var ek)  && ek.GetBoolean();

            Console.WriteLine($"[GetSnippets] lineIds={lineIds.Count} query=\"{query}\" maxCharDist={maxCharDistance} contextChars={contextChars} expandKetiv={expandKetiv}");

            if (lineIds.Count == 0 || string.IsNullOrWhiteSpace(query))
            {
                Console.WriteLine("[GetSnippets] Early exit: empty lineIds or query");
                _bridge.Reply(id, new { snippets = new SnippetPayload[0] });
                return;
            }

            SeforimIndex index = _state.GetIndex();
            if (index == null)
            {
                Console.WriteLine("[GetSnippets] Early exit: index is null");
                _bridge.Reply(id, new { snippets = new SnippetPayload[0] });
                return;
            }

            string effectiveQuery = expandKetiv ? SeforimIndex.ApplyKetivExpansion(query) : query;
            var    terms          = index.ExtractQueryTerms(effectiveQuery);
            var    termArray      = new string[terms.Count];
            for (int i = 0; i < terms.Count; i++) termArray[i] = terms[i];

            Console.WriteLine($"[GetSnippets] effectiveQuery=\"{effectiveQuery}\" stems=[{string.Join(", ", termArray)}]");

            var snippets = new List<SnippetPayload>(lineIds.Count);

            foreach (int lineId in lineIds)
            {
                var result = index.GenerateSnippet(lineId, effectiveQuery, maxCharDistance, contextChars);

                Console.WriteLine($"[GetSnippets] lineId={lineId} isMatch={result.IsMatch} score={result.Score} htmlLen={result.Html?.Length ?? 0}");

                if (!result.IsMatch) continue;

                bool isWeak = result.Score > maxCharDistance;

                snippets.Add(new SnippetPayload
                {
                    lineId       = lineId,
                    score        = result.Score,
                    snippet      = result.Html,
                    matchedTerms = termArray,
                    isWeakMatch  = isWeak,
                });
            }

            Console.WriteLine($"[GetSnippets] Replying with {snippets.Count}/{lineIds.Count} snippets");
            _bridge.Reply(id, new { snippets = snippets.ToArray() });
        }

        // ── Cancel ────────────────────────────────────────────────────────────────

        internal void HandleSearchCancel(JsonElement root, string id)
        {
            string searchId = root.TryGetProperty("0", out var s) ? s.GetString() : null;
            if (searchId != null && _searches.TryRemove(searchId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
            _bridge.Reply(id, new { });
        }

        private void PostSearch(object payload) => _bridge.PushEvent(payload);
    }
}
