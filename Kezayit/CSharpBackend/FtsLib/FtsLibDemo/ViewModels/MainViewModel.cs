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
        private string _resultCountText = string.Empty;
        private string _currentQuery   = string.Empty;   // query used for the current results
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

            BuildIndexCommand  = new AsyncRelayCommand(OnBuildIndexAsync, () => !_isIndexing && !_isSearching);
            SearchCommand      = new AsyncRelayCommand(OnSearchAsync,     () => !_isSearching && (_indexService.IsReady || _isIndexing) && !string.IsNullOrWhiteSpace(_searchQuery));
            CancelIndexCommand = new RelayCommand(OnCancelIndex,          () => _isIndexing);

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

        // ── Commands ─────────────────────────────────────────────────────────
        public ICommand BuildIndexCommand  { get; }
        public ICommand SearchCommand      { get; }
        public ICommand CancelIndexCommand { get; }

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
            StatusText     = "בונה אינדקס…";
            _liveIndexPath = _indexService.GetIndexPath(dbPath);

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

            var progress = new Progress<(double pct, string detail)>(report =>
            {
                IndexProgress  = report.pct;
                ProgressDetail = report.detail;
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

            Results.Clear();
            ResultCountText = string.Empty;
            IsSearching     = true;
            CurrentQuery    = _searchQuery.Trim();
            StatusText      = isLiveSearch ? "מחפש (בזמן בניית אינדקס)…" : "מחפש…";

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
                var status = await _searchService.SearchStreamingAsync(query, OnBatch, ct, index);
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

        // ── Helpers ──────────────────────────────────────────────────────────

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
