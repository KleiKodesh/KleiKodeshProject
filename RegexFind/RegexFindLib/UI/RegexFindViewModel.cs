using RegexFindLib.Search;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using WpfLib;
using WpfLib.ViewModels;

namespace RegexFindLib.UI
{
    /// <summary>
    /// ViewModel for the RegexFind task pane.
    /// Knows about the model (RegexSearch, IWordService) and exposes bindable
    /// state to the view. Does NOT reference any WPF controls or Vsto/Globals.
    /// </summary>
    public class RegexFindViewModel : ViewModelBase
    {
        readonly RegexSearch _search;
        readonly IWordService _word;

        public RegexFindViewModel(IWordService word)
        {
            _word = word;
            _search = new RegexSearch(word);

            SearchCommand              = new RelayCommand(ExecuteSearch);
            ReplaceCommand             = new RelayCommand(ExecuteReplace);
            ReplaceAllCommand          = new RelayCommand(ExecuteReplaceAll);
            CopyFindFormattingCommand  = new RelayCommand(() => CopyFormatting(FindFormatting));
            CopyReplaceFormattingCommand = new RelayCommand(() => CopyFormatting(ReplaceFormatting));
            ClearFindFormattingCommand = new RelayCommand(() => FindFormatting.Clear());
            ClearReplaceFormattingCommand = new RelayCommand(() => ReplaceFormatting.Clear());
            ToggleReplaceCommand       = new RelayCommand(() => ShowReplace = !ShowReplace);
            ToggleRegexPaletteCommand  = new RelayCommand(() => ShowRegexPalette = !ShowRegexPalette);
            LoadFontsCommand           = new RelayCommand(LoadFonts);
            LoadStylesCommand          = new RelayCommand(LoadStyles);
        }

        // ── Search text ───────────────────────────────────────────────────────
        string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

        // ── Replace text ──────────────────────────────────────────────────────
        string _replaceText = "";
        public string ReplaceText
        {
            get => _replaceText;
            set => SetProperty(ref _replaceText, value);
        }

        // ── Recent search history ─────────────────────────────────────────────
        public ObservableCollection<string> RecentSearches  { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> RecentReplacements { get; } = new ObservableCollection<string>();

        public void LoadRecentSearches()
        {
            RecentSearches.Clear();
            foreach (var s in SearchHistory.Find.Load())    RecentSearches.Add(s);
            RecentReplacements.Clear();
            foreach (var s in SearchHistory.Replace.Load()) RecentReplacements.Add(s);
        }

        public void AddSearchToHistory()
        {
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                SearchHistory.Find.Add(SearchText);
                LoadRecentSearches();
            }
        }

        public void AddReplaceToHistory()
        {
            if (!string.IsNullOrWhiteSpace(ReplaceText))
            {
                SearchHistory.Replace.Add(ReplaceText);
                LoadRecentSearches();
            }
        }

        // ── Which input last had focus (for regex tip insertion) ──────────────
        bool _findFocused = true;
        public bool FindFocused
        {
            get => _findFocused;
            set => SetProperty(ref _findFocused, value);
        }

        // ── Search mode ───────────────────────────────────────────────────────
        // Labels match the HTML exactly
        public IReadOnlyList<string> SearchModes { get; } =
            new[] { "הכל", "כלפי מטה", "כלפי מעלה", "לפי בחירה" };

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

        // ── Font / Style lists ────────────────────────────────────────────────
        public ObservableCollection<string> FontList  { get; } = new ObservableCollection<string>();
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

        // ── Commands ──────────────────────────────────────────────────────────
        public ICommand SearchCommand              { get; }
        public ICommand ReplaceCommand             { get; }
        public ICommand ReplaceAllCommand          { get; }
        public ICommand CopyFindFormattingCommand  { get; }
        public ICommand CopyReplaceFormattingCommand { get; }
        public ICommand ClearFindFormattingCommand { get; }
        public ICommand ClearReplaceFormattingCommand { get; }
        public ICommand ToggleReplaceCommand       { get; }
        public ICommand ToggleRegexPaletteCommand  { get; }
        public ICommand LoadFontsCommand           { get; }
        public ICommand LoadStylesCommand          { get; }

        // ── Execution ─────────────────────────────────────────────────────────

        void ExecuteSearch()
        {
            try
            {
                AddSearchToHistory();
                _search.Execute(BuildFind());
                RefreshResults();
            }
            catch (Exception ex) { StatusText = ex.Message; }
        }

