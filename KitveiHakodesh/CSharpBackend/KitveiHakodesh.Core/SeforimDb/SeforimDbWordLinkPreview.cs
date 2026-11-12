using System.Collections.Generic;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// What the lines view and its word-link hover ask per request, each in ONE call:
    /// a window's decorations (word-link anchors + inline notes) and a link target's preview.
    /// </summary>
    public sealed partial class SeforimDbQueries
    {
        /// <summary>
        /// Cap on how many lines one preview section may hold. A TOC section is normally a
        /// chapter or a siman, but a book whose TOC only marks volumes would pull thousands
        /// into a hover panel. Past this the section is dropped (the preview shows the cited
        /// line alone) rather than truncated.
        /// </summary>
        public const int WordLinkPreviewMaxSectionLines = 400;

        /// <summary>Word-link anchors for <paramref name="anchorLineIds"/> and inline notes for
        /// <paramref name="noteLineIds"/> - the lines view's per-window decorations in one call.
        /// Either list may be empty; each half keeps its own Supported answer.</summary>
        public LineDecorationsResult GetLineDecorationsForLines(List<int> anchorLineIds, List<int> noteLineIds) => new()
        {
            Anchors = GetWordLinkAnchorsForLines(anchorLineIds),
            Notes = GetLineNotesForLines(noteLineIds),
        };

        /// <summary>
        /// The hover preview of a word-link target line: its text and TOC path, and the whole
        /// TOC section around it when one can be established. Why a section: a range anchor
        /// cites a passage, not a line, and the DB's line breaks follow the printed edition's
        /// typography - the cited line alone routinely starts mid-sentence. The panel shows the
        /// section and scrolls to the cited line. The section is the book's TOC
        /// read here (never shipped):
        ///   start = the last entry with a line at or before the target (ties: the last one,
        ///           the deepest),
        ///   end   = the next entry after it at the same or a shallower level, else the book's end.
        /// Null section = the caller previews the single line (no TOC, a target ahead of every
        /// entry, a section over the cap, or the target not inside it).
        /// </summary>
        public WordLinkPreviewResult GetWordLinkPreview(int lineId)
        {
            var result = new WordLinkPreviewResult();
            var at = GetLineIndexFromLineId(lineId);
            if (at.Count == 0) return result;
            result.BookId = at[0].BookId;
            result.LineIndex = at[0].LineIndex;
            var content = GetLineContents(new List<int> { lineId });
            result.Content = content.Count > 0 ? content[0].Content : "";
            if (result.Content.Length == 0) return result;
            var paths = GetTocPathsForLines(new List<int> { lineId });
            foreach (var line in paths.Lines)
            {
                if (line.LineId != lineId) continue;
                var path = paths.Paths.Find(p => p.TocEntryId == line.TocEntryId);
                if (path != null) { result.TocPath = path.Path; break; }
            }
            result.Section = ReadWordLinkSection(result.BookId, result.LineIndex, lineId);
            return result;
        }

        private WordLinkPreviewSection? ReadWordLinkSection(int bookId, int lineIndex, int lineId)
        {
            var entries = GetAllTocEntries(bookId);
            int startAt = -1;
            for (int i = 0; i < entries.Count; i++)
            {
                var index = entries[i].LineIndex;
                if (index == null) continue;
                if (index.Value > lineIndex) break;
                startAt = i;
            }
            if (startAt < 0) return null;
            var start = entries[startAt];
            int startIndex = start.LineIndex!.Value;
            int? end = null;
            for (int i = startAt + 1; i < entries.Count; i++)
            {
                if (entries[i].LineIndex == null || entries[i].Level > start.Level) continue;
                end = entries[i].LineIndex;
                break;
            }
            // No following entry = to the book's end, whose length is not known here: ask one
            // past the cap so an oversized tail fails the same length check.
            int limit = end == null ? WordLinkPreviewMaxSectionLines + 1 : end.Value - startIndex;
            if (limit <= 0 || limit > WordLinkPreviewMaxSectionLines) return null;
            var lines = GetLinesPaged(bookId, limit, startIndex);
            if (lines.Count == 0 || lines.Count > WordLinkPreviewMaxSectionLines) return null;
            if (lines[0].LineIndex != startIndex) return null;
            // A section the cited line is not in would scroll the panel to the wrong place.
            int focus = lines.FindIndex(l => l.Id == lineId);
            if (focus < 0) return null;
            return new WordLinkPreviewSection { TocText = start.Text, FocusIndex = focus, Lines = lines };
        }
    }
}
