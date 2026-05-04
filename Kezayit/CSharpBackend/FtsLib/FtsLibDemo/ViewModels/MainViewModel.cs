using FtsLib;
using FtsLib.Core;
using FtsLibDemo.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace FtsLibDemo.ViewModels
{
    public sealed class MainViewModel : ViewModelBase
    {
        // ── Services ─────────────────────────────────────────────────────────
        private readonly ISettingsService _settings;

        // ── Index state ──────────────────────────────────────────────────────
        private IndexReader _reader;

        // ── Backing fields ───────────────────────────────────────────────────
        private string  _searchQuery        = string.Empty;
        private string  _statusText         = "מוכן";
        private string  _indexedDbPath      = string.Empty;
        private double  _indexProgress;        // 0–100
        private bool    _isIndexing;
        private bool    _isSearching;
        private bool    _indexReady;
        private string  _progressDetail     = string.Empty;
        private string  _elapsedTime        = string.Empty;
        private System.Diagnostics.Stopwatch _indexStopwatch;
        private System.Windows.Threading.DispatcherTimer _elapsedTimer;
        private CancellationTokenSource _indexCts;

        // ── Constructor ──────────────────────────────────────────────────────
        public MainViewModel(ISettingsService settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            Results = new ObservableCollection<SearchResultItem>();

            BuildIndexCommand  = new RelayCommand((object _) => OnBuildIndex(_),  (_) => !_isIndexing && !_isSearching);
            SearchCommand      = new RelayCommand((object _) => OnSearch(_),      (_) => !_isIndexing && !_isSearching && _indexReady && !string.IsNullOrWhiteSpace(_searchQuery));
            CancelIndexCommand = new RelayCommand((object _) => OnCancelIndex(_), (_) => _isIndexing);

            // Restore previously indexed DB path and try to open the index
            _indexedDbPath = _settings.IndexedDbPath ?? string.Empty;
            TryOpenExistingIndex();
        }

        // ── Public properties ────────────────────────────────────────────────

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetField(ref _searchQuery, value))
                    RelayCommand.RaiseCanExecuteChanged();
            }
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetField(ref _statusText, value);
        }

        public string IndexedDbPath
        {
            get => _indexedDbPath;
            private set => SetField(ref _indexedDbPath, value);
        }

        public double IndexProgress
        {
            get => _indexProgress;
            private set => SetField(ref _indexProgress, value);
        }

        public bool IsIndexing
        {
            get => _isIndexing;
            private set
            {
                if (SetField(ref _isIndexing, value))
                    RelayCommand.RaiseCanExecuteChanged();
            }
        }

        public bool IsSearching
        {
            get => _isSearching;
            private set
            {
                if (SetField(ref _isSearching, value))
                    RelayCommand.RaiseCanExecuteChanged();
            }
        }

        public bool IndexReady
        {
            get => _indexReady;
            private set
            {
                if (SetField(ref _indexReady, value))
                    RelayCommand.RaiseCanExecuteChanged();
            }
        }

        public string ProgressDetail
        {
            get => _progressDetail;
            private set => SetField(ref _progressDetail, value);
        }

        public string ElapsedTime
        {
            get => _elapsedTime;
            private set => SetField(ref _elapsedTime, value);
        }

        public ObservableCollection<SearchResultItem> Results { get; }

        // ── Commands ─────────────────────────────────────────────────────────
        public ICommand BuildIndexCommand  { get; }
        public ICommand SearchCommand      { get; }
        public ICommand CancelIndexCommand { get; }

        // ── Command handlers ─────────────────────────────────────────────────

        private async void OnBuildIndex(object _)
        {
            // Let the user pick the SQLite database file
            var dlg = new OpenFileDialog
            {
                Title  = "בחר קובץ מסד נתונים",
                Filter = "SQLite DB (*.db)|*.db|All files (*.*)|*.*",
            };

            if (dlg.ShowDialog() != true) return;

            string dbPath    = dlg.FileName;
            string indexPath = GetIndexPath(dbPath);

            // Confirm overwrite if an index already exists
            if (Directory.Exists(indexPath) &&
                File.Exists(Path.Combine(indexPath, "postings.dat")))
            {
                var answer = MessageBox.Show(
                    "אינדקס קיים כבר עבור קובץ זה. האם לבנות מחדש?",
                    "בניית אינדקס", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;

                // Clean up old index
                try { Directory.Delete(indexPath, recursive: true); }
                catch { /* ignore — IndexWriter will overwrite */ }
            }

            // Close any open reader before rebuilding
            CloseReader();
            IndexReady = false;
            Results.Clear();

            IsIndexing    = true;
            IndexProgress = 0;
            ProgressDetail = string.Empty;
            ElapsedTime   = string.Empty;
            StatusText    = "בונה אינדקס…";

            _indexStopwatch = System.Diagnostics.Stopwatch.StartNew();
            _elapsedTimer   = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _elapsedTimer.Tick += (s, e) =>
                ElapsedTime = _indexStopwatch.Elapsed.ToString(@"hh\:mm\:ss");
            _elapsedTimer.Start();

            _indexCts = new CancellationTokenSource();
            var ct    = _indexCts.Token;

            // Progress reporter — marshals updates to the UI thread
            var progress = new Progress<(double pct, string detail)>(report =>
            {
                IndexProgress  = report.pct;
                ProgressDetail = report.detail;
            });

            bool success = false;
            try
            {
                await Task.Run(() => BuildIndexCore(dbPath, indexPath, progress, ct), ct);
                success = true;
            }
            catch (OperationCanceledException)
            {
                StatusText = "בניית האינדקס בוטלה";
            }
            catch (Exception ex)
            {
                StatusText = $"שגיאה בבניית האינדקס: {ex.Message}";
                MessageBox.Show($"שגיאה בבניית האינדקס:\n{ex.Message}",
                    "שגיאה", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _elapsedTimer?.Stop();
                _elapsedTimer = null;
                _indexStopwatch?.Stop();
                IsIndexing     = false;
                IndexProgress  = success ? 100 : 0;
                ProgressDetail = string.Empty;
                ElapsedTime    = string.Empty;
                _indexCts?.Dispose();
                _indexCts = null;
            }

            if (success)
            {
                IndexedDbPath = dbPath;
                _settings.IndexedDbPath = dbPath;
                _settings.Save();

                TryOpenExistingIndex();
                StatusText = "האינדקס נבנה בהצלחה";
            }
        }

        private void OnSearch(object _)
        {
            if (_reader == null || string.IsNullOrWhiteSpace(_searchQuery)) return;

            Results.Clear();
            IsSearching = true;
            StatusText  = "מחפש…";

            string query    = _searchQuery.Trim();
            string dbPath   = _indexedDbPath;
            var    reader   = _reader;

            // Run search + DB fetch on a background thread, then populate results on UI thread
            Task.Run(() =>
            {
                var tokenizer = new Tokenizer();
                var terms     = new List<string>(tokenizer.Extract(query));

                if (terms.Count == 0)
                    return (new List<SearchResultItem>(), "אין מילות חיפוש תקינות");

                var ids = new List<int>(reader.Search(terms));

                if (ids.Count == 0)
                    return (new List<SearchResultItem>(), "לא נמצאו תוצאות");

                var rows = FetchResultRows(dbPath, ids);
                return (rows, $"נמצאו {rows.Count:N0} תוצאות");
            })
            .ContinueWith(t =>
            {
                IsSearching = false;

                if (t.IsFaulted)
                {
                    StatusText = $"שגיאה בחיפוש: {t.Exception?.GetBaseException().Message}";
                    return;
                }

                var (rows, status) = t.Result;
                StatusText = status;
                foreach (var row in rows)
                    Results.Add(row);

            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void OnCancelIndex(object _)
        {
            _indexCts?.Cancel();
        }

        // ── Index build (runs on background thread) ───────────────────────────

        private static void BuildIndexCore(
            string dbPath,
            string indexPath,
            IProgress<(double pct, string detail)> progress,
            CancellationToken ct)
        {
            // Count total lines first so we can report accurate progress
            long totalLines = CountLines(dbPath);

            var tokenizer = new Tokenizer();
            long indexed  = 0;

            using (var writer = new IndexWriter(indexPath))
            {
                var connStr = $"Data Source={dbPath};Version=3;Read Only=True;Page Size=4096;Cache Size=100000;Temp Store=Memory;";
                using (var conn = new SQLiteConnection(connStr))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "PRAGMA mmap_size=2147483648; PRAGMA cache_size=100000; PRAGMA temp_store=MEMORY;";
                        cmd.ExecuteNonQuery();
                    }

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT id, content FROM line ORDER BY id";
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                ct.ThrowIfCancellationRequested();

                                int    lineId  = r.GetInt32(0);
                                string content = r.IsDBNull(1) ? string.Empty : r.GetString(1);

                                foreach (var term in tokenizer.Extract(content))
                                    writer.Add(lineId, term);

                                indexed++;

                                // Report progress every 10k lines to avoid flooding the UI
                                if (indexed % 10_000 == 0)
                                {
                                    double pct    = totalLines > 0 ? 100.0 * indexed / totalLines : 0;
                                    string detail = totalLines > 0
                                        ? $"{indexed:N0} / {totalLines:N0}  ({pct:F1}%)"
                                        : $"{indexed:N0} שורות";
                                    progress.Report((pct, detail));
                                }
                            }
                        }
                    }
                }
                // IndexWriter.Dispose() calls Commit() — this is the slow merge step
                // Report indeterminate progress during commit
                progress.Report((99, "מסיים ומשמר אינדקס…"));
            }
        }

        private static long CountLines(string dbPath)
        {
            var connStr = $"Data Source={dbPath};Version=3;Read Only=True;";
            using (var conn = new SQLiteConnection(connStr))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM line";
                    return (long)cmd.ExecuteScalar();
                }
            }
        }

        // ── Result fetch ─────────────────────────────────────────────────────

        private static List<SearchResultItem> FetchResultRows(string dbPath, List<int> ids)
        {
            if (ids.Count == 0) return new List<SearchResultItem>();

            var connStr = $"Data Source={dbPath};Version=3;Read Only=True;Page Size=4096;";
            var rows    = new List<SearchResultItem>(ids.Count);

            using (var conn = new SQLiteConnection(connStr))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        $@"SELECT l.id, l.lineIndex, l.heRef, l.content, b.title
                             FROM line l JOIN book b ON b.id = l.bookId
                            WHERE l.id IN ({string.Join(",", ids)})
                            ORDER BY l.bookId, l.lineIndex";

                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            int    lineId    = r.GetInt32(0);
                            int    lineIndex = r.GetInt32(1);
                            string heRef     = r.IsDBNull(2) ? null : r.GetString(2);
                            string content   = r.IsDBNull(3) ? string.Empty : r.GetString(3);
                            string bookTitle = r.IsDBNull(4) ? string.Empty : r.GetString(4);

                            string reference = heRef ?? $"שורה {lineIndex}";
                            string snippet   = StripHtml(content);

                            rows.Add(new SearchResultItem(lineId, bookTitle, reference, snippet));
                        }
                    }
                }
            }

            return rows;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void TryOpenExistingIndex()
        {
            if (string.IsNullOrEmpty(_indexedDbPath)) return;

            string indexPath = GetIndexPath(_indexedDbPath);
            if (!File.Exists(Path.Combine(indexPath, "postings.dat"))) return;

            try
            {
                CloseReader();
                _reader    = new IndexReader(indexPath);
                IndexReady = true;
                StatusText = $"אינדקס טעון: {Path.GetFileName(_indexedDbPath)}";
            }
            catch (Exception ex)
            {
                StatusText = $"לא ניתן לפתוח אינדקס: {ex.Message}";
                IndexReady = false;
            }
        }

        private void CloseReader()
        {
            _reader?.Dispose();
            _reader = null;
        }

        /// <summary>
        /// Derives a stable index directory path from the DB file path.
        /// Stored next to the DB file in a sibling folder named after the DB.
        /// </summary>
        private static string GetIndexPath(string dbPath)
        {
            string dir  = Path.GetDirectoryName(dbPath) ?? AppDomain.CurrentDomain.BaseDirectory;
            string name = Path.GetFileNameWithoutExtension(dbPath);
            return Path.Combine(dir, name + "-fts-index");
        }

        private static string StripHtml(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb    = new StringBuilder(s.Length);
            bool inTag = false;
            foreach (char c in s)
            {
                if (c == '<') { inTag = true;  continue; }
                if (c == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(c);
            }
            return sb.ToString().Trim();
        }
    }
}
