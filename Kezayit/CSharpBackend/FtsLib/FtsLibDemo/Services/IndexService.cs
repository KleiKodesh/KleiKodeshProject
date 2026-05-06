using FtsLib.SeforimDb;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FtsLibDemo.Services
{
    /// <summary>
    /// Builds and manages the FTS index for a SQLite seforim database.
    /// Owns the SeforimIndex lifetime — call Dispose when the app exits.
    /// </summary>
    public sealed class IndexService : IIndexService
    {
        private SeforimIndex _index;
        private string       _openDbPath;
        private bool         _disposed;

        public bool IsReady => _index != null;

        public SeforimIndex Index => _index;

        // ── Path helpers ─────────────────────────────────────────────

        public string GetIndexPath(string dbPath)
        {
            string name = Path.GetFileNameWithoutExtension(dbPath);
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name + "-fts-index");
        }

        public bool IndexExists(string dbPath)
        {
            string indexPath = GetIndexPath(dbPath);
            if (!Directory.Exists(indexPath)) return false;
            return Directory.GetFiles(indexPath, "seg_*.dat").Length > 0;
        }

        // ── Open / Close ─────────────────────────────────────────────

        public void Open(string dbPath)
        {
            Close();
            _openDbPath = dbPath;
            _index      = new SeforimIndex(GetIndexPath(dbPath), dbPath);
        }

        public void Close()
        {
            _index      = null;
            _openDbPath = null;
        }

        public SeforimIndex GetLiveIndex(string indexPath, string dbPath)
        {
            if (string.IsNullOrEmpty(indexPath) || !Directory.Exists(indexPath)) return null;
            if (Directory.GetFiles(indexPath, "seg_*.dat").Length == 0)          return null;
            try   { return new SeforimIndex(indexPath, dbPath); }
            catch { return null; }
        }

        // ── Build ────────────────────────────────────────────────────

        public Task BuildAsync(
            string dbPath,
            IProgress<(double pct, string detail)> progress,
            CancellationToken ct)
        {
            return Task.Run(() => BuildCore(dbPath, GetIndexPath(dbPath), progress, ct), ct);
        }

        private static void BuildCore(
            string dbPath,
            string indexPath,
            IProgress<(double pct, string detail)> progress,
            CancellationToken ct)
        {
            // Count lines first so we can report percentage
            long totalLines = CountLines(dbPath, indexPath);
            long indexed    = 0;

            var seforimIndex = new SeforimIndex(indexPath, dbPath);

            seforimIndex.BuildIndex(
                limit: 0,
                onProgress: n =>
                {
                    ct.ThrowIfCancellationRequested();
                    indexed = n;
                    if (indexed % 10_000 == 0)
                    {
                        double pct    = totalLines > 0 ? 100.0 * indexed / totalLines : 0;
                        string detail = totalLines > 0
                            ? $"{indexed:N0} / {totalLines:N0}  ({pct:F1}%)"
                            : $"{indexed:N0} שורות";
                        progress.Report((pct, detail));
                    }
                });

            progress.Report((99, "מסיים ומשמר אינדקס…"));
        }

        private static long CountLines(string dbPath, string indexPath)
        {
            try
            {
                var tmp = new SeforimIndex(indexPath, dbPath);
                return tmp.CountLines();
            }
            catch { return 0; }
        }

        // ── Dispose ──────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Close();
        }
    }
}
