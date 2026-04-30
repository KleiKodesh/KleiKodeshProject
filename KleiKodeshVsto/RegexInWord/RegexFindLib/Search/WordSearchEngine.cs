using Microsoft.Office.Interop.Word;
using System;
using System.Collections.Generic;

namespace RegexFindLib.Search
{
    /// <summary>
    /// Search engine backed by Word's native Find API.
    /// Implements ISearchEngine so the ViewModel can swap it in at runtime.
    ///
    /// Behaviour differences vs RegexSearch:
    ///   - MatchWildcards drives Word's Find.MatchWildcards (Word wildcard syntax, not .NET regex)
    ///   - Formatting filters are applied via Word's Find.Font / Find.Style — Word does the matching
    ///   - Results are collected by looping Find.Execute on a Range (no .NET regex involved)
    ///   - Slop is not supported (ignored)
    /// </summary>
    public class WordSearchEngine : ISearchEngine
    {
        readonly IWordService _word;

        public WordSearchEngine(IWordService word)
        {
            _word = word;
        }

        Application App       => _word.Application;
        Document    Document  => _word.ActiveDocument;
        Selection   Selection => _word.Selection;

        // ── ISearchEngine ─────────────────────────────────────────────────────

        public SearchResult[] Results { get; private set; }

        public void Execute(FindRequest request, bool replace = false, bool replaceAll = false)
        {
            // Allow empty text when searching by formatting only (non-wildcard mode).
            // Word's Find API handles this natively — empty text + Format=true matches
            // any run that satisfies the formatting criteria, exactly like the built-in dialog.
            // Wildcard mode is excluded because an empty wildcard pattern is invalid in Word.
            bool hasText = !string.IsNullOrEmpty(request?.Text);
            if (!hasText && (request.MatchWildcards || !HasFormatting(request.Formatting)))
                return;

            if (replace)
                ExecuteReplace(request);
            else if (replaceAll)
                ExecuteReplaceAll(request);
            else
                ExecuteFind(request);
        }

        public FindFormatting GetSelectionFormatting()
        {
            try
            {
                var sel = Selection;
                if (sel?.Range == null) return new FindFormatting();

                dynamic rng = sel.Range;

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
                System.Diagnostics.Debug.WriteLine($"[WordSearchEngine] GetSelectionFormatting error: {ex.Message}");
                return new FindFormatting();
            }
        }

        public void SelectResultByIndex(int index)
        {
            if (Results == null || index < 0 || index >= Results.Length) return;
            Selection.SetRange(Results[index].Start, Results[index].End);
        }

        // ── Find ──────────────────────────────────────────────────────────────

        void ExecuteFind(FindRequest request)
        {
            Results = CollectResults(request);
            if (Results == null || Results.Length == 0) return;

            // Navigate: find the next result after the current cursor position
            int cursorPos = Selection.Start;
            SearchResult target = null;

            if (request.Forward)
            {
                foreach (var r in Results)
                    if (r.Start > cursorPos) { target = r; break; }
                if (target == null) target = Results[0]; // wrap
            }
            else
            {
                for (int i = Results.Length - 1; i >= 0; i--)
                    if (Results[i].Start < cursorPos) { target = Results[i]; break; }
                if (target == null) target = Results[Results.Length - 1]; // wrap
            }

            Selection.SetRange(target.Start, target.End);
        }

        // ── Replace ───────────────────────────────────────────────────────────

        void ExecuteReplace(FindRequest request)
        {
            var doc = Document;
            if (doc == null) return;

            // Replace only the current selection match
            Range rng = Selection.Range.Duplicate;
            ApplyFindToRange(rng.Find, request);
            rng.Find.Replacement.Text = request.Replacement.Text ?? "";
            ApplyFormattingToReplacement(rng.Find.Replacement, request.Replacement.Formatting);
            rng.Find.Execute(Replace: WdReplace.wdReplaceOne);

            Results = CollectResults(request);
        }

        void ExecuteReplaceAll(FindRequest request)
        {
            var doc = Document;
            if (doc == null) return;

            Range rng = doc.Content;
            ApplyFindToRange(rng.Find, request);
            rng.Find.Replacement.Text = request.Replacement.Text ?? "";
            ApplyFormattingToReplacement(rng.Find.Replacement, request.Replacement.Formatting);
            rng.Find.Execute(Replace: WdReplace.wdReplaceAll);

            Results = new SearchResult[0];
        }

        // ── Result collection ─────────────────────────────────────────────────

