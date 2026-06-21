using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FtsLib.Indexing
{
    /// <summary>
    /// Write-ahead log for crash recovery during segment merges.
    ///
    /// Log format (one operation per line):
    ///   BEGIN_FORCE_MERGE                          — force merge session started
    ///   BEGIN_MERGE level=N sources=id1,... target=id  — one level merge started
    ///   END_MERGE   level=N target=id              — one level merge committed
    ///   END_FORCE_MERGE                            — all levels converged
    ///
    /// Recovery rules for a pending BEGIN_MERGE (no matching END_MERGE):
    ///   - sources exist → target is partial; delete target, redo merge.
    ///   - sources gone, target exists → complete; register target, write END_MERGE.
    ///   - sources gone, target missing → unrecoverable; wipe and rebuild.
    ///
    /// Recovery for BEGIN_FORCE_MERGE without END_FORCE_MERGE:
    ///   The force merge was interrupted. After handling any pending BEGIN_MERGE,
    ///   continue merging up the LSM tree until all levels have ≤1 segment.
    /// </summary>
    internal sealed class SegmentWal
    {
        private readonly string _walPath;
        private StreamWriter    _writer;

        public SegmentWal(string segmentsDir)
        {
            _walPath = Path.Combine(segmentsDir, "wal.log");
        }

        public void Open()
        {
            if (_writer != null) return;
            // FileShare.ReadWrite so recovery in a concurrent SeforimIndex constructor
            // (same process, in-test only) can open the file for writing even if an
            // abandoned task's StreamWriter is still open. In production the process
            // restarts so there is no competing handle — this is belt-and-suspenders.
            var stream = new FileStream(_walPath, FileMode.Append,
                FileAccess.Write, FileShare.ReadWrite);
            _writer = new StreamWriter(stream, Encoding.UTF8);
            _writer.AutoFlush = true;
        }

        public void Close()
        {
            _writer?.Dispose();
            _writer = null;
        }

        public void Clear()
        {
            Close();
            if (File.Exists(_walPath)) File.Delete(_walPath);
        }

        // ── Write ─────────────────────────────────────────────────────

        public void BeginForceMerge()  => _writer.WriteLine("BEGIN_FORCE_MERGE");
        public void EndForceMerge()    => _writer.WriteLine("END_FORCE_MERGE");

        public void BeginMerge(int level, int[] sources, int target) =>
            _writer.WriteLine($"BEGIN_MERGE level={level} sources={string.Join(",", sources)} target={target}");

        public void EndMerge(int level, int target) =>
            _writer.WriteLine($"END_MERGE level={level} target={target}");

        // ── Analyze ───────────────────────────────────────────────────

        public RecoveryState Analyze()
        {
            var state = new RecoveryState();
            if (!File.Exists(_walPath)) return state;

            // Use FileShare.ReadWrite so recovery can read the WAL even if the
            // previous build task still has it open for appending (e.g. in our
            // in-process interrupt test where the abandoned task hasn't released
            // its StreamWriter yet). In production the process restarts so the
            // old handle is gone, but this makes the test reliable too.
            string content;
            try
            {
                using (var stream = new FileStream(_walPath, FileMode.Open,
                           FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    content = reader.ReadToEnd();
            }
            catch
            {
                return state;
            }

            foreach (var line in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.StartsWith("BEGIN_FORCE_MERGE"))
                {
                    state.PendingForceMerge = true;
                }
                else if (line.StartsWith("END_FORCE_MERGE"))
                {
                    state.PendingForceMerge = false;
                }
                else if (line.StartsWith("BEGIN_MERGE "))
                {
                    var parts = ParseKV(line.Substring("BEGIN_MERGE ".Length));
                    string levelStr   = GetKV(parts, "level");
                    string targetStr  = GetKV(parts, "target");
                    string sourcesStr = GetKV(parts, "sources");
                    // Skip malformed/truncated lines — treat as if this entry doesn't exist
                    if (levelStr == null || targetStr == null || sourcesStr == null) continue;
                    int level;
                    int target;
                    if (!int.TryParse(levelStr, out level) || !int.TryParse(targetStr, out target)) continue;
                    int[] sources;
                    try { sources = Array.ConvertAll(sourcesStr.Split(','), int.Parse); }
                    catch { continue; }
                    state.PendingMerge = new MergeOp(level, sources, target);
                }
                else if (line.StartsWith("END_MERGE "))
                {
                    state.PendingMerge = null;
                }
                // Legacy entries from old format — ignore safely
            }

            return state;
        }

        private static Dictionary<string, string> ParseKV(string s)
        {
            var result = new Dictionary<string, string>();
            foreach (var pair in s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0) result[pair.Substring(0, eq)] = pair.Substring(eq + 1);
            }
            return result;
        }

        // Safe getter — returns null if key absent (handles truncated WAL lines)
        private static string GetKV(Dictionary<string, string> d, string key)
        {
            string v;
            return d.TryGetValue(key, out v) ? v : null;
        }
    }

    // ── Recovery state ────────────────────────────────────────────────

    internal sealed class RecoveryState
    {
        public MergeOp PendingMerge;
        /// <summary>True when BEGIN_FORCE_MERGE was written but END_FORCE_MERGE was not.</summary>
        public bool PendingForceMerge;
    }

    internal sealed class MergeOp
    {
        public readonly int   Level;
        public readonly int[] Sources;
        public readonly int   Target;

        public MergeOp(int level, int[] sources, int target)
        { Level = level; Sources = sources; Target = target; }
    }
}
