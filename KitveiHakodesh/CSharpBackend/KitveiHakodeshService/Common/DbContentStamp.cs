using System.Data.Common;
using System.Globalization;
#if !NETFRAMEWORK
using Microsoft.Data.Sqlite;
#endif

namespace KitveiHakodeshService.Common;

/// <summary>
/// A fingerprint of a seforim database's CONTENT, used to decide whether a derived
/// index was built from the database it is being asked to answer for.
///
/// This is the provenance counterpart to <see cref="DbChangeStamp"/>, and the two
/// answer deliberately different questions:
///
///   - DbChangeStamp asks "did anything touch this file?" It reads no content and is
///     built to be maximally sensitive (size, mtime, NTFS ctime, per-file USN, file id,
///     -wal metadata), because a MISSED change there means serving stale search results.
///     That is the right trade for a live-edit watcher.
///
///   - DbContentStamp asks "are these the same rows?" A false positive here is not a
///     missed edit, it is a full index REBUILD — minutes of work and a search-dead app.
///     So it ignores everything about the file except the rows themselves.
///
/// Using the change-stamp for provenance made every launch a rebuild whenever anything
/// rewrote the DB without changing a row: a stray `PRAGMA journal_mode` on open, a
/// SQLite checkpoint by another reader, a copy, a restore, a reinstall. The USN and
/// file-id fields make that unrecoverable — they never return to a previous value, so
/// the stamp could never match again and the index rebuilt on every single start.
///
/// Cost: index seeks and a header read, no table scans. COUNT(*) over `line` is a
/// multi-second full scan on a real corpus and is deliberately NOT part of the stamp.
/// </summary>
public static class DbContentStamp
{
    /// <summary>Stamp format version. Bump when the fingerprint's shape changes, so an
    /// older stamp is recognised as a different FORMAT rather than compared as a
    /// different database — see <see cref="IsLegacy"/>.</summary>
    private const string StampPrefix = "c1";

    /// <summary>Computes the content stamp for <paramref name="dbPath"/>.
    /// <paramref name="prefix"/> is prepended verbatim (the derived index's format
    /// version), so an index-pipeline change still forces a rebuild. A missing file
    /// yields a stable "missing" stamp; an unreadable one falls back to the file's size,
    /// which still separates two genuinely different databases without tracking the
    /// journal churn that mtime and USN pick up.</summary>
    public static string Compute(string dbPath, string prefix = "")
    {
        string head = string.IsNullOrEmpty(prefix) ? "" : prefix + "|";
        head += StampPrefix + "|" + dbPath.ToLowerInvariant() + "|";

        if (!File.Exists(dbPath)) return head + "missing";

        string? content = TryReadContentStamp(dbPath);
        if (content != null) return head + content;

        try { return head + "len=" + new FileInfo(dbPath).Length; }
        catch { return head + "unreadable"; }
    }

    /// <summary>True for a stamp written before this STAMP FORMAT existed. Such a stamp
    /// can never compare equal to a current one, so treating it as a mismatch would wipe a
    /// good index on the first launch after the format change. Callers should read it as
    /// "provenance unknown": keep a COMPLETED index (it gets re-stamped on its next
    /// build), but still refuse to RESUME an interrupted build on a watermark whose
    /// source database cannot be verified.
    ///
    /// This deliberately tests the stamp FORMAT only, and ignores the caller's index-format
    /// prefix. Those are different questions: an unrecognised stamp format means "I cannot
    /// compare this", whereas a changed prefix means "the index pipeline changed, REBUILD".
    /// Folding the prefix in here would report a genuine prefix bump as merely-unreadable,
    /// and the caller would keep segments built by an incompatible pipeline — silently
    /// serving results from the old format instead of rebuilding.</summary>
    public static bool IsLegacy(string? stamp)
    {
        if (stamp is null) return false;
        // The prefix is caller-owned and may contain '|', so look for the format tag as a
        // delimited field anywhere in the head rather than at a fixed offset.
        return !stamp.StartsWith(StampPrefix + "|", StringComparison.Ordinal)
            // IndexOf, not Contains(string, StringComparison): that overload is .NET Core
            // only, and this file is shared source that also compiles for net48.
            && stamp.IndexOf("|" + StampPrefix + "|", StringComparison.Ordinal) < 0;
    }

