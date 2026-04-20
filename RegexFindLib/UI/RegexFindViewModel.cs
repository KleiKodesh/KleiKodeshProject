using RegexFindLib.Search;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Input;
using WpfLib;
using WpfLib.ViewModels;

namespace RegexFindLib.UI
{
    public class RegexFindViewModel : ViewModelBase
    {
        readonly RegexSearch _search = new RegexSearch();

        // ── Search text ──────────────────────────────────────────────────────
        string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

        // ── Replace text ─────────────────────────────────────────────────────
        string _replaceText = "";
        public string ReplaceText
        {
            get => _replaceText;
            set => SetProperty(ref _replaceText, value);
        }

        // ── Search mode ───────────────────────────────────────────────────────
        public IReadOnlyList<string> SearchModes { get; } =
            new[] { "הכל", "קדימה", "אחורה", "בחירה" };

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

        // ── Find formatting ───────────────────────────────────────────────────
        public FormattingOptions FindFormatting { get; } = new FormattingOptions();

        // ── Replace formatting ────────────────────────────────────────────────
        public FormattingOptions ReplaceFormatting { get; } = new FormattingOptions();

        // ── Results ───────────────────────────────────────────────────────────
        public ObservableCollection<string> Results { get; } = new ObservableCollection<string>();

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

