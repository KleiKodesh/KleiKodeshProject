using System.Text.Json;
using System.Text.Json.Serialization;
using KitveiHakodeshService.SeforimDb;

namespace KitveiHakodeshService.Ipc;

// ── Clean semantic RPC envelope ────────────────────────────────────────────────
// Request  : {"op":"<name>","args":{...}}
// Response : {"ok":true,"result":{...}}  |  {"ok":false,"error":"..."}
//
// This is deliberately NOT the DocumentLocator ad-hoc protocol and NOT raw SQL —
// the frontend asks for *what it needs* by op name and never learns the backend.

/// <summary>Inbound request envelope. <see cref="Args"/> is the raw MessagePack bytes of
/// the args map, deserialized into the op's typed DTO by the dispatcher (mirrors how the
/// JSON path kept Args as a deferred JsonElement).</summary>
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

/// <summary>Result of <c>getFonts</c>: system font families that can render Hebrew, sorted
/// alphabetically. Same contract as the hosted <c>getFonts</c> bridge action, so the settings
/// font picker is identical in dev and hosted. Empty when DirectWrite is unavailable — the
/// frontend then falls back to its canvas probe.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class FontsResult
{
    public string[] Fonts { get; set; } = [];
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
/// The bind values are inherently dynamic (string | number | null) and the rows come back in
/// arbitrary shapes, so this path stays JSON — there is no point re-encoding already-JSON,
/// schema-less data as MessagePack. <see cref="ParamsJson"/> is a JSON array string carried
/// verbatim inside the msgpack envelope; the query result likewise rides as a JSON string
/// (see <see cref="RawRowsResult"/>).</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class RawSqlArgs
{
    public string? Sql { get; set; }
    public string? ParamsJson { get; set; }
}

/// <summary>User-settings query result: the rows as a JSON array string (dynamic shape).</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class RawRowsResult
{
    public string RowsJson { get; set; } = "[]";
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
    public bool Restarting { get; set; }
    /// <summary><c>pickSeforimDbPath</c> only: the user dismissed the native dialog, so
    /// nothing was persisted and the caller must leave the current path as-is.</summary>
    public bool Cancelled { get; set; }
    public string? Error { get; set; }
}

/// <summary>A single file-system hit, in the exact shape the Vue app already consumes.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class FileHit
{
    public string FileName  { get; set; } = "";
    public string Path      { get; set; } = "";
    public long   ModifiedDate { get; set; }
    /// <summary>Non-empty only for Otzaria addin entry-point files. "תוסף אוצריא: {name}".</summary>
    public string AddinName { get; set; } = "";
}

/// <summary>Result payload for <c>locateDocuments</c>.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LocateDocumentsResult
{
    public List<FileHit> Results { get; set; } = new();
    public int Total { get; set; }
}

/// <summary>Args for the <c>hbSearch</c> op (HebrewBooks catalog search).</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class HbSearchArgs
{
    public string? Query { get; set; }
    public string? LocalFolder { get; set; }
    public int Limit { get; set; }
}

/// <summary>A HebrewBooks catalog row, in the exact shape the Vue app consumes
/// (see hebrewBooksCatalog.ts HebrewBook).</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class HebrewBook
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string PrintingPlace { get; set; } = "";
    public string PrintingYear { get; set; } = "";
    public int? Pages { get; set; }
    public string Categories { get; set; } = "";
    public bool HasLocalFile { get; set; }
}

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
    public bool NotFound { get; set; }
    public bool NoInternet { get; set; }
    public bool Redownload { get; set; }
    /// <summary>The download was aborted by the user (ביטול) — the caller closes the tab quietly,
    /// no error banner.</summary>
    public bool Cancelled { get; set; }
    public string? Error { get; set; }
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
}

/// <summary>Result of <c>deleteHbLocalFile</c>.</summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class HbDeleteLocalResult
{
    public bool Ok { get; set; }
    public bool NotFound { get; set; }
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

// ── Dictionary (KitveiHakodesh_dictionary.db) ──────────────────────────────────

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
    [JsonPropertyName("source_id")]
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

    public static byte[] Err(string message) => MsgPack.Ser(new RpcEnvelope { Ok = false, Error = message });
}

