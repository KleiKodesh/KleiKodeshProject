using KitveiHakodeshService.SeforimDb;


namespace KitveiHakodeshService.Ipc;

// ── Clean semantic RPC envelope ────────────────────────────────────────────────
// Request  : {"op":"<name>","args":{...}}
// Response : {"ok":true,"result":{...}}  |  {"ok":false,"error":"..."}
//
// This is deliberately NOT the DocumentLocator ad-hoc protocol and NOT raw SQL —
// the frontend asks for *what it needs* by op name and never learns the backend.

/// <summary>Inbound request envelope. <see cref="Args"/> is the raw MessagePack bytes of
/// the args map, deserialized into the op's typed DTO by the dispatcher.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class RpcRequest
{
    public string? Op { get; set; }
    public byte[]? Args { get; set; }
}

/// <summary>Args for the <c>locateDocuments</c> op.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LocateDocumentsArgs
{
    public string? Query { get; set; }
    public int Max { get; set; }
}

/// <summary>Args for <c>openLocalFile</c> — a local file path to authorize for serving.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class OpenLocalFileArgs
{
    public string? Path { get; set; }
}

/// <summary>Result of <c>openLocalFile</c>: an opaque capability <see cref="Handle"/> for
/// <c>GET /file/&lt;handle&gt;</c> (empty when rejected), the file's display name, and an error message
/// when the path failed validation.
///
/// For HTML files, <see cref="FolderHandle"/> is also set — a folder-scoped capability that
/// allows serving any file inside the same directory (CSS, JS, images, fonts). The URL the
/// browser loads is then <c>/khs-file/&lt;FolderHandle&gt;/filename.html</c> so sibling requests
/// like <c>/khs-file/&lt;FolderHandle&gt;/css/style.css</c> resolve automatically. This mirrors the
/// hosted mode's <c>SetVirtualHostNameToFolderMapping</c> which already serves the whole folder.
///
/// <see cref="IsOtzariaAddin"/> is true when a <c>manifest.json</c> exists next to the HTML file —
/// the Vue HtmlViewPage uses this to activate the Otzaria addin bridge.
/// </summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class OpenLocalFileResult
{
    public string Handle { get; set; } = "";
    public string FileName { get; set; } = "";
    /// <summary>Folder-scoped handle — set for HTML files. Serves the whole containing folder
    /// so sibling CSS/JS/image assets load at /file/&lt;FolderHandle&gt;/relative/path.</summary>
    public string FolderHandle { get; set; } = "";
    /// <summary>True when manifest.json exists next to the HTML file (Otzaria addin).</summary>
    public bool IsOtzariaAddin { get; set; }
    /// <summary>The conversion was aborted by the user (ביטול) — the caller closes the tab quietly,
    /// no error dialog.</summary>
    public bool Cancelled { get; set; }
    public string? Error { get; set; }
}

/// <summary>Result of <c>pickLocalFile</c>: the absolute path the user chose in the native
/// open-file dialog (empty + <see cref="Cancelled"/> when dismissed). The client follows up with
/// <c>openLocalFile</c> on the path — picking grants nothing by itself.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class PickLocalFileResult
{
    public string Path { get; set; } = "";
    public string FileName { get; set; } = "";
    public bool Cancelled { get; set; }
}

/// <summary>Result of <c>pickFolder</c>: the absolute folder path the user chose in the native
/// browse-for-folder dialog (empty + <see cref="Cancelled"/> when dismissed). Used by the settings
/// page for the HebrewBooks local folder and for adding excluded folders.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class PickFolderResult
{
    public string Path { get; set; } = "";
    public bool Cancelled { get; set; }
}

/// <summary>Args for <c>setExcludedFolders</c> — the full replacement list of folders to exclude
/// from file-search results.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class ExcludedFoldersArgs
{
    public List<string>? Folders { get; set; }
}

/// <summary>Result of <c>getExcludedFolders</c> / <c>setExcludedFolders</c>: the persisted list.
/// <see cref="Error"/> is set when the save failed (the list then reflects what is still on disk).</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class ExcludedFoldersResult
{
    public List<string> Folders { get; set; } = [];
    public string? Error { get; set; }
}

/// <summary>Args for <c>openFileInDefaultApp</c> — a local file path to hand off to the OS's
/// registered default program (shell-execute). Unlike <c>openLocalFile</c>, this does not serve
/// any bytes over HTTP; it only launches the associated program on the service's machine.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class OpenInDefaultAppArgs
{
    public string? Path { get; set; }
}

