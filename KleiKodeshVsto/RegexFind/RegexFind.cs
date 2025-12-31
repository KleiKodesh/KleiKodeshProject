using KleiKodesh.Helpers;
using Microsoft.Office.Core;
using Microsoft.Office.Interop.Word;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using System.Web;
using System.Windows.Forms;

namespace KleiKodesh.RegexFind
{
    public enum SearchMode
    {
        All,
        Forward,
        Back,
        Selection
    }

    public class SearchResult
    {
        public Range Range { get; set; }
        public int Start => Range.Start;
        public int End => Range.End;
        public string Before { get; set; }
        public string After { get; set; }
    }

    public class RegexFindBase
    {
        public string Text { get; set; }
        public bool? Bold { get; set; }
        public bool? Italic { get; set; }
        public bool? Underline { get; set; }
        public bool? Superscript { get; set; }
        public bool? Subscript { get; set; }
        public string Style { get; set; }
        public string Font { get; set; }
        public float? FontSize { get; set; }
        public int? TextColor { get; set; }
    }

    public class RegexFind : RegexFindBase
    {
        // Constants
        const short SnippetLength = 150;

        // Properties
        Microsoft.Office.Interop.Word.Application App => Globals.ThisAddIn.Application;
        Document Document => App.ActiveDocument;
        Selection Selection => App.Selection;
        int SelectionStart => Selection.Start;
        Range CurrentRange => GetCurrentRange();
        Match[] Matches => Regex.Matches(CurrentRange.Text, _effectivePattern).Cast<Match>().ToArray();

        // public Properties
        public SearchMode Mode { get; set; } = SearchMode.All;
        public short Slop { get; set; }
        public bool UseWildcards { get; set; } = false;
        public SearchResult[] Results { get; private set; }

        private string _effectivePattern;

        // Public Methods      
        public void Search()
        {
            if (!HasSearchText())
                return;

            _effectivePattern = ComputeEffectivePattern();
            Results = GetResults();
            
            // Select appropriate result based on mode
            if (Results.Length > 0)
            {
                if (Mode == SearchMode.Back)
                    SelectPrevious();
                else
                    SelectNext();
            }
        }

        public void SelectNext()
        {
            if (Results == null || Results.Length == 0)
                return;
            var currentPos = SelectionStart;
            var nextResult = Results.FirstOrDefault(r => r.Start > currentPos);
            if (nextResult == null)
                nextResult = Results.First();
            Selection.SetRange(nextResult.Start, nextResult.End);
        }

        public void SelectPrevious()
        {
            if (Results == null || Results.Length == 0)
                return;
            var currentPos = SelectionStart;
            var previousResult = Results.LastOrDefault(r => r.Start < currentPos);
            if (previousResult == null)
                previousResult = Results.Last();
            Selection.SetRange(previousResult.Start, previousResult.End);
        }

        public void Select(int index)
        {
            if (Results == null || Results.Length == 0)
                return;
            if (index < 0 || index >= Results.Length)
                return;
            var result = Results[index];
            Selection.SetRange(result.Start, result.End);
        }

        public void ReplaceAll()
        {
            if (!HasSearchText())
                return;

            Search();

            if (Results == null || Results.Length == 0)
                return;

            using (_ = new RecordUndo("החלפה"))
                for (int i = Results.Length - 1; i >= 0; i--)
                    ApplyReplace(Results[i].Range);
        }

        public void ReplaceCurrent()
        {
            if (!HasSearchText())
                return;

            _effectivePattern = ComputeEffectivePattern();
            using (_ = new RecordUndo("החלפה"))
                ApplyReplace(Selection.Range);

            // Rerun the search to update results
            Search();
        }

