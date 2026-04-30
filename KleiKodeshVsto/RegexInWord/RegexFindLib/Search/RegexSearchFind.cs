using Microsoft.Office.Interop.Word;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RegexFindLib.Search
{
    public partial class RegexSearch
    {
        const short SnippetLength = 75;

        // ── ISearchEngine.Results ─────────────────────────────────────────────
        public SearchResult[] Results { get; private set; }

        // ── ISearchEngine.SelectResultByIndex ─────────────────────────────────
        public void SelectResultByIndex(int index)
        {
            if (Results == null || index < 0 || index >= Results.Length) return;
            Selection.SetRange(Results[index].Start, Results[index].End);
        }

        // ── Internal result collection ────────────────────────────────────────

        SearchResult[] GetSearchResults(FindRequest find)
        {
            Range searchRange = GetSearchRange(find);
            int startPos = searchRange.Start;

            string rangeText = searchRange.Text;
            if (string.IsNullOrEmpty(rangeText))
                return new SearchResult[0];

            var matches = Regex.Matches(rangeText, find.Text, RegexOptions.Multiline)
                               .Cast<Match>().ToArray();

            var list = new List<SearchResult>();
            foreach (var match in matches)
            {
                // Build match range within the same story as searchRange.
                // Document.Range() always uses the main story — use Duplicate + SetRange instead
                // so footnote/endnote story ranges are preserved correctly.
                Range matchRange = searchRange.Duplicate;
                matchRange.SetRange(
                    startPos + match.Index,
                    startPos + match.Index + match.Length);

                if (MatchedConditions(find.Formatting, matchRange))
                    list.Add(BuildResult(matchRange, searchRange));
            }

            return list.ToArray();
        }

        Range GetSearchRange(FindRequest find)
        {
            if (find.Scope == SearchScope.Selection)
                return Selection.Range.Duplicate;

            Range actionRange;
            using (_ = new Helpers.WdActionManager(_word, true, true, false))
            {
                Selection.WholeStory();
                actionRange = Selection.Range.Duplicate;
            }

            // Only trim the range when a direction was explicitly chosen.
            // SearchModeIndex 0 = "הכל" — use the full story range as-is.
            // Forward=true + Scope=All with a directional intent trims from cursor forward.
            // Forward=false trims from cursor backward.
            // We distinguish "הכל" from "כלפי מטה" via the Slop==0 && !explicitly directional
            // check — but the cleanest way is to carry the intent explicitly.
            // The ViewModel sets Forward=true for both "הכל" (0) and "כלפי מטה" (1),
            // so we use the IsDirectional flag on the request.
            if (find.IsDirectional)
            {
                if (find.Forward)
                    actionRange.Start = Selection.Start;
                else
                    actionRange.End = Selection.End;
            }

            return actionRange;
        }

        bool MatchedConditions(FindFormatting fmt, dynamic range)
        {
            bool matchesBold = !fmt.Bold.HasValue ||
                (fmt.Bold.Value ? (range.Font.Bold == -1 || range.Font.BoldBi == -1)
                                : (range.Font.Bold == 0  && range.Font.BoldBi == 0));

            bool matchesItalic = !fmt.Italic.HasValue ||
                (fmt.Italic.Value ? (range.Font.Italic == -1 || range.Font.ItalicBi == -1)
                                  : (range.Font.Italic == 0  && range.Font.ItalicBi == 0));

            bool matchesUnderline = !fmt.Underline.HasValue ||
                (fmt.Underline.Value ? (range.Font.Underline != (int)WdUnderline.wdUnderlineNone)
                                     : (range.Font.Underline == (int)WdUnderline.wdUnderlineNone));

            bool matchesSuperscript = !fmt.Superscript.HasValue ||
                (fmt.Superscript.Value ? (range.Font.Superscript == -1) : (range.Font.Superscript == 0));

            bool matchesSubscript = !fmt.Subscript.HasValue ||
                (fmt.Subscript.Value ? (range.Font.Subscript == -1) : (range.Font.Subscript == 0));

            bool matchesFontSize = !fmt.FontSize.HasValue ||
                (Math.Abs(range.Font.Size   - fmt.FontSize.Value) < 0.01 ||
                 Math.Abs(range.Font.SizeBi - fmt.FontSize.Value) < 0.01);

            bool matchesFont = string.IsNullOrEmpty(fmt.FontName) ||
                (range.Font.Name == fmt.FontName || range.Font.NameBi == fmt.FontName);

            bool matchesStyle = string.IsNullOrEmpty(fmt.Style) ||
                (range.Style as Style)?.NameLocal == fmt.Style;

            bool matchesColor = !fmt.TextColor.HasValue ||
                range.Font.Color == (int)fmt.TextColor.Value;

            return matchesBold && matchesItalic && matchesUnderline &&
                   matchesSuperscript && matchesSubscript && matchesFontSize &&
                   matchesFont && matchesStyle && matchesColor;
        }

        SearchResult BuildResult(Range matchRange, Range storyRange)
        {
            var dup = matchRange.Duplicate;
            int start = dup.Start;
            int end   = dup.End;
            string matchText = dup.Text ?? "";

            int storyStart = storyRange.Start;
            int storyEnd   = storyRange.End;

            dup.SetRange(Math.Max(start - SnippetLength, storyStart), start);
            string before = dup.Text ?? "";

            dup.SetRange(end, Math.Min(end + SnippetLength, storyEnd));
            string after = dup.Text ?? "";

            return new SearchResult
            {
                Range         = matchRange,
                ContextBefore = before,
                MatchText     = matchText,
                ContextAfter  = after
            };
        }

        // ── Navigation ────────────────────────────────────────────────────────

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
    }
}
