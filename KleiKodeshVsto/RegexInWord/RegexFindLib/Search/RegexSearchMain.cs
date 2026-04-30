using Microsoft.Office.Interop.Word;
using System;
using System.Text.RegularExpressions;

namespace RegexFindLib.Search
{
    /// <summary>
    /// Custom .NET-regex search engine. Implements ISearchEngine.
    /// Split across partial files:
    ///   RegexSearchMain.cs    — Execute, pattern building, GetSelectionFormatting
    ///   RegexSearchFind.cs    — result collection, range helpers, navigation
    ///   RegexSearchReplace.cs — replace / replace-all
    /// </summary>
    public partial class RegexSearch : ISearchEngine
    {
        readonly IWordService _word;

        public RegexSearch(IWordService word)
        {
            _word = word;
        }

        Application App       => _word.Application;
        Document    Document  => _word.ActiveDocument;
        Selection   Selection => _word.Selection;
        int SelectionStart    => Selection.Start;

        // ── ISearchEngine.Execute ─────────────────────────────────────────────

        public void Execute(FindRequest request, bool replace = false, bool replaceAll = false)
        {
            if (string.IsNullOrEmpty(request?.Text))
                return;

            // Work on a copy so we don't mutate the caller's object
            var find = CloneRequest(request);
            ComputeEffectivePattern(find);

            if (replace)
                Replace(find);

            Results = GetSearchResults(find);
            if (Results == null || Results.Length == 0)
                return;

            if (replaceAll)
            {
                ReplaceAll(find);
                return;
            }

            if (!find.Forward)
                SelectPreviousResult();
            else
                SelectNextResult();
        }

        // ── Pattern building ──────────────────────────────────────────────────

        void ComputeEffectivePattern(FindRequest find)
        {
            if (!find.MatchWildcards)
                find.Text = Regex.Escape(find.Text);

            if (find.Slop > 0)
            {
                string[] terms = find.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (terms.Length > 1)
                {
                    string joiner = $@"\b\W+(?:[\w""]+\W+){{0,{find.Slop}}}";
                    find.Text = string.Join(joiner, terms);
                }
            }
        }

        static FindRequest CloneRequest(FindRequest src) => new FindRequest
        {
            Text           = src.Text,
            MatchWildcards = src.MatchWildcards,
            Forward        = src.Forward,
            IsDirectional  = src.IsDirectional,
            Scope          = src.Scope,
            Slop           = src.Slop,
            Formatting     = src.Formatting,
            Replacement    = src.Replacement
        };

        // ── ISearchEngine.GetSelectionFormatting ──────────────────────────────

        public FindFormatting GetSelectionFormatting()
        {
            try
            {
                var selection = Selection;
                if (selection?.Range == null)
                    return new FindFormatting();

                dynamic rng = selection.Range;

                bool? bold = (rng.Font.Bold == -1 || rng.Font.BoldBi == -1) ? true
                           : (rng.Font.Bold == 0  && rng.Font.BoldBi == 0)  ? false
                           : (bool?)null;

                bool? italic = (rng.Font.Italic == -1 || rng.Font.ItalicBi == -1) ? true
                             : (rng.Font.Italic == 0  && rng.Font.ItalicBi == 0)  ? false
                             : (bool?)null;

                float? fontSize = rng.Font.SizeBi > 0 ? (float)rng.Font.SizeBi
                                : rng.Font.Size   > 0 ? (float)rng.Font.Size
                                : (float?)null;

                string fontName = !string.IsNullOrEmpty((string)rng.Font.NameBi)
                                ? (string)rng.Font.NameBi
                                : (string)rng.Font.Name ?? "";

                return new FindFormatting
                {
                    Bold        = bold,
                    Italic      = italic,
                    Underline   = rng.Font.Underline != (int)WdUnderline.wdUnderlineNone ? true : (bool?)false,
                    Superscript = rng.Font.Superscript == -1 ? true
                                : rng.Font.Superscript == 0  ? false : (bool?)null,
                    Subscript   = rng.Font.Subscript == -1 ? true
                                : rng.Font.Subscript == 0   ? false : (bool?)null,
                    Style       = (rng.Style as Style)?.NameLocal ?? "",
                    FontName    = fontName,
                    FontSize    = fontSize,
                    TextColor   = (int)rng.Font.Color
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetSelectionFormatting error: {ex.Message}");
                return new FindFormatting();
            }
        }
    }
}
