using System;
using System.IO;
using Microsoft.Data.Sqlite;
using KitveiHakodesh.Core.Common;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// A fingerprint of the seforim database's CONTENT, used to decide whether a derived index
    /// was built from the database it is now being asked to answer for.
    ///
    /// This lives beside the seforim queries rather than in Common because it reads the
    /// `line` and `book` tables by name — it knows this schema, so it is not reusable and
    /// should not pretend to be.
    ///
    /// It is the provenance counterpart to <see cref="DbFileFingerprint"/>, and the two answer
    /// deliberately different questions:
    ///
    ///   DbFileFingerprint asks "did anything touch this file?" It reads no content and is
    ///   built to be maximally sensitive — size, mtime, NTFS ctime, per-file USN, file id, wal
    ///   metadata — because a MISSED change there means serving stale search results. That is
    ///   the right trade for a live-edit watcher.
    ///
    ///   This asks "are these the same rows?" A false positive here is not a missed edit, it is
    ///   a full index REBUILD: minutes of work with search dead throughout. So it ignores
    ///   everything about the file except the rows.
    ///
    /// Using the file fingerprint for provenance made every launch a rebuild whenever anything
    /// rewrote the database without changing a row — a stray journal-mode pragma on open, a
    /// checkpoint by another reader, a copy, a restore, a reinstall. The USN and file-id fields
    /// make that unrecoverable: they never return to a previous value, so the stamp could never
    /// match again and the index rebuilt on every single start.
    ///
    /// Cost is index seeks and a header read. COUNT(*) over `line` is a multi-second full scan
    /// on a real corpus and is deliberately NOT part of the fingerprint.
    /// </summary>
    public static class SeforimDbContentFingerprint
    {
        /// <summary>Fingerprint format version. Bump when the SHAPE changes, so an older value
        /// is recognised as a different format rather than compared as a different database —
        /// see <see cref="IsLegacy"/>.</summary>
        private const string FormatTag = "c1";

        /// <summary>
        /// Fingerprints <paramref name="databasePath"/>. <paramref name="prefix"/> is prepended
        /// verbatim (normally the derived index's own format version), so changing the index
        /// pipeline still forces a rebuild.
        ///
        /// A missing file yields a stable "missing" value. An unreadable one falls back to the
        /// file's size, which still separates two genuinely different databases without picking
        /// up the journal churn that mtime and USN would.
        /// </summary>
        public static string Compute(string databasePath, string prefix = "")
        {
            string head = string.IsNullOrEmpty(prefix) ? "" : prefix + "|";
            head += FormatTag + "|" + databasePath.ToLowerInvariant() + "|";

            if (!File.Exists(databasePath)) return head + "missing";

            string? content = TryReadContent(databasePath);
            if (content != null) return head + content;

            try { return head + "len=" + new FileInfo(databasePath).Length; }
            catch (Exception) { return head + "unreadable"; }
        }

        /// <summary>
        /// True for a value written before this FORMAT existed. Such a value can never compare
        /// equal to a current one, so treating it as a mismatch would wipe a good index on the
        /// first launch after the format changed. Read it as "provenance unknown": keep a
        /// COMPLETED index — it gets re-stamped on its next build — but still refuse to RESUME
        /// an interrupted build on a watermark whose source cannot be verified.
        ///
        /// This tests the FORMAT only and ignores the caller's index-format prefix, because
        /// those are different questions. An unrecognised format means "I cannot compare this";
        /// a changed prefix means "the pipeline changed, REBUILD". Folding the prefix in here
        /// would report a genuine prefix bump as merely-unreadable, and the caller would keep
        /// segments built by an incompatible pipeline — silently serving results in the old
        /// format instead of rebuilding.
        /// </summary>
        public static bool IsLegacy(string? fingerprint)
        {
            if (fingerprint == null) return false;

            // The prefix is caller-owned and may itself contain '|', so look for the format tag
            // as a delimited field anywhere in the head rather than at a fixed offset.
            return !fingerprint.StartsWith(FormatTag + "|", StringComparison.Ordinal)
                && fingerprint.IndexOf("|" + FormatTag + "|", StringComparison.Ordinal) < 0;
        }

        /// <summary>
        /// The identity of the `line` and `book` tables, or null when the database cannot be
        /// read — locked, corrupt, or an unexpected schema — so the caller can fall back.
        /// Opened through the probe policy: read-only, unpooled, no pragmas. This must never
        /// write the header or leave a -wal sidecar, since that churn is exactly what this
        /// fingerprint exists to see through.
        /// </summary>
        private static string? TryReadContent(string databasePath)
        {
            try
            {
                using var connection = SqliteConnectionFactory.OpenCorpusProbe(databasePath);
                using var command = connection.CreateCommand();

                // schema_version moves on any DDL, so a rebuilt database that happens to share
                // the same id ranges still mismatches.
                command.CommandText = "PRAGMA schema_version";
                object? schemaVersion = command.ExecuteScalar();

                // MIN/MAX over an INTEGER PRIMARY KEY are index seeks, not scans.
                var lines = ReadIdRange(command, "line");
                var books = ReadIdRange(command, "book");

                // Id ranges alone would miss a database swapped for one with the same ranges
                // but different text; the last line's length pins the actual rows.
                command.Parameters.Clear();
                command.CommandText = "SELECT content FROM line WHERE id = $id";
                command.Parameters.AddWithValue("$id", lines.Max);
                object? lastLine = command.ExecuteScalar();
                int tail = lastLine is string text ? text.Length : -1;

                return "schema=" + schemaVersion
                     + "|line=" + lines.Min + ":" + lines.Max
                     + "|book=" + books.Min + ":" + books.Max
                     + "|tail=" + tail;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static (long Min, long Max) ReadIdRange(SqliteCommand command, string table)
        {
            command.Parameters.Clear();
            // `table` is a compile-time literal from this file only — never caller input.
            command.CommandText = "SELECT MIN(id), MAX(id) FROM " + table;
            using var reader = command.ExecuteReader();
            if (reader.Read() && !reader.IsDBNull(0)) return (reader.GetInt64(0), reader.GetInt64(1));
            return (0, 0);
        }
    }
}