/// <summary>Result of <c>openFileInDefaultApp</c>: <see cref="Ok"/> true when the launch was
/// requested, or an <see cref="Error"/> message on validation/launch failure.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class OpenInDefaultAppResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
}

/// <summary>Args for <c>exportToWord</c> — the assembled document HTML and the book title used
/// for the temp file name.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class ExportToWordArgs
{
    public string? Html { get; set; }
    public string? Title { get; set; }
}

/// <summary>Result of <c>exportToWord</c>: <see cref="Ok"/> true once Word opened the exported
/// HTML, or an <see cref="Error"/> message. Mirrors the hosted bridge action's { ok, error }
/// shape so the frontend caller is identical in dev and hosted.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class ExportToWordResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
}

/// <summary>Result of <c>pasteIntoWord</c>: <see cref="Ok"/> true once the clipboard was pasted
/// into Word at the cursor, or an <see cref="Error"/> message (Word not installed, COM failure).
/// Mirrors the hosted bridge action's <c>{ ok, error }</c> shape so the frontend caller is
/// identical in dev and hosted.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class PasteIntoWordResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
}

/// <summary>Parameterized SQL (positional '?') for the generic user-settings read/write ops.
/// The bind values are plain MessagePack values (string | number | bool | null) — no JSON
/// string riding inside the envelope.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class RawSqlArgs
{
    public string? Sql { get; set; }
    public List<object?>? Params { get; set; }
}

/// <summary>User-settings query result: one map per row, column name -> value. The client
/// lower-cases each key's first letter (serviceClient decodeKey), so queries must name their
/// columns in camelCase — every user_settings column already is.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class RawRowsResult
{
    public List<Dictionary<string, object?>> Rows { get; set; } = [];
}

/// <summary>User-settings execute result.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class ExecuteResult
{
    public long LastInsertId { get; set; }
}

/// <summary>Args for <c>setSeforimDbPath</c>.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class DbPathArgs
{
    public string? Path { get; set; }
}

/// <summary>Result for the seforim-DB-path ops. <c>IsCustom</c> = the registry value is
/// set (user's explicit choice); <c>Restarting</c> = the service restarts to apply the
/// change (the dev courier respawns it on the next request).</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class DbPathResult
{
    public string Path { get; set; } = "";
    public bool IsCustom { get; set; }
    public bool Exists { get; set; }
    /// <summary><c>getSeforimDbPath</c> only: an Otzaria update of the database stopped
    /// half-way (its hot -journal or .applying marker is still beside the file), so every
    /// read-only query fails until Otzaria is opened and finishes it.</summary>
    public bool UpdateInterrupted { get; set; }
    /// <summary><c>getSeforimDbPath</c> only: the database answers a query. False with
    /// <see cref="Exists"/> true means the file is there but cannot be read (not a seforim
    /// database, corrupt, locked).</summary>
    public bool Readable { get; set; }
    /// <summary><c>getSeforimDbPath</c> only: with <see cref="Readable"/> false, the read
    /// failed TRANSIENTLY (SQLITE_BUSY/LOCKED, I/O error) — it will clear by itself, so the
    /// page shows no "unreadable" banner.</summary>
    public bool ReadBusy { get; set; }
    public bool Restarting { get; set; }
    /// <summary>Identifies the service instance that answered. A path change restarts the
    /// service, but the old instance keeps answering (with the OLD database) for a few seconds
    /// after it said <see cref="Restarting"/>; the page compares this against later
    /// <c>getSeforimDbPath</c> replies to know the fresh instance is up before it reloads.</summary>
    public string InstanceId { get; set; } = "";
    /// <summary><c>pickSeforimDbPath</c> only: the user dismissed the native dialog, so
    /// nothing was persisted and the caller must leave the current path as-is.</summary>
    public bool Cancelled { get; set; }
    public string? Error { get; set; }
}

/// <summary>Args for the <c>hbSearch</c> op (HebrewBooks catalog search).</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class HbSearchArgs
{
    public string? Query { get; set; }
    public string? LocalFolder { get; set; }
    public int Limit { get; set; }
}

// A HebrewBooks catalog row is KitveiHakodesh.Core.HebrewBooks.HebrewBook — see the alias at
// the top of this file. Core's type carries the same keyAsPropertyName MessagePack contract and
// the same field names, so the bytes the Vue app receives are unchanged (hebrewBooksCatalog.ts
// HebrewBook). The mirror that used to sit here was a duplicate, not a transport envelope.

