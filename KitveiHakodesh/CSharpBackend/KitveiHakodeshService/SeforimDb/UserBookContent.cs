using Microsoft.Data.Sqlite;
using System.Text;

namespace KitveiHakodeshService.SeforimDb;

/// <summary>
/// FILE-BACKED content for personal books.
///
/// Otzaria's user_books.db is a catalog+TOC index over files on disk: every book row
/// carries a filePath, totalLines is 0, and the `line` table is EMPTY — the text lives
/// in the file and Otzaria reads it at view time. This partial supplies the same for
/// the routed query methods: when a personal book's DB rows come back empty, the
/// content falls back to the file.
///
/// LINE MODEL. tocEntry.lineIndex is 0-BASED into the file's '\n'-split lines
/// (verified empirically against real Otzaria data — heading entries align exactly
/// with their &lt;h1&gt;/&lt;h2&gt;/&lt;h3&gt; file lines). Served rows carry Id = 0: file lines have
/// NO line ids, and 0 is this API's existing "no id" sentinel — per-line features
/// (notes, highlights, links) key on line ids and are guarded off for id-less lines.
/// Synthesizing ids is NOT safe: shifted ids may collide with real user line rows
/// (Otzaria's import flow DOES populate `line` for non-file-backed books), and
/// negative ids break the search layer's bitmap filter.
///
/// ENCODING: BOM-aware UTF-8; invalid bytes decode with replacement characters
/// rather than failing the whole book (the real corpus is UTF-8-with-BOM; legacy
/// Windows-1255 detection can be added when a real file needs it — the service has
/// no CodePages provider today).
///
/// v1 serves fileType 'txt' only. PDF books go through the PDF flow (separate leg);
/// docx needs DocConvertLib (ditto). Non-txt books keep their DB answer (0 lines).
/// </summary>
public sealed partial class SeforimDbService
{
    private sealed class FileBookLines
    {
        public required int LocalBookId;
        public required string Path;
        public required long Stamp;
        public required string[] Lines;
    }

    // Tiny LRU: realistically only the open book is hot, and a large sefer's split
    // lines can run tens of MB — this service guards its idle footprint jealously.
    private const int FileLinesCacheSize = 2;
    private readonly List<FileBookLines> _fileLinesCache = new(FileLinesCacheSize);
    private readonly object _fileLinesLock = new();

    /// <summary>
    /// The '\n'-split file lines of a personal book, or null when the book is not
    /// file-backed text (no filePath, not txt, file missing). Cached per book and
    /// keyed on the file's (mtime ^ length) so an edited file re-reads.
    /// </summary>
    private string[]? GetUserBookFileLines(int localBookId)
    {
        string? path = null, fileType = null;
        Run(Corpus.UserBooks, () =>
        {
            using var conn = Open(Corpus.UserBooks);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT filePath, fileType FROM book WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", localBookId);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                path = r.IsDBNull(0) ? null : r.GetString(0);
                fileType = r.IsDBNull(1) ? null : r.GetString(1);
            }
        }, "getUserBookFilePath");

        if (string.IsNullOrWhiteSpace(path)) return null;
        if (!string.Equals(fileType, "txt", StringComparison.OrdinalIgnoreCase)) return null;

        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists) return null;
        }
        catch { return null; }
        long stamp = info.LastWriteTimeUtc.Ticks ^ info.Length;

        lock (_fileLinesLock)
        {
            for (int i = 0; i < _fileLinesCache.Count; i++)
            {
                var c = _fileLinesCache[i];
                if (c.LocalBookId == localBookId && c.Path == path && c.Stamp == stamp)
                {
                    // Move to front (LRU).
                    _fileLinesCache.RemoveAt(i);
                    _fileLinesCache.Insert(0, c);
                    return c.Lines;
                }
            }
        }

        string[] lines;
        try
        {
            lines = ReadTextFileLines(path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "reading personal-book file failed: {Path}", path);
            return null;
        }

        lock (_fileLinesLock)
        {
            _fileLinesCache.RemoveAll(c => c.LocalBookId == localBookId);
            _fileLinesCache.Insert(0, new FileBookLines
            {
                LocalBookId = localBookId,
                Path = path,
                Stamp = stamp,
                Lines = lines,
            });
            if (_fileLinesCache.Count > FileLinesCacheSize)
                _fileLinesCache.RemoveAt(_fileLinesCache.Count - 1);
        }
        return lines;
    }

    /// <summary>BOM-aware UTF-8 read, '\n'-split with per-line '\r' trim. The SPLIT
    /// indexes must match Otzaria's 0-based tocEntry.lineIndex exactly, so the split
    /// keeps every element (including a trailing empty line) — only the display-only
    /// '\r' is trimmed.</summary>
    private static string[] ReadTextFileLines(string path)
    {
        // UTF8Encoding with BOM detection; invalid bytes become U+FFFD rather than
        // failing the book (File.ReadAllText never throws on decoding by default).
        string text = File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length > 0 && line[line.Length - 1] == '\r')
                lines[i] = line.Substring(0, line.Length - 1);
        }
        return lines;
    }

    /// <summary>File-backed fallback for <see cref="GetLinesPaged"/>: rows with Id = 0
    /// (no line ids exist — see class doc) and lineIndex = file line number.</summary>
    private List<LineRow> GetUserFileLinesPaged(int localBookId, int limit, int offset)
    {
        var list = new List<LineRow>();
        var lines = GetUserBookFileLines(localBookId);
        if (lines is null) return list;
        for (int i = offset; i < lines.Length && list.Count < limit; i++)
            list.Add(new LineRow { Id = 0, LineIndex = i, Content = lines[i] });
        return list;
    }

    /// <summary>File-backed line count (0 when the book is not file-backed text).</summary>
    private int GetUserFileTotalLines(int localBookId) =>
        GetUserBookFileLines(localBookId)?.Length ?? 0;
}
