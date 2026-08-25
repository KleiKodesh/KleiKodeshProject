using System;
using System.IO;
using System.Threading;

namespace KitveiHakodesh.Core.Common
{
    /// <summary>
    /// Notices when a database file is replaced or edited while the app is running, and says so
    /// once the writing has stopped.
    ///
    /// WHY A WATCHER AT ALL. Each derived index checks its source once at startup and rebuilds
    /// if stale. That check is latched, so a long-lived process would never notice a database
    /// updated underneath it. This fills that gap; it does not replace the startup check.
    ///
    /// It watches the file's DIRECTORY with a "&lt;name&gt;*" filter rather than the single file,
    /// because in SQLite WAL mode the live write lands in "&lt;name&gt;-wal" and a file-scoped
    /// watcher would see nothing at all. Idle cost is negligible: FileSystemWatcher is
    /// event-driven through the kernel, not a poll loop.
    ///
    /// IT NEVER FIRES IMMEDIATELY. Replacing a corpus is a long operation of many writes and
    /// pauses; reacting mid-write would either waste a rebuild or index half a file. So after
    /// activity it waits a generous quiet window and then CONFIRMS the file has actually
    /// stopped changing, repeating that wait as long as it keeps moving — bounded by a hard cap
    /// so a continuously trickling write still eventually gets served.
    ///
    /// WHAT IT DOES NOT DO: decide what a change means. It calls back; the orchestrator owns
    /// which indexes to rebuild, what to log, and whether to restart. This class knows about a
    /// path and a callback, nothing else.
    /// </summary>
    public sealed class DbChangeWatcher : IDisposable
    {
        /// <summary>Two minutes of quiet before the file is even considered settled. Long
        /// because the event this exists for — a corpus being replaced — takes minutes.</summary>
        public static readonly TimeSpan DefaultSettleWindow = TimeSpan.FromMinutes(2);

        /// <summary>After a quarter of an hour of continuous change, act anyway. Without a cap,
        /// a file written slowly forever would defer forever.</summary>
        public static readonly TimeSpan DefaultMaxDeferral = TimeSpan.FromMinutes(15);

        /// <summary>One file plus its -wal / -shm / -journal sidecars — the smallest buffer the
        /// kernel accepts is more than enough.</summary>
        private const int WatchBufferBytes = 4096;

        private readonly string _databasePath;
        private readonly SettleDetector _settle;

        private FileSystemWatcher? _watcher;

        /// <param name="databasePath">The database to watch. Its directory is watched, with a
        /// filter covering the file and its sidecars.</param>
        /// <param name="onSettled">Called once per settle cycle, on a timer thread.</param>
        /// <param name="settleWindow">How long the file must be quiet. Defaults to
        /// <see cref="DefaultSettleWindow"/>.</param>
        /// <param name="maxDeferral">How long to keep deferring a file that will not settle.
        /// Defaults to <see cref="DefaultMaxDeferral"/>.</param>
        public DbChangeWatcher(
            string databasePath,
            Action onSettled,
            TimeSpan? settleWindow = null,
            TimeSpan? maxDeferral = null)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("databasePath is required", nameof(databasePath));
            if (onSettled == null) throw new ArgumentNullException(nameof(onSettled));

            _databasePath = databasePath;
            _settle = new SettleDetector(
                databasePath, onSettled,
                settleWindow ?? DefaultSettleWindow,
                maxDeferral ?? DefaultMaxDeferral);
        }

        /// <summary>
        /// Starts watching. Returns false when there is nothing to watch — the folder is gone,
        /// a drive is disconnected, the OS refused the watch. That is a legitimate outcome
        /// (changes get picked up on the next launch instead), not an error, so the caller
        /// decides whether to mention it.
        /// </summary>
        public bool TryStart()
        {
            string? folder = Path.GetDirectoryName(_databasePath);
            string name = Path.GetFileName(_databasePath);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return false;

            try
            {
                var watcher = new FileSystemWatcher(folder, name + "*")
                {
                    InternalBufferSize = WatchBufferBytes,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
                                 | NotifyFilters.FileName | NotifyFilters.CreationTime,
                };
                watcher.Changed += OnFileSystemEvent;
                watcher.Created += OnFileSystemEvent;
                watcher.Deleted += OnFileSystemEvent;
                watcher.Renamed += OnFileSystemEvent;
                watcher.EnableRaisingEvents = true;
                _watcher = watcher;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void OnFileSystemEvent(object sender, FileSystemEventArgs e) => _settle.Poke();

        public void Dispose()
        {
            _watcher?.Dispose();
            _watcher = null;
            _settle.Dispose();
        }

        /// <summary>
        /// Decides WHEN the file has stopped changing. Private because it is how this watcher
        /// makes that call, not a job anything else has: a caller with a path and a callback
        /// wants the watcher, and a caller without them has no use for the timer.
        /// </summary>
        private sealed class SettleDetector : IDisposable
        {
            private readonly string _databasePath;
            private readonly Action _onSettled;
            private readonly TimeSpan _settleWindow;
            private readonly TimeSpan _maxDeferral;

            private readonly object _gate = new object();
            private Timer? _timer;
            private string? _lastSeenFingerprint;
            private DateTime _deferStartedUtc;

            public SettleDetector(string databasePath, Action onSettled,
                TimeSpan settleWindow, TimeSpan maxDeferral)
            {
                _databasePath = databasePath;
                _onSettled = onSettled;
                _settleWindow = settleWindow;
                _maxDeferral = maxDeferral;
            }

            /// <summary>Activity happened — re-arm the quiet window without firing.</summary>
            public void Poke()
            {
                lock (_gate)
                {
                    if (_timer == null)
                    {
                        _deferStartedUtc = DateTime.UtcNow;
                        _lastSeenFingerprint = SafeFingerprint();
                        _timer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
                    }
                    _timer.Change(_settleWindow, Timeout.InfiniteTimeSpan);
                }
            }

            private void Tick()
            {
                string current = SafeFingerprint();

                lock (_gate)
                {
                    bool stable = current.Length > 0 && current == _lastSeenFingerprint;
                    bool capReached = DateTime.UtcNow - _deferStartedUtc >= _maxDeferral;

                    if (!stable && !capReached)
                    {
                        // Still moving — a slow writer paused longer than the window and then
                        // wrote again — or momentarily unreadable. Reset the baseline and wait
                        // another full window.
                        _lastSeenFingerprint = current;
                        _timer?.Change(_settleWindow, Timeout.InfiniteTimeSpan);
                        return;
                    }

                    _timer?.Dispose();
                    _timer = null;
                    _lastSeenFingerprint = null;
                }

                _onSettled();
            }

            /// <summary>An unreadable file yields "", which never compares equal to a real
            /// fingerprint, so a file that cannot be read counts as still moving.</summary>
            private string SafeFingerprint()
            {
                try { return DbFileFingerprint.Compute(_databasePath); }
                catch (Exception) { return ""; }
            }

            public void Dispose()
            {
                lock (_gate)
                {
                    _timer?.Dispose();
                    _timer = null;
                }
            }
        }
    }
}
