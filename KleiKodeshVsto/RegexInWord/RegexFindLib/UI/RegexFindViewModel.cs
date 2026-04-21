using RegexFindLib.Search;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using WpfLib;
using WpfLib.ViewModels;

namespace RegexFindLib.UI
{
    /// <summary>
    /// ViewModel for the RegexFind task pane — split across partial files:
    ///   RegexFindViewModel.cs          — state, properties, shared statics
    ///   RegexFindViewModel.Commands.cs — command execution (search/replace/copy)
    ///   RegexFindViewModel.Loading.cs  — font/style/history loading
    /// </summary>
    public partial class RegexFindViewModel : ViewModelBase
    {
        readonly RegexSearch _search;
        readonly IWordService _word;

        // ── Shared across all instances ───────────────────────────────────────

        public static readonly ObservableCollection<FontItem> FontList =
            new ObservableCollection<FontItem>();

        static bool _fontsLoaded = false;
        static readonly object _fontLock = new object();

        public static readonly ObservableCollection<string> RecentSearches =
            new ObservableCollection<string>();

        public static readonly ObservableCollection<string> RecentReplacements =
            new ObservableCollection<string>();

        public static readonly IReadOnlyList<string> SearchModes =
            new[] { "הכל", "כלפי מטה", "כלפי מעלה", "לפי בחירה" };

        // ── Constructor ───────────────────────────────────────────────────────

        public RegexFindViewModel(IWordService word)
        {
            _word   = word;
            _search = new RegexSearch(word);
            InitCommands();
            LoadFonts();
        }

        // ── Instance proxies for static collections (WPF binding) ────────────
        public ObservableCollection<FontItem> FontListBinding            => FontList;
        public ObservableCollection<string> RecentSearchesBinding      => RecentSearches;
        public ObservableCollection<string> RecentReplacementsBinding  => RecentReplacements;
        public IReadOnlyList<string>        SearchModesBinding         => SearchModes;

        // ── Search / Replace text ─────────────────────────────────────────────
        string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

        string _replaceText = "";
        public string ReplaceText
        {
            get => _replaceText;
            set => SetProperty(ref _replaceText, value);
        }

        // ── Focus tracking ────────────────────────────────────────────────────
        bool _findFocused = true;
        public bool FindFocused
        {
            get => _findFocused;
            set => SetProperty(ref _findFocused, value);
        }

        // ── Search mode ───────────────────────────────────────────────────────
        int _searchModeIndex = 0;
        public int SearchModeIndex
        {
            get => _searchModeIndex;
            set => SetProperty(ref _searchModeIndex, value);
        }

        RegexSearchMode SelectedMode => (RegexSearchMode)_searchModeIndex;

        // ── Options ───────────────────────────────────────────────────────────
        bool _useRegex = false;
        public bool UseRegex
        {
            get => _useRegex;
            set => SetProperty(ref _useRegex, value);
        }

        int _slop = 0;
        public int Slop
        {
            get => _slop;
            set => SetProperty(ref _slop, value);
        }

        // ── Formatting options ────────────────────────────────────────────────
        public FormattingOptions FindFormatting    { get; } = new FormattingOptions();
        public FormattingOptions ReplaceFormatting { get; } = new FormattingOptions();

        // ── Results ───────────────────────────────────────────────────────────
        public ObservableCollection<SnippetModel> Results { get; } =
            new ObservableCollection<SnippetModel>();

        int _selectedResultIndex = -1;
        public int SelectedResultIndex
        {
            get => _selectedResultIndex;
            set
            {
                if (SetProperty(ref _selectedResultIndex, value) && value >= 0)
                    _search.SelectResultByIndex(value);
            }
        }

        string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        bool _noResults = false;
        public bool NoResults
        {
            get => _noResults;
            set => SetProperty(ref _noResults, value);
        }

        // ── Style list — per-instance (document-specific) ─────────────────────
        public ObservableCollection<string> StyleList { get; } = new ObservableCollection<string>();

        // ── UI state ──────────────────────────────────────────────────────────
        bool _showReplace = false;
        public bool ShowReplace
        {
            get => _showReplace;
            set => SetProperty(ref _showReplace, value);
        }

        bool _showRegexPalette = false;
        public bool ShowRegexPalette
        {
            get => _showRegexPalette;
            set => SetProperty(ref _showRegexPalette, value);
        }

        // ── Commands (declared here, initialized in Commands partial) ─────────
        public ICommand SearchCommand                { get; private set; }
        public ICommand ReplaceCommand               { get; private set; }
        public ICommand ReplaceAllCommand            { get; private set; }
        public ICommand CopyFindFormattingCommand    { get; private set; }
        public ICommand CopyReplaceFormattingCommand { get; private set; }
        public ICommand ClearFindFormattingCommand   { get; private set; }
        public ICommand ClearReplaceFormattingCommand { get; private set; }
        public ICommand ToggleReplaceCommand         { get; private set; }
        public ICommand ToggleRegexPaletteCommand    { get; private set; }
        public ICommand LoadStylesCommand            { get; private set; }
    }
}