/// <summary>Result payload for <c>hbSearch</c>.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class HbSearchResult
{
    public List<HebrewBook> Books { get; set; } = new();
}

/// <summary>Args for <c>triggerHbDownload</c> / <c>restoreHbPdf</c> — download/serve a book's PDF
/// entirely in the service. <c>AllowDownload</c> false = restore-only (report a miss instead of
/// fetching), <c>IsOnline</c> false = skip the network attempt and report no-internet.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class HbDownloadArgs
{
    public string? BookId { get; set; }
    public string? LocalFolder { get; set; }
    public bool AllowDownload { get; set; } = true;
    public bool IsOnline { get; set; } = true;
}

/// <summary>Result of <c>triggerHbDownload</c> / <c>restoreHbPdf</c>: a capability <see cref="Handle"/>
/// for <c>GET /file/{h}</c> when the PDF resolved, or one of the miss reasons. <c>Redownload</c> is
/// set by restore when nothing is cached and the caller must re-run the download flow.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class HbDownloadResult
{
    public string Handle { get; set; } = "";
    /// <summary>The PDF's path on disk, set whenever <see cref="Handle"/> is. The dev page loads
    /// the file through <c>GET /file/{Handle}</c>; the hosted page has no HTTP host and asks its
    /// WebView2 host to serve this path from a virtual host instead.</summary>
    public string Path { get; set; } = "";
    public bool NotFound { get; set; }
    public bool NoInternet { get; set; }
    public bool Redownload { get; set; }
    /// <summary>The download was aborted by the user (ביטול) — the caller closes the tab quietly,
    /// no error banner.</summary>
    public bool Cancelled { get; set; }
    public string? Error { get; set; }
    /// <summary>Full failure detail (exception ToString, HTTP status/content-type/final URL) for
    /// the webview console — never shown in the UI.</summary>
    public string? Detail { get; set; }
}

/// <summary>Args for <c>getHebrewBooksEditions</c>: a seforim.db book id.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class HebrewBooksEditionsArgs
{
    public int BookId { get; set; }
}

/// <summary>Result of <c>getHebrewBooksEditions</c>: the book's HebrewBooks scans, best first;
/// empty when it has none or the library in use has no Otzaria map.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class HebrewBooksEditionsResult
{
    public List<HebrewBooksEdition> Editions { get; set; } = new();
}

/// <summary>Args for <c>checkHbLocalFiles</c>.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class HbCheckLocalArgs
{
    public List<string>? BookIds { get; set; }
    public string? LocalFolder { get; set; }
}

/// <summary>Result of <c>checkHbLocalFiles</c>: the subset of ids present on disk.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class HbCheckLocalResult
{
    public List<string> ExistingIds { get; set; } = new();
}

/// <summary>Args for <c>deleteHbLocalFile</c>.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class HbDeleteLocalArgs
{
    public string? BookId { get; set; }
    public string? LocalFolder { get; set; }
    /// <summary>Move to the Recycle Bin instead of deleting permanently. Dev only: the installed
    /// service runs as LocalSystem, so the hosted page recycles through its user-side host.</summary>
    public bool ToRecycleBin { get; set; }
}

/// <summary>Result of <c>deleteHbLocalFile</c>.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class HbDeleteLocalResult
{
    public bool Ok { get; set; }
    public bool NotFound { get; set; }
    /// <summary>The file's drive has no Recycle Bin; nothing was deleted.</summary>
    public bool NoRecycleBin { get; set; }
    public string? Error { get; set; }
}

/// <summary>Args for <c>hbDownloadProgress</c> — the book id whose in-flight download to poll.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class HbProgressArgs
{
    public string? BookId { get; set; }
}

/// <summary>Result of <c>hbDownloadProgress</c>. <c>Active</c> is false when no download is in
/// flight (done, or never started). <c>Total</c> 0 means the server sent no Content-Length, so
/// only <c>Received</c> is meaningful (show MB, not %).</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class HbProgressResult
{
    public bool Active { get; set; }
    public long Received { get; set; }
    public long Total { get; set; }
}

/// <summary>Generic single-string arg (e.g. <c>setHbLocalFolder</c>).</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class StringArg
{
    public string? Value { get; set; }
}

/// <summary>Generic single-string result (e.g. <c>getHbLocalFolder</c>).</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class StringResult
{
    public string Value { get; set; } = "";
}