        // ── Font / Style lists ────────────────────────────────────────────────
        public ObservableCollection<string> FontList { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> StyleList { get; } = new ObservableCollection<string>();

        // ── Show/hide replace panel ───────────────────────────────────────────
        bool _showReplace = false;
        public bool ShowReplace
        {
            get => _showReplace;
            set => SetProperty(ref _showReplace, value);
        }

        // ── Show/hide formatting panels ───────────────────────────────────────
        bool _showRegexPalette = false;
        public bool ShowRegexPalette
        {
            get => _showRegexPalette;
            set => SetProperty(ref _showRegexPalette, value);
        }

        // ── Commands ──────────────────────────────────────────────────────────
        public ICommand SearchCommand { get; }
        public ICommand ReplaceCommand { get; }
        public ICommand ReplaceAllCommand { get; }
        public ICommand CopyFindFormattingCommand { get; }
        public ICommand CopyReplaceFormattingCommand { get; }
        public ICommand ClearFindFormattingCommand { get; }
        public ICommand ClearReplaceFormattingCommand { get; }
        public ICommand ToggleReplaceCommand { get; }
        public ICommand ToggleRegexPaletteCommand { get; }
        public ICommand LoadFontsCommand { get; }
        public ICommand LoadStylesCommand { get; }

        public RegexFindViewModel()
        {
            SearchCommand = new RelayCommand(ExecuteSearch);
            ReplaceCommand = new RelayCommand(ExecuteReplace);
            ReplaceAllCommand = new RelayCommand(ExecuteReplaceAll);
            CopyFindFormattingCommand = new RelayCommand(() => CopyFormatting(FindFormatting));
            CopyReplaceFormattingCommand = new RelayCommand(() => CopyFormatting(ReplaceFormatting));
            ClearFindFormattingCommand = new RelayCommand(() => FindFormatting.Clear());
            ClearReplaceFormattingCommand = new RelayCommand(() => ReplaceFormatting.Clear());
            ToggleReplaceCommand = new RelayCommand(() => ShowReplace = !ShowReplace);
            ToggleRegexPaletteCommand = new RelayCommand(() => ShowRegexPalette = !ShowRegexPalette);
            LoadFontsCommand = new RelayCommand(LoadFonts);
            LoadStylesCommand = new RelayCommand(LoadStyles);
        }

        // ── Execution ─────────────────────────────────────────────────────────

        void ExecuteSearch()
        {
            try
            {
                var find = BuildFind();
                _search.Execute(find);
                RefreshResults();
            }
            catch (Exception ex)
            {
                StatusText = ex.Message;
            }
        }

        void ExecuteReplace()
        {
            try
            {
                var find = BuildFind();
                var replace = BuildReplace();
                _search.Execute(find, replace, replace: true);
                RefreshResults();
            }
            catch (Exception ex)
            {
                StatusText = ex.Message;
            }
        }

        void ExecuteReplaceAll()
        {
            try
            {
                var find = BuildFind();
                var replace = BuildReplace();
                _search.Execute(find, replace, replaceAll: true);
                StatusText = $"הוחלפו {_search.Results?.Length ?? 0} תוצאות";
                Results.Clear();
            }
            catch (Exception ex)
            {
                StatusText = ex.Message;
            }
        }

        void RefreshResults()
        {
            Results.Clear();
            if (_search.Results == null || _search.Results.Length == 0)
            {
                StatusText = "לא נמצאו תוצאות";
                return;
            }

            foreach (var r in _search.Results)
                Results.Add(r.Snippet);

            StatusText = $"נמצאו {_search.Results.Length} תוצאות";
        }

        void CopyFormatting(FormattingOptions target)
        {
            try
            {
                var fmt = _search.GetSelectionFormatting();
                target.Bold = fmt.Bold;
                target.Italic = fmt.Italic;
                target.Underline = fmt.Underline;
                target.Superscript = fmt.Superscript;
                target.Subscript = fmt.Subscript;
                target.FontName = fmt.Font ?? "";
                target.FontSize = fmt.FontSize?.ToString() ?? "";
                target.StyleName = fmt.Style ?? "";
                target.TextColor = fmt.TextColor.HasValue
                    ? (System.Windows.Media.Color?)WordColorToHex(fmt.TextColor.Value)
                    : (System.Windows.Media.Color?)null;
            }
            catch (Exception ex)
            {
                StatusText = ex.Message;
            }
        }

        void LoadFonts()
        {
            if (FontList.Count > 0) return;
            try
            {
                using (var col = new InstalledFontCollection())
                    foreach (var f in col.Families)
                        FontList.Add(f.Name);
            }
            catch { }
        }

        void LoadStyles()
        {
            StyleList.Clear();
            try
            {
                var doc = Helpers.Vsto.ActiveDocument;
                if (doc == null) return;
                foreach (Microsoft.Office.Interop.Word.Style s in doc.Styles)
                    try { if (s.InUse) StyleList.Add(s.NameLocal); } catch { }
            }
            catch { }
        }

        // ── Builders ──────────────────────────────────────────────────────────

        RegexFind BuildFind() => new RegexFind
        {
            Text = SearchText,
            Mode = SelectedMode,
            Slop = (short)Slop,
            UseWildcards = UseRegex,
            Bold = FindFormatting.Bold,
            Italic = FindFormatting.Italic,
            Underline = FindFormatting.Underline,
            Superscript = FindFormatting.Superscript,
            Subscript = FindFormatting.Subscript,
            Font = FindFormatting.FontName,
            FontSize = TryParseFloat(FindFormatting.FontSize),
            Style = FindFormatting.StyleName,
            TextColor = FindFormatting.TextColor.HasValue
                ? HexToWordColor(FindFormatting.TextColor.Value) : (int?)null
        };

        RegexFindReplace BuildReplace() => new RegexFindReplace
        {
            Text = ReplaceText,
            Bold = ReplaceFormatting.Bold,
            Italic = ReplaceFormatting.Italic,
            Underline = ReplaceFormatting.Underline,
            Superscript = ReplaceFormatting.Superscript,
            Subscript = ReplaceFormatting.Subscript,
            Font = ReplaceFormatting.FontName,
            FontSize = TryParseFloat(ReplaceFormatting.FontSize),
            Style = ReplaceFormatting.StyleName,
            TextColor = ReplaceFormatting.TextColor.HasValue
                ? HexToWordColor(ReplaceFormatting.TextColor.Value) : (int?)null
        };

        // ── Color helpers ─────────────────────────────────────────────────────

        /// <summary>Word stores RGB as BGR int. Convert to WPF Color.</summary>
        static System.Windows.Media.Color WordColorToHex(int wordColor)
        {
            byte r = (byte)(wordColor & 0xFF);
            byte g = (byte)((wordColor >> 8) & 0xFF);
            byte b = (byte)((wordColor >> 16) & 0xFF);
            return System.Windows.Media.Color.FromRgb(r, g, b);
        }

        /// <summary>WPF Color back to Word BGR int.</summary>
        static int HexToWordColor(System.Windows.Media.Color c) =>
            c.R | (c.G << 8) | (c.B << 16);

        static float? TryParseFloat(string s) =>
            float.TryParse(s, out float v) ? v : (float?)null;
    }
}
