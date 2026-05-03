using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace FtsEngine.Core
{
    /// <summary>
    /// Builds a full-text index using Sort-Based Index Construction (SBIC):
    ///
    ///   1. Stream (docId, content) pairs via Add()
    ///   2. Tokenize → buffer (term, docId) pairs in a 100 MB RAM buffer
    ///   3. Buffer full → sort → flush to a run file → clear buffer
    ///   4. Build() → k-way merge of all runs → postings.bin + index.db
    ///
    /// Peak RAM ≈ 100 MB (one buffer) vs ~816 MB for the old dictionary approach.
    ///
    /// Progress is reported via the <see cref="Progress"/> event, which fires on
    /// the calling thread — safe to marshal to any UI (Console, WinForms, WPF, etc.)
    /// </summary>
    public sealed class IndexBuilder : IDisposable
    {
        // ---- progress reporting ----

        /// <summary>
        /// Raised periodically during indexing and at each phase transition.
        /// Subscribe before calling Add() / Build().
        /// The event fires on whichever thread calls Add() or Build().
        /// For WinForms/WPF, use Control.Invoke / Dispatcher.Invoke in your handler.
        /// </summary>
        public event Action<IndexProgress> Progress;

        /// <summary>How many lines between progress events during the Indexing phase.</summary>
        public int ProgressInterval { get; set; } = 100_000;

        // ---- internals ----
        private readonly string          _runDir;
        private readonly OccurenceBuffer _buffer;
        private readonly Tokenizer       _tokenizer;
        private readonly List<string>    _runPaths;
        private int                      _runIndex;

        private long    _totalLines;   // set by caller via TotalLines property
        private long    _linesProcessed;
        private readonly Stopwatch _swWindow = new Stopwatch();
        private long    _windowLines;

        /// <summary>
        /// Optional: set before calling Add() so ETA can be calculated.
        /// If 0, ETA will be reported as unknown.
        /// </summary>
        public long TotalLines
        {
            get => _totalLines;
            set => _totalLines = value;
        }

        public long LinesProcessed => _linesProcessed;
        public int  RunsFlushed    => _runPaths.Count;

        public IndexBuilder(string runDir)
        {
            _runDir    = runDir;
            _buffer    = new OccurenceBuffer();
            _tokenizer = new Tokenizer();
            _runPaths  = new List<string>();
            Directory.CreateDirectory(runDir);
            _swWindow.Start();
        }

        /// <summary>Add one document. Call from any thread (single-threaded only).</summary>
        public void Add(int docId, string content)
        {
            foreach (var term in _tokenizer.Extract(content))
                _buffer.Add(term, docId);

            _linesProcessed++;
            _windowLines++;

            if (_buffer.IsFull)
            {
                Report(IndexPhase.FlushingRun,
                    $"Flushing run {_runIndex + 1} ({_linesProcessed:N0} lines processed)...");
                FlushRun();
                Report(IndexPhase.Indexing, BuildIndexingMessage(0));
                _swWindow.Restart();
                _windowLines = 0;
            }
            else if (_windowLines >= ProgressInterval)
            {
                double rate = _windowLines * 1000.0 / Math.Max(_swWindow.ElapsedMilliseconds, 1);
                double eta  = _totalLines > 0
                    ? (_totalLines - _linesProcessed) / Math.Max(rate, 1)
                    : 0;
                Report(IndexPhase.Indexing, BuildIndexingMessage(rate), rate, eta);
                _swWindow.Restart();
                _windowLines = 0;
            }
        }

        /// <summary>
        /// Finalises the index: flushes remaining buffer, merges all runs,
        /// writes postings.bin and index.db.
        /// </summary>
        public void Build(string postingsPath, string indexDbPath)
        {
            if (_buffer.Count > 0)
            {
                Report(IndexPhase.FlushingRun,
                    $"Flushing final run ({_linesProcessed:N0} lines total)...");
                FlushRun();
            }

            if (_runPaths.Count == 0)
                throw new InvalidOperationException("No data was added.");

            Report(IndexPhase.Merging,
                $"Merging {_runPaths.Count} run file(s) into final index...");

            IndexWriter.Merge(
                _runPaths.ToArray(),
                postingsPath,
                indexDbPath,
                onDictionaryWrite: () => Report(IndexPhase.WritingDictionary,
                    "Building term dictionary..."));

            Report(IndexPhase.Complete,
                $"Done. {_linesProcessed:N0} lines indexed.");
        }

        // ---- helpers ----

        private void FlushRun()
        {
            string path = _buffer.Flush(_runDir, _runIndex++);
            _runPaths.Add(path);
        }

        private string BuildIndexingMessage(double rate)
        {
            if (_totalLines > 0)
            {
                double pct = 100.0 * _linesProcessed / _totalLines;
                return $"Indexing: {_linesProcessed:N0} / {_totalLines:N0} ({pct:F1}%)  " +
                       $"{rate:N0} lines/s  runs: {_runPaths.Count}";
            }
            return $"Indexing: {_linesProcessed:N0} lines  {rate:N0} lines/s  runs: {_runPaths.Count}";
        }

        private void Report(IndexPhase phase, string message,
                            double linesPerSec = 0, double etaSec = 0)
        {
            Progress?.Invoke(new IndexProgress(
                phase, _linesProcessed, _totalLines,
                linesPerSec, etaSec, _runPaths.Count, message));
        }

        public void Dispose()
        {
            foreach (var p in _runPaths)
                if (File.Exists(p)) File.Delete(p);
        }
    }
}
