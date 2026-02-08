
namespace BloomSearchEngineLib
{
    using System;

    public sealed class SearchResultItem
    {
        public int LineId { get; set; }
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

        public IndexProgressChangedEventArgs(
            int processedChunks,
            int totalChunks,
            TimeSpan elapsed,
            TimeSpan eta)
        {
            ProcessedChunks = processedChunks;
            TotalChunks = totalChunks;
            Percentage = processedChunks * 100.0 / totalChunks;
            Elapsed = elapsed;
            Eta = eta;
        }
    }

    public sealed class DatabaseInitProgressEventArgs : EventArgs
    {
        public int ProcessedRows { get; }
        public int TotalRows { get; }
        public double Percentage { get; }
        public TimeSpan Elapsed { get; }
        public TimeSpan Eta { get; }

        public DatabaseInitProgressEventArgs(
            int processedRows,
            int totalRows,
            TimeSpan elapsed,
            TimeSpan eta)
        {
            ProcessedRows = processedRows;
            TotalRows = totalRows;
            Percentage = processedRows * 100.0 / totalRows;
            Elapsed = elapsed;
            Eta = eta;
        }
    }

}
