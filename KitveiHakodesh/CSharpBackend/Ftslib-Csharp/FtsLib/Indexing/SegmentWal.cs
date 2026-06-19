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
    ///   BEGIN_MERGE level=N sources=id1,id2,... target=id
    ///   END_MERGE   level=N target=id
    ///
    /// Deletion order (crash-safe):
    ///   1. Write merged target to .tmp, then rename to final name.
    ///   2. Delete source segments.
    ///   3. Log END_MERGE.
    ///   4. Update live state in memory.
    ///
    /// Recovery rules:
    ///   - BEGIN_MERGE present, sources exist → target is partial; delete target, redo merge.
    ///   - BEGIN_MERGE present, sources gone, target exists → sources deleted but END_MERGE not
    ///     written; target is complete — register it as live and write END_MERGE to close the WAL.
    ///   - BEGIN_MERGE present, sources gone, target missing → unrecoverable; wipe and rebuild.
    ///   - No BEGIN_MERGE (or matched END_MERGE) → nothing to recover.
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

                if (line.StartsWith("BEGIN_MERGE "))
                {
                    var parts   = ParseKV(line.Substring("BEGIN_MERGE ".Length));
                    int level   = int.Parse(parts["level"]);
                    int target  = int.Parse(parts["target"]);
                    var sources = Array.ConvertAll(parts["sources"].Split(','), int.Parse);
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
    }

    // ── Recovery state ────────────────────────────────────────────────

    internal sealed class RecoveryState
    {
        public MergeOp PendingMerge;
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
