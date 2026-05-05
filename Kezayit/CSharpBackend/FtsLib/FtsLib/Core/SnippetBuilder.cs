using System.Collections.Generic;
using System.Text;

namespace FtsLib.Core
{
    /// <summary>
    /// Builds a highlighted HTML snippet from raw HTML content and a set of query terms.
    ///
    /// Single-pass design — the raw HTML is scanned exactly once:
    ///   1. <see cref="TokenStream"/> tokenizes the raw HTML, producing tokens with
    ///      raw character positions pointing into the original string.
    ///   2. <see cref="ProximityWindow"/> (inlined) finds the tightest window.
    ///   3. The renderer walks the raw HTML in the window range, stripping tags
    ///      and wrapping matched tokens in highlight tags — no separate strip pass.
    ///
    /// All internal data structures are reused across calls — zero per-call heap
    /// allocation on the hot path. Not thread-safe — one instance per thread.
    /// </summary>
    internal sealed class SnippetBuilder
    {
        private readonly string _preTag;
        private readonly string _postTag;
        private readonly int    _snippetLength;
        private readonly int    _contextMargin;

        private readonly TokenStream _tokenStream = new TokenStream();

        // ── Reused per-call state ─────────────────────────────────────

        private readonly Dictionary<string, int> _termToGroup =
            new Dictionary<string, int>(System.StringComparer.Ordinal);
        private int[] _groupCount = new int[8];

        private readonly HashSet<string>        _termSet  =
            new HashSet<string>(System.StringComparer.Ordinal);
        private readonly StringBuilder          _renderBuf = new StringBuilder(512);

        private readonly HashSet<string> _allTerms =
            new HashSet<string>(System.StringComparer.Ordinal);

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

        public SnippetResult Build(string rawHtml, IReadOnlyCollection<string> queryTerms)
        {
            if (string.IsNullOrEmpty(rawHtml) || queryTerms == null || queryTerms.Count == 0)
                return new SnippetResult(Encode(rawHtml ?? string.Empty), int.MaxValue, false);

            return BuildCoreLiteral(rawHtml, queryTerms);
        }

        public SnippetResult Build(
            string                                     rawHtml,
            IReadOnlyList<IReadOnlyCollection<string>> queryGroups)
        {
            if (string.IsNullOrEmpty(rawHtml) || queryGroups == null || queryGroups.Count == 0)
                return new SnippetResult(Encode(rawHtml ?? string.Empty), int.MaxValue, false);

            _allTerms.Clear();
            foreach (var g in queryGroups)
                foreach (var t in g)
                    _allTerms.Add(t);

            return BuildCoreGroups(rawHtml, queryGroups, _allTerms);
        }

        // ── Core pipelines ────────────────────────────────────────────

        private SnippetResult BuildCoreLiteral(
            string                      rawHtml,
            IReadOnlyCollection<string> queryTerms)
        {
            // 1. Tokenize raw HTML — tokens carry positions into rawHtml.
            var tokens = _tokenStream.Tokenize(rawHtml);
            if (tokens.Count == 0)
                return new SnippetResult(Encode(rawHtml), int.MaxValue, false);

            // 2. Find window (literal: each term is its own group).
            var (winStart, winEnd, score) = FindWindowLiteral(tokens, queryTerms);
            if (score == int.MaxValue)
                return new SnippetResult(Encode(rawHtml), int.MaxValue, false);

            // 3. Expand window.
            var (snapStart, snapEnd) = ExpandWindow(rawHtml.Length, winStart, winEnd);

            // 4. Render directly from raw HTML — single pass, no strip step.
            string html = RenderFromRaw(rawHtml, tokens, queryTerms, snapStart, snapEnd);
            return new SnippetResult(html, score, true);
        }

        private SnippetResult BuildCoreGroups(
            string                                     rawHtml,
            IReadOnlyList<IReadOnlyCollection<string>> queryGroups,
            IReadOnlyCollection<string>                highlightTerms)
        {
            var tokens = _tokenStream.Tokenize(rawHtml);
            if (tokens.Count == 0)
                return new SnippetResult(Encode(rawHtml), int.MaxValue, false);

            var (winStart, winEnd, score) = FindWindowGroups(tokens, queryGroups);
            if (score == int.MaxValue)
                return new SnippetResult(Encode(rawHtml), int.MaxValue, false);

            var (snapStart, snapEnd) = ExpandWindow(rawHtml.Length, winStart, winEnd);
            string html = RenderFromRaw(rawHtml, tokens, highlightTerms, snapStart, snapEnd);
            return new SnippetResult(html, score, true);
        }

        // ── Window finding ────────────────────────────────────────────

        private (int winStart, int winEnd, int score) FindWindowLiteral(
            List<TextToken>             tokens,
            IReadOnlyCollection<string> queryTerms)
        {
            _termToGroup.Clear();
            int g = 0;
            foreach (var t in queryTerms)
                if (!_termToGroup.ContainsKey(t))
                    _termToGroup[t] = g++;

            int required = _termToGroup.Count;
            EnsureGroupCount(required);
            for (int i = 0; i < required; i++) _groupCount[i] = 0;
            return RunSlidingWindow(tokens, required);
        }

