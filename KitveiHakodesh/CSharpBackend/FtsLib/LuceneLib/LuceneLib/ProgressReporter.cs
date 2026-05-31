using System;

namespace LuceneLib
{
    /// <summary>
    /// Writes an in-place progress line to the console once per second.
    /// Shows row count, optional percentage, rows/sec, elapsed time, and ETA.
    /// </summary>
    internal sealed class ProgressReporter
    {
        private readonly long   _total;
        private readonly System.Diagnostics.Stopwatch _sw;
        private long _nextTick;

        public long ElapsedMs => _sw.ElapsedMilliseconds;

        /// <param name="total">Total expected rows. Pass 0 to omit percentage and ETA.</param>
        public ProgressReporter(long total = 0)
        {
            _total    = total;
            _sw       = System.Diagnostics.Stopwatch.StartNew();
            _nextTick = 1000;
        }

        /// <summary>
        /// Call on every row. Prints a progress line if a second has elapsed since the last one.
        /// </summary>
        public void Tick(long count)
        {
            if (_sw.ElapsedMilliseconds >= _nextTick)
            {
                Write(count);
                _nextTick = _sw.ElapsedMilliseconds + 1000;
            }
        }

        /// <summary>Writes the final progress line and moves to a new line.</summary>
        public void Complete(long count)
        {
            Write(count);
            Console.WriteLine();
        }

        public TimeSpan Elapsed => _sw.Elapsed;

        // ── Formatting ────────────────────────────────────────────────

        private void Write(long count)
        {
            double rate = _sw.Elapsed.TotalSeconds > 0 ? count / _sw.Elapsed.TotalSeconds : 0;

            string pct = _total > 0
                ? $" {100.0 * count / _total,5:F1}%"
                : string.Empty;

            string eta = _total > 0 && rate > 0
                ? $"  ETA {FormatElapsed(TimeSpan.FromSeconds((_total - count) / rate))}"
                : string.Empty;

            Console.Write($"\r  {count,12:N0}{pct}  {FormatRate(rate),10}  elapsed {FormatElapsed(_sw.Elapsed)}{eta}   ");
        }

        public static string FormatElapsed(TimeSpan t) =>
            t.TotalHours >= 1
                ? $"{(int)t.TotalHours}h {t.Minutes:D2}m {t.Seconds:D2}s"
                : t.TotalMinutes >= 1
                    ? $"{(int)t.TotalMinutes}m {t.Seconds:D2}s"
                    : $"{t.TotalSeconds:F1}s";

        private static string FormatRate(double rate) =>
            rate > 0 ? $"{rate:N0}/s" : "—";
    }
}