    /// <summary>Identity of the `line` and `book` tables. Returns null when the database
    /// cannot be read (locked, corrupt, unexpected schema) so the caller can fall back.
    /// Opens READ-ONLY and sets no pragmas: this must never write the header or create a
    /// -wal sidecar, since doing so is exactly the churn this stamp exists to survive.</summary>
    private static string? TryReadContentStamp(string dbPath)
    {
        try
        {
            // The only provider-specific lines here — see CatalogTocIndex.OpenDb for why the
            // two legs use different SQLite providers. Both open read-only with no pooling.
#if NETFRAMEWORK
            var cs = new System.Data.SQLite.SQLiteConnectionStringBuilder
            {
                DataSource = dbPath,
                ReadOnly = true,
                Pooling = false,
            }.ConnectionString;

            using DbConnection conn = new System.Data.SQLite.SQLiteConnection(cs);
#else
            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ConnectionString;

            using DbConnection conn = new SqliteConnection(cs);
#endif
            conn.Open();
            using var cmd = conn.CreateCommand();

            // schema_version moves on any DDL, so a rebuilt DB that happens to share the
            // same id ranges still mismatches.
            // Coerced to long rather than interpolated as the boxed object: a PRAGMA column
            // has no declared affinity, so the two providers are free to box it differently
            // (System.Data.SQLite int vs Microsoft.Data.Sqlite long). The stamp is compared
            // as an opaque string across both legs, so a rendering difference alone would
            // make each consider the other's index stale and rebuild the whole corpus on
            // every switch between hosted and dev. Same discipline as ReadIdRange's GetInt64.
            cmd.CommandText = "PRAGMA schema_version";
            object? schemaRaw = cmd.ExecuteScalar();
            long schemaVer = schemaRaw is null || schemaRaw is DBNull
                ? -1
                : Convert.ToInt64(schemaRaw, CultureInfo.InvariantCulture);

            // MIN/MAX over an INTEGER PRIMARY KEY are index seeks, not scans.
            var (lineMin, lineMax) = ReadIdRange(cmd, "line");
            var (bookMin, bookMax) = ReadIdRange(cmd, "book");

            // Ids alone would miss a DB swapped for one with the same ranges but different
            // text; the last line's length pins the actual rows.
            cmd.Parameters.Clear();
            // "@id" not "$id": System.Data.SQLite only recognises the @ prefix, while
            // Microsoft.Data.Sqlite accepts both — so @ is the one form both legs parse.
            cmd.CommandText = "SELECT content FROM line WHERE id = @id";
            cmd.AddWithValue("@id", lineMax);
            // Length of the TEXT, not of whichever CLR type the provider chose to box it as:
            // a BLOB-affinity row comes back as byte[] from System.Data.SQLite while
            // Microsoft.Data.Sqlite may still surface a string, and an `is string` test would
            // silently yield -1 on one leg only — two permanently-disagreeing stamps, no error.
            object? lastLine = cmd.ExecuteScalar();
            int tail = lastLine is null || lastLine is DBNull
                ? -1
                : (Convert.ToString(lastLine, CultureInfo.InvariantCulture) ?? "").Length;

            return $"schema={schemaVer}|line={lineMin}:{lineMax}|book={bookMin}:{bookMax}|tail={tail}";
        }
        catch
        {
            return null;
        }
    }

    private static (long Min, long Max) ReadIdRange(DbCommand cmd, string table)
    {
        cmd.Parameters.Clear();
        // `table` is a compile-time literal from this file only — never caller input.
        cmd.CommandText = $"SELECT MIN(id), MAX(id) FROM {table}";
        using var r = cmd.ExecuteReader();
        if (r.Read() && !r.IsDBNull(0)) return (r.GetInt64(0), r.GetInt64(1));
        return (0, 0);
    }
}
