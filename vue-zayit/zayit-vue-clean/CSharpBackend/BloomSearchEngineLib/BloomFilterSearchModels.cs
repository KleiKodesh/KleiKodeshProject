using System;

namespace BloomSearchEngineLib
{
    public sealed class SearchResultItem
    {
        public int LineId { get; set; }
        public int BookId { get; set; }
        public string BookTitle { get; set; }
        public string TocText { get; set; }
        public int Score { get; set; }
        public double ProximityScore { get; set; }
        public string Snippet { get; set; }
    }

    public sealed class IndexProgressChangedEventArgs : EventArgs
    {
        public int ProcessedChunks { get; }
        public int TotalChunks { get; }
        public double Percentage { get; }
        public TimeSpan Elapsed { get; }
        public TimeSpan Eta { get; }

        public IndexProgressChangedEventArgs(int processed, int total, TimeSpan elapsed, TimeSpan eta)
        {
            ProcessedChunks = processed; TotalChunks = total;
            Percentage = processed * 100.0 / total; Elapsed = elapsed; Eta = eta;
        }
    }

    public sealed class DatabaseInitProgressEventArgs : EventArgs
    {
        public int ProcessedRows { get; }
        public int TotalRows { get; }
        public double Percentage { get; }
        public TimeSpan Elapsed { get; }
        public TimeSpan Eta { get; }

        public DatabaseInitProgressEventArgs(int processed, int total, TimeSpan elapsed, TimeSpan eta)
        {
            ProcessedRows = processed; TotalRows = total;
            Percentage = processed * 100.0 / total; Elapsed = elapsed; Eta = eta;
        }
    }
}