        SearchResult[] CollectResults(FindRequest request)
        {
            var doc = Document;
            if (doc == null) return new SearchResult[0];

            Range searchRange = GetSearchRange(request);
            if (searchRange == null) return new SearchResult[0];

            var find = searchRange.Find;
            ApplyFindToRange(find, request);
            find.Wrap = WdFindWrap.wdFindStop;

            var list = new List<SearchResult>();
            const int maxResults = 500; // safety cap

            while (find.Execute() && find.Found && list.Count < maxResults)
            {
                var matchRange = searchRange.Duplicate;
                list.Add(BuildResult(matchRange, doc.Content));
            }

            return list.ToArray();
        }

        Range GetSearchRange(FindRequest request)
        {
            if (request.Scope == SearchScope.Selection)
                return Selection.Range.Duplicate;

            var doc = Document;
            if (doc == null) return null;

            Range range = doc.Content;

            if (request.IsDirectional)
            {
                if (request.Forward)
                    range.Start = Selection.Start;
                else
                    range.End = Selection.End;
            }

            return range;
        }

        void ApplyFindToRange(Find find, FindRequest request)
        {
            find.ClearFormatting();
            find.Text           = request.Text;
            find.MatchWildcards = request.MatchWildcards;
            find.Forward        = request.Forward;
            find.Format         = HasFormatting(request.Formatting);

            var fmt = request.Formatting;
            if (fmt == null) return;

            if (fmt.Bold.HasValue)      find.Font.Bold      = fmt.Bold.Value      ? -1 : 0;
            if (fmt.Italic.HasValue)    find.Font.Italic    = fmt.Italic.Value    ? -1 : 0;
            if (fmt.Underline.HasValue) find.Font.Underline = fmt.Underline.Value
                                            ? WdUnderline.wdUnderlineSingle
                                            : WdUnderline.wdUnderlineNone;
            if (fmt.Superscript.HasValue) find.Font.Superscript = fmt.Superscript.Value ? -1 : 0;
            if (fmt.Subscript.HasValue)   find.Font.Subscript   = fmt.Subscript.Value   ? -1 : 0;
            if (!string.IsNullOrEmpty(fmt.FontName))
            {
                find.Font.Name   = fmt.FontName;
                find.Font.NameBi = fmt.FontName;
            }
            if (fmt.FontSize.HasValue)
            {
                find.Font.Size   = fmt.FontSize.Value;
                find.Font.SizeBi = fmt.FontSize.Value;
            }
            if (!string.IsNullOrEmpty(fmt.Style))
            {
                object style = fmt.Style;
                find.set_Style(ref style);
            }
            if (fmt.TextColor.HasValue)
                find.Font.Color = (WdColor)fmt.TextColor.Value;
        }

        void ApplyFormattingToReplacement(Replacement replacement, FindFormatting fmt)
        {
            replacement.ClearFormatting();
            if (fmt == null) return;

            if (fmt.Bold.HasValue)      replacement.Font.Bold      = fmt.Bold.Value      ? -1 : 0;
            if (fmt.Italic.HasValue)    replacement.Font.Italic    = fmt.Italic.Value    ? -1 : 0;
            if (fmt.Underline.HasValue) replacement.Font.Underline = fmt.Underline.Value
                                            ? WdUnderline.wdUnderlineSingle
                                            : WdUnderline.wdUnderlineNone;
            if (fmt.Superscript.HasValue) replacement.Font.Superscript = fmt.Superscript.Value ? -1 : 0;
            if (fmt.Subscript.HasValue)   replacement.Font.Subscript   = fmt.Subscript.Value   ? -1 : 0;
            if (!string.IsNullOrEmpty(fmt.FontName))
            {
                replacement.Font.Name   = fmt.FontName;
                replacement.Font.NameBi = fmt.FontName;
            }
            if (fmt.FontSize.HasValue)
            {
                replacement.Font.Size   = fmt.FontSize.Value;
                replacement.Font.SizeBi = fmt.FontSize.Value;
            }
            if (!string.IsNullOrEmpty(fmt.Style))
            {
                object style = fmt.Style;
                replacement.set_Style(ref style);
            }
            if (fmt.TextColor.HasValue)
                replacement.Font.Color = (WdColor)fmt.TextColor.Value;
        }

        static bool HasFormatting(FindFormatting fmt) =>
            fmt != null && (
                fmt.Bold.HasValue || fmt.Italic.HasValue || fmt.Underline.HasValue ||
                fmt.Superscript.HasValue || fmt.Subscript.HasValue ||
                !string.IsNullOrEmpty(fmt.FontName) || fmt.FontSize.HasValue ||
                !string.IsNullOrEmpty(fmt.Style) || fmt.TextColor.HasValue);

        const short SnippetLength = 75;

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
    }
}
