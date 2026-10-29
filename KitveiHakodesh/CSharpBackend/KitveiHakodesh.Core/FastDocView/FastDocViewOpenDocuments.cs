using System.Diagnostics;
using KitveiHakodesh.Core.Common;
using global::FastDocView;

namespace KitveiHakodesh.Core.FastDocView;

/// <summary>
/// The FastDocView documents open in this process: id -> document. Every call on a document runs
/// under that document's lock (the library's documents are single-threaded). A document nobody
/// asked for in <see cref="IdleCloseAfter"/> is closed and disposed; a later call with its id fails
/// with <see cref="FastDocViewSourceChangedException"/> and the page reopens - quickly, because
/// the sidecar index lives in <see cref="IndexCacheDirectory"/>, shared by every process.
/// Data is windowed: only rendered windows of blocks leave here, never the whole document.
/// </summary>
public sealed class FastDocViewOpenDocuments : IDisposable
{
    public static readonly TimeSpan IdleCloseAfter = TimeSpan.FromMinutes(10);
    public const int MaxBlocksPerRequest = 100;
    public const int MaxBlocksPerScan = 100_000;
    /// <summary>One scan call holds the document's lock for about this long at most; the page sends no
    /// scan while a block fetch waits, so the screen never queues behind more than one call. Measured on
    /// the 87 MB .docx sample (123,007 blocks): 5000 blocks per call took median 83-116 ms, max 221-237 ms;
    /// this budget: calls median 23-27 ms, p95 33-43 ms, full build 2.5-4.2 s live (library alone ~3 s).</summary>
    public static readonly TimeSpan ScanTimeBudget = TimeSpan.FromMilliseconds(20);
    /// <summary>Blocks scanned between two budget checks.</summary>
    public const int ScanStepBlocks = 64;

    /// <summary>Sidecar index cache under the app's per-user data root.</summary>
    public static string IndexCacheDirectory => Path.Combine(AppFileLocator.UserInstallRoot, "FastDocViewIndexCache");

    private sealed class OpenDocument(IWordWindowedDocument document)
    {
        public readonly IWordWindowedDocument Document = document;
        public readonly object Gate = new();
        public long LastUsedTicks = Environment.TickCount64;
    }

    private readonly Dictionary<string, OpenDocument> _documents = new(StringComparer.Ordinal);
    private readonly object _documentsGate = new();
    private readonly Timer _idleTimer;

    public FastDocViewOpenDocuments()
    {
        _idleTimer = new Timer(_ => CloseIdle(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>Opens <paramref name="validatedPath"/> (the caller validated access). A first open of
    /// a big file returns after its first blocks; the page then calls <see cref="Scan"/> until complete.</summary>
    public FastDocViewSummary Open(string validatedPath)
    {
        string cacheDirectory = IndexCacheDirectory;
        Directory.CreateDirectory(cacheDirectory);
        var policy = new WordIndexPolicy { CacheDirectory = cacheDirectory, IndexesOnDemand = true };
        IWordWindowedDocument document = WordWindowedDocumentOpener.Open(validatedPath, policy);
        string id = Guid.NewGuid().ToString("N");
        var open = new OpenDocument(document);
        lock (open.Gate)
        {
            lock (_documentsGate) _documents[id] = open;
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
        }
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
            int blockCount = document.BlockCount;
            var bytes = new byte[blockCount * 4];
            for (int index = 0; index < blockCount; index++)
                BitConverter.TryWriteBytes(bytes.AsSpan(index * 4), document.EstimatedTextLength(index));
            return bytes;
        });

    /// <summary>Builds up to <paramref name="blocks"/> more of the index, stopping once
    /// <see cref="ScanTimeBudget"/> is spent; renders nothing, returns only counts. The page calls
    /// again until complete, and block fetches get the lock between calls.</summary>
    public FastDocViewScanResult Scan(string documentId, int blocks) =>
        Use(documentId, document =>
        {
            int target = (int)Math.Min(int.MaxValue, (long)document.KnownBlockCount + Math.Clamp(blocks, 1, MaxBlocksPerScan));
            long started = Stopwatch.GetTimestamp();
            while (!document.IsIndexComplete && document.KnownBlockCount < target)
            {
                document.ScanAhead(Math.Min(ScanStepBlocks, target - document.KnownBlockCount));
                if (Stopwatch.GetElapsedTime(started) >= ScanTimeBudget) break;
            }
            (int footnotes, int endnotes) = document.NoteCounts;
            return new FastDocViewScanResult
            {
                KnownBlockCount = document.KnownBlockCount,
                IsIndexComplete = document.IsIndexComplete,
                FootnoteCount = footnotes,
                EndnoteCount = endnotes,
            };
        });

    public void Close(string documentId)
    {
        OpenDocument? open;
        lock (_documentsGate)
        {
            if (!_documents.Remove(documentId ?? "", out open)) return;
        }
        lock (open.Gate) open.Document.Dispose();
    }

    private T Use<T>(string documentId, Func<IWordWindowedDocument, T> action)
    {
        OpenDocument? open;
        lock (_documentsGate) _documents.TryGetValue(documentId ?? "", out open);
        if (open is null) throw new FastDocViewSourceChangedException("the document is not open");
        lock (open.Gate)
        {
            Interlocked.Exchange(ref open.LastUsedTicks, Environment.TickCount64);
            try { return action(open.Document); }
            catch (WordDocumentSourceChangedException)
            {
                Close(documentId!);
                throw new FastDocViewSourceChangedException("the file changed on disk");
            }
            catch (ObjectDisposedException)
            {
                throw new FastDocViewSourceChangedException("the document was closed");
            }
        }
    }

    private void CloseIdle()
    {
        long now = Environment.TickCount64;
        var idle = new List<string>();
        lock (_documentsGate)
        {
            foreach (var (id, open) in _documents)
                if (now - Interlocked.Read(ref open.LastUsedTicks) > (long)IdleCloseAfter.TotalMilliseconds) idle.Add(id);
        }
        foreach (string id in idle) Close(id);
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

    public void Dispose()
    {
        _idleTimer.Dispose();
        List<string> ids;
        lock (_documentsGate) ids = [.. _documents.Keys];
        foreach (string id in ids) Close(id);
    }
}
