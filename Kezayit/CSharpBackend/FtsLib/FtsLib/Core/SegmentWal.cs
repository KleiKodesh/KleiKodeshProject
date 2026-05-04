using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FtsLib.Core
{
    /// <summary>
    /// Write-ahead log for crash recovery during segment merges and commits.
    /// 
    /// Log format (one operation per line):
    ///   BEGIN_MERGE level=N sources=id1,id2,... target=id
    ///   END_MERGE level=N target=id
    ///   BEGIN_COMMIT segment=filename
    ///   END_COMMIT
    /// 
    /// Recovery logic:
    /// - BEGIN_MERGE without END_MERGE → delete partial target, redo merge
    /// - END_MERGE without source cleanup → delete source segments
    /// - BEGIN_COMMIT without END_COMMIT → delete partial postings.dat/Meta.db, redo commit
    /// </summary>
    internal sealed class SegmentWal
    {
        private readonly string _walPath;
        private StreamWriter _writer;

        public SegmentWal(string segmentsDir)
        {
            _walPath = Path.Combine(segmentsDir, "wal.log");
        }

        public void Open()
        {
            if (_writer != null) return; // already open
            _writer = new StreamWriter(_walPath, append: true, Encoding.UTF8);
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
            if (File.Exists(_walPath))
                File.Delete(_walPath);
        }

        // ── Write operations ─────────────────────────────────────────

        public void BeginMerge(int level, int[] sources, int target)
        {
            _writer.WriteLine($"BEGIN_MERGE level={level} sources={string.Join(",", sources)} target={target}");
        }

        public void EndMerge(int level, int target)
        {
            _writer.WriteLine($"END_MERGE level={level} target={target}");
        }

        public void BeginCommit(string segmentFile)
        {
            _writer.WriteLine($"BEGIN_COMMIT segment={Path.GetFileName(segmentFile)}");
        }

        public void EndCommit()
        {
            _writer.WriteLine("END_COMMIT");
        }

        // ── Recovery ─────────────────────────────────────────────────

        /// <summary>
        /// Analyzes WAL and returns pending operations that need recovery.
        /// </summary>
        public RecoveryState Analyze()
        {
            var state = new RecoveryState();
            if (!File.Exists(_walPath))
                return state;

            foreach (var line in File.ReadAllLines(_walPath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.StartsWith("BEGIN_MERGE "))
                {
                    var parts = ParseKV(line.Substring("BEGIN_MERGE ".Length));
                    int level = int.Parse(parts["level"]);
                    int target = int.Parse(parts["target"]);
                    var sources = Array.ConvertAll(parts["sources"].Split(','), int.Parse);
                    state.PendingMerge = new MergeOp(level, sources, target);
                }
                else if (line.StartsWith("END_MERGE "))
                {
                    state.PendingMerge = null; // merge completed
                }
                else if (line.StartsWith("BEGIN_COMMIT "))
                {
                    var parts = ParseKV(line.Substring("BEGIN_COMMIT ".Length));
                    state.PendingCommit = parts["segment"];
                }
                else if (line == "END_COMMIT")
                {
                    state.PendingCommit = null; // commit completed
                }
            }

            return state;
        }

        private static Dictionary<string, string> ParseKV(string s)
        {
            var result = new Dictionary<string, string>();
            foreach (var pair in s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0)
                    result[pair.Substring(0, eq)] = pair.Substring(eq + 1);
            }
            return result;
        }
    }

    // ── Recovery state ───────────────────────────────────────────────

    internal sealed class RecoveryState
    {
        public MergeOp PendingMerge;
        public string  PendingCommit; // segment filename
    }

    internal sealed class MergeOp
    {
        public readonly int   Level;
        public readonly int[] Sources;
        public readonly int   Target;

        public MergeOp(int level, int[] sources, int target)
        {
            Level = level;
            Sources = sources;
            Target = target;
        }
    }
}
