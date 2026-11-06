using System;
using System.Collections.Generic;
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
    /// <summary>The seforim DB exists but its content could not be probed right now, so no
    /// fingerprint can be computed. Transient by nature; never evidence of a changed DB.</summary>
    public sealed class SeforimDbUnreadableException : IOException
    {
        public SeforimDbUnreadableException(string databasePath)
            : base("seforim DB could not be read right now (locked or busy): " + databasePath) { }
    }

    public static class SeforimDbContentFingerprint
    {
        /// <summary>Fingerprint format version. Bump when the SHAPE changes, so an older value
        /// is recognised as a different format rather than compared as a different database —
        /// see <see cref="IsLegacy"/>.</summary>
        private const string FormatTag = "c1";
        // NOT bumped for |dbv= and |release= (2026-10-13), on purpose: a bump would make every
        // existing stamp "legacy" and KEEP those indexes — including ones built before an
        // Otzaria delta that is already applied. One rebuild for everyone is the safe side.

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
            if (content != null) return head + content + ReleaseStamp(databasePath);

            // The file is there but could not be read right now (SQLITE_BUSY/locked by another
            // process, a drive still waking up, AV holding it). This used to fall back to
            // "len=<size>" — a value that can never equal a recorded content stamp, so one
            // transient failure read as "different database" and WIPED a good index. An
            // exception instead: callers comparing stamps treat it as "cannot tell, keep what
            // is there and check again later".
            throw new SeforimDbUnreadableException(databasePath);
        }

        /// <summary>
        /// Why <paramref name="recorded"/> (what an index was built from) differs from
        /// <paramref name="current"/>, in words for a log line: the index format, the database
        /// path, or which parts of the database's content. Null when they are equal.
        /// </summary>
        public static string? DescribeChange(string? recorded, string current)
        {
            if (recorded != null) current = Reconcile(recorded, current);
            if (string.Equals(recorded, current, StringComparison.OrdinalIgnoreCase)) return null;
            if (recorded == null) return "the existing index has no provenance stamp";
            if (IsLegacy(recorded)) return "the existing index has a pre-" + FormatTag + " stamp";

            string[] was = recorded.Split('|'), now = current.Split('|');
            int wasTag = Array.IndexOf(was, FormatTag), nowTag = Array.IndexOf(now, FormatTag);
            string wasPrefix = string.Join("|", was, 0, Math.Max(wasTag, 0));
            string nowPrefix = string.Join("|", now, 0, Math.Max(nowTag, 0));
            if (wasTag < 0 || nowTag < 0 || wasPrefix != nowPrefix)
                return $"index format changed ({wasPrefix} -> {nowPrefix})";

            string wasPath = wasTag + 1 < was.Length ? was[wasTag + 1] : "";
            string nowPath = nowTag + 1 < now.Length ? now[nowTag + 1] : "";
            if (!string.Equals(wasPath, nowPath, StringComparison.OrdinalIgnoreCase))
                return $"seforim DB path changed ({wasPath} -> {nowPath})";

            // The rest are key=value parts (schema=, line=, book=, tail=, dbv=, release=) or "missing".
            var changed = new System.Collections.Generic.List<string>();
            var wasParts = new System.Collections.Generic.HashSet<string>(was, StringComparer.OrdinalIgnoreCase);
            var nowParts = new System.Collections.Generic.HashSet<string>(now, StringComparer.OrdinalIgnoreCase);
            foreach (string part in now)
                if (!wasParts.Contains(part)) changed.Add(KeyOf(part));
            foreach (string part in was)
                if (!nowParts.Contains(part) && !changed.Contains(KeyOf(part))) changed.Add(KeyOf(part));
            return changed.Count == 0
                ? "seforim DB content changed"
                : "seforim DB content changed (" + string.Join(", ", changed) + ")";

            static string KeyOf(string part) { int eq = part.IndexOf('='); return eq < 0 ? part : part.Substring(0, eq); }
        }

        /// <summary>The value an additional library contributes while it cannot be read right now.</summary>
        private const string UnreadableTag = "unreadable|";
        private const string LibrarySeparator = "||lib";

        /// <summary>
        /// True when <paramref name="current"/> is the same set of sources as <paramref name="recorded"/>,
        /// where an additional library that cannot be read right now counts as UNKNOWN rather than
        /// as changed (see <see cref="Reconcile"/>). Use this, not string equality, to decide
        /// whether an index may be kept.
        /// </summary>
        public static bool Matches(string? recorded, string current) =>
            recorded != null
            && string.Equals(recorded, Reconcile(recorded, current), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// <paramref name="current"/> with every additional library that is unreadable RIGHT NOW
        /// replaced by what <paramref name="recorded"/> says about it (or dropped when the
        /// recorded stamp does not mention it). A temporary lock is not evidence that the library
        /// changed: the primary's design is "keep what is there and check again later"
        /// (<see cref="SeforimDbUnreadableException"/>), and a library lock must not wipe a good
        /// index and rebuild it again once the lock lifts. The reverse direction still counts as a
        /// change: a recorded "unreadable" against a readable library means the index was built
        /// WITHOUT it, so it is rebuilt to cover it. A library the recorded stamp does not mention
        /// and that is unreadable now is dropped: it cannot be indexed yet, and the index never
        /// claimed it.
        /// </summary>
        public static string Reconcile(string recorded, string current)
        {
            if (current.IndexOf(UnreadableTag, StringComparison.Ordinal) < 0) return current;

            string[] now = current.Split(new[] { LibrarySeparator }, StringSplitOptions.None);
            if (now.Length == 1) return current;
            var was = new Dictionary<string, string>(StringComparer.Ordinal);
            string[] before = recorded.Split(new[] { LibrarySeparator }, StringSplitOptions.None);
            for (int i = 1; i < before.Length; i++)
            {
                int eq = before[i].IndexOf('=');
                if (eq > 0) was[before[i].Substring(0, eq)] = before[i].Substring(eq + 1);
            }

            var sb = new System.Text.StringBuilder(now[0]);
            for (int i = 1; i < now.Length; i++)
            {
                int eq = now[i].IndexOf('=');
                string key = eq > 0 ? now[i].Substring(0, eq) : now[i];
                string value = eq > 0 ? now[i].Substring(eq + 1) : "";
                if (value.StartsWith(UnreadableTag, StringComparison.Ordinal))
                {
                    if (!was.TryGetValue(key, out var recordedValue)) continue;
                    value = recordedValue;
                }
                sb.Append(LibrarySeparator).Append(key).Append('=').Append(value);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Fingerprints <paramref name="primaryPath"/> PLUS every additional library ATTACHed
        /// alongside it (see <see cref="SeforimDbAttachedLibraries"/>), so an index built over
        /// several sources rebuilds when ANY of them changes -- not just the primary database.
        ///
        /// Also changes whenever the SET of additional libraries changes (one added, removed, or
        /// swapped for a different path at the same slot): each library's stamp is prefixed with
        /// its persisted id-space index, so adding/removing one shifts the whole combined value
        /// even if every individual file's content fingerprint is unchanged. That is
        /// deliberate -- a changed library set changes what the index must cover, and the
        /// service/hosted-app restart this feature already requires on add/remove (see
        /// SeforimDbAdditionalLibraries' remarks) means the next build simply redoes the whole
        /// thing from scratch, exactly like a changed primary database does today.
        ///
        /// Libraries are folded in by their PERSISTED index, sorted ascending, so the combined
        /// stamp does not depend on the order <paramref name="additionalLibraries"/> happens to
        /// be enumerated in.
        /// </summary>
        public static string ComputeAll(
            string primaryPath,
            IReadOnlyList<SeforimDbAttachedLibraries.Library> additionalLibraries,
            string prefix = "")
        {
            string combined = Compute(primaryPath, prefix);
            if (additionalLibraries is null || additionalLibraries.Count == 0) return combined;

            var ordered = new List<SeforimDbAttachedLibraries.Library>(additionalLibraries);
            ordered.Sort((a, b) => a.Index.CompareTo(b.Index));

            var sb = new System.Text.StringBuilder(combined);
            foreach (var library in ordered)
                sb.Append("||lib").Append(library.Index).Append('=').Append(ComputeAdditional(library.Path));
            return sb.ToString();
        }

        /// <summary>
        /// An ADDITIONAL library's part of the combined stamp. Unlike the primary database, a
        /// library that cannot be read right now (corrupt, not a database, locked) must never
        /// make the whole stamp - and with it the primary's index - unavailable: it contributes
        /// a stable "unreadable" value keyed on its size instead. Comparisons go through
        /// <see cref="Matches"/>/<see cref="Reconcile"/>, which read that value as "unknown" (a
        /// temporary lock must not wipe a good index) - while an index recorded as built WITHOUT
        /// the library is rebuilt once it can be read - and the primary's own unreadability
        /// still surfaces from <see cref="Compute"/>.
        /// </summary>
        private static string ComputeAdditional(string libraryPath)
        {
            try
            {
                string stamp = Compute(libraryPath);
                // A library whose line / TOC ids are translated (SeforimDbIdMap) hands out ids that are
                // POSITIONS in its layout: adding or removing a book in the middle shifts every later id
                // while min / max / count may stay the same. The layout's signature goes into the stamp so
                // an index built over the old layout is never trusted for the new one.
                SeforimDbAttachedLibraries.Rejection(libraryPath);
                var lineMap = SeforimDbAttachedLibraries.LineMapFor(libraryPath);
                var tocMap = SeforimDbAttachedLibraries.TocMapFor(libraryPath);
                if (lineMap is not null || tocMap is not null)
                    stamp += "|ids=" + (lineMap?.Signature() ?? "-") + "," + (tocMap?.Signature() ?? "-");
                return stamp;
            }
            catch (SeforimDbUnreadableException)
            {
                long length = -1;
                try { length = new FileInfo(libraryPath).Length; } catch { /* size unknown: still stable */ }
                return UnreadableTag + "len=" + length.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
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

                // Which layout this database uses - the content column moved in schema 6.
                var schema = SeforimDbSchema.Probe(connection);

                // MIN/MAX over an INTEGER PRIMARY KEY are index seeks, not scans.
                var lines = ReadIdRange(command, "line");
                var books = ReadIdRange(command, "book");

                // Id ranges alone would miss a database swapped for one with the same ranges
                // but different text; the last line's length pins the actual rows.
                //
                // Read the STORED value's length rather than the decoded text: on a schema-6
                // database that is the compressed frame's size, which changes with the text
                // just as faithfully and costs no dictionary load on a path whose whole job is
                // to touch the database as little as possible. The earlier build threw here
                // rather than fingerprint a compressed database at all.
                command.Parameters.Clear();
                command.CommandText = schema.HasSplitLineContent
                    ? "SELECT length(content) FROM line_content WHERE id = $id"
                    : "SELECT length(content) FROM line WHERE id = $id";
                command.Parameters.AddWithValue("$id", lines.Max);
                object? lastLineLength = command.ExecuteScalar();
                long tail = lastLineLength is null || lastLineLength is DBNull
                    ? -1
                    : Convert.ToInt64(lastLineLength);

                return "schema=" + schemaVersion
                     + "|line=" + lines.Min + ":" + lines.Max
                     + "|book=" + books.Min + ":" + books.Max
                     + "|tail=" + tail
                     + "|dbv=" + ReadDbVersion(command);
            }
            // Deliberately NOT caught: this catch exists for a database that cannot be read
            // (locked, corrupt, unexpected schema), where falling back is correct. Compressed
            // content is a different thing — a database we can read and must not guess about.
            catch (NotSupportedException)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// schema_meta.db_version — bumped by every library update Otzaria applies in place
        /// (a delta replaces whole books in the middle of the database, which the id ranges and
        /// the last line usually do not show), and the baseline Zayit's updater reads. Empty when
        /// the database has no such row.
        /// </summary>
        private static string ReadDbVersion(SqliteCommand command)
        {
            // Probe for the table rather than catch its absence: a transient BUSY must not read
            // as "no version" — that would change the stamp and wipe the index for nothing.
            command.Parameters.Clear();
            command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'schema_meta'";
            if (command.ExecuteScalar() == null) return "";
            command.CommandText = "SELECT value FROM schema_meta WHERE key = 'db_version'";
            return command.ExecuteScalar()?.ToString() ?? "";
        }

        /// <summary>
        /// Zayit's release_info.txt beside the database: the release timestamp its reinstall
        /// writes. A reinstall can bring a database whose ids and size look like the old one.
        /// Empty when the file is absent (Otzaria, or no Zayit install).
        /// </summary>
        private static string ReleaseStamp(string databasePath)
        {
            try
            {
                string? folder = Path.GetDirectoryName(databasePath);
                string path = folder == null ? "" : Path.Combine(folder, "release_info.txt");
                return File.Exists(path) ? "|release=" + File.ReadAllText(path).Trim() : "";
            }
            catch (Exception)
            {
                return "";
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
