using System;
using System.Collections.Generic;
using System.IO;
using CoreAnnotations = KitveiHakodesh.Core.UserAnnotations;

namespace KitveiHakodeshLib.UserSettings
{
    /// <summary>
    /// The hosted app's access point for the user's own annotations — highlights and notes.
    ///
    /// The database, its schema, the quote-aware '?' rewrite and the SQL GUARD are
    /// KitveiHakodesh.Core's (<see cref="CoreAnnotations.UserAnnotationStore"/>); what stays
    /// here is the process-wide singleton the WebView2 handlers resolve against, and nothing
    /// else.
    ///
    /// Raw SQL from the page is DELIBERATE here and only here — typed methods for the
    /// read-only shipped corpus, frontend-supplied SQL for user-owned mutable data. That is
    /// exactly why the guard matters: this project used to run that SQL with no filter at all,
    /// where the dev service has enforced an allowlist since 2026-08-21. Going through Core
    /// closes that gap, and fixes a second one — the '?' to @pN rewrite here was a plain
    /// Regex.Replace, so a note whose text contained a question mark was mangled.
    ///
    /// The file is still called user_settings.db: it holds real highlights on existing
    /// installs, and renaming it would orphan them. See the remarks on UserAnnotationStore.
    /// </summary>
    public class UserSettingsDbAccess
    {
        // ── Shared singleton ──────────────────────────────────────────────────────

        private static readonly object _lock = new object();
        private static UserSettingsDbAccess _shared;

        /// <summary>
        /// Opens (or re-uses) the process-wide access point for the given seforim DB path.
        /// Thread-safe. Idempotent when called repeatedly with the same path.
        /// </summary>
        public static UserSettingsDbAccess Open(string seforimDbPath)
        {
            if (string.IsNullOrEmpty(seforimDbPath) || !File.Exists(seforimDbPath))
                return null;

            string desiredPath = DeriveUserSettingsDbPath(seforimDbPath);

            lock (_lock)
            {
                if (_shared != null &&
                    string.Equals(_shared.Path, desiredPath, StringComparison.OrdinalIgnoreCase))
                    return _shared;

                // Path changed or first call — replace the singleton.
                _shared = null;
                try
                {
                    var instance = new UserSettingsDbAccess(desiredPath);
                    instance._store.EnsureCreated();
                    _shared = instance;
                }
                catch (Exception ex)
                {
                    // Non-fatal — user annotations won't be saved this session, but the rest
                    // of the app works normally.
                    System.Diagnostics.Debug.WriteLine(
                        "[UserSettingsDbAccess] Failed to open/init: " + ex.Message);
                    _shared = null;
                }
                return _shared;
            }
        }

        /// <summary>
        /// Returns the currently active shared instance, or null if not yet opened.
        /// </summary>
        public static UserSettingsDbAccess Current
        {
            get { lock (_lock) { return _shared; } }
        }

        /// <summary>
        /// Clears the singleton at process teardown. Safe to call multiple times.
        /// No connection needs closing since connections are never held open.
        /// </summary>
        public static void DisposeShared()
        {
            lock (_lock)
            {
                _shared = null;
            }
        }

        // ── Instance ──────────────────────────────────────────────────────────────

        private readonly CoreAnnotations.UserAnnotationStore _store;

        private UserSettingsDbAccess(string path)
        {
            Path = path;
            _store = new CoreAnnotations.UserAnnotationStore(path);
        }

        public string Path { get; private set; }

        /// <summary>
        /// Derives the user settings database path from the seforim database path.
        /// Example: C:\data\seforim.db  →  C:\data\Settings\user_settings.db
        /// </summary>
        public static string DeriveUserSettingsDbPath(string seforimDbPath) =>
            CoreAnnotations.UserAnnotationStore.DeriveDatabasePath(seforimDbPath);

        /// <summary>
        /// Runs a SELECT the frontend supplied and returns its rows.
        ///
        /// No lock: Core opens a fresh connection per call and the database is WAL, so
        /// concurrent readers and writers — including another process holding the same
        /// library open — do not block each other.
        /// </summary>
        public IEnumerable<IDictionary<string, object>> Query(string sql, object[] parameters)
        {
            var rows = _store.Query(sql, parameters ?? new object[0]);
            var result = new List<IDictionary<string, object>>(rows.Count);
            foreach (var row in rows) result.Add(row);
            return result;
        }

        /// <summary>
        /// Runs an INSERT/UPDATE/DELETE the frontend supplied and returns the row id of an
        /// INSERT, read on the same connection so it cannot pick up another one's insert.
        /// </summary>
        public long Execute(string sql, object[] parameters) =>
            _store.Execute(sql, parameters ?? new object[0]);
    }
}
