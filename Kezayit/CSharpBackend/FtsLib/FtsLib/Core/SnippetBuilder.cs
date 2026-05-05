using System.Collections.Generic;
using System.Net;
using System.Text;

namespace FtsLib.Core
{
    /// <summary>
    /// Builds a highlighted HTML snippet from raw HTML content and a set of query terms.
    /// In one logical pipeline:
    ///   1. Tokenizes the raw HTML via <see cref="TokenStream"/>.
    ///   2. Finds the tightest window covering all terms via <see cref="ProximityWindow"/>.
    ///   3. Expands that window to a readable snippet length.
    ///   4. Renders the snippet as HTML with matched terms wrapped in highlight tags.
    ///
    /// The <see cref="SnippetResult.Score"/> (character span of the best window) can be
    /// used by the caller to rank or filter results — smaller score = terms closer together.
    /// Not thread-safe — one instance per thread.
    /// </summary>
    public sealed class SnippetBuilder
    {
        private readonly string _preTag;
        private readonly string _postTag;
        private readonly int    _snippetLength;
        private readonly int    _contextMargin;

        private readonly TokenStream _tokenStream = new TokenStream();

        /// <param name="preTag">Inserted before each matched term. Default: &lt;mark&gt;</param>
        /// <param name="postTag">Inserted after each matched term. Default: &lt;/mark&gt;</param>
        /// <param name="snippetLength">Target total character length of the rendered snippet.</param>
        /// <param name="contextMargin">Minimum extra chars added on each side of the best window.</param>
        public SnippetBuilder(
            string preTag        = "<mark>",
            string postTag       = "</mark>",
            int    snippetLength = 300,
            int    contextMargin = 60)
        {
            _preTag        = preTag;
            _postTag       = postTag;
            _snippetLength = snippetLength;
            _contextMargin = contextMargin;
        }

        // ── Public API ───────────────────────────────────────────────

        /// <summary>
        /// Tokenizes <paramref name="rawHtml"/>, finds the best window covering all
        /// <paramref name="queryTerms"/>, and returns a scored highlighted HTML snippet.
        /// </summary>
        public SnippetResult Build(string rawHtml, IReadOnlyCollection<string> queryTerms)
        {
            if (string.IsNullOrEmpty(rawHtml) || queryTerms == null || queryTerms.Count == 0)
                return new SnippetResult(Encode(rawHtml ?? string.Empty), int.MaxValue, false);

            // 1. Tokenize — single pass, captures raw positions + normalized forms.
            var tokens = _tokenStream.Tokenize(rawHtml);

            // 2. Find the tightest window covering all query terms.
            var (winStart, winEnd, score) = ProximityWindow.Find(tokens, queryTerms);

            if (score == int.MaxValue)
                return new SnippetResult(Encode(rawHtml), int.MaxValue, false);

            // 3. Expand window to a readable snippet, snapped to word boundaries.
            var (snapStart, snapEnd) = ExpandWindow(rawHtml, winStart, winEnd);

            // 4. Render highlighted HTML for the snippet range.
            string html = Render(rawHtml, tokens, queryTerms, snapStart, snapEnd);

            return new SnippetResult(html, score, true);
        }

        // ── Stage 3: expand + snap to word boundaries ────────────────

        private (int start, int end) ExpandWindow(string text, int winStart, int winEnd)
        {
            int len       = text.Length;
            int windowLen = winEnd - winStart;

            // Distribute remaining snippet budget equally on each side,
            // but never exceed _contextMargin — the snippet length controls
            // the total output size, not how far we expand from the window.
            int remaining = _snippetLength - windowLen;
            int half      = remaining > 0 ? remaining / 2 : 0;
            int margin    = half < _contextMargin ? half : _contextMargin;
            if (margin < 0) margin = 0;

            int start = System.Math.Max(0,   winStart - margin);
            int end   = System.Math.Min(len, winEnd   + margin);

            return (start, end);
        }

        // ── Stage 4: render highlighted HTML ────────────────────────

        private string Render(
            string                      text,
            List<TextToken>             tokens,
            IReadOnlyCollection<string> queryTerms,
            int                         snapStart,
            int                         snapEnd)
        {
            // Index hit tokens by their raw start position for O(1) lookup during render.
            var termSet  = new HashSet<string>(queryTerms);
            var hitIndex = new Dictionary<int, TextToken>();
            foreach (var tok in tokens)
            {
                if (tok.RawStart >= snapStart && tok.RawEnd <= snapEnd
                    && termSet.Contains(tok.Normalized))
                {
                    hitIndex[tok.RawStart] = tok;
                }
            }

            var  sb           = new StringBuilder((snapEnd - snapStart) + hitIndex.Count * 15);
            bool inTag        = false;

            if (snapStart > 0)        sb.Append("…");

            int i = snapStart;
            while (i < snapEnd)
            {
                char c = text[i];

                // Pass HTML tags through verbatim — never highlight inside a tag.
                if (inTag)
                {
                    sb.Append(c);
                    if (c == '>') inTag = false;
                    i++;
                    continue;
                }

                if (c == '<')
                {
                    inTag = true;
                    sb.Append(c);
                    i++;
                    continue;
                }

                // Hit token starts here — wrap the raw slice in highlight tags.
                if (hitIndex.TryGetValue(i, out TextToken hit))
                {
                    sb.Append(_preTag);
                    int end = hit.RawEnd < snapEnd ? hit.RawEnd : snapEnd;
                    for (int j = hit.RawStart; j < end; j++)
                        sb.Append(text[j]);
                    sb.Append(_postTag);
                    i = hit.RawEnd;
                    continue;
                }

                // Ordinary character — HTML-encode it.
                switch (c)
                {
                    case '&': sb.Append("&amp;");  break;
                    case '>': sb.Append("&gt;");   break;
                    case '"': sb.Append("&quot;"); break;
                    default:  sb.Append(c);        break;
                }
                i++;
            }

            if (snapEnd < text.Length) sb.Append("…");

            return sb.ToString();
        }

        private static string Encode(string s) => WebUtility.HtmlEncode(s ?? string.Empty);
    }
}
