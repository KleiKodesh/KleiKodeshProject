using RegexFindLib.Search;
using System;
using System.Diagnostics;
using System.Windows.Media;
using WpfLib.ViewModels;

namespace RegexFindLib.UI
{
    public partial class RegexFindViewModel
    {
        void InitCommands()
        {
            SearchCommand                = new RelayCommand(ExecuteSearch);
            ReplaceCommand               = new RelayCommand(ExecuteReplace);
            ReplaceAllCommand            = new RelayCommand(ExecuteReplaceAll);
            CopyFindFormattingCommand    = new RelayCommand(() => CopyFormatting(FindFormatting));
            CopyReplaceFormattingCommand = new RelayCommand(() => CopyFormatting(ReplaceFormatting));
            ClearFindFormattingCommand   = new RelayCommand(() => FindFormatting.Clear());
            ClearReplaceFormattingCommand = new RelayCommand(() => ReplaceFormatting.Clear());
            ToggleReplaceCommand         = new RelayCommand(ToggleReplace);
            ToggleRegexPaletteCommand    = new RelayCommand(() => ShowRegexPalette = !ShowRegexPalette);
            LoadStylesCommand            = new RelayCommand(LoadStyles);
        }

        void ToggleReplace()
        {
            ShowReplace = !ShowReplace;
            // Slop is meaningless in replace mode — zero it out when replace opens
            if (ShowReplace)
                Slop = 0;
        }

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
            ShowRegexPalette = false;

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
                if (fmt.TextColor.HasValue)
                    target.SetTextColorFromWord(fmt.TextColor.Value);
                else
                    target.TextColor = null;

                // DEBUG — eyedropper color
                Debug.WriteLine($"[Eyedropper] Raw Word decimal: {fmt.TextColor?.ToString() ?? "null"} (0x{((uint)(fmt.TextColor ?? 0)):X8})");
                if (target.TextColor.HasValue)
                    Debug.WriteLine($"[Eyedropper] WPF Color: {target.TextColor.Value} → WordDecimal stored: {target.TextColorWordDecimal}");
                else
                    Debug.WriteLine("[Eyedropper] TextColor: null (no color)");
            }
            catch (Exception ex) { StatusText = ex.Message; }
        }

        RegexFind BuildFind()
        {
            var textColor = FindFormatting.TextColorWordDecimal;

            // DEBUG — color picker selection
            Debug.WriteLine($"[BuildFind] FindFormatting.TextColor: {FindFormatting.TextColor?.ToString() ?? "null"} → WordDecimal: {textColor?.ToString() ?? "null"}");

            return new RegexFind
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
                TextColor    = textColor
            };
        }

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
            TextColor    = ReplaceFormatting.TextColorWordDecimal
        };
    }
}
