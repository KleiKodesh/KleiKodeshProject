using KleiKodesh.Helpers;
using Microsoft.Office.Core;
using Microsoft.Office.Interop.Word;
using System;
using System.Collections.Generic;
using System.Linq;
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
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Underline { get; set; }
        public bool Superscript { get; set; }
        public bool Subscript { get; set; }
        public string Style { get; set; }
        public string Font { get; set; }
        public float? FontSize { get; set; }
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
            if (string.IsNullOrEmpty(Text))
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
            if (string.IsNullOrEmpty(Text))
            {
                MessageBox.Show("אנא הזן מחרוזת לחיפוש");
                return;
            }

            if (Results == null || Results.Length == 0)
                return;

            using (_ = new RecordUndo("החלפה"))
            {
                for (int i = Results.Length - 1; i >= 0; i--)
                {
                    var result = Results[i];
                    var rng = result.Range;
                    string replacementText = Regex.Replace(rng.Text, _effectivePattern, Replace.Text);
                    rng.Text = replacementText;
                    // Apply formatting
                    if (Replace.Bold) rng.Font.Bold = 1; else rng.Font.Bold = 0;
                    if (Replace.Italic) rng.Font.Italic = 1; else rng.Font.Italic = 0;
                    if (Replace.Underline) rng.Font.Underline = WdUnderline.wdUnderlineSingle; else rng.Font.Underline = WdUnderline.wdUnderlineNone;
                    if (Replace.Superscript) rng.Font.Superscript = 1; else rng.Font.Superscript = 0;
                    if (Replace.Subscript) rng.Font.Subscript = 1; else rng.Font.Subscript = 0;
                    if (!string.IsNullOrEmpty(Replace.Style)) rng.set_Style(Replace.Style);
                    if (!string.IsNullOrEmpty(Replace.Font)) rng.Font.Name = Replace.Font;
                    if (Replace.FontSize.HasValue) rng.Font.Size = Replace.FontSize.Value;
                }
            }
        }

        public void ReplaceCurrent()
        {
            if (string.IsNullOrEmpty(Text))
            {
                MessageBox.Show("אנא הזן מחרוזת לחיפוש");
                return;
            }

            
            using (_ = new RecordUndo("החלפה"))
            {
                try
                {
                    var regex = new Regex(_effectivePattern);
                    var rng = Selection.Range;
                    var match = regex.Match(rng.Text);
                    if (!match.Success)
                        return;

                    rng.Start += match.Index;
                    rng.End = rng.Start + match.Length;
                    rng.Text = regex.Replace(rng.Text ?? "", Replace.Text);

                    // Apply formatting
                    if (Replace.Bold) rng.Font.Bold = 1; else rng.Font.Bold = 0;
                    if (Replace.Italic) rng.Font.Italic = 1; else rng.Font.Italic = 0;
                    if (Replace.Underline) rng.Font.Underline = WdUnderline.wdUnderlineSingle; else rng.Font.Underline = WdUnderline.wdUnderlineNone;
                    if (Replace.Superscript) rng.Font.Superscript = 1; else rng.Font.Superscript = 0;
                    if (Replace.Subscript) rng.Font.Subscript = 1; else rng.Font.Subscript = 0;
                    if (!string.IsNullOrEmpty(Replace.Style)) rng.set_Style(Replace.Style);
                    if (!string.IsNullOrEmpty(Replace.Font)) rng.Font.Name = Replace.Font;
                    if (Replace.FontSize.HasValue) rng.Font.Size = Replace.FontSize.Value;
                }
                catch { }
            }

            // Rerun the search to update results
            Search();
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

            foreach (var m in matches)
            {
                Range rng = Document.Range(startPos + m.Index, startPos + m.Index + m.Length);
                bool matchConditions = true;
                if (Bold) matchConditions &= rng.Font.Bold == 1;
                if (Italic) matchConditions &= rng.Font.Italic == 1;
                if (Underline) matchConditions &= rng.Font.Underline != WdUnderline.wdUnderlineNone;
                if (Superscript) matchConditions &= rng.Font.Superscript == 1;
                if (Subscript) matchConditions &= rng.Font.Subscript == 1;
                if (!string.IsNullOrEmpty(Style)) matchConditions &= rng.get_Style().NameLocal == Style;
                if (!string.IsNullOrEmpty(Font)) matchConditions &= rng.Font.Name == Font;
                if (FontSize.HasValue) matchConditions &= Math.Abs(rng.Font.Size - FontSize.Value) < 0.01;

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

            return list.OrderBy(r => r.Start).ToArray();
        }

        public readonly RegexFindBase Replace = new RegexFindBase();
    }
}