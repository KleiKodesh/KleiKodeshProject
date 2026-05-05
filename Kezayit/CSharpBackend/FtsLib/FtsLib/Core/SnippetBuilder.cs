using System.Collections.Generic;
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
    internal sealed class SnippetBuilder
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
        /// Strips HTML tags from <paramref name="rawHtml"/>, tokenizes the clean text,
        /// finds the best window covering all <paramref name="queryTerms"/>, and returns
        /// a scored snippet with matched terms wrapped in highlight tags.
        /// The output contains only plain text and highlight tags — no original HTML.
        ///
        /// Each term is treated as its own group (AND semantics across all terms).
        /// Use <see cref="Build(string, IReadOnlyList{IReadOnlyCollection{string}})"/>
        /// for fuzzy/wildcard queries where each group has multiple alternatives.
        /// </summary>
        public SnippetResult Build(string rawHtml, IReadOnlyCollection<string> queryTerms)
        {
            if (string.IsNullOrEmpty(rawHtml) || queryTerms == null || queryTerms.Count == 0)
                return new SnippetResult(Encode(rawHtml ?? string.Empty), int.MaxValue, false);

            // Wrap each term as a single-element group.
            var groups = new List<IReadOnlyCollection<string>>(queryTerms.Count);
            foreach (var t in queryTerms)
                groups.Add(new[] { t });

            return BuildCore(rawHtml, groups, queryTerms);
        }

        /// <summary>
        /// Overload for fuzzy/wildcard queries: each group is a set of alternative
        /// terms (OR within the group, AND across groups). The window finder requires
        /// one term from every group to be present; the highlighter marks any term
        /// from any group that appears in the snippet.
        /// </summary>
        public SnippetResult Build(
            string                                       rawHtml,
            IReadOnlyList<IReadOnlyCollection<string>>   queryGroups)
        {
            if (string.IsNullOrEmpty(rawHtml) || queryGroups == null || queryGroups.Count == 0)
                return new SnippetResult(Encode(rawHtml ?? string.Empty), int.MaxValue, false);

            // Flatten all terms for the highlighter.
            var allTerms = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var g in queryGroups)
                foreach (var t in g)
                    allTerms.Add(t);

            return BuildCore(rawHtml, queryGroups, allTerms);
        }

        private SnippetResult BuildCore(
            string                                     rawHtml,
            IReadOnlyList<IReadOnlyCollection<string>> queryGroups,
            IReadOnlyCollection<string>                highlightTerms)
        {
            // 1. Strip all HTML tags — work on clean text throughout.
            string text = StripHtml(rawHtml);
            if (text.Length == 0)
                return new SnippetResult(string.Empty, int.MaxValue, false);

            // 2. Tokenize the clean text.
            var tokens = _tokenStream.Tokenize(text);

            // 3. Find the tightest window covering one term from every group.
            var (winStart, winEnd, score) = ProximityWindow.Find(tokens, queryGroups);

            if (score == int.MaxValue)
                return new SnippetResult(Encode(text), int.MaxValue, false);

            // 4. Expand window to a readable snippet length.
            var (snapStart, snapEnd) = ExpandWindow(text, winStart, winEnd);

            // 5. Render: plain text with <mark> tags around hits — no other HTML.
            string html = Render(text, tokens, highlightTerms, snapStart, snapEnd);

            return new SnippetResult(html, score, true);
        }

        // ── HTML stripper ────────────────────────────────────────────

        /// <summary>
        /// Removes all HTML tags in a single pass.
        /// Block-level tags (div, p, br, li, tr, h1–h6 …) emit a space so adjacent
        /// words don't merge. Inline tags (b, i, span, …) vanish silently.
        /// Reuses a per-instance char buffer — no extra allocation on the hot path.
        /// </summary>
        private string StripHtml(string s)
        {
            if (_stripBuf == null || _stripBuf.Length < s.Length)
                _stripBuf = new char[s.Length];

            int  outLen       = 0;
            bool inTag        = false;
            bool lastWasSpace = true;
            // Collect tag name (up to 16 chars) to decide block vs inline.
            int  tagNameLen   = 0;

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];

                if (inTag)
                {
                    if (c == '>')
                    {
                        // Emit space for block tags only.
                        if (HtmlScannerHelpers.IsBlockTag(_tagName, tagNameLen) && !lastWasSpace)
                        {
                            _stripBuf[outLen++] = ' ';
                            lastWasSpace = true;
                        }
                        inTag      = false;
                        tagNameLen = 0;
                    }
                    else if (tagNameLen < 16 && c != ' ' && c != '\t' && c != '/')
                    {
                        _tagName[tagNameLen++] = c;
                    }
                    continue;
                }

                if (c == '<')
                {
                    inTag      = true;
                    tagNameLen = 0;
                    continue;
                }

                // Collapse whitespace runs.
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                {
                    if (!lastWasSpace)
                    {
                        _stripBuf[outLen++] = ' ';
                        lastWasSpace = true;
                    }
                    continue;
                }

                _stripBuf[outLen++] = c;
                lastWasSpace = false;
            }

            if (outLen > 0 && _stripBuf[outLen - 1] == ' ') outLen--;

            return new string(_stripBuf, 0, outLen);
        }

        // Reusable buffers — allocated once, grown if needed.
        private char[]  _stripBuf;
        private readonly char[] _tagName = new char[16];

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

        // ── Stage 5: render plain text + highlight tags ──────────────

        private string Render(
            string                      text,
            List<TextToken>             tokens,
            IReadOnlyCollection<string> queryTerms,
            int                         snapStart,
            int                         snapEnd)
        {
            var termSet  = new HashSet<string>(queryTerms);
            var hitIndex = new Dictionary<int, TextToken>();
            foreach (var tok in tokens)
                if (tok.RawStart >= snapStart && tok.RawEnd <= snapEnd
                    && termSet.Contains(tok.Normalized))
                    hitIndex[tok.RawStart] = tok;

            var sb = new StringBuilder((snapEnd - snapStart) + hitIndex.Count * 15);

            if (snapStart > 0) sb.Append("…");

            int i = snapStart;
            while (i < snapEnd)
            {
                if (hitIndex.TryGetValue(i, out TextToken hit))
                {
                    sb.Append(_preTag);
                    int end = hit.RawEnd < snapEnd ? hit.RawEnd : snapEnd;
                    sb.Append(text, hit.RawStart, end - hit.RawStart);
                    sb.Append(_postTag);
                    i = hit.RawEnd;
                }
                else
                {
                    // HTML-encode only the chars that matter in a plain-text context.
                    char c = text[i];
                    switch (c)
                    {
                        case '&': sb.Append("&amp;");  break;
                        case '<': sb.Append("&lt;");   break;
                        case '>': sb.Append("&gt;");   break;
                        default:  sb.Append(c);        break;
                    }
                    i++;
                }
            }

            if (snapEnd < text.Length) sb.Append("…");

            return sb.ToString();
        }

        private static string Encode(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            // Minimal HTML encoding for plain-text fallback output.
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
