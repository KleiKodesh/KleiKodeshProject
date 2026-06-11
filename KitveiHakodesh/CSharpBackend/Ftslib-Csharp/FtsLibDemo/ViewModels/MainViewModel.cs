using FtsLibDemo.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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
        private readonly IIndexService    _indexService;
        private readonly ISearchService   _searchService;

        // ── Backing fields ───────────────────────────────────────────────────
        private string _searchQuery    = string.Empty;
        private string _statusText     = "מוכן";
        private string _indexedDbPath  = string.Empty;
        private double _indexProgress;
        private bool   _isIndexing;
        private bool   _isSearching;
        private string _progressDetail = string.Empty;
        private string _elapsedTime    = string.Empty;
        private string _etaText        = string.Empty;
        private string _resultCountText = string.Empty;
        private string _currentQuery   = string.Empty;
        private int    _maxWordDistance = 10;
        private bool   _isOrderedSearch = false;
        private double _lastReportedPct;                  // last pct received from progress callback
        private System.Diagnostics.Stopwatch _indexStopwatch;
        private System.Windows.Threading.DispatcherTimer _elapsedTimer;
        private CancellationTokenSource _indexCts;
        private CancellationTokenSource _searchCts;
        private string _liveIndexPath  = string.Empty;

        // ── Constructor ──────────────────────────────────────────────────────
        public MainViewModel(
            ISettingsService settings,
            IIndexService    indexService,
            ISearchService   searchService)
        {
            _settings      = settings      ?? throw new ArgumentNullException(nameof(settings));
            _indexService  = indexService  ?? throw new ArgumentNullException(nameof(indexService));
            _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));

            BuildIndexCommand    = new AsyncRelayCommand(OnBuildIndexAsync, () => !_isIndexing && !_isSearching);
            SearchCommand        = new AsyncRelayCommand(OnSearchAsync,     () => !_isSearching && (_indexService.IsReady || _isIndexing) && !string.IsNullOrWhiteSpace(_searchQuery));
            CancelIndexCommand   = new RelayCommand(OnCancelIndex,          () => _isIndexing);
            ShowSyntaxHelpCommand = new RelayCommand(OnShowSyntaxHelp);

            _indexedDbPath = _settings.IndexedDbPath ?? string.Empty;
            TryOpenExistingIndex();
        }

        // ── Public properties ────────────────────────────────────────────────

        /// <summary>Live collection of search results — items are added as they stream in.</summary>
        public ObservableCollection<SearchResultItem> Results { get; } = new ObservableCollection<SearchResultItem>();

        /// <summary>The query string that produced the current Results (used for highlighting).</summary>
        public string CurrentQuery
        {
            get => _currentQuery;
            private set => SetField(ref _currentQuery, value);
        }

        /// <summary>
        /// Maximum allowed word distance between matched query terms.
        /// Results where terms are farther apart than this are filtered out.
        /// </summary>
        public int MaxWordDistance
        {
            get => _maxWordDistance;
            set
            {
                if (value < 0) value = 0;
                SetField(ref _maxWordDistance, value);
            }
        }

        /// <summary>
        /// When true, results are only returned when query terms appear in the
        /// same left-to-right order as the query. False (default) = unordered.
        /// </summary>
        public bool IsOrderedSearch
        {
            get => _isOrderedSearch;
            set => SetField(ref _isOrderedSearch, value);
        }

        /// <summary>E.g. "נמצאו 1,234 תוצאות" — shown above the list.</summary>
        public string ResultCountText
        {
            get => _resultCountText;
            private set => SetField(ref _resultCountText, value);
        }

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetField(ref _searchQuery, value))
                    AsyncRelayCommand.RaiseCanExecuteChanged();
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

        public bool IndexReady => _indexService.IsReady;

        public bool IsIndexing
        {
            get => _isIndexing;
            private set
            {
                if (SetField(ref _isIndexing, value))
                    AsyncRelayCommand.RaiseCanExecuteChanged();
            }
        }

        public bool IsSearching
        {
            get => _isSearching;
            private set
            {
                if (SetField(ref _isSearching, value))
                    AsyncRelayCommand.RaiseCanExecuteChanged();
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

        public string EtaText
        {
            get => _etaText;
            private set => SetField(ref _etaText, value);
        }

        // ── Commands ─────────────────────────────────────────────────────────
        public ICommand BuildIndexCommand    { get; }
        public ICommand SearchCommand        { get; }
        public ICommand CancelIndexCommand   { get; }
        public ICommand ShowSyntaxHelpCommand { get; }

        // ── Command handlers ─────────────────────────────────────────────────

        private async Task OnBuildIndexAsync()
        {
            var dlg = new OpenFileDialog
            {
                Title  = "בחר קובץ מסד נתונים",
                Filter = "SQLite DB (*.db)|*.db|All files (*.*)|*.*",
            };

            if (dlg.ShowDialog() != true) return;

            string dbPath    = dlg.FileName;
            string indexPath = _indexService.GetIndexPath(dbPath);

            if (_indexService.IndexExists(dbPath))
            {
                var answer = MessageBox.Show(
                    "אינדקס קיים כבר עבור קובץ זה. האם לבנות מחדש?",
                    "בניית אינדקס", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;

                try { Directory.Delete(indexPath, recursive: true); }
                catch { /* ignore */ }
            }

            _indexService.Close();
            OnPropertyChanged(nameof(IndexReady));
            Results.Clear();
            ResultCountText = string.Empty;

            IsIndexing     = true;
            IndexProgress  = 0;
            ProgressDetail = string.Empty;
            ElapsedTime    = string.Empty;
            EtaText        = string.Empty;
            StatusText     = "בונה אינדקס…";
            _liveIndexPath    = _indexService.GetIndexPath(dbPath);
            _lastReportedPct  = 0;

            _indexStopwatch = System.Diagnostics.Stopwatch.StartNew();
            _elapsedTimer   = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _elapsedTimer.Tick += (s, e) =>
            {
                ElapsedTime = _indexStopwatch.Elapsed.ToString(@"hh\:mm\:ss");
                UpdateEta();
            };
            _elapsedTimer.Start();

            _indexCts = new CancellationTokenSource();
            var ct    = _indexCts.Token;

            var progress = new Progress<(double pct, string detail)>(report =>
            {
                IndexProgress    = report.pct;
                ProgressDetail   = report.detail;
                _lastReportedPct = report.pct;
                AsyncRelayCommand.RaiseCanExecuteChanged();
            });

            bool success = false;
            try
            {
                await _indexService.BuildAsync(dbPath, progress, ct);
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
                EtaText        = string.Empty;
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

                // If the user already ran a search during the build (live search),
                // automatically resume it now against the complete index so they
                // get the full result set without having to search again manually.
                if (!string.IsNullOrEmpty(_currentQuery) && Results.Count > 0)
                {
                    _searchQuery = _currentQuery;
                    OnPropertyChanged(nameof(SearchQuery));
                    await OnSearchAsync();
                }
            }
        }

        private async Task OnSearchAsync()
        {
            // Cancel any previous search still streaming
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            bool isLiveSearch = _isIndexing;
            var index = isLiveSearch
                ? _indexService.GetLiveIndex(_liveIndexPath, _indexedDbPath)
                : _indexService.Index;

            if (index == null || string.IsNullOrWhiteSpace(_searchQuery)) return;

            // Resume: if the query is the same as the current results and we are
            // resuming after a live-index search was interrupted, skip already-shown results.
            bool isSameQuery = string.Equals(_searchQuery.Trim(), _currentQuery, StringComparison.Ordinal);
            int skipCount = (isSameQuery && Results.Count > 0) ? Results.Count : 0;

            if (skipCount == 0)
            {
                Results.Clear();
                ResultCountText = string.Empty;
            }

            IsSearching  = true;
            CurrentQuery = _searchQuery.Trim();
            StatusText   = isLiveSearch ? "מחפש (בזמן בניית אינדקס)…" : "מחפש…";

            string query = CurrentQuery;

            // Dispatcher used to marshal batches onto the UI thread
            var dispatcher = System.Windows.Application.Current.Dispatcher;

            void OnBatch(IReadOnlyList<SearchResultItem> batch)
            {
                if (ct.IsCancellationRequested) return;
                dispatcher.BeginInvoke(new Action(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    foreach (var item in batch)
                        Results.Add(item);
                    ResultCountText = $"נמצאו {Results.Count:N0} תוצאות";
                }));
            }

            try
            {
                var status = await _searchService.SearchStreamingAsync(
                    query, OnBatch, ct, index, _maxWordDistance, _isOrderedSearch, skipCount);
                if (!ct.IsCancellationRequested)
                {
                    StatusText = status;
                    if (Results.Count == 0)
                        ResultCountText = status;
                }
            }
            catch (OperationCanceledException)
            {
                StatusText = "החיפוש בוטל";
            }
            catch (Exception ex)
            {
                StatusText      = $"שגיאה בחיפוש: {ex.Message}";
                ResultCountText = StatusText;
            }
            finally
            {
                IsSearching = false;
            }
        }

        private void OnCancelIndex()
        {
            _indexCts?.Cancel();
        }

        private void OnShowSyntaxHelp()
        {
            var win = new SearchHelpWindow
            {
                Owner = Application.Current.MainWindow
            };
            win.ShowDialog();
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Called every second from the elapsed timer.
        /// Uses actual elapsed time + current progress percentage to compute ETA.
        /// Falls back to the baseline estimate (25 min / 5.5M lines) until enough
        /// progress has accumulated for a reliable dynamic estimate.
        /// </summary>
        private void UpdateEta()
        {
            const double BaselineMinutes = 25.0;   // empirical: ~25 min for 5.5M lines
            const double MinPctForDynamic = 1.0;   // need at least 1% before trusting dynamic ETA

            double pct = _lastReportedPct;

            if (pct <= 0 || _indexStopwatch == null)
            {
                // Nothing indexed yet — show baseline estimate
                EtaText = $"~{BaselineMinutes:F0} דק׳";
                return;
            }

            double elapsedSec = _indexStopwatch.Elapsed.TotalSeconds;

            TimeSpan remaining;
            if (pct >= MinPctForDynamic)
            {
                // Dynamic: extrapolate from actual rate
                double totalEstSec = elapsedSec / (pct / 100.0);
                double remainSec   = totalEstSec - elapsedSec;
                remaining = TimeSpan.FromSeconds(Math.Max(0, remainSec));
            }
            else
            {
                // Too early for dynamic — use baseline minus elapsed
                double baselineSec = BaselineMinutes * 60.0;
                remaining = TimeSpan.FromSeconds(Math.Max(0, baselineSec - elapsedSec));
            }

            EtaText = remaining.TotalHours >= 1
                ? $"~{remaining:hh\\:mm\\:ss}"
                : $"~{remaining:mm\\:ss}";
        }

        private void TryOpenExistingIndex()
        {
            if (string.IsNullOrEmpty(_indexedDbPath)) return;
            if (!_indexService.IndexExists(_indexedDbPath)) return;

            try
            {
                _indexService.Open(_indexedDbPath);
                OnPropertyChanged(nameof(IndexReady));
                AsyncRelayCommand.RaiseCanExecuteChanged();
                StatusText = $"אינדקס טעון: {Path.GetFileName(_indexedDbPath)}";
            }
            catch (Exception ex)
            {
                StatusText = $"לא ניתן לפתוח אינדקס: {ex.Message}";
            }
        }
    }
}
