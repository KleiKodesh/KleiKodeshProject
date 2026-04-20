using Microsoft.Office.Interop.Word;
using RegexFindLib.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RegexFindLib.Search
{
    public partial class RegexSearch
    {
        const short SnippetLength = 75;
        readonly StringBuilder _snippetBuilder = new StringBuilder();
        public SearchResult[] Results { get; private set; }

        private SearchResult[] GetSearchResults(RegexFind find)
        {
            Range searchRange = GetSearchRange(find.Mode);
            int startPos = searchRange.Start;

            var matches = Regex.Matches(searchRange.Text, find.Text, RegexOptions.Multiline)
                               .Cast<Match>().ToArray();

            var list = new List<SearchResult>();
            foreach (var match in matches)
            {
                Range matchRange = Document.Range(startPos + match.Index, startPos + match.Index + match.Length);
                if (MatchedConditions(match, find, matchRange))
                    list.Add(new SearchResult { Range = matchRange, Snippet = GenerateSnippet(matchRange) });
            }

            return list.ToArray();
        }

        private Range GetSearchRange(RegexSearchMode mode)
        {
            if (mode == RegexSearchMode.Selection)
                return Selection.Range.Duplicate;

            Range actionRange;
            using (_ = new WdActionManager(true, true, false))
            {
                Selection.WholeStory();
                actionRange = Selection.Range.Duplicate;
            }

            if (mode == RegexSearchMode.Forward)
                actionRange.Start = Selection.Start;
            else if (mode == RegexSearchMode.Back)
                actionRange.End = Selection.End;

            return actionRange;
        }

        bool MatchedConditions(Match match, RegexFind find, dynamic range)
        {
            bool matchesBold = !find.Bold.HasValue ||
                (find.Bold.Value ? (range.Font.Bold == -1 || range.Font.BoldBi == -1)
                                 : (range.Font.Bold == 0 && range.Font.BoldBi == 0));

            bool matchesItalic = !find.Italic.HasValue ||
                (find.Italic.Value ? (range.Font.Italic == -1 || range.Font.ItalicBi == -1)
                                   : (range.Font.Italic == 0 && range.Font.ItalicBi == 0));

            bool matchesUnderline = !find.Underline.HasValue ||
                (find.Underline.Value ? (range.Font.Underline != (int)WdUnderline.wdUnderlineNone)
                                      : (range.Font.Underline == (int)WdUnderline.wdUnderlineNone));

            bool matchesSuperscript = !find.Superscript.HasValue ||
                (find.Superscript.Value ? (range.Font.Superscript == -1) : (range.Font.Superscript == 0));

            bool matchesSubscript = !find.Subscript.HasValue ||
                (find.Subscript.Value ? (range.Font.Subscript == -1) : (range.Font.Subscript == 0));

            bool matchesFontSize = !find.FontSize.HasValue ||
                (Math.Abs(range.Font.Size - find.FontSize.Value) < 0.01 ||
                 Math.Abs(range.Font.SizeBi - find.FontSize.Value) < 0.01);

            bool matchesFont = string.IsNullOrEmpty(find.Font) ||
                (range.Font.Name == find.Font || range.Font.NameBi == find.Font);

            bool matchesStyle = string.IsNullOrEmpty(find.Style) ||
                (range.Style as Style)?.NameLocal == find.Style;

            bool matchesColor = !find.TextColor.HasValue ||
                range.Font.Color == (int)find.TextColor.Value;

            return matchesBold && matchesItalic && matchesUnderline &&
                   matchesSuperscript && matchesSubscript && matchesFontSize &&
                   matchesFont && matchesStyle && matchesColor;
        }

        string GenerateSnippet(Range range)
        {
            _snippetBuilder.Clear();

            Range actionRange = range.Duplicate;
            int start = actionRange.Start;
            int end = actionRange.End;

            _snippetBuilder.Append($"<b>{actionRange.Text}</b>");

            actionRange.End = start;
            actionRange.MoveStart(WdUnits.wdCharacter, -SnippetLength);
            _snippetBuilder.Insert(0, actionRange.Text);

            actionRange.SetRange(end, end);
            actionRange.MoveEnd(WdUnits.wdCharacter, SnippetLength);
            _snippetBuilder.Append(actionRange.Text);

            return _snippetBuilder.ToString();
        }

        public void SelectNextResult()
        {
            if (Results == null || Results.Length == 0) return;
            var currentPos = SelectionStart;
            var next = Results.FirstOrDefault(r => r.Start > currentPos) ?? Results.First();
            Selection.SetRange(next.Start, next.End);
        }

        public void SelectPreviousResult()
        {
            if (Results == null || Results.Length == 0) return;
            var currentPos = SelectionStart;
            var prev = Results.LastOrDefault(r => r.Start < currentPos) ?? Results.Last();
            Selection.SetRange(prev.Start, prev.End);
        }

        public void SelectResultByIndex(int index)
        {
            if (Results == null || index < 0 || index >= Results.Length) return;
            var result = Results[index];
            Selection.SetRange(result.Start, result.End);
        }
    }
}
