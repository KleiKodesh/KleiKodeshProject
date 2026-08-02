using KitveiHakodeshLib.Bridge;
using Microsoft.Win32;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace KitveiHakodeshLib.Db
{
    /// <summary>
    /// Read-only access to Otzaria's OPTIONAL personal-books database (user_books.db)
    /// for the hosted app.
    ///
    /// Bridge actions handled:
    ///   userBooks-sql   — SELECT against user_books.db; same raw-SQL contract as
    ///                     __webviewQuery. When the DB is absent the reply is
    ///                     { rows: [], unavailable: true } — NOT an error: the frontend
    ///                     routes personal-book ids here, and for a DB that was deleted
    ///                     mid-session "no rows" IS the correct answer, not a fault.
    ///   userBooksInfo   — { present, path } availability probe; the frontend calls it
    ///                     once at startup to decide whether to route at all.
    ///
    /// The file belongs to ANOTHER APP (Otzaria): it appears when the user adds their
    /// first personal book, changes while both apps run, and can be deleted. So:
    ///   - resolution is lazy and re-probed on a 5s TTL in BOTH directions (a DB that
    ///     appears mid-run is picked up; a vanished one is dropped instead of failing
    ///     its open with an error forever)
    ///   - connections are strictly read-only and nothing is ever written — no
    ///     EnsureIndexes, no PRAGMA that touches the file (contrast DbAccess on our
    ///     own seforim.db)
    ///
    /// Path resolution mirrors the service's UserBooksDbLocator so dev and hosted find
    /// the same file (first EXISTING file wins):
    ///   1. registry UserBooksPath (beside the seforim Path value)
    ///   2. USER_BOOKS_DB_PATH environment variable (dev override)
    ///   3. %APPDATA%\otzaria\databases            — Otzaria's per-user default
    ///   4. %ProgramData%\otzaria\databases        — Otzaria's system-wide install mode
    ///   5. a `databases` folder beside the seforim DB — travels with a moved library
    /// </summary>
    public class UserBooksDbHandler : IDisposable
    {
        private const string RegistryKeyPath = @"Software\VB and VBA Program Settings\KitveiHakodesh\Database";
        private const string DatabaseFileName = "user_books.db";

        private readonly WebBridge _bridge;
        // The CURRENT seforim DB path — a delegate, not a snapshot, because the sibling
        // candidate (5) must follow the user's DB-path changes without a re-wire.
        private readonly Func<string> _seforimDbPath;

        private readonly object _lock = new object();
        private DbAccess _db;
        private string _path;
        private DateTime _nextProbeUtc = DateTime.MinValue;

        public UserBooksDbHandler(WebBridge bridge, Func<string> seforimDbPath)
        {
            _bridge = bridge;
            _seforimDbPath = seforimDbPath;
        }

        // ── Bridge action handlers ────────────────────────────────────────────────

        public async Task HandleQuery(JsonElement root, string id)
        {
            var db = Current();
            if (db == null)
            {
                _bridge.Reply(id, new { rows = Array.Empty<object>(), unavailable = true });
                return;
            }
            string sql = root.GetProperty("sql").GetString();
            try
            {
                // Off-UI continuation — see DbHandler.HandleSql.
                var rows = await Task.Run(() => db.Query(sql, DbHandler.ParseParamsStatic(root))).ConfigureAwait(false);
                _bridge.Reply(id, new { rows });
            }
            catch (Exception ex)
            {
                // Distinguish "the file went away" (ordinary — reply unavailable and let
                // the TTL re-probe recover) from a genuine SQL error (must surface, or
                // frontend query bugs become invisible).
                bool fileGone;
                lock (_lock)
                {
                    fileGone = _path == null || !File.Exists(_path);
                    if (fileGone) DropLocked();
                }
                if (fileGone)
                    _bridge.Reply(id, new { rows = Array.Empty<object>(), unavailable = true });
                else
                    _bridge.Reply(id, new { error = ex.Message });
            }
        }

        /// <summary>
        /// FILE-BACKED content: Otzaria keeps a personal book's text in the file at
        /// book.filePath (totalLines = 0, `line` table empty). Serves a page of
        /// '\n'-split file lines with Id = 0 — file lines have NO line ids, and
        /// per-line features are guarded off for id-less rows. tocEntry.lineIndex is
        /// 0-based into this split (verified against real Otzaria data), so the split
        /// keeps every element; only the display-only '\r' is trimmed.
        /// Message: { bookId (LOCAL id), offset, limit } — limit 0 returns just
        /// totalLines (the frontend's virtual-scroll init needs the real count).
        /// v1 serves fileType 'txt' only (PDF/docx go through their own flows).
        /// </summary>
        public async Task HandleFileLines(JsonElement root, string id)
        {
            var db = Current();
            if (db == null)
            {
                _bridge.Reply(id, new { rows = Array.Empty<object>(), totalLines = 0, unavailable = true });
                return;
            }
            int bookId = root.GetProperty("bookId").GetInt32();
            int offset = root.TryGetProperty("offset", out var o) ? o.GetInt32() : 0;
            int limit = root.TryGetProperty("limit", out var l) ? l.GetInt32() : 0;
            try
            {
                var reply = await Task.Run(() =>
                {
                    string[] lines = GetFileLines(db, bookId);
                    if (lines == null) return new { rows = Array.Empty<object>(), totalLines = 0 };
                    var rows = new System.Collections.Generic.List<object>();
                    for (int i = offset; i >= 0 && i < lines.Length && rows.Count < limit; i++)
                        rows.Add(new { id = 0, lineIndex = i, content = lines[i] });
                    return new { rows = rows.ToArray(), totalLines = lines.Length };
                }).ConfigureAwait(false);
                _bridge.Reply(id, reply);
            }
            catch (Exception ex)
            {
                _bridge.Reply(id, new { error = ex.Message });
            }
        }

        // Tiny LRU (2 entries): realistically only the open book is hot, and a large
        // sefer's split lines can run tens of MB.
        private readonly System.Collections.Generic.List<FileBookLines> _fileCache
            = new System.Collections.Generic.List<FileBookLines>(2);

        private sealed class FileBookLines
        {
            public int BookId;
            public string Path;
            public long Stamp;
            public string[] Lines;
        }

        private string[] GetFileLines(DbAccess db, int bookId)
        {
            string path = null, fileType = null;
            long totalLines = 0;
            foreach (var row in db.Query("SELECT filePath, fileType, totalLines FROM book WHERE id = ?", new object[] { bookId }))
            {
                object p, t, n;
                row.TryGetValue("filePath", out p);
                row.TryGetValue("fileType", out t);
                row.TryGetValue("totalLines", out n);
                path = p as string;
                fileType = t as string;
                if (n is long lv) totalLines = lv;
                break;
            }
            // totalLines > 0 means the book OWNS DB lines (Otzaria's import flow) —
            // never splice file content into a DB-lined book.
            if (totalLines > 0) return null;
            if (string.IsNullOrWhiteSpace(path)) return null;
            if (!string.Equals(fileType, "txt", StringComparison.OrdinalIgnoreCase)) return null;

            var info = new FileInfo(path);
            if (!info.Exists) return null;
            long stamp = info.LastWriteTimeUtc.Ticks ^ info.Length;

            lock (_lock)
            {
                for (int i = 0; i < _fileCache.Count; i++)
                {
                    var c = _fileCache[i];
                    if (c.BookId == bookId && c.Path == path && c.Stamp == stamp)
                    {
                        _fileCache.RemoveAt(i);
                        _fileCache.Insert(0, c);
                        return c.Lines;
                    }
                }
            }

            // BOM-aware UTF-8; invalid bytes decode to replacement chars rather than
            // failing the book. Keep IDENTICAL semantics to the service's reader.
            string text = File.ReadAllText(path, new System.Text.UTF8Encoding(false));
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length > 0 && line[line.Length - 1] == '\r')
                    lines[i] = line.Substring(0, line.Length - 1);
            }

            lock (_lock)
            {
                _fileCache.RemoveAll(c => c.BookId == bookId);
                _fileCache.Insert(0, new FileBookLines { BookId = bookId, Path = path, Stamp = stamp, Lines = lines });
                if (_fileCache.Count > 2) _fileCache.RemoveAt(_fileCache.Count - 1);
            }
            return lines;
        }

        public void HandleInfo(string id)
        {
            _ = Current(); // refresh resolution, then read both facts under ONE lock
            bool present;
            string path;
            lock (_lock)
            {
                present = _db != null;
                path = _path;
            }
            _bridge.Reply(id, new { present, path });
        }

        // ── Resolution ────────────────────────────────────────────────────────────

        private DbAccess Current()
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                if (now < _nextProbeUtc) return _db;
                _nextProbeUtc = now.AddSeconds(5);

                if (_db != null)
                {
                    if (File.Exists(_path)) return _db;
                    DropLocked(); // vanished — fall through to a fresh probe
                }

                string path = ResolvePath(_seforimDbPath == null ? null : _seforimDbPath());
                if (path == null) return null;
                try
                {
                    // Same pool size as the main DB: opening a personal book fires the
                    // same concurrent query fan-out (bookById + TOC + line pages + …),
                    // and SQLiteConnection instances must not run commands concurrently.
                    // STRICTLY no index writes — this is another app's live file.
                    _db = new DbAccess(path, ensureIndexes: false);
                    _path = path;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[UserBooksDb] open failed for \"" + path + "\": " + ex.Message);
                    _db = null;
                    _path = null;
                }
                return _db;
            }
        }

        private void DropLocked()
        {
            try { if (_db != null) _db.Dispose(); } catch { /* best effort */ }
            _db = null;
            _path = null;
        }

        /// <summary>First existing candidate, or null. Mirrors the service's
        /// UserBooksDbLocator.Resolve — keep the two in sync.</summary>
        internal static string ResolvePath(string seforimDbPath)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath))
                {
                    var v = key == null ? null : key.GetValue("UserBooksPath") as string;
                    if (!string.IsNullOrWhiteSpace(v) && File.Exists(v)) return v;
                }
            }
            catch { /* no registry access — fall through */ }

            string env = Environment.GetEnvironmentVariable("USER_BOOKS_DB_PATH");
            if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(appData))
            {
                string p = Path.Combine(appData, "otzaria", "databases", DatabaseFileName);
                if (File.Exists(p)) return p;
            }

            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (!string.IsNullOrWhiteSpace(programData))
            {
                string p = Path.Combine(programData, "otzaria", "databases", DatabaseFileName);
                if (File.Exists(p)) return p;
            }

            if (!string.IsNullOrWhiteSpace(seforimDbPath))
            {
                string folder = Path.GetDirectoryName(seforimDbPath);
                string root = folder == null ? null : Path.GetDirectoryName(folder);
                if (!string.IsNullOrWhiteSpace(root))
                {
                    string p = Path.Combine(root, "databases", DatabaseFileName);
                    if (File.Exists(p)) return p;
                }
            }

            return null;
        }

        public void Dispose()
        {
            lock (_lock) DropLocked();
        }
    }
}
