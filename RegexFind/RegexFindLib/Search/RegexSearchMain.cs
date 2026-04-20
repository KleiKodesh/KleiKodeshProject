using Microsoft.Office.Interop.Word;
using System;
using System.Text.RegularExpressions;

namespace RegexFindLib.Search
{
    public partial class RegexSearch
    {
        readonly IWordService _word;

        public RegexSearch(IWordService word)
        {
            _word = word;
        }

        Application App => _word.Application;
        Document Document => _word.ActiveDocument;
        Selection Selection => _word.Selection;
        int SelectionStart => Selection.Start;

        public void Execute(RegexFind regexFind, RegexFindReplace regexReplace = null,
                            bool replace = false, bool replaceAll = false)
        {
            if (string.IsNullOrEmpty(regexFind?.Text))
                return;

            ComputeEffectivePattern(regexFind);

            if (replace && regexReplace != null)
                Replace(regexFind, regexReplace);

            Results = GetSearchResults(regexFind);
            if (Results == null || Results.Length == 0)
                return;

            if (replaceAll && regexReplace != null)
            {
                ReplaceAll(regexFind, regexReplace);
                return;
            }

            if (regexFind.Mode == RegexSearchMode.Back)
                SelectPreviousResult();
            else
                SelectNextResult();
        }

        void ComputeEffectivePattern(RegexFind find)
        {
            if (!find.UseWildcards)
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

        /// <summary>
        /// Returns the formatting of the current Word selection as a model object.
        /// The ViewModel maps this to FormattingOptions.
        /// </summary>
        public RegexFindBase GetSelectionFormatting()
        {
            try
            {
                var selection = Selection;
                if (selection?.Range == null)
                    return new RegexFindBase();

                dynamic rng = selection.Range;

                bool? bold = (rng.Font.Bold == -1 || rng.Font.BoldBi == -1) ? true
                           : (rng.Font.Bold == 0 && rng.Font.BoldBi == 0) ? false
                           : (bool?)null;

                bool? italic = (rng.Font.Italic == -1 || rng.Font.ItalicBi == -1) ? true
                             : (rng.Font.Italic == 0 && rng.Font.ItalicBi == 0) ? false
                             : (bool?)null;

                float? fontSize = rng.Font.SizeBi > 0 ? (float)rng.Font.SizeBi
                                : rng.Font.Size > 0 ? (float)rng.Font.Size
                                : (float?)null;

                string fontName = !string.IsNullOrEmpty((string)rng.Font.NameBi)
                                ? (string)rng.Font.NameBi
                                : (string)rng.Font.Name ?? "";

                return new RegexFindBase
                {
                    Bold = bold,
                    Italic = italic,
                    Underline = rng.Font.Underline != (int)WdUnderline.wdUnderlineNone,
                    Superscript = rng.Font.Superscript == -1 ? true
                                : rng.Font.Superscript == 0 ? false : (bool?)null,
                    Subscript = rng.Font.Subscript == -1 ? true
                              : rng.Font.Subscript == 0 ? false : (bool?)null,
                    Style = (rng.Style as Style)?.NameLocal ?? "",
                    Font = fontName,
                    FontSize = fontSize,
                    TextColor = (int)rng.Font.Color
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetSelectionFormatting error: {ex.Message}");
                return new RegexFindBase();
            }
        }
    }
}
