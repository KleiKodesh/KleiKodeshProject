using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using KitveiHakodeshService.Ipc;

namespace KitveiHakodeshService.LocalFiles;

/// <summary>
/// The service's file-system search capability. It does NOT reimplement the
/// NTFS/Lucene crawler — it delegates to the existing standalone DocumentLocator
/// Windows service over its named pipe (starting it on demand via the SCM, no
/// elevation required), then shapes the reply into the object the Vue app already
/// consumes: { results: [{ fileName, path, modifiedDate }], total }.
///
/// This transform previously lived in the Vite dev middleware; moving it here is
/// the point of the migration — the dev courier becomes a dumb proxy and all
/// backend knowledge lives in the service.
/// </summary>
public sealed class DocumentLocatorService(ILogger<DocumentLocatorService> logger)
{
    private const string DlPipeName = "DocumentLocator";
    private const string DlServiceName = "DocumentLocatorSvc";
    private const int ConnectTimeoutMs = 1_500;
    private const int StartupTimeoutMs = 30_000; // cold .NET service start can take a few seconds
    private const int StartupPollMs = 500;

    public async Task<LocateDocumentsResult> LocateAsync(string query, int max, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new LocateDocumentsResult();

        string request = BuildSearchRequest(query, max);

        string? response = await TryCallAsync(request, ct);
        if (response is null)
        {
            // Not running — start it via the SCM, wait for the pipe, retry once.
            StartDocumentLocatorService();
            await WaitForPipeAsync(ct);
            response = await TryCallAsync(request, ct)
                       ?? throw new InvalidOperationException(
                           "DocumentLocator service did not respond after start.");
        }

        // The index may still be building on a cold start; the service answers a
        // search with {"status":"building"} until it is ready. Re-issue the search
        // until it returns results (Vue's loading animation covers the wait).
        int waited = 0;
        while (IsBuilding(response) && waited < StartupTimeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(StartupPollMs, ct);
            waited += StartupPollMs;
            response = await TryCallAsync(request, ct) ?? response;
        }

        return Transform(response);
    }

    /// <summary>Tell DocumentLocator to wipe its index and rebuild from scratch
    /// (the dev "reset file-search index"). Starts the service on demand and returns
    /// once the reindex has been acknowledged; the rebuild then runs in its service.</summary>
    public async Task ReindexAsync(CancellationToken ct)
    {
        const string reindexRequest = "{\"type\":\"reindex\"}";
        string? response = await TryCallAsync(reindexRequest, ct);
        if (response is null)
        {
            StartDocumentLocatorService();
            await WaitForPipeAsync(ct);
            await TryCallAsync(reindexRequest, ct);
        }
    }