/// <summary>AOT-safe source-generated (de)serialization for every RPC type.
/// CamelCase policy maps C# PascalCase properties to the camelCase JSON the
/// frontend and DocumentLocator both use.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RpcRequest))]
[JsonSerializable(typeof(LocateDocumentsArgs))]
[JsonSerializable(typeof(RawSqlArgs))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(LocateDocumentsResult))]
[JsonSerializable(typeof(FileHit))]
[JsonSerializable(typeof(HbSearchArgs))]
[JsonSerializable(typeof(HebrewBook))]
[JsonSerializable(typeof(HbSearchResult))]
[JsonSerializable(typeof(HbDownloadArgs))]
[JsonSerializable(typeof(HbDownloadResult))]
[JsonSerializable(typeof(HbCheckLocalArgs))]
[JsonSerializable(typeof(HbCheckLocalResult))]
[JsonSerializable(typeof(HbDeleteLocalArgs))]
[JsonSerializable(typeof(HbDeleteLocalResult))]
[JsonSerializable(typeof(HbProgressArgs))]
[JsonSerializable(typeof(HbProgressResult))]
[JsonSerializable(typeof(PickLocalFileResult))]
[JsonSerializable(typeof(StringArg))]
[JsonSerializable(typeof(StringResult))]
[JsonSerializable(typeof(DictTermArgs))]
[JsonSerializable(typeof(DictCandidatesArgs))]
[JsonSerializable(typeof(SenseRow))]
[JsonSerializable(typeof(DictLink))]
[JsonSerializable(typeof(DictSensesResult))]
[JsonSerializable(typeof(DictExactResult))]
[JsonSerializable(typeof(DictAbbrevResult))]
[JsonSerializable(typeof(DictWordsResult))]
[JsonSerializable(typeof(DictLinksResult))]
[JsonSerializable(typeof(CategoryRow))]
[JsonSerializable(typeof(BookRow))]
[JsonSerializable(typeof(CategoriesResult))]
[JsonSerializable(typeof(BooksResult))]
[JsonSerializable(typeof(BookByIdArgs))]
[JsonSerializable(typeof(BookInfo))]
[JsonSerializable(typeof(BookByIdResult))]
[JsonSerializable(typeof(LinesPagedArgs))]
[JsonSerializable(typeof(LineRow))]
[JsonSerializable(typeof(LinesResult))]
[JsonSerializable(typeof(TocByBookArgs))]
[JsonSerializable(typeof(TocByStructureArgs))]
[JsonSerializable(typeof(TocEntryRow))]
[JsonSerializable(typeof(TocEntriesResult))]
[JsonSerializable(typeof(AltTocStructureRow))]
[JsonSerializable(typeof(AltTocStructuresResult))]
[JsonSerializable(typeof(TocTitleRow))]
[JsonSerializable(typeof(TocTitlesArgs))]
[JsonSerializable(typeof(TocTitlesResult))]
[JsonSerializable(typeof(TocPrefixArgs))]
[JsonSerializable(typeof(TocPrefixRow))]
[JsonSerializable(typeof(TocPrefixResult))]
[JsonSerializable(typeof(LineIdsArgs))]
[JsonSerializable(typeof(BookIdArgs))]
[JsonSerializable(typeof(CommentaryLinkRow))]
[JsonSerializable(typeof(CommentaryLinksResult))]
[JsonSerializable(typeof(LineContentRow))]
[JsonSerializable(typeof(LineContentsResult))]
[JsonSerializable(typeof(WordLinkAnchorRow))]
[JsonSerializable(typeof(WordLinkAnchorsResult))]
[JsonSerializable(typeof(ConnectionTypeRow))]
[JsonSerializable(typeof(ConnectionTypesResult))]
[JsonSerializable(typeof(DefaultCommentatorRow))]
[JsonSerializable(typeof(DefaultCommentatorsResult))]
[JsonSerializable(typeof(ReverseLineDataArgs))]
[JsonSerializable(typeof(ReverseLineRow))]
[JsonSerializable(typeof(ReverseLineDataResult))]
[JsonSerializable(typeof(ReverseBooksArgs))]
[JsonSerializable(typeof(ReverseBookRow))]
[JsonSerializable(typeof(ReverseBooksResult))]
[JsonSerializable(typeof(StaticFilterArgs))]
[JsonSerializable(typeof(StaticFilterRow))]
[JsonSerializable(typeof(StaticFilterResult))]
[JsonSerializable(typeof(SectionNavArgs))]
[JsonSerializable(typeof(SectionNavRow))]
[JsonSerializable(typeof(SectionNavResult))]
[JsonSerializable(typeof(TocSectionArgs))]
[JsonSerializable(typeof(TocSectionRow))]
[JsonSerializable(typeof(TocSectionResult))]
[JsonSerializable(typeof(LinkTargetArgs))]
[JsonSerializable(typeof(LinkTargetRow))]
[JsonSerializable(typeof(LinkTargetResult))]
[JsonSerializable(typeof(TocPathRow))]
[JsonSerializable(typeof(TocPathsResult))]
[JsonSerializable(typeof(EnclosingTocPathArgs))]
[JsonSerializable(typeof(EnclosingTocPathRow))]
[JsonSerializable(typeof(EnclosingTocPathResult))]
[JsonSerializable(typeof(LineBookRow))]
[JsonSerializable(typeof(LineBooksResult))]
[JsonSerializable(typeof(LineIdArgs))]
[JsonSerializable(typeof(LineIndexRow))]
[JsonSerializable(typeof(LineIndexResult))]
[JsonSerializable(typeof(TitlePatternArgs))]
[JsonSerializable(typeof(ExactTitleArgs))]
[JsonSerializable(typeof(BookIdRow))]
[JsonSerializable(typeof(BookIdsResult))]
[JsonSerializable(typeof(BoldLinesArgs))]
[JsonSerializable(typeof(BoldLineRow))]
[JsonSerializable(typeof(BoldLinesResult))]
[JsonSerializable(typeof(EitherPatternArgs))]
[JsonSerializable(typeof(LineByIndexArgs))]
[JsonSerializable(typeof(RawLineRow))]
[JsonSerializable(typeof(RawLinesResult))]
[JsonSerializable(typeof(FtsSearchArgs))]
[JsonSerializable(typeof(FtsHit))]
[JsonSerializable(typeof(FtsSearchResult))]
[JsonSerializable(typeof(FtsIndexStatus))]
[JsonSerializable(typeof(string))]
internal partial class RpcJsonContext : JsonSerializerContext;