        private (int winStart, int winEnd, int score) FindWindowGroups(
            List<TextToken>                            tokens,
            IReadOnlyList<IReadOnlyCollection<string>> queryGroups)
        {
            _termToGroup.Clear();
            for (int gi = 0; gi < queryGroups.Count; gi++)
                foreach (var t in queryGroups[gi])
                    if (!_termToGroup.ContainsKey(t))
                        _termToGroup[t] = gi;

            int required = queryGroups.Count;
            EnsureGroupCount(required);
            for (int i = 0; i < required; i++) _groupCount[i] = 0;
            return RunSlidingWindow(tokens, required);
        }

        private (int winStart, int winEnd, int score) RunSlidingWindow(
            List<TextToken> tokens,
            int             required)
        {
            int covered   = 0;
            int bestStart = -1, bestEnd = -1, bestScore = int.MaxValue;
            int L = 0;

            for (int R = 0; R < tokens.Count; R++)
            {
                string rt = tokens[R].Normalized;
                if (_termToGroup.TryGetValue(rt, out int rg))
                    if (_groupCount[rg]++ == 0) covered++;

                while (covered == required)
                {
                    int span = tokens[R].RawEnd - tokens[L].RawStart;
                    if (span < bestScore)
                    {
                        bestScore = span;
                        bestStart = tokens[L].RawStart;
                        bestEnd   = tokens[R].RawEnd;
                    }
                    string lt = tokens[L].Normalized;
                    if (_termToGroup.TryGetValue(lt, out int lg))
                        if (--_groupCount[lg] == 0) covered--;
                    L++;
                }
            }

            return (bestStart, bestEnd, bestScore);
        }

        private void EnsureGroupCount(int required)
        {
            if (_groupCount.Length < required)
                _groupCount = new int[required * 2];
        }

        // ── Window expansion ──────────────────────────────────────────

        private (int start, int end) ExpandWindow(int rawLen, int winStart, int winEnd)
        {
            int windowLen = winEnd - winStart;
            int remaining = _snippetLength - windowLen;
            int half      = remaining > 0 ? remaining / 2 : 0;
            int margin    = half < _contextMargin ? half : _contextMargin;
            if (margin < 0) margin = 0;
            return (System.Math.Max(0,      winStart - margin),
                    System.Math.Min(rawLen, winEnd   + margin));
        }

        // ── Single-pass renderer from raw HTML ────────────────────────

        /// <summary>
        /// Renders a snippet directly from the raw HTML string — no separate strip pass.
        ///
        /// Instead of walking character-by-character and checking every position
        /// against the hit map, this iterates the token list to find the next hit,
        /// then copies the raw HTML between tokens (skipping tags inline).
        /// This is O(tokens) rather than O(characters) for the hit-check loop.
        /// </summary>
        private string RenderFromRaw(
            string                      rawHtml,
            List<TextToken>             tokens,
            IReadOnlyCollection<string> queryTerms,
            int                         snapStart,
            int                         snapEnd)
        {
            // Build term set.
            _termSet.Clear();
            foreach (var t in queryTerms) _termSet.Add(t);

            _renderBuf.Clear();
            if (snapStart > 0) _renderBuf.Append('…');

            // Walk the raw HTML from snapStart to snapEnd.
            // Use the token list to know where matched words are — jump directly
            // to each token rather than checking every character position.
            int pos = snapStart;

            foreach (var tok in tokens)
            {
                // Skip tokens outside the window.
                if (tok.RawEnd <= snapStart) continue;
                if (tok.RawStart >= snapEnd) break;

                if (!_termSet.Contains(tok.Normalized)) continue;

                // Copy raw HTML from current position up to this token's start,
                // stripping tags inline.
                AppendRawStripped(rawHtml, pos, tok.RawStart, snapEnd);
                pos = tok.RawStart;

                // Emit the highlight tag + the raw token span.
                _renderBuf.Append(_preTag);
                int tokEnd = tok.RawEnd < snapEnd ? tok.RawEnd : snapEnd;
                _renderBuf.Append(rawHtml, tok.RawStart, tokEnd - tok.RawStart);
                _renderBuf.Append(_postTag);
                pos = tok.RawEnd;
            }

            // Copy any remaining raw HTML after the last hit.
            AppendRawStripped(rawHtml, pos, snapEnd, snapEnd);

            if (snapEnd < rawHtml.Length) _renderBuf.Append('…');
            return _renderBuf.ToString();
        }

        /// <summary>
        /// Appends rawHtml[from..to) to _renderBuf, stripping HTML tags and
        /// HTML-encoding &amp; and &gt; in plain text. Stops at limit.
        /// </summary>
        private void AppendRawStripped(string rawHtml, int from, int to, int limit)
        {
            if (to > limit) to = limit;
            bool inTag = false;
            for (int i = from; i < to; i++)
            {
                char c = rawHtml[i];
                if (inTag)      { if (c == '>') inTag = false; continue; }
                if (c == '<')   { inTag = true; continue; }
                switch (c)
                {
                    case '&': _renderBuf.Append("&amp;"); break;
                    case '>': _renderBuf.Append("&gt;");  break;
                    default:  _renderBuf.Append(c);       break;
                }
            }
        }

        private static string Encode(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
