using System.Collections.Generic;

namespace KitveiHakodesh.Core.LocalFiles;

// Wire types for the local file search. Property names are payload keys - do not rename.

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

/// <summary>
/// Result payload for <c>localFileIndexStatus</c>: whether the file index is still being
/// built. Polled by the local-file-search page while a search is outstanding, so that a
/// cold build (minutes) says "building the index" instead of "searching".
/// <para>Non-blocking: it reports the state as of right now and never waits for the build.</para>
/// </summary>
[MessagePack.MessagePackObject(keyAsPropertyName: true)]
public sealed class LocalFileIndexStatusResult
{
    public bool IsBuilding { get; set; }
    /// <summary>The crawler's latest progress line (English, diagnostic). The UI shows its
    /// own wording; this is detail, not display text.</summary>
    public string? Message { get; set; }
}
