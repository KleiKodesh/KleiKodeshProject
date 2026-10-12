using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// Word links for databases WITHOUT a link_anchor table (Zayit), built from the commentary
    /// markers their line text carries inline:
    ///
    ///     &lt;i data-commentator="Mishnah Berurah" data-label="…" data-order="3"&gt;&lt;/i&gt;
    ///
    /// Each marker names its commentary in English, while book titles in the database are
    /// Hebrew, so the name cannot be looked up. And the same name means a different book under
    /// each source book (every part of the Shulchan Aruch has its own Be'er HaGolah). So the
    /// name is resolved per SOURCE book, from the links: the book that the marker lines of that
    /// name link to most consistently is the one the name means. Measured on the Zayit DB
    /// 2026-10-12: 88% of marker lines (by weight) resolve with ≥ 90% consistency; a per-line
    /// guess alone is ambiguous on 65% of them.
    ///
    /// Within a line, the k-th marker of a name is paired with the k-th linked line of its
    /// book (by line index). data-order cannot be used instead — most markers have none, and
    /// commentary lines mostly have no TOC entry to count from.
    /// </summary>
    internal static class SeforimDbInlineCommentatorMarkers
    {
        /// <summary>A name must point at one book on at least this share of its marker lines
        /// before it is trusted. A wrong link is worse than none.</summary>
        private const double MinNameConsistency = 0.7;

        internal readonly struct Marker
        {
            public Marker(string commentator, string? label, int visibleOffset)
            {
                Commentator = commentator;
                Label = label;
                VisibleOffset = visibleOffset;
            }

            public string Commentator { get; }
            public string? Label { get; }
            /// <summary>Visible-char offset of the marker, in the same convention as
            /// link_anchor.charStart (tags 0, each entity 1, every other char 1).</summary>
            public int VisibleOffset { get; }
        }

        internal readonly struct LinkedLine
        {
            public LinkedLine(int bookId, int lineId, int lineIndex)
            {
                BookId = bookId;
                LineId = lineId;
                LineIndex = lineIndex;
            }

            public int BookId { get; }
            public int LineId { get; }
            public int LineIndex { get; }
        }

        /// <summary>What a source book's marker names resolve to.</summary>
        internal sealed class BookResolution
        {
            public BookResolution(Dictionary<string, int> bookIdByName, List<WordLinkTargetRow> targets)
            {
                BookIdByName = bookIdByName;
                Targets = targets;
            }

            public Dictionary<string, int> BookIdByName { get; }
            /// <summary>Distinct (commentary book, label) pairs — the answer to
            /// GetWordLinkAnchorTargetsForBook for this book.</summary>
            public List<WordLinkTargetRow> Targets { get; }
        }

        private const string MarkerAttribute = "data-commentator=";

        private static readonly Regex CommentatorAttr = new Regex("data-commentator=\"([^\"]*)\"", RegexOptions.CultureInvariant);
        private static readonly Regex LabelAttr = new Regex("data-label=\"([^\"]*)\"", RegexOptions.CultureInvariant);

        /// <summary>Cheap pre-check, so the HTML walk only runs on lines that have markers.</summary>
        internal static bool HasMarkers(string content)
            => content.IndexOf(MarkerAttribute, StringComparison.Ordinal) >= 0;

        /// <summary>The markers of one line, in document order.</summary>
        internal static List<Marker> Parse(string content)
        {
            var markers = new List<Marker>();
            if (string.IsNullOrEmpty(content) || !HasMarkers(content)) return markers;

            int visible = 0;
            int i = 0;
            while (i < content.Length)
            {
                char ch = content[i];
                if (ch == '<')
                {
                    int close = content.IndexOf('>', i);
                    if (close < 0) break;
                    string tag = content.Substring(i, close - i + 1);
                    if (tag.StartsWith("<i ", StringComparison.Ordinal))
                    {
                        var name = CommentatorAttr.Match(tag);
                        if (name.Success && name.Groups[1].Value.Length > 0)
                        {
                            var label = LabelAttr.Match(tag);
                            string? labelText = label.Success && label.Groups[1].Value.Trim().Length > 0
                                ? WebUtility.HtmlDecode(label.Groups[1].Value) // raw attribute text; link_anchor labels are plain
                                : null;
                            markers.Add(new Marker(name.Groups[1].Value, labelText, visible));
                        }
                    }
                    i = close + 1;
                    continue;
                }
                if (ch == '&')
                {
                    // Upstream's countVisibleChars: `&` starts an entity iff a `;` follows
                    // within the next 9 chars — no whitespace check. Mirrors wordLinkAnchors.ts.
                    int semi = -1;
                    for (int j = i + 1; j < content.Length && j < i + 10; j++)
                        if (content[j] == ';') { semi = j; break; }
                    visible++;
                    i = semi >= 0 ? semi + 1 : i + 1;
                    continue;
                }
                visible++;
                i++;
            }
            return markers;
        }

        /// <summary>Resolves a source book's marker names from its marker lines and the books
        /// each of those lines links to.</summary>
        internal static BookResolution ResolveBook(
            IEnumerable<(List<Marker> Markers, HashSet<int> LinkedBookIds)> markerLines)
        {
            var linesPerName = new Dictionary<string, int>(StringComparer.Ordinal);
            var linesPerNameAndBook = new Dictionary<(string Name, int BookId), int>();
            var labelsPerName = new Dictionary<string, HashSet<string?>>(StringComparer.Ordinal);

            foreach (var (markers, linkedBookIds) in markerLines)
            {
                foreach (var marker in markers)
                {
                    if (!labelsPerName.TryGetValue(marker.Commentator, out var labels))
                        labelsPerName[marker.Commentator] = labels = new HashSet<string?>();
                    labels.Add(marker.Label);
                }
                foreach (var name in markers.Select(m => m.Commentator).Distinct(StringComparer.Ordinal))
                {
                    linesPerName[name] = linesPerName.TryGetValue(name, out var n) ? n + 1 : 1;
                    foreach (int bookId in linkedBookIds)
                    {
                        var key = (name, bookId);
                        linesPerNameAndBook[key] = linesPerNameAndBook.TryGetValue(key, out var c) ? c + 1 : 1;
                    }
                }
            }

            var bookIdByName = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var group in linesPerNameAndBook.GroupBy(kv => kv.Key.Name, StringComparer.Ordinal))
            {
                var best = group.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.BookId).First();
                if (best.Value >= MinNameConsistency * linesPerName[group.Key])
                    bookIdByName[group.Key] = best.Key.BookId;
            }

            var targets = bookIdByName
                .SelectMany(kv => labelsPerName[kv.Key].Select(label => (BookId: kv.Value, Label: label)))
                .Distinct()
                .OrderBy(t => t.BookId)
                .Select(t => new WordLinkTargetRow { TargetBookId = t.BookId, Label = t.Label })
                .ToList();

            return new BookResolution(bookIdByName, targets);
        }

        /// <summary>The anchors of one line: each marker paired with its commentary line.</summary>
        internal static IEnumerable<WordLinkAnchorRow> AnchorsForLine(
            int lineId, int sourceBookId, List<Marker> markers, List<LinkedLine> linkedLines,
            BookResolution resolution)
        {
            foreach (var group in markers.GroupBy(m => m.Commentator, StringComparer.Ordinal))
            {
                if (!resolution.BookIdByName.TryGetValue(group.Key, out int bookId)) continue;
                var targets = linkedLines
                    .Where(l => l.BookId == bookId)
                    .GroupBy(l => l.LineId).Select(g => g.First())
                    .OrderBy(l => l.LineIndex)
                    .ToList();
                var ofName = group.ToList();

                // Equal counts pair in order. One commentary line serves every marker. Any other
                // mismatch has no safe pairing, so those markers stay plain.
                if (targets.Count != ofName.Count && targets.Count != 1) continue;

                for (int k = 0; k < ofName.Count; k++)
                {
                    var target = targets.Count == 1 ? targets[0] : targets[k];
                    yield return new WordLinkAnchorRow
                    {
                        LineId = lineId,
                        CharStart = ofName[k].VisibleOffset,
                        CharEnd = null,
                        Label = ofName[k].Label,
                        TargetBookId = target.BookId,
                        TargetLineId = target.LineId,
                        TargetLineIndex = target.LineIndex,
                        SourceBookId = sourceBookId,
                    };
                }
            }
        }
    }
}
