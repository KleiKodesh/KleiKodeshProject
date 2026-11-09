using KitveiHakodesh.Core.Common;
using global::FastDocView;

namespace KitveiHakodesh.Core.FastDocView;

/// <summary>
/// The FastDocView documents open in this process, mapped to the service's MessagePack types. The
/// library's <see cref="WordWindowedDocumentSet"/> does the hosting: per-document lock, idle close after
/// 10 minutes, the shared sidecar cache in <see cref="IndexCacheDirectory"/>. A call on a closed or changed
/// document throws <see cref="FastDocViewSourceChangedException"/> and the page reopens.
/// Data is windowed: only rendered windows of blocks leave here, never the whole document.
/// Blocks and notes are rendered through the library's display path (RenderBlocksForDisplay: colours theme-aware).
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

    /// <summary>Sidecar index cache in the app's own data folder (beside the exe, like FtsIndex and
    /// convert-cache), so the portable app keeps it beside itself and never writes to a fixed path.</summary>
    public static string IndexCacheDirectory => Path.Combine(AppFileLocator.DataRoot, "FastDocViewIndexCache");

    private readonly WordWindowedDocumentSet _documents = new(IndexCacheDirectory);
    private readonly Action<string, Exception>? _logError;

    /// <param name="logError">The host's error log (the service's ILogger): a block that could not be rendered
    /// (shown on the page as a placeholder) is written there once per document and block.</param>
    public FastDocViewOpenDocuments(Action<string, Exception>? logError = null)
    {
        _logError = logError;
        if (logError is not null) WordDocumentBlockRenderGuard.BlockRenderFailed += OnBlockRenderFailed;
    }

    private void OnBlockRenderFailed(string filePath, int blockIndex, Exception cause) =>
        _logError?.Invoke($"FastDocView: block {blockIndex} of {filePath} could not be rendered; shown as a placeholder", cause);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> _pendingOpens = new(StringComparer.Ordinal);

    /// <summary>Opens <paramref name="validatedPath"/> (the caller validated access). A first open of
    /// a big file returns after its first blocks; the page then calls <see cref="Scan"/> until complete.
    /// <see cref="CancelOpen"/> with <paramref name="openId"/> (or <paramref name="cancellation"/>) stops the open's
    /// work - the page closed the tab or opened another file before this returned - and throws
    /// <see cref="OperationCanceledException"/>; nothing stays open.</summary>
    public FastDocViewSummary Open(string validatedPath, string? openId = null, CancellationToken cancellation = default)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        bool isRegistered = !string.IsNullOrEmpty(openId) && _pendingOpens.TryAdd(openId!, source);
        if (isRegistered && WasCancelledEarly(openId!)) source.Cancel();   // the cancel arrived before this open registered
        string id;
        try { id = _documents.Open(validatedPath, cancellation: source.Token); }
        catch (Exception exception) when (exception is not OperationCanceledException && FastDocViewOpenFailedException.CodeOf(exception) is { } code)
        {
            throw new FastDocViewOpenFailedException(code, exception.Message, exception);
        }
        finally
        {
            if (isRegistered) _pendingOpens.TryRemove(openId!, out _);
        }
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
                Title = (document as global::FastDocView.Markup.MarkupWindowedDocument)?.DocumentTitle,
                Sections = SectionsOf(document),
            };
        });
    }

    public List<FastDocViewBlock> Blocks(string documentId, int first, int count) =>
        Use(documentId, document =>
        {
            int start = Math.Max(0, first);
            int clampedCount = Math.Clamp(count, 0, MaxBlocksPerRequest);
            var blocks = new List<FastDocViewBlock>(clampedCount);
            foreach (WordDocumentBlockHtml block in document.RenderBlocksForDisplay(start, clampedCount))
            {
                var notes = new List<FastDocViewNote>(block.Notes.Count);
                foreach (WordDocumentNoteHtml note in block.Notes)
                    notes.Add(new FastDocViewNote { Kind = NoteKindName(note.Kind), Number = note.Number, Label = note.Label, Html = note.Html });
                blocks.Add(new FastDocViewBlock { Index = block.BlockIndex, Kind = BlockKindName(block.Kind), Html = block.Html, Notes = notes, Provisional = block.IsProvisional });
            }
            return blocks;
        });

    /// <summary>Notes [first, first + count) of the document's note references (body order) - the notes pane's own
    /// windows over every note, each note read on its own (never the notes part); clamped to the notes known so far.</summary>
    public List<FastDocViewNote> Notes(string documentId, int first, int count) =>
        Use(documentId, document =>
        {
            IReadOnlyList<WordDocumentNoteHtml> rendered = document.RenderNotesForDisplay(Math.Max(0, first), Math.Clamp(count, 0, MaxBlocksPerRequest));
            var notes = new List<FastDocViewNote>(rendered.Count);
            foreach (WordDocumentNoteHtml note in rendered)
                notes.Add(new FastDocViewNote { Kind = NoteKindName(note.Kind), Number = note.Number, Label = note.Label, Html = note.Html });
            return notes;
        });

    /// <summary>Int32 little-endian per known note (body order): the block holding its reference. Index-time data.</summary>
    public byte[] NoteBlocks(string documentId) =>
        Use(documentId, document =>
        {
            IReadOnlyList<WordDocumentNoteReference> references = document.NoteReferences;
            var bytes = new byte[references.Count * 4];
            for (int i = 0; i < references.Count; i++)
            {
                int block = references[i].BlockIndex;
                bytes[i * 4] = (byte)block; bytes[i * 4 + 1] = (byte)(block >> 8); bytes[i * 4 + 2] = (byte)(block >> 16); bytes[i * 4 + 3] = (byte)(block >> 24);
            }
            return bytes;
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
            return new FastDocViewSearchResult { HitBlocks = batch.HitBlocks, HitCounts = batch.HitCounts, NextBlock = batch.NextBlock, ReachedEnd = batch.ReachedEnd };
        });

    /// <summary>The block holding bookmark <paramref name="name"/> (an internal link's target); an index
    /// still building scans on only until it is found. Null when the document has no such bookmark.</summary>
    public FastDocViewBookmarkResult Bookmark(string documentId, string name) =>
        Use(documentId, document => new FastDocViewBookmarkResult { BlockIndex = string.IsNullOrEmpty(name) ? null : document.FindBookmark(name) });

    /// <summary>One picture of a rendered block, by the id its &lt;img data-word-picture&gt; carries: only that picture
    /// is read (the library's windowed ReadPicture), never the block or the document. Null for an unknown id or a
    /// format a browser cannot show.</summary>
    public FastDocViewPicture? Picture(string documentId, string pictureId) =>
        Use(documentId, document => document.ReadPicture(pictureId ?? "") is { } picture
            ? new FastDocViewPicture { ContentType = picture.ContentType, Bytes = picture.Bytes }
            : null);

    /// <summary>Page text width per section (index-time data); the known ones while the index builds.</summary>
    public List<FastDocViewSection> Sections(string documentId) => Use(documentId, SectionsOf);

    private static List<FastDocViewSection> SectionsOf(IWordWindowedDocument document)
    {
        var sections = new List<FastDocViewSection>(document.Sections.Count);
        foreach (WordDocumentSection section in document.Sections)
            sections.Add(new FastDocViewSection { FirstBlockIndex = section.FirstBlockIndex, TextWidthTwips = section.TextWidthTwips });
        return sections;
    }

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
                Sections = SectionsOf(document),
            };
        });

    public void Close(string documentId) => _documents.Close(documentId);

    /// <summary>Stops the open the page started with <paramref name="openId"/>, if it is still running. False when none is.</summary>
    public bool CancelOpen(string openId)
    {
        if (string.IsNullOrEmpty(openId)) return false;
        if (!_pendingOpens.TryGetValue(openId, out CancellationTokenSource? source))
        {
            RememberEarlyCancel(openId);     // the open may not have registered yet (requests race): stop it when it does
            return false;
        }
        try { source.Cancel(); }
        catch (ObjectDisposedException) { return false; }     // the open finished meanwhile
        return true;
    }

    // cancels that found no running open, kept briefly by id (bounded): an open that registers later is stopped at once
    private const int MaxEarlyCancels = 64;
    private static readonly TimeSpan EarlyCancelLifetime = TimeSpan.FromSeconds(30);
    private readonly Dictionary<string, DateTime> _earlyCancels = new(StringComparer.Ordinal);

    private void RememberEarlyCancel(string openId)
    {
        lock (_earlyCancels)
        {
            DateTime now = DateTime.UtcNow;
            foreach (string stale in _earlyCancels.Where(pair => now - pair.Value > EarlyCancelLifetime).Select(pair => pair.Key).ToList()) _earlyCancels.Remove(stale);
            if (_earlyCancels.Count >= MaxEarlyCancels) _earlyCancels.Remove(_earlyCancels.OrderBy(pair => pair.Value).First().Key);
            _earlyCancels[openId] = now;
        }
        // the open may have registered between the lookup and here
        if (_pendingOpens.TryGetValue(openId, out CancellationTokenSource? late))
        {
            try { late.Cancel(); } catch (ObjectDisposedException) { }
        }
    }

    private bool WasCancelledEarly(string openId)
    {
        lock (_earlyCancels)
            return _earlyCancels.Remove(openId, out DateTime at) && DateTime.UtcNow - at <= EarlyCancelLifetime;
    }

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

    public void Dispose()
    {
        WordDocumentBlockRenderGuard.BlockRenderFailed -= OnBlockRenderFailed;
        _documents.Dispose();
    }
}
