namespace FtsEngine.Core
{
    /// <summary>
    /// Progress snapshot raised by IndexBuilder at regular intervals.
    /// All properties are safe to read from any thread.
    /// </summary>
    public sealed class IndexProgress
    {
        /// <summary>Current phase of the indexing pipeline.</summary>
        public IndexPhase Phase { get; }

        /// <summary>Lines read from the source so far.</summary>
        public long LinesProcessed { get; }

        /// <summary>Total lines expected (0 if unknown).</summary>
        public long TotalLines { get; }

        /// <summary>0.0–1.0 progress within the current phase (NaN if unknown).</summary>
        public double Fraction => TotalLines > 0
            ? (double)LinesProcessed / TotalLines
            : double.NaN;

        /// <summary>Percentage string, e.g. "42.3%" or "?" if total unknown.</summary>
        public string PercentText => double.IsNaN(Fraction)
            ? "?"
            : $"{Fraction * 100:F1}%";

        /// <summary>Instantaneous throughput in lines/sec (0 during non-indexing phases).</summary>
        public double LinesPerSecond { get; }

        /// <summary>Estimated seconds remaining (0 if unknown).</summary>
        public double EtaSeconds { get; }

        /// <summary>Number of run files flushed to disk so far.</summary>
        public int RunsFlushed { get; }

        /// <summary>Human-readable one-line summary.</summary>
        public string Message { get; }

        internal IndexProgress(
            IndexPhase phase,
            long       linesProcessed,
            long       totalLines,
            double     linesPerSecond,
            double     etaSeconds,
            int        runsFlushed,
            string     message)
        {
            Phase          = phase;
            LinesProcessed = linesProcessed;
            TotalLines     = totalLines;
            LinesPerSecond = linesPerSecond;
            EtaSeconds     = etaSeconds;
            RunsFlushed    = runsFlushed;
            Message        = message;
        }
    }

    public enum IndexPhase
    {
        /// <summary>Reading source lines and filling the RAM buffer.</summary>
        Indexing,

        /// <summary>Sorting and flushing a buffer to a run file.</summary>
        FlushingRun,

        /// <summary>K-way merging run files into the final index.</summary>
        Merging,

        /// <summary>Building the SQLite term dictionary.</summary>
        WritingDictionary,

        /// <summary>All done.</summary>
        Complete
    }
}
