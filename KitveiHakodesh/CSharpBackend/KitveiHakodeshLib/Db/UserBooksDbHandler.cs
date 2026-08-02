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
