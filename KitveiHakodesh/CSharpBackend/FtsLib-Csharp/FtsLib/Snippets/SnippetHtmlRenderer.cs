using System.Text;

namespace FtsLib.Snippets
{
    /// <summary>
    /// Renders a <see cref="SnippetDescriptor"/> over its source text as highlighted HTML.
    ///
    /// NOT on the search path — the service ships descriptors and the page renders them
    /// (vue-frontend fullTextSearchSnippets.ts). This is the REFERENCE rendering the page's
    /// port must match, and what the tests and diagnostics print. Keep the two in step:
    /// window [SnapStart, SnapEnd) of the source with tags and {X} paragraph markers
    /// stripped, each mark range wrapped in the highlight tags, "…" on each cut side. The
    /// mark's term ordinal (third int of each triple) is not rendered — it is for naming the
    /// hit's matched terms, not for highlighting.
    /// </summary>
    internal static class SnippetHtmlRenderer
    {
        public static string Render(
            string                source,
            in SnippetDescriptor  descriptor,
            string                preTag  = "<mark>",
            string                postTag = "</mark>")
        {
            if (string.IsNullOrEmpty(source)) return string.Empty;
            if (!descriptor.IsMatch) return StripTagsAndMarkers(source);

            var html = new StringBuilder(descriptor.SnapEnd - descriptor.SnapStart + 64);
            if (descriptor.LeadingEllipsis) html.Append('…');

            int pos   = descriptor.SnapStart;
            // Marks are (start, length, termOrdinal) triples; rendering ignores the ordinal.
            var marks = descriptor.MarkRanges;
            for (int i = 0; i + 1 < marks.Count; i += SnippetDescriptor.MarkStride)
            {
                int markStart = marks[i];
                int markEnd   = markStart + marks[i + 1];

                AppendStripped(html, source, pos, markStart);
                html.Append(preTag);
                // The mark's raw span can cross inline tags (word-transparent formatting
                // inside a word) — strip them instead of copying raw: a pair that closes
                // beyond the mark would emit an unbalanced opener inside the highlight and
                // bleed formatting over the rest of the snippet.
                AppendStripped(html, source, markStart, markEnd);
                html.Append(postTag);
                pos = markEnd;
            }

            AppendStripped(html, source, pos, descriptor.SnapEnd);
            if (descriptor.TrailingEllipsis) html.Append('…');
            return html.ToString();
        }

        /// <summary>
        /// Appends source[from..to) stripping HTML tags. If <paramref name="from"/> lands
        /// mid-tag, scans backwards to detect and skip the partial tag. HTML entities pass
        /// through as-is — the raw HTML already holds valid entities. {X} paragraph markers
        /// where X is a Hebrew letter are stripped.
        /// </summary>
        private static void AppendStripped(StringBuilder html, string source, int from, int to)
        {
            bool inTag = false;
            for (int k = from - 1; k >= 0; k--)
            {
                if (source[k] == '>') break;
                if (source[k] == '<') { inTag = true; break; }
            }

            for (int i = from; i < to; i++)
            {
                char c = source[i];
                if (inTag) { if (c == '>') inTag = false; continue; }
                if (c == '<') { inTag = true; continue; }

                if (IsParagraphMarkerAt(source, i, to)) { i += 2; continue; }

                html.Append(c);
            }
        }

        /// <summary>Whole source with tags and {X} markers stripped — what a no-match line
        /// renders as.</summary>
        private static string StripTagsAndMarkers(string source)
        {
            var text = new StringBuilder(source.Length);
            bool inTag = false;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (inTag) { if (c == '>') inTag = false; continue; }
                if (c == '<') { inTag = true; continue; }
                if (IsParagraphMarkerAt(source, i, source.Length)) { i += 2; continue; }
                text.Append(c);
            }
            return text.ToString();
        }

        /// <summary>{X} where X is a Hebrew letter (U+05D0–U+05EA), fully inside [i, limit).</summary>
        private static bool IsParagraphMarkerAt(string source, int i, int limit)
        {
            if (source[i] != '{' || i + 2 >= limit || source[i + 2] != '}') return false;
            char inner = source[i + 1];
            return inner >= 'א' && inner <= 'ת';
        }
    }
}