        //need to use dynamic to expose decimal text color prop
        void ApplyReplace(dynamic rng)
        {
            var regex = new Regex(_effectivePattern);
            var match = regex.Match(rng.Text);
            if (!match.Success)
                return;

            rng.Start += match.Index;
            rng.End = rng.Start + match.Length;
            rng.Text = regex.Replace(rng.Text ?? "", Replace.Text);

            // Apply formatting only if specified
            if (Replace.Bold.HasValue) rng.Font.Bold = Replace.Bold.Value ? -1 : 0;
            if (Replace.Italic.HasValue) rng.Font.Italic = Replace.Italic.Value ? -1 : 0;
            if (Replace.Underline.HasValue) rng.Font.Underline = Replace.Underline.Value ? WdUnderline.wdUnderlineSingle : WdUnderline.wdUnderlineNone;
            if (Replace.Superscript.HasValue) rng.Font.Superscript = Replace.Superscript.Value ? -1 : 0;
            if (Replace.Subscript.HasValue) rng.Font.Subscript = Replace.Subscript.Value ? -1 : 0;
            if (!string.IsNullOrEmpty(Replace.Style)) rng.set_Style(Replace.Style);
            if (!string.IsNullOrEmpty(Replace.Font)) rng.Font.Name = Replace.Font;
            if (Replace.FontSize.HasValue) rng.Font.Size = Replace.FontSize.Value;
            if (Replace.TextColor.HasValue) rng.Font.Color = (int)Replace.TextColor;
        }

        bool HasSearchText()
        {
            if (string.IsNullOrEmpty(Text))
            {
                MessageBox.Show("אנא הזן מחרוזת לחיפוש");
                return false;
            }

            return true;
        }


        // Private Methods
        private Range GetCurrentRange()
        {
            var doc = Document;
            var content = doc.Content;
            var start = content.Start;
            var end = content.End;

            if (Mode == SearchMode.Forward)
            {
                start = Selection.End;
            }
            else if (Mode == SearchMode.Back)
            {
                end = Selection.Start;
            }
            else if (Mode == SearchMode.Selection)
            {
                start = Selection.Start;
                end = Selection.End;
            }

            if (start >= end) return doc.Range(end, end);
            return doc.Range(start, end);
        }

        private string ComputeEffectivePattern()
        {
            string pattern;
            if (Slop <= 0)
            {
                pattern = UseWildcards ? WildcardToRegex(Text) : Regex.Escape(Text);
            }
            else
            {
                string[] terms = Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (terms.Length < 2)
                {
                    pattern = UseWildcards ? WildcardToRegex(Text) : Regex.Escape(Text);
                    pattern = @"\b" + pattern + @"\b";
                }
                else
                {
                    var processedTerms = terms.Select(t => UseWildcards ? WildcardToRegex(t) : Regex.Escape(t)).ToArray();
                    string joiner = $@"\b\W+(?:[\w""]+\W+){{0,{Slop}}}";
                    pattern = @"\b" + string.Join(joiner, processedTerms) + @"\b";
                }
            }
            return pattern;
        }

        private string WildcardToRegex(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return "";
            string regex = Regex.Escape(pattern);
            regex = regex.Replace("\\*", ".*").Replace("\\?", ".");
            return regex;
        }

