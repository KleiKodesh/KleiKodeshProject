using System.Globalization;
using System.Text;
using KitveiHakodesh.Core.Settings;
using MessagePack;

namespace KitveiHakodesh.Core.DailyLearning;

/// <summary>A cycle's portion of a day: how many times it was learned, and each review's own count.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed class DailyLearningMark
{
    public string Cycle { get; set; } = "";
    public int Times { get; set; }
    /// <summary>Reviews 1..4 (index 0..3).</summary>
    public List<int> Reviews { get; set; } = [];
}

/// <summary>Reply of every learning-log op: the day's marks, the file, or why it failed.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed class DailyLearningLogResult
{
    public List<DailyLearningMark> Marks { get; set; } = [];
    /// <summary>The log file's full path; empty while the user has not chosen a folder.</summary>
    public string Path { get; set; } = "";
    /// <summary>"locked" = open in another program (usually Excel); "noFolder"; else a message.</summary>
    public string? Error { get; set; }
}

/// <summary>
/// The daily-learning log: a CSV in a folder the user picks, kept so the user can EDIT it in Excel.
/// It mirrors the widget, one row per learned cycle of a day:
/// <c>Hebrew date, cycle, place, times, review 1, review 2, review 3, review 4</c>. A review's count
/// sits on the row of the portion it repeats (reviews come 1, 8, 37 and 127 days later; the widget
/// works that out). Columns after these are the user's (notes). The date is Hebrew text (Excel
/// never reformats it); the day is computed from it here.
///
/// What Excel or LibreOffice may do to the file is all accepted: re-sorting rows, saving as plain
/// "CSV" (Windows-1255) instead of UTF-8, ';' or tabs between columns, quoted cells, extra columns,
/// notes (even with line breaks), a renamed header. Every record the app does not change is
/// written back exactly as it was, where it was; a changed row rewrites only its count cell; new rows
/// go in by date, in the file's own separator.
///
/// One day is read by halving the file (no index, nothing else loaded) while the file is as this app
/// last wrote it; after an outside edit it is read whole, until the next write sorts it again.
/// </summary>
public static class DailyLearningLog
{
    public const string FileName = "לימוד יומי.csv";
    private const string Header = "תאריך,לימוד,מקום,פעמים,חזרה ראשונה - לאחר יום,חזרה שנייה - לאחר שבוע,חזרה שלישית - לאחר חודש,חזרה רביעית - לאחר שלושה חודשים";
    /// <summary>The count columns: times (0) and reviews 1..4.</summary>
    public const int ReviewCount = 4;
    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);
    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);
    private static readonly object Gate = new();
    /// <summary>The file and its write time right after this app last wrote it (sorted, UTF-8, ',').</summary>
    private static (string Path, DateTime WrittenUtc) _ownWrite;

    public static string Folder() => AppSettingsRegistry.Get("KitveiHakodesh", "DailyLearning", "LogFolder", "");

    public static string CurrentPath()
    {
        string folder = Folder();
        return folder.Length == 0 ? "" : System.IO.Path.Combine(folder, FileName);
    }

    /// <summary>The marks of one day (date = yyyy-MM-dd).</summary>
    public static DailyLearningLogResult Day(string date)
    {
        string path = CurrentPath();
        var result = new DailyLearningLogResult { Path = path };
        if (path.Length == 0 || !File.Exists(path) || HebrewDates.DayKey(date) is not int day) return result;
        try
        {
            lock (Gate)
            {
                bool ownFile = _ownWrite.Path == path && _ownWrite.WrittenUtc == File.GetLastWriteTimeUtc(path);
                List<Row> rows;
                if (ownFile)
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    rows = RowsOfDay(stream, day).ToList();
                }
                else rows = ReadAll(path).Records.Where(r => r.Day == day).ToList();
                // A row copied twice in Excel counts twice; a count set to 0 is not learned.
                foreach (var group in rows.GroupBy(r => r.Cycle))
                {
                    var mark = new DailyLearningMark { Cycle = group.Key, Times = group.Sum(r => r.Counts[0]) };
                    for (int k = 1; k <= ReviewCount; k++) mark.Reviews.Add(group.Sum(r => r.Counts[k]));
                    if (mark.Times > 0 || mark.Reviews.Any(n => n > 0)) result.Marks.Add(mark);
                }
            }
        }
        catch (IOException ex) { result.Error = IsLocked(ex) ? "locked" : ex.Message; }
        catch (UnauthorizedAccessException ex) { result.Error = ex.Message; }
        return result;
    }

    /// <summary>One more time learned - the portion itself (<paramref name="review"/> 0) or one of its
    /// reviews (1..4), on the portion's own day - and returns that day.</summary>
    public static DailyLearningLogResult Mark(string date, string cycle, string place, int review = 0) =>
        review is < 0 or > ReviewCount ? new DailyLearningLogResult { Error = "bad review" } :
        Rewrite(date, (rows, day) =>
        {
            int at = rows.FindIndex(r => r.Day == day && r.Cycle == cycle);
            if (at >= 0) { rows[at] = rows[at].WithCount(review, rows[at].Counts[review] + 1); return; }
            // New row by date: after the last row of that day or earlier; existing rows never move.
            int after = rows.FindLastIndex(r => r.Day != Row.Unreadable && r.Day <= day);
            int before = rows.FindIndex(r => r.Day != Row.Unreadable && r.Day > day);
            int insertAt = after >= 0 ? after + 1 : before >= 0 ? before : rows.Count;
            var counts = new int[ReviewCount + 1];
            counts[review] = 1;
            rows.Insert(insertAt, new Row(day, HebrewDates.Format(day), cycle, place, counts, Raw: null));
        });

    /// <summary>One time fewer of the portion or a review; a row with nothing left goes. Returns the day.</summary>
    public static DailyLearningLogResult Unmark(string date, string cycle, int review = 0) =>
        review is < 0 or > ReviewCount ? new DailyLearningLogResult { Error = "bad review" } :
        Rewrite(date, (rows, day) =>
        {
            int at = rows.FindIndex(r => r.Day == day && r.Cycle == cycle && r.Counts[review] > 0);
            if (at < 0) return;
            var row = rows[at].WithCount(review, rows[at].Counts[review] - 1);
            // All counts at 0: the row goes, unless the user wrote something of their own on it.
            if (row.Counts.Any(n => n > 0) || row.HasOwnCells) rows[at] = row;
            else rows.RemoveAt(at);
        });

    /// <summary>Uses another folder; an existing log moves along with it.</summary>
    public static DailyLearningLogResult MoveTo(string folder)
    {
        var result = new DailyLearningLogResult();
        if (string.IsNullOrWhiteSpace(folder)) { result.Error = "noFolder"; result.Path = CurrentPath(); return result; }
        try
        {
            Directory.CreateDirectory(folder);
            string from = CurrentPath(), to = System.IO.Path.Combine(folder, FileName);
            lock (Gate)
            {
                if (from.Length > 0 && File.Exists(from) && !string.Equals(System.IO.Path.GetFullPath(from), System.IO.Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(to)) { result.Error = "exists"; result.Path = from; return result; }
                    File.Move(from, to);
                }
                AppSettingsRegistry.Set("KitveiHakodesh", "DailyLearning", "LogFolder", folder);
            }
            result.Path = to;
        }
        catch (IOException ex) { result.Error = IsLocked(ex) ? "locked" : ex.Message; result.Path = CurrentPath(); }
        catch (UnauthorizedAccessException ex) { result.Error = ex.Message; result.Path = CurrentPath(); }
        catch (ArgumentException ex) { result.Error = ex.Message; result.Path = CurrentPath(); }
        return result;
    }

    private static DailyLearningLogResult Rewrite(string date, Action<List<Row>, int> change)
    {
        string path = CurrentPath();
        if (path.Length == 0) return new DailyLearningLogResult { Error = "noFolder" };
        if (HebrewDates.DayKey(date) is not int day) return new DailyLearningLogResult { Path = path, Error = "bad date" };
        string temp = path + ".tmp";
        try
        {
            lock (Gate)
            {
                var file = File.Exists(path) ? ReadAll(path) : new LogFile(Header, ',', []);
                change(file.Records, day);
                using (var writer = new StreamWriter(temp, false, Utf8Bom))
                {
                    writer.WriteLine(file.HeaderLine);
                    // Every record where it was; only changed rows are re-written.
                    foreach (var row in file.Records) writer.WriteLine(row.Raw ?? row.Format(file.Separator));
                }
                // Open in another program: Excel locks the file itself; LibreOffice leaves ".~lock.<name>#"
                // beside it instead (its own later save would overwrite this write). Fail before replacing.
                if (OpenInLibreOffice(path)) { TryDelete(temp); return new DailyLearningLogResult { Path = path, Error = "locked" }; }
                if (File.Exists(path)) { using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } }
                File.Move(temp, path, overwrite: true);
                // The halving read needs one ',' line per row, in date order; otherwise the file is read whole.
                _ownWrite = file.Separator == ',' && file.FitsHalvingRead() ? (path, File.GetLastWriteTimeUtc(path)) : default;
            }
        }
        catch (IOException ex) { TryDelete(temp); return new DailyLearningLogResult { Path = path, Error = IsLocked(ex) ? "locked" : ex.Message }; }
        catch (UnauthorizedAccessException ex) { TryDelete(temp); return new DailyLearningLogResult { Path = path, Error = ex.Message }; }
        return Day(date);
    }

    /// <param name="Records">Every record in file order: rows, and lines the app can't read
    /// (<see cref="Row.Unreadable"/>), which are written back as they were, where they were.</param>
    private sealed record LogFile(string HeaderLine, char Separator, List<Row> Records)
    {
        /// <summary>One line per record (no line break inside a cell) and the rows in date order.</summary>
        public bool FitsHalvingRead()
        {
            int last = 0;
            foreach (var r in Records)
            {
                if ((r.Raw ?? r.Format(',')).IndexOfAny(['\n', '\r']) >= 0) return false;
                if (r.Day == Row.Unreadable) continue;
                if (r.Day < last) return false;
                last = r.Day;
            }
            return true;
        }
    }

    /// <summary>The text cut into records at line ends outside quotes (a quoted cell may hold a line break).</summary>
    private static List<string> SplitRecords(string text)
    {
        var records = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"') quoted = !quoted; // a doubled quote flips twice: still inside
            if (!quoted && (c == '\n' || c == '\r'))
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                records.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) records.Add(current.ToString());
        return records;
    }

    /// <summary>The whole file, in whatever encoding and separator Excel or LibreOffice left it.
    /// Opened shared: Excel keeps the file open for writing while it shows it.</summary>
    private static LogFile ReadAll(string path)
    {
        byte[] bytes;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
        }
        string text;
        try { text = StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException) { text = CodePagesEncodingProvider.Instance.GetEncoding(1255)!.GetString(bytes); }
        var records = SplitRecords(text.TrimStart('\uFEFF'));
        string first = records.Count > 0 ? records[0] : "";
        char separator = SeparatorOf(first);
        bool firstIsRow = Row.Parse(first, separator) is not null;
        var file = new LogFile(firstIsRow || first.Trim().Length == 0 ? Header : first, separator, []);
        foreach (string record in records.Skip(firstIsRow ? 0 : 1))
        {
            if (Row.Parse(record, separator) is { } row) file.Records.Add(row);
            else if (record.Trim().Trim(separator).Length > 0) file.Records.Add(Row.UnreadableLine(record));
        }
        return file;
    }

    /// <summary>The file's column separator: ',' as written here; Excel in some regions saves ';',
    /// LibreOffice may save tabs. Read from the first line (the header).</summary>
    private static char SeparatorOf(string line) =>
        line.Contains('	') ? '	' : line.Contains(';') && !line.Contains(',') ? ';' : ',';

    /// <summary>Halves the sorted (own-written) file down to the day, then reads that day only.</summary>
    private static IEnumerable<Row> RowsOfDay(FileStream stream, int day)
    {
        long low = 0, high = stream.Length;
        // Invariant: every line starting before `low` is earlier than `day`.
        while (high - low > 256)
        {
            long mid = (low + high) / 2;
            stream.Position = mid;
            SkipLine(stream);
            long lineStart = stream.Position;
            string? line = ReadLine(stream);
            if (line is null) { high = mid; continue; }
            var row = Row.Parse(line, ',');
            if (row is null || row.Day < day) low = lineStart;
            else high = mid;
        }
        stream.Position = low;
        if (low > 0) SkipLine(stream);
        while (ReadLine(stream) is { } line)
        {
            if (Row.Parse(line, ',') is not { } row) continue; // header or a line the app can't read
            if (row.Day > day) yield break;
            if (row.Day == day) yield return row;
        }
    }

    private static void SkipLine(Stream stream)
    {
        int b;
        while ((b = stream.ReadByte()) >= 0 && b != '\n') { }
    }

    private static string? ReadLine(Stream stream)
    {
        var bytes = new List<byte>(128);
        int b;
        while ((b = stream.ReadByte()) >= 0 && b != '\n') bytes.Add((byte)b);
        if (b < 0 && bytes.Count == 0) return null;
        return Encoding.UTF8.GetString(bytes.ToArray()).TrimEnd('\r').TrimStart('﻿');
    }

    private static bool OpenInLibreOffice(string path) =>
        File.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, ".~lock." + System.IO.Path.GetFileName(path) + "#"));

    private static bool IsLocked(IOException ex) => (ex.HResult & 0xFFFF) is 32 or 33; // sharing / lock violation

    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }

    /// <param name="Day">The civil day as yyyymmdd, computed from the Hebrew date.</param>
    /// <param name="Raw">The line as read; written back untouched while the row is unchanged.</param>
    /// <param name="Counts">Times (index 0) and reviews 1..4.</param>
    /// <param name="Fields">The line's columns as read (null for a new row): a changed count rewrites
    /// only its own cells, so notes and extra columns the user added stay.</param>
    private sealed record Row(int Day, string HebrewDate, string Cycle, string Place, int[] Counts, string? Raw, List<string>? Fields = null)
    {
        /// <summary><see cref="Day"/> of a line the app can't read (kept as written).</summary>
        public const int Unreadable = -1;
        /// <summary>Columns before the counts: date, cycle, place.</summary>
        private const int FirstCount = 3;
        private const int CountColumns = ReviewCount + 1;

        public static Row UnreadableLine(string line) => new(Unreadable, "", "", "", new int[CountColumns], line);

        public Row WithCount(int index, int value)
        {
            var counts = (int[])Counts.Clone();
            counts[index] = Math.Max(value, 0);
            return this with { Counts = counts, Raw = null };
        }

        /// <summary>The user wrote something of their own on the row - past the count columns, or
        /// words in a review cell: the row is theirs to delete.</summary>
        public bool HasOwnCells => Fields is { } f && f.Select((c, i) =>
            i >= FirstCount + CountColumns ? c.Trim().Length > 0 : i > FirstCount && IsWords(c)).Any(own => own);

        /// <summary>A cell with text that is not a count.</summary>
        private static bool IsWords(string cell) =>
            cell.Trim().Length > 0 && !int.TryParse(cell.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

        /// <summary>Times always as a number; a review cell stays empty until that review is marked,
        /// and words the user wrote in an unmarked review cell stay.</summary>
        public string Format(char separator)
        {
            var cells = Fields is null ? [HebrewDate, Cycle, Place] : new List<string>(Fields);
            while (cells.Count < FirstCount + CountColumns) cells.Add("");
            for (int k = 0; k < CountColumns; k++)
            {
                if (k > 0 && Counts[k] == 0 && IsWords(cells[FirstCount + k])) continue;
                cells[FirstCount + k] = k == 0 || Counts[k] > 0 ? Counts[k].ToString(CultureInfo.InvariantCulture) : "";
            }
            // Trailing empty review cells are left off a new row, so it reads like a short line in Excel.
            if (Fields is null) while (cells.Count > FirstCount + 1 && cells[^1].Length == 0) cells.RemoveAt(cells.Count - 1);
            return string.Join(separator, cells.Select(c => Field(c, separator)));
        }

        private static string Field(string value, char separator) =>
            value.IndexOfAny([separator, '"', '\n', '\r']) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\""; // a line break stays, inside the quotes

        /// <summary>Null for a header or a line whose first column is not a Hebrew date.</summary>
        public static Row? Parse(string line, char separator)
        {
            line = line.TrimEnd('\r');
            var f = Split(line, separator);
            if (f.Count < 2 || HebrewDates.Parse(f[0]) is not int day) return null;
            // Times: empty or words = learned once, a number = the count (0 = not learned).
            // A review: a number = its count; empty or words = not reviewed.
            var counts = new int[CountColumns];
            for (int k = 0; k < CountColumns; k++)
            {
                string cell = f.Count > FirstCount + k ? f[FirstCount + k].Trim() : "";
                counts[k] = int.TryParse(cell, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? Math.Max(n, 0) : k == 0 ? 1 : 0;
            }
            return new Row(day, f[0].Trim(), f[1].Trim(), f.Count > 2 ? f[2].Trim() : "", counts, line, f);
        }

        private static List<string> Split(string line, char separator)
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else current.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == separator) { fields.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }
            fields.Add(current.ToString());
            return fields;
        }
    }
}