    /// <summary>Fire-and-forget: make sure the DocumentLocator service is up so the
    /// first real query is fast. Errors are swallowed — it's a hint, not a promise.</summary>
    public void Warmup()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                string? status = await TryCallAsync("{\"type\":\"status\"}", CancellationToken.None);
                if (status is null) StartDocumentLocatorService();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "DocumentLocator warmup failed (non-fatal)");
            }
        });
    }

    // ── DocumentLocator pipe client ────────────────────────────────────────────

    private static string BuildSearchRequest(string query, int max)
    {
        // DocumentLocator matches on the RAW query string and does NOT decode \uXXXX
        // escapes, so the Hebrew must go over the wire as raw UTF-8 (as the old Vite
        // middleware sent it). Default System.Text.Json escapes all non-ASCII to
        // \uXXXX → 0 matches for Hebrew. Write with the relaxed encoder (escapes only
        // the JSON-mandatory characters) so Hebrew stays literal. AOT-safe (no reflection).
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("q", query);
            if (max > 0) w.WriteNumber("limit", max);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static bool IsBuilding(string response) =>
        response.Contains("\"status\":\"building\"", StringComparison.Ordinal);

    /// <summary>One attempt; returns null when the pipe isn't answering.</summary>
    private static async Task<string?> TryCallAsync(string request, CancellationToken ct)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".", DlPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(ConnectTimeoutMs, ct);
            // The upstream DocumentLocator pipe speaks UTF-8 JSON (not our msgpack channel),
            // so encode/decode the string ourselves over the byte-oriented frame protocol.
            await FrameProtocol.WriteFrameAsync(pipe, System.Text.Encoding.UTF8.GetBytes(request), ct);
            byte[]? reply = await FrameProtocol.ReadFrameAsync(pipe, ct);
            return reply is null ? null : System.Text.Encoding.UTF8.GetString(reply);
        }
        catch (TimeoutException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static async Task WaitForPipeAsync(CancellationToken ct)
    {
        int elapsed = 0;
        while (elapsed < StartupTimeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var pipe = new NamedPipeClientStream(
                    ".", DlPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(300, ct);
                return;
            }
            catch { /* not ready yet */ }
            await Task.Delay(StartupPollMs, ct);
            elapsed += StartupPollMs;
        }
        throw new TimeoutException("DocumentLocator service did not start in time.");
    }

    private static LocateDocumentsResult Transform(string response)
    {
        var dl = JsonSerializer.Deserialize(response, RpcJsonContext.Default.DlResponse)
                 ?? throw new InvalidOperationException("Empty DocumentLocator response.");

        if (dl.Status == "error")
            throw new InvalidOperationException(dl.Message ?? "DocumentLocator search error.");

        var result = new LocateDocumentsResult { Total = dl.Total };

        IEnumerable<(string path, long date)> entries =
            dl.Entries is { Count: > 0 }
                ? dl.Entries.Select(e => (e.Path ?? "", e.Date))
                : (dl.Paths ?? new List<string>()).Select(p => (p, 0L));

        foreach (var (path, date) in entries)
        {
            if (string.IsNullOrEmpty(path)) continue;

            int lastSep = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
            string fileName = lastSep >= 0 ? path[(lastSep + 1)..] : path;
            string dir = lastSep >= 0 ? path[..lastSep] : "";

            long modified = date;
            if (modified == 0)
            {
                try { modified = new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeMilliseconds(); }
                catch { modified = 0; }
            }

            result.Results.Add(new FileHit { FileName = fileName, Path = dir, ModifiedDate = modified });
        }

        if (result.Total <= 0) result.Total = result.Results.Count;
        return result;
    }

    // ── Start the DocumentLocator service via the SCM (no elevation needed) ─────
    // Ported from DocumentLocator.Client/ServiceBridge.cs; LibraryImport keeps the
    // P/Invoke marshalling AOT-friendly.

    private void StartDocumentLocatorService()
    {
        const uint SC_MANAGER_CONNECT = 0x0001;
        const uint SERVICE_START = 0x0010;
        const int ERROR_SERVICE_ALREADY_RUNNING = 1056;

        nint scm = NativeMethods.OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scm == 0)
            throw new InvalidOperationException(
                $"Cannot connect to the Service Control Manager (error {Marshal.GetLastWin32Error()}).");
        try
        {
            nint svc = NativeMethods.OpenService(scm, DlServiceName, SERVICE_START);
            if (svc == 0)
                throw new InvalidOperationException(
                    $"DocumentLocator service is not installed (error {Marshal.GetLastWin32Error()}).");
            try
            {
                if (!NativeMethods.StartService(svc, 0, 0))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != ERROR_SERVICE_ALREADY_RUNNING)
                        throw new InvalidOperationException($"StartService failed (error {err}).");
                }
            }
            finally { NativeMethods.CloseServiceHandle(svc); }
        }
        finally { NativeMethods.CloseServiceHandle(scm); }
    }
}

internal static partial class NativeMethods
{
    [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint OpenService(nint scManager, string serviceName, uint desiredAccess);

    [LibraryImport("advapi32.dll", EntryPoint = "StartServiceW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool StartService(nint service, uint numArgs, nint argVectors);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseServiceHandle(nint handle);
}