        void ExecuteReplace()
        {
            try
            {
                AddSearchToHistory();
                AddReplaceToHistory();
                _search.Execute(BuildFind(), BuildReplace(), replace: true);
                RefreshResults();
            }
            catch (Exception ex) { StatusText = ex.Message; }
        }

        void ExecuteReplaceAll()
        {
            try
            {
                AddSearchToHistory();
                AddReplaceToHistory();
                _search.Execute(BuildFind(), BuildReplace(), replaceAll: true);
                int count = _search.Results?.Length ?? 0;
                StatusText = $"הוחלפו {count} תוצאות";
                Results.Clear();
                NoResults = false;
            }
            catch (Exception ex) { StatusText = ex.Message; }
        }

        void RefreshResults()
        {
            Results.Clear();
            ShowRegexPalette = false; // hide palette when search runs

            if (_search.Results == null || _search.Results.Length == 0)
            {
                NoResults = true;
                StatusText = "לא נמצאו תוצאות התואמות לחיפוש";
                return;
            }

            NoResults = false;
            foreach (var r in _search.Results)
                Results.Add(new SnippetModel(r.ContextBefore, r.MatchText, r.ContextAfter));

            StatusText = $"נמצאו {_search.Results.Length} תוצאות";
        }

        void CopyFormatting(FormattingOptions target)
        {
            try
            {
                var fmt = _search.GetSelectionFormatting();
                target.Bold        = fmt.Bold;
                target.Italic      = fmt.Italic;
                target.Underline   = fmt.Underline;
                target.Superscript = fmt.Superscript;
                target.Subscript   = fmt.Subscript;
                target.FontName    = fmt.Font ?? "";
                target.FontSize    = fmt.FontSize ?? 0f;
                target.StyleName   = fmt.Style ?? "";
                target.TextColor   = fmt.TextColor.HasValue
                    ? (System.Windows.Media.Color?)WordColors.WordDecimalToColor(fmt.TextColor.Value)
                    : null;
            }
            catch (Exception ex) { StatusText = ex.Message; }
        }

        void LoadFonts()
        {
            if (FontList.Count > 0) return;
            try
            {
                foreach (var name in _word.GetFontNames())
                    FontList.Add(name);
            }
            catch { }
        }

        void LoadStyles()
        {
            StyleList.Clear();
            try
            {
                foreach (var name in _word.GetStyleNames())
                    StyleList.Add(name);
            }
            catch { }
        }

        /// <summary>Loads styles on demand (called when style combobox is focused).</summary>
        public void EnsureStylesLoaded()
        {
            if (StyleList.Count == 0)
                LoadStylesCommand.Execute(null);
        }

        // ── Model builders ────────────────────────────────────────────────────

        RegexFind BuildFind() => new RegexFind
        {
            Text         = SearchText,
            Mode         = SelectedMode,
            Slop         = (short)Slop,
            UseWildcards = UseRegex,
            Bold         = FindFormatting.Bold,
            Italic       = FindFormatting.Italic,
            Underline    = FindFormatting.Underline,
            Superscript  = FindFormatting.Superscript,
            Subscript    = FindFormatting.Subscript,
            Font         = FindFormatting.FontName,
            FontSize     = FindFormatting.FontSize > 0 ? FindFormatting.FontSize : (float?)null,
            Style        = FindFormatting.StyleName,
            TextColor    = FindFormatting.TextColor.HasValue
                ? WordColors.ColorToWordDecimal(FindFormatting.TextColor.Value) : (int?)null
        };

        RegexFindReplace BuildReplace() => new RegexFindReplace
        {
            Text         = ReplaceText,
            Bold         = ReplaceFormatting.Bold,
            Italic       = ReplaceFormatting.Italic,
            Underline    = ReplaceFormatting.Underline,
            Superscript  = ReplaceFormatting.Superscript,
            Subscript    = ReplaceFormatting.Subscript,
            Font         = ReplaceFormatting.FontName,
            FontSize     = ReplaceFormatting.FontSize > 0 ? ReplaceFormatting.FontSize : (float?)null,
            Style        = ReplaceFormatting.StyleName,
            TextColor    = ReplaceFormatting.TextColor.HasValue
                ? WordColors.ColorToWordDecimal(ReplaceFormatting.TextColor.Value) : (int?)null
        };

    }
}
