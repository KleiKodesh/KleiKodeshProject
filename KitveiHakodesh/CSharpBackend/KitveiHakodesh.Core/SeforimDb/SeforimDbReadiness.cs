using System;
using System.Buffers.Binary;
using System.IO;
using System.Text.RegularExpressions;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// Whether the seforim database file is safe to build an index from right now — answered
    /// BEFORE any index compares stamps or starts a build, because both read the file, and a
    /// file another app is still writing looks like a changed database: the index would be
    /// wiped and rebuilt from half a library.
    ///
    /// The apps that own these files update them while we run:
    ///   Zayit     reinstalls by deleting the old database and extracting the new one IN PLACE
    ///             under its final name (a tar.zst stream, minutes long, no temp name).
    ///   Otzaria   downloads to temp files beside the database and swaps them in by rename, or
    ///             applies an in-place delta (whole books deleted and re-inserted).
    ///
    /// Cheap enough to ask on every attempt: one directory listing and a 100-byte header read.
    /// A "not ready" answer means "ask again later", never "the database is broken".
    /// </summary>
    public static class SeforimDbReadiness
    {
        /// <summary>A file written this recently may still be being written.</summary>
        private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(10);

        /// <summary>An update file untouched this long belongs to an update that died; it no
        /// longer means one is running.</summary>
        private static readonly TimeSpan AbandonedAfter = TimeSpan.FromMinutes(30);

        /// <summary>Otzaria's temps of THIS database (its name plus one of these): the download,
        /// and the backup / restore copies of a swap. Tied to the database's own name so another
        /// tool's backups in a shared folder do not count.</summary>
        private static readonly string[] OtzariaUpdateSuffixes =
        {
            ".download", ".bak-update", ".backup", ".backup.tmp", ".restore.tmp",
        };

        /// <summary>Zayit's downloaded release archive, whole or in parts (.tar.zst, .tar.zst.partNN).</summary>
        private static readonly Regex ZayitArchive =
            new Regex(@"\.tar\.zst(\.part\d+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>True when the database can be indexed now; otherwise
        /// <paramref name="reason"/> says what to wait for. <paramref name="ignoreQuietPeriod"/>
        /// skips only the "written in the last seconds" test — for a caller that has given up
        /// waiting for a file that never goes quiet; the completeness checks still apply.</summary>
        public static bool IsReadyToIndex(string databasePath, out string reason, bool ignoreQuietPeriod = false)
        {
            try
            {
                var file = new FileInfo(databasePath);
                if (!file.Exists) { reason = "the database file does not exist"; return false; }

                DateTime now = DateTime.UtcNow;
                // An in-place update writes the -wal (WAL mode) or keeps a -journal (rollback
                // mode) while it runs; the main file's own time can stay still meanwhile.
                if (!ignoreQuietPeriod)
                {
                    if (now - file.LastWriteTimeUtc < QuietPeriod)
                    {
                        reason = "the database file was written in the last few seconds";
                        return false;
                    }
                    var wal = new FileInfo(file.FullName + "-wal");
                    if (wal.Exists && now - wal.LastWriteTimeUtc < QuietPeriod)
                    {
                        reason = "the database is being written (its -wal changed in the last few seconds)";
                        return false;
                    }
                }
                if (File.Exists(file.FullName + "-journal"))
                {
                    reason = "a write transaction is open on the database (-journal present)";
                    return false;
                }

                string? folder = file.DirectoryName;
                if (folder != null)
                    foreach (var sibling in new DirectoryInfo(folder).EnumerateFiles())
                    {
                        if (now - sibling.LastWriteTimeUtc > AbandonedAfter) continue;
                        bool otzariaTemp = false;
                        foreach (string suffix in OtzariaUpdateSuffixes)
                            if (string.Equals(sibling.Name, file.Name + suffix, StringComparison.OrdinalIgnoreCase))
                                otzariaTemp = true;
                        if (otzariaTemp || ZayitArchive.IsMatch(sibling.Name))
                        {
                            reason = "another app is updating the database (" + sibling.Name + ")";
                            return false;
                        }
                    }

                return HeaderSaysComplete(file, out reason);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                reason = "the database file cannot be read right now (" + ex.Message + ")";
                return false;
            }
        }

        /// <summary>
        /// The SQLite header records the database size in pages (bytes 28-31, trusted only when
        /// bytes 92-95 match the change counter at 24-27). A file shorter than that is still
        /// being written — exactly the state of an in-place extraction, whose first bytes are
        /// the finished header.
        /// </summary>
        private static bool HeaderSaysComplete(FileInfo file, out string reason)
        {
            var header = new byte[100];
            using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read,
                                               FileShare.ReadWrite | FileShare.Delete))
            {
                int read = 0;
                while (read < header.Length)
                {
                    int n = stream.Read(header, read, header.Length - read);
                    if (n == 0) break;
                    read += n;
                }
                if (read < header.Length) { reason = "the database file is shorter than its header"; return false; }
            }

            const string Magic = "SQLite format 3\0";
            for (int i = 0; i < Magic.Length; i++)
                if (header[i] != Magic[i]) { reason = "the database file is not (yet) a SQLite database"; return false; }

            int pageSize = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(16));
            if (pageSize == 1) pageSize = 65536;
            uint changeCounter = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(24));
            uint pageCount = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(28));
            uint validFor = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(92));

            if (pageCount != 0 && validFor == changeCounter && file.Length < (long)pageCount * pageSize)
            {
                reason = "the database file is shorter than the database it describes (still being written)";
                return false;
            }

            reason = "";
            return true;
        }
    }
}