        private SearchResult[] GetResults()
        {
            int startPos = CurrentRange.Start;
            var matches = Matches;
            var list = new List<SearchResult>();
            var docStart = Document.Range().Start;
            var docEnd = Document.Range().End;

            System.Diagnostics.Debug.WriteLine($"GetResults: Found {matches.Length} text matches, applying formatting filters...");
            System.Diagnostics.Debug.WriteLine($"Formatting filters - Bold: {Bold}, Italic: {Italic}, Underline: {Underline}");

            foreach (var m in matches)
            {
                dynamic rng = Document.Range(startPos + m.Index, startPos + m.Index + m.Length);
                bool matchConditions = true;
                
                // Debug each formatting check
                if (Bold.HasValue) 
                {
                    bool isBold = rng.Font.Bold == -1;
                    bool boldMatches = isBold == Bold.Value;
                    System.Diagnostics.Debug.WriteLine($"Bold check: text is bold={isBold}, required={Bold.Value}, matches={boldMatches}");
                    matchConditions &= boldMatches;
                }
                if (Italic.HasValue) 
                {
                    bool isItalic = rng.Font.Italic == -1;
                    bool italicMatches = isItalic == Italic.Value;
                    System.Diagnostics.Debug.WriteLine($"Italic check: text is italic={isItalic}, required={Italic.Value}, matches={italicMatches}");
                    matchConditions &= italicMatches;
                }
                if (Underline.HasValue) 
                {
                    bool isUnderlined = rng.Font.Underline != WdUnderline.wdUnderlineNone;
                    bool underlineMatches = isUnderlined == Underline.Value;
                    System.Diagnostics.Debug.WriteLine($"Underline check: text is underlined={isUnderlined}, required={Underline.Value}, matches={underlineMatches}");
                    matchConditions &= underlineMatches;
                }
                if (Superscript.HasValue) matchConditions &= (rng.Font.Superscript == -1) == Superscript.Value;
                if (Subscript.HasValue) matchConditions &= (rng.Font.Subscript == -1) == Subscript.Value;
                if (!string.IsNullOrEmpty(Style)) matchConditions &= rng.get_Style().NameLocal == Style;
                if (!string.IsNullOrEmpty(Font)) matchConditions &= rng.Font.Name == Font;
                if (FontSize.HasValue) matchConditions &= Math.Abs(rng.Font.Size - FontSize.Value) < 0.01;
                if (TextColor.HasValue) matchConditions &= rng.Font.Color == (int)TextColor.Value;

                System.Diagnostics.Debug.WriteLine($"Match at {m.Index}: '{m.Value}' - conditions met: {matchConditions}");

                if (matchConditions)
                {
                    var r = new SearchResult { Range = rng };

                    // Before snippet
                    Range beforeRange = rng.Duplicate;
                    beforeRange.End = beforeRange.Start;
                    beforeRange.MoveStart(WdUnits.wdCharacter, -SnippetLength);
                    if (beforeRange.Start < docStart) beforeRange.Start = docStart;
                    r.Before = beforeRange.Text;

                    // After snippet
                    Range afterRange = rng.Duplicate;
                    afterRange.Start = afterRange.End;
                    afterRange.MoveEnd(WdUnits.wdCharacter, SnippetLength);
                    if (afterRange.End > docEnd) afterRange.End = docEnd;
                    r.After = afterRange.Text;

                    list.Add(r);
                }
            }

            System.Diagnostics.Debug.WriteLine($"GetResults: {list.Count} matches passed formatting filters");
            return list.OrderBy(r => r.Start).ToArray();
        }

        public RegexFindBase GetSelectionFormatting()
        {
            try
            {
                var selection = Selection;
                if (selection == null || selection.Range == null)
                    return new RegexFindBase();

                var rng = selection.Range;
                return new RegexFindBase
                {
                    Bold = rng.Font.Bold == -1 ? true : (rng.Font.Bold == 0 ? false : (bool?)null),
                    Italic = rng.Font.Italic == -1 ? true : (rng.Font.Italic == 0 ? false : (bool?)null),
                    Underline = rng.Font.Underline != WdUnderline.wdUnderlineNone ? true : false,
                    Superscript = rng.Font.Superscript == -1 ? true : (rng.Font.Superscript == 0 ? false : (bool?)null),
                    Subscript = rng.Font.Subscript == -1 ? true : (rng.Font.Subscript == 0 ? false : (bool?)null),
                    Style = rng.get_Style()?.NameLocal ?? "",
                    Font = rng.Font.Name ?? "",
                    FontSize = rng.Font.Size > 0 ? rng.Font.Size : (float?)null,
                    TextColor = (int)rng.Font.Color
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting selection formatting: {ex.Message}");
                return new RegexFindBase();
            }
        }

        public readonly RegexFindBase Replace = new RegexFindBase();
    }
}