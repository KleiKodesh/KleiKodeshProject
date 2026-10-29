using KitveiHakodesh.Core.Common;
using global::FastDocView;

namespace KitveiHakodesh.Core.FastDocView;

/// <summary>
/// The FastDocView documents open in this process, mapped to the service's MessagePack types. The
/// library's <see cref="WordWindowedDocumentSet"/> does the hosting: per-document lock, idle close after
/// 10 minutes, the shared sidecar cache in <see cref="IndexCacheDirectory"/>. A call on a closed or changed
/// document throws <see cref="FastDocViewSourceChangedException"/> and the page reopens.
/// Data is windowed: only rendered windows of blocks leave here, never the whole document.
/// </summary>
public sealed class FastDocViewOpenDocuments : IDisposable
{
    public const int MaxBlocksPerRequest = 100;
    public const int MaxBlocksPerScan = 100_000;
    /// <summary>One scan call holds the document's lock for about this long at most; the page sends no
    /// scan while a block fetch waits, so the screen never queues behind more than one call. Measured on
    /// the 87 MB .docx sample (123,007 blocks): 5000 blocks per call took median 83-116 ms, max 221-237 ms;
    /// this budget: calls median 23-27 ms, p95 33-43 ms, full build 2.5-4.2 s live (library alone ~3 s).</summary>
    public static readonly TimeSpan ScanTimeBudget = TimeSpan.FromMilliseconds(20);

    /// <summary>Sidecar index cache under the app's per-user data root.</summary>
    public static string IndexCacheDirectory => Path.Combine(AppFileLocator.UserInstallRoot, "FastDocViewIndexCache");

    private readonly WordWindowedDocumentSet _documents = new(IndexCacheDirectory);

    /// <summary>Opens <paramref name="validatedPath"/> (the caller validated access). A first open of
    /// a big file returns after its first blocks; the page then calls <see cref="Scan"/> until complete.</summary>
    public FastDocViewSummary Open(string validatedPath)
    {
        string id = _documents.Open(validatedPath);
        return Use(id, document =>
        {
            (int footnotes, int endnotes) = document.NoteCounts;
            return new FastDocViewSummary
            {
                DocumentId = id,
                BlockCount = document.BlockCount,
                KnownBlockCount = document.KnownBlockCount,
                IsIndexComplete = document.IsIndexComplete,
                IsRightToLeft = document.IsRightToLeft,
                Stylesheet = document.Stylesheet,
                HeadingCount = document.Headings.Count,
                FootnoteCount = footnotes,
                EndnoteCount = endnotes,
            };
        });
    }

    public List<FastDocViewBlock> Blocks(string documentId, int first, int count) =>
        Use(documentId, document =>
        {
            int start = Math.Max(0, first);
            int clampedCount = Math.Clamp(count, 0, MaxBlocksPerRequest);
            var blocks = new List<FastDocViewBlock>(clampedCount);
            foreach (WordDocumentBlockHtml block in document.RenderBlocks(start, clampedCount))
            {
                var notes = new List<FastDocViewNote>(block.Notes.Count);
                foreach (WordDocumentNoteHtml note in block.Notes)
                    notes.Add(new FastDocViewNote { Kind = NoteKindName(note.Kind), Number = note.Number, Label = note.Label, Html = note.Html });
                blocks.Add(new FastDocViewBlock { Index = block.BlockIndex, Kind = BlockKindName(block.Kind), Html = block.Html, Notes = notes });
            }
            return blocks;
        });

    /// <summary>Headings known so far (all of them once the index is complete).</summary>
    public List<FastDocViewHeading> Headings(string documentId) =>
        Use(documentId, document =>
        {
            var headings = new List<FastDocViewHeading>(document.Headings.Count);
            foreach (WordDocumentHeading heading in document.Headings)
                headings.Add(new FastDocViewHeading { BlockIndex = heading.BlockIndex, Level = heading.Level, Text = heading.Text });
            return headings;
        });