/// <summary>Result of <c>getUpdateStatus</c> / <c>checkForUpdateNow</c>: whether a newer release
/// is downloaded and ready to install, and the version installed now.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class UpdateStatusResult
{
    public bool Ready { get; set; }
    public string Version { get; set; } = "";
    /// <summary>The installer's stamp; "" when this is not an installed copy.</summary>
    public string InstalledVersion { get; set; } = "";
    /// <summary><c>checkForUpdateNow</c> only: why the check could not run or failed; "" when it ran.</summary>
    public string CheckError { get; set; } = "";
}

/// <summary>One frame of <c>updateProgressStream</c>: the release being downloaded now ("" when
/// none) with its bytes so far, and the downloaded update ready to install ("" when none).</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class UpdateProgressFrame
{
    public string DownloadingVersion { get; set; } = "";
    public long Received { get; set; }
    public long Total { get; set; }
    public string ReadyVersion { get; set; } = "";
}

/// <summary>Args for <c>registerOwner</c> / <c>releaseOwner</c>: a host process using this console
/// instance (see Http/OwnerWatcher).</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class OwnerArgs
{
    public int Pid { get; set; }
}

/// <summary>Generic single-bool arg (e.g. <c>setTurnOffUpdates</c>).</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class BoolArg
{
    public bool Value { get; set; }
}

/// <summary>Generic single-bool result (e.g. <c>getTurnOffUpdates</c>).</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class BoolResult
{
    public bool Value { get; set; }
}

// ── Dictionary (Dictionary.db) ──────────────────────────────────

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class DictTermArgs
{
    public string? Term { get; set; }
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class DictCandidatesArgs
{
    public List<string>? Candidates { get; set; }
}

/// <summary>A dictionary sense row, matching the Vue SenseRow shape exactly
/// (note the snake_case <c>source_id</c> the frontend expects).</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class SenseRow
{
    public string Headword { get; set; } = "";
    public string? Nikud { get; set; }
    public string Text { get; set; } = "";
    public string? Source { get; set; }
    /// <summary>Reaches the app as <c>source_id</c>: serviceClient's decodeKey renames it.</summary>
    public int? SourceId { get; set; }
}

/// <summary>A related-word link (kind + target headword).</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class DictLink
{
    public string Kind { get; set; } = "";
    public string Word { get; set; } = "";
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class DictSensesResult
{
    public List<SenseRow> Rows { get; set; } = new();
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class DictExactResult
{
    public List<SenseRow> Rows { get; set; } = new();
    public bool IsExact { get; set; }
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class DictAbbrevResult
{
    public string? Matched { get; set; }
    public List<SenseRow> Rows { get; set; } = new();
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class DictWordsResult
{
    public List<string> Words { get; set; } = new();
}

/// <summary>Args for <c>dictSeforimHeadwords</c>: one seforim.db reference work
/// (metzudat/malbim/menachem/aruch/micropedia), the term, and the tier
/// ("exact" | "prefix" | "contains").</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class DictSeforimHeadwordArgs
{
    public string? Source { get; set; }
    public string? Term { get; set; }
    public string? Mode { get; set; }
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class DictSeforimHeadwordsResult
{
    public List<global::KitveiHakodesh.Core.Dictionary.SeforimHeadwordPointer> Rows { get; set; } = new();
}

[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class DictLinksResult
{
    public List<DictLink> Links { get; set; } = new();
}

/// <summary>Builds the MessagePack response envelope. Ok wraps the op's already-serialized
/// result bytes (nested bin); Err carries the message.</summary>
internal static class RpcResponse
{
    public static byte[] Ok(byte[]? resultBytes) => MsgPack.Ser(new RpcEnvelope { Ok = true, Result = resultBytes });

    /// <summary>A reply that carries an op-level failure in its result (e.g. Error = ex.Message):
    /// the whole exception rides on the envelope so the client console still shows it.</summary>
    public static byte[] OkWithFailure(byte[]? resultBytes, string op, Exception ex) =>
        MsgPack.Ser(new RpcEnvelope { Ok = true, Result = resultBytes, Detail = $"op '{op}' failed:\n{ex}" });

    public static byte[] Err(string message, string? detail = null) =>
        MsgPack.Ser(new RpcEnvelope { Ok = false, Error = message, Detail = detail });

    /// <summary>An op threw: short message for callers, the whole exception for the console.</summary>
    public static byte[] Err(string op, Exception ex) => Err(ex.Message, $"op '{op}' threw:\n{ex}");
}

