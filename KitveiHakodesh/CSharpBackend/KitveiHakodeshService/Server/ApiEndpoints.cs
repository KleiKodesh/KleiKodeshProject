using KitveiHakodeshService.Dictionary;
using KitveiHakodeshService.HebrewBooks;
using KitveiHakodeshService.Seforim;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace KitveiHakodeshService.Server;

/// <summary>
/// Registers all HTTP routes for the KitveiHakodesh backend service.
///
/// Route map:
///   POST /query              — parameterised SQL against the seforim database
///   POST /query-dict         — parameterised SQL against the dictionary database
///   GET  /events             — Server-Sent Events stream for push notifications
///   POST /search/start       — start a full-text search, returns { searchId }
///   POST /search/cancel      — cancel a running search by searchId
///   GET  /search/progress    — current FTS index build state
///   POST /search/reset       — wipe and rebuild the FTS index
///   POST /hebrewbooks/search — search the HebrewBooks catalogue
///
/// All SQL endpoints accept { sql: string, params: any[] } and return { rows: object[] }
/// so the frontend can reuse the same query() function it uses against the WebView2 host.
/// </summary>
public static class ApiEndpoints
{
    public static void Register(WebApplication app)
    {
        RegisterQueryEndpoints(app);
        RegisterSseEndpoint(app);
        RegisterSearchEndpoints(app);
        RegisterHebrewBooksEndpoints(app);
    }

    // ── SQL query endpoints ────────────────────────────────────────────────────

    private static void RegisterQueryEndpoints(WebApplication app)
    {
        app.MapPost("/query", async (
            [FromBody] SqlQueryRequest request,
            SeforimDbManager dbManager) =>
        {
            if (!dbManager.IsReady)
                return Results.Json(new { error = "No database loaded" });

            try
            {
                var rows = await Task.Run(() => dbManager.Query(request.Sql, request.Params));
                return Results.Json(new { rows });
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message });
            }
        });

        app.MapPost("/query-dict", async (
            [FromBody] SqlQueryRequest request,
            DictionaryDbManager dictionaryDbManager) =>
        {
            try
            {
                var rows = await Task.Run(() => dictionaryDbManager.Query(request.Sql, request.Params));
                return Results.Json(new { rows });
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message });
            }
        });
    }

    // ── SSE push event stream ──────────────────────────────────────────────────

    private static void RegisterSseEndpoint(WebApplication app)
    {
        app.MapGet("/events", async (
            HttpContext context,
            SseManager sseManager,
            CancellationToken requestAborted) =>
        {
            context.Response.Headers.Append("Content-Type", "text/event-stream");
            context.Response.Headers.Append("Cache-Control", "no-cache");
            context.Response.Headers.Append("X-Accel-Buffering", "no"); // Disable nginx buffering

            var (connectionId, reader) = sseManager.AddClient();

            // Send an initial "connected" comment so the client knows the stream is live.
            await context.Response.WriteAsync(": connected\n\n", requestAborted);
            await context.Response.Body.FlushAsync(requestAborted);

            try
            {
                await foreach (var message in reader.ReadAllAsync(requestAborted))
                {
                    await context.Response.WriteAsync(message, requestAborted);
                    await context.Response.Body.FlushAsync(requestAborted);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected — normal exit.
            }
            finally
            {
                sseManager.RemoveClient(connectionId);
            }
        });
    }

    // ── Full-text search endpoints ─────────────────────────────────────────────

    private static void RegisterSearchEndpoints(WebApplication app)
    {
        // POST /search/start
        // Body: { query, skipCount, maxWordDistance, requireOrdered, contextWords, expandKetiv }
        // Returns: { searchId } on success, { searchId: null, failReason } on failure.
        // Results stream back as SSE events: searchBatch, searchComplete, searchCancelled, searchError.
        app.MapPost("/search/start", (
            [FromBody] SearchStartRequest request,
            SeforimFullTextSearch fts,
            SseManager sseManager) =>
        {
            var result = fts.StartSearch(request, sseManager);
            return Results.Json(result);
        });

        // POST /search/cancel
        // Body: { searchId }
        app.MapPost("/search/cancel", (
            [FromBody] SearchCancelRequest request,
            SeforimFullTextSearch fts) =>
        {
            fts.CancelSearch(request.SearchId);
            return Results.Json(new { });
        });

        // GET /search/progress
        // Returns current FTS index build state.
        app.MapGet("/search/progress", (SeforimFullTextSearch fts) =>
        {
            return Results.Json(fts.GetProgress());
        });

        // POST /search/reset
        // Wipes the FTS index and starts a fresh build.
        app.MapPost("/search/reset", (
            SeforimFullTextSearch fts,
            SseManager sseManager) =>
        {
            fts.Reset(sseManager);
            return Results.Json(new { });
        });
    }

    // ── HebrewBooks endpoints ──────────────────────────────────────────────────

    private static void RegisterHebrewBooksEndpoints(WebApplication app)
    {
        // POST /hebrewbooks/search
        // Body: { query: string }
        // Returns: { books: object[] }
        app.MapPost("/hebrewbooks/search", async (
            [FromBody] HebrewBooksSearchRequest request,
            HebrewBooksDbManager hebrewBooksDbManager) =>
        {
            try
            {
                var books = await Task.Run(() => hebrewBooksDbManager.Search(request.Query));
                return Results.Json(new { books });
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message });
            }
        });
    }
}

// ── Request / response models ──────────────────────────────────────────────────

/// <summary>Shared body for POST /query and POST /query-dict.</summary>
public sealed record SqlQueryRequest(string Sql, object[] Params);

/// <summary>Body for POST /search/start. Mirrors the positional params FtsSearchExecutor reads.</summary>
public sealed record SearchStartRequest(
    string Query,
    int SkipCount = 0,
    int MaxWordDistance = 10,
    bool RequireOrdered = false,
    int ContextWords = 5,
    bool ExpandKetiv = false);

/// <summary>Body for POST /search/cancel.</summary>
public sealed record SearchCancelRequest(string SearchId);

/// <summary>Body for POST /hebrewbooks/search.</summary>
public sealed record HebrewBooksSearchRequest(string Query);