    /// <summary>Blocks searched per call at most - each call holds the document's lock, so a block fetch for
    /// the screen waits at most one step.</summary>
    public const int MaxBlocksPerSearch = 2000;

    /// <summary>One bounded step of an in-document search (the library's windowed Search, the app's book-view
    /// normalisation): scans up to <paramref name="maxBlocks"/> blocks from <paramref name="fromBlock"/>.</summary>
    public FastDocViewSearchResult Search(string documentId, string query, bool matchCase, int fromBlock, int maxBlocks) =>
        Use(documentId, document =>
        {
            WordDocumentSearchBatch batch = document.Search(query ?? "", new WordDocumentSearchOptions(MatchCase: matchCase),
                                                            Math.Max(0, fromBlock), Math.Clamp(maxBlocks, 1, MaxBlocksPerSearch));
            return new FastDocViewSearchResult { HitBlocks = batch.HitBlocks, NextBlock = batch.NextBlock, ReachedEnd = batch.ReachedEnd };
        });

    /// <summary>The block holding bookmark <paramref name="name"/> (an internal link's target); an index
    /// still building scans on only until it is found. Null when the document has no such bookmark.</summary>
    public FastDocViewBookmarkResult Bookmark(string documentId, string name) =>
        Use(documentId, document => new FastDocViewBookmarkResult { BlockIndex = string.IsNullOrEmpty(name) ? null : document.FindBookmark(name) });

    /// <summary>Page text width per section (index-time data); the known ones while the index builds.</summary>
    public List<FastDocViewSection> Sections(string documentId) =>
        Use(documentId, document =>
        {
            var sections = new List<FastDocViewSection>(document.Sections.Count);
            foreach (WordDocumentSection section in document.Sections)
                sections.Add(new FastDocViewSection { FirstBlockIndex = section.FirstBlockIndex, TextWidthTwips = section.TextWidthTwips });
            return sections;
        });

    /// <summary>Int32 little-endian per known block: the estimated text length (index-time data, no file access).</summary>
    public byte[] Weights(string documentId) =>
        Use(documentId, document =>
        {
            int[] lengths = document.EstimatedTextLengths();
            var bytes = new byte[lengths.Length * 4];
            Buffer.BlockCopy(lengths, 0, bytes, 0, bytes.Length);
            return bytes;
        });

    /// <summary>Builds up to <paramref name="blocks"/> more of the index, stopping once
    /// <see cref="ScanTimeBudget"/> is spent; renders nothing, returns only counts. The page calls
    /// again until complete, and block fetches get the lock between calls.</summary>
    public FastDocViewScanResult Scan(string documentId, int blocks) =>
        Use(documentId, document =>
        {
            document.ScanFor(Math.Clamp(blocks, 1, MaxBlocksPerScan), ScanTimeBudget);
            (int footnotes, int endnotes) = document.NoteCounts;
            return new FastDocViewScanResult
            {
                KnownBlockCount = document.KnownBlockCount,
                IsIndexComplete = document.IsIndexComplete,
                FootnoteCount = footnotes,
                EndnoteCount = endnotes,
            };
        });

    public void Close(string documentId) => _documents.Close(documentId);

    private T Use<T>(string documentId, Func<IWordWindowedDocument, T> action)
    {
        try { return _documents.Use(documentId, action); }
        catch (WordDocumentClosedException exception) { throw new FastDocViewSourceChangedException(exception.Message); }
    }

    private static string NoteKindName(WordDocumentNoteKind kind) => kind == WordDocumentNoteKind.Endnote ? "endnote" : "footnote";

    private static string BlockKindName(WordDocumentBlockKind kind) => kind switch
    {
        WordDocumentBlockKind.Paragraph => "paragraph",
        WordDocumentBlockKind.Table => "table",
        WordDocumentBlockKind.TableRow => "tableRow",
        WordDocumentBlockKind.ContentControl => "contentControl",
        _ => "other",
    };

    public void Dispose() => _documents.Dispose();
}
