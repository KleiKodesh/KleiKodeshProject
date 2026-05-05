using FtsLib.Core;
using FtsLib.Misc;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FtsLibDemo.Services
{
    /// <summary>
    /// Builds and manages the FTS index for a SQLite seforim database.
    /// Owns the IndexReader lifetime — call Dispose when the app exits.
    /// </summary>
    public sealed class IndexService : IIndexService
    {
        private IndexReader _reader;
        private bool _disposed;

        public bool IsReady => _reader != null;

        public IndexReader Reader => _reader;

        /// <summary>
        /// Returns a reader for the live index being built, or null if no build is in progress.
        /// </summary>
        public IndexReader GetLiveReader(string indexPath)
        {
            if (string.IsNullOrEmpty(indexPath) || !Directory.Exists(indexPath))
                return null;
            try
            {
                return new IndexReader(indexPath);
            }
            catch
            {
                return null;
            }
        }

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
            _reader = new IndexReader(GetIndexPath(dbPath));
        }

        public void Close()
        {
            _reader?.Dispose();
            _reader = null;
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
            long totalLines = CountLines(dbPath);
            long indexed    = 0;

            var tokenizer = new Tokenizer();

            using (var writer = new IndexWriter(indexPath))
            using (var db = new ZayitDb(dbPath))
            {
                foreach (var (lineId, content) in db.ReadLines(0))
                {
                    foreach (var term in tokenizer.Extract(content))
                        writer.Add(lineId, term);

                    indexed++;

                    if (indexed % 10_000 == 0)
                    {
                        double pct    = totalLines > 0 ? 100.0 * indexed / totalLines : 0;
                        string detail = totalLines > 0
                            ? $"{indexed:N0} / {totalLines:N0}  ({pct:F1}%)"
                            : $"{indexed:N0} שורות";
                        progress.Report((pct, detail));
                    }
                }

                // IndexWriter.Dispose() triggers the final merge — report indeterminate progress
                progress.Report((99, "מסיים ומשמר אינדקס…"));
            }
        }

        private static long CountLines(string dbPath)
        {
            using (var db = new ZayitDb(dbPath))
            {
                return db.CountLines();
            }
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
