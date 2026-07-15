using System.Text.Json;
using System.Text.Json.Serialization;
using KitveiHakodeshService.SefroimDb;

namespace KitveiHakodeshService.Ipc;

// ── Clean semantic RPC envelope ────────────────────────────────────────────────
// Request  : {"op":"<name>","args":{...}}
// Response : {"ok":true,"result":{...}}  |  {"ok":false,"error":"..."}
//
// This is deliberately NOT the DocumentLocator ad-hoc protocol and NOT raw SQL —
// the frontend asks for *what it needs* by op name and never learns the backend.

/// <summary>Inbound request envelope. <see cref="Args"/> is left as a raw element
/// and deserialized per-op by the dispatcher.</summary>
public sealed class RpcRequest
{
    public string? Op { get; set; }
    public JsonElement Args { get; set; }
}

/// <summary>Args for the <c>locateDocuments</c> op.</summary>
public sealed class LocateDocumentsArgs
{
    public string? Query { get; set; }
    public int Max { get; set; }
}

/// <summary>Parameterized SQL (positional '?') for the generic user-settings read/write ops.</summary>
public sealed class RawSqlArgs
{
    public string? Sql { get; set; }
    public JsonElement[]? Params { get; set; }
}

/// <summary>A single file-system hit, in the exact shape the Vue app already consumes.</summary>
public sealed class FileHit
{
    public string FileName { get; set; } = "";
    public string Path { get; set; } = "";
    public long ModifiedDate { get; set; }
}

/// <summary>Result payload for <c>locateDocuments</c>.</summary>
public sealed class LocateDocumentsResult
{
    public List<FileHit> Results { get; set; } = new();
    public int Total { get; set; }
}

/// <summary>The subset of the DocumentLocator pipe response we consume.</summary>
public sealed class DlResponse
{
    public string? Status { get; set; }
    public string? Message { get; set; }
    public int Total { get; set; }
    public List<DlEntry>? Entries { get; set; }
    public List<string>? Paths { get; set; }
}

public sealed class DlEntry
{
    public string? Path { get; set; }
    public long Date { get; set; }
}

/// <summary>Args for the <c>hbSearch</c> op (HebrewBooks catalog search).</summary>
public sealed class HbSearchArgs
{
    public string? Query { get; set; }
    public string? LocalFolder { get; set; }
    public int Limit { get; set; }
}

/// <summary>A HebrewBooks catalog row, in the exact shape the Vue app consumes
/// (see hebrewBooksCatalog.ts HebrewBook).</summary>
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
public sealed class HbSearchResult
{
    public List<HebrewBook> Books { get; set; } = new();
}

// ── Dictionary (KitveiHakodesh_dictionary.db) ──────────────────────────────────

public sealed class DictTermArgs
{
    public string? Term { get; set; }
}

public sealed class DictCandidatesArgs
{
    public List<string>? Candidates { get; set; }
}

/// <summary>A dictionary sense row, matching the Vue SenseRow shape exactly
/// (note the snake_case <c>source_id</c> the frontend expects).</summary>
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
public sealed class DictLink
{
    public string Kind { get; set; } = "";
    public string Word { get; set; } = "";
}

public sealed class DictSensesResult
{
    public List<SenseRow> Rows { get; set; } = new();
}

public sealed class DictExactResult
{
    public List<SenseRow> Rows { get; set; } = new();
    public bool IsExact { get; set; }
}

public sealed class DictAbbrevResult
{
    public string? Matched { get; set; }
    public List<SenseRow> Rows { get; set; } = new();
}

public sealed class DictWordsResult
{
    public List<string> Words { get; set; } = new();
}

public sealed class DictLinksResult
{
    public List<DictLink> Links { get; set; } = new();
}

/// <summary>Composes the <c>{"ok":...}</c> envelope around an already-serialized result.</summary>
internal static class RpcResponse
{
    public static string Ok(string resultJson) => $"{{\"ok\":true,\"result\":{resultJson}}}";

    public static string Err(string message) =>
        $"{{\"ok\":false,\"error\":{JsonSerializer.Serialize(message, RpcJsonContext.Default.String)}}}";
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
[JsonSerializable(typeof(DlResponse))]
[JsonSerializable(typeof(DlEntry))]
[JsonSerializable(typeof(HbSearchArgs))]
[JsonSerializable(typeof(HebrewBook))]
[JsonSerializable(typeof(HbSearchResult))]
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
[JsonSerializable(typeof(FtsSearchStartArgs))]
[JsonSerializable(typeof(FtsSearchStartResult))]
[JsonSerializable(typeof(FtsSearchPollArgs))]
[JsonSerializable(typeof(FtsSearchPollResult))]
[JsonSerializable(typeof(FtsCancelArgs))]
[JsonSerializable(typeof(string))]
internal partial class RpcJsonContext : JsonSerializerContext;
