using System.Collections.Generic;
using System.Text;

namespace FtsLib.Core
{
    /// <summary>
    /// Builds a highlighted HTML snippet from raw HTML content and a set of query terms.
    ///
    /// Single-pass design — the raw HTML is scanned exactly once:
    ///   1. <see cref="TokenStream"/> tokenizes the raw HTML, producing tokens with
    ///      raw character positions AND cumulative visible-char offsets.
    ///   2. A sliding-window algorithm finds the tightest token window covering all terms.
    ///   3. ExpandWindow binary-searches the token list to add context — O(log n),
    ///      no re-scanning of the raw string.
    ///   4. The renderer walks only the snippet range, stripping tags inline.
    ///
    /// All internal data structures are reused across calls — zero per-call heap
    /// allocation on the hot path. Not thread-safe — one instance per thread.
    /// </summary>
    internal sealed class SnippetBuilder
    {
        private readonly string _preTag;
        private readonly string _postTag;
        private readonly int    _snippetLength;  // budget in visible chars
        private readonly int    _contextMargin;  // max context on each side, visible chars

        private readonly TokenStream _tokenStream = new TokenStream();

        // ── Reused per-call state ─────────────────────────────────────

        private readonly Dictionary<string, int> _termToGroup =
            new Dictionary<string, int>(System.StringComparer.Ordinal);
        private int[] _groupCount = new int[8];

        private readonly HashSet<string> _termSet    = new HashSet<string>(System.StringComparer.Ordinal);
        private readonly HashSet<string> _allTerms   = new HashSet<string>(System.StringComparer.Ordinal);
        private readonly StringBuilder   _renderBuf  = new StringBuilder(512);

        public SnippetBuilder(
            string preTag        = "<mark>",
            string postTag       = "</mark>",
            int    snippetLength = 400,
            int    contextMargin = 150)
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
                return new SnippetResult(Encode(rawHtml ?? string.Empty), int.MaxValue, int.MaxValue, false);

            var tokens = _tokenStream.Tokenize(rawHtml);
            return BuildCoreLiteral(tokens, rawHtml, queryTerms);
        }

        public SnippetResult Build(
            string                                     rawHtml,
            IReadOnlyList<IReadOnlyCollection<string>> queryGroups,
            bool                                       requireOrdered     = false,
            int                                        originalGroupCount = 0)
        {
            if (string.IsNullOrEmpty(rawHtml) || queryGroups == null || queryGroups.Count == 0)
                return new SnippetResult(Encode(rawHtml ?? string.Empty), int.MaxValue, int.MaxValue, false);

            _allTerms.Clear();
            foreach (var g in queryGroups)
                foreach (var t in g)
                    _allTerms.Add(t);

            int denominator = originalGroupCount > 0 ? originalGroupCount : queryGroups.Count;

            var tokens = _tokenStream.Tokenize(rawHtml);
            var result = BuildCoreGroups(tokens, rawHtml, queryGroups, _allTerms, denominator);

            if (requireOrdered && result.IsMatch && queryGroups.Count > 1)
            {
                if (!HasOrderedMatch(tokens))
                    return new SnippetResult(result.Html, result.Score, result.WordDistance, false);
            }

            return result;
        }

        // ── Core pipelines ────────────────────────────────────────────

        private SnippetResult BuildCoreLiteral(
            List<TextToken>             tokens,
            string                      rawHtml,
            IReadOnlyCollection<string> queryTerms)
        {
            if (tokens.Count == 0)
                return new SnippetResult(Encode(rawHtml), int.MaxValue, int.MaxValue, false);

            var (iLeft, iRight, score) = FindWindowLiteral(tokens, queryTerms);
            if (score == int.MaxValue)
                return new SnippetResult(Encode(rawHtml), int.MaxValue, int.MaxValue, false);

            var (snapStart, snapEnd) = ExpandWindow(tokens, rawHtml.Length, iLeft, iRight);
            string html = RenderFromRaw(rawHtml, tokens, queryTerms, snapStart, snapEnd);
            // WordDistance = extra words between matched terms (0 = all terms consecutive).
            int wordDist = iRight - iLeft - (queryTerms.Count - 1);
            if (wordDist < 0) wordDist = 0;
            return new SnippetResult(html, score, wordDist, true);
        }

        private SnippetResult BuildCoreGroups(
            List<TextToken>                            tokens,
            string                                     rawHtml,
            IReadOnlyList<IReadOnlyCollection<string>> queryGroups,
            IReadOnlyCollection<string>                highlightTerms,
            int                                        originalGroupCount)
        {
            if (tokens.Count == 0)
                return new SnippetResult(Encode(rawHtml), int.MaxValue, int.MaxValue, false);

            var (iLeft, iRight, score) = FindWindowGroups(tokens, queryGroups);
            if (score == int.MaxValue)
                return new SnippetResult(Encode(rawHtml), int.MaxValue, int.MaxValue, false);

            var (snapStart, snapEnd) = ExpandWindow(tokens, rawHtml.Length, iLeft, iRight);
            string html = RenderFromRaw(rawHtml, tokens, highlightTerms, snapStart, snapEnd);
            // Use originalGroupCount so skipped wildcards still count as a slot.
            int wordDist = iRight - iLeft - (originalGroupCount - 1);
            if (wordDist < 0) wordDist = 0;
            return new SnippetResult(html, score, wordDist, true);
        }

        // ── Window finding ────────────────────────────────────────────
        // Returns (iLeft, iRight, score) — token indices, not raw positions.

        private (int iLeft, int iRight, int score) FindWindowLiteral(
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

        private (int iLeft, int iRight, int score) FindWindowGroups(
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

        private (int iLeft, int iRight, int score) RunSlidingWindow(
            List<TextToken> tokens,
            int             required)
        {
            int covered    = 0;
            int bestILeft  = -1, bestIRight = -1, bestScore = int.MaxValue;
            int L = 0;

            for (int R = 0; R < tokens.Count; R++)
            {
                string rt = tokens[R].Normalized;
                if (_termToGroup.TryGetValue(rt, out int rg))
                    if (_groupCount[rg]++ == 0) covered++;

                while (covered == required)
                {
                    // Score in raw chars (used only for picking the tightest window).
                    int span = tokens[R].RawEnd - tokens[L].RawStart;
                    if (span < bestScore)
                    {
                        bestScore  = span;
                        bestILeft  = L;
                        bestIRight = R;
                    }
                    string lt = tokens[L].Normalized;
                    if (_termToGroup.TryGetValue(lt, out int lg))
                        if (--_groupCount[lg] == 0) covered--;
                    L++;
                }
            }

            return (bestILeft, bestIRight, bestScore);
        }

        private void EnsureGroupCount(int required)
        {
            if (_groupCount.Length < required)
                _groupCount = new int[required * 2];
        }

        // ── Ordered-match validation ──────────────────────────────────

        /// <summary>
        /// Returns true when there exists a position in <paramref name="tokens"/>
        /// where each query group is satisfied by a token appearing strictly after
        /// the token satisfying the previous group (left-to-right order).
        ///
        /// Relies on <see cref="_termToGroup"/> already being populated by the
        /// preceding <see cref="FindWindowGroups"/> call — no rebuild needed.
        /// Uses a greedy forward scan: O(n) in token count.
        /// </summary>
        private bool HasOrderedMatch(List<TextToken> tokens)
        {
            // _termToGroup is already populated by FindWindowGroups.
            // Determine the number of groups from the max value stored.
            int numGroups = 0;
            foreach (var kv in _termToGroup)
                if (kv.Value >= numGroups) numGroups = kv.Value + 1;

            if (numGroups <= 1) return true; // single group — order is trivially satisfied

            // Try every starting token that belongs to group 0.
            for (int start = 0; start < tokens.Count; start++)
            {
                if (!_termToGroup.TryGetValue(tokens[start].Normalized, out int g0) || g0 != 0)
                    continue;

                // Greedily advance through groups 1..numGroups-1.
                int pos       = start + 1;
                int nextGroup = 1;
                while (nextGroup < numGroups && pos < tokens.Count)
                {
                    if (_termToGroup.TryGetValue(tokens[pos].Normalized, out int tg) && tg == nextGroup)
                        nextGroup++;
                    pos++;
                }

                if (nextGroup == numGroups)
                    return true;
            }

            return false;
        }

        // ── Window expansion ──────────────────────────────────────────

        /// <summary>
        /// Expands the match window (given as token indices iLeft..iRight) by up to
        /// <see cref="_contextMargin"/> visible chars on each side, capped so the total
        /// never exceeds <see cref="_snippetLength"/> visible chars.
        ///
        /// The context margin scales down when the match window is large (terms far apart),
        /// so distant-term matches get a tight snippet rather than dumping the whole line.
        ///
        /// Uses binary search on VisibleStart — O(log n), no re-scanning.
        /// Returns raw character positions (snapStart, snapEnd).
        /// </summary>
        private (int snapStart, int snapEnd) ExpandWindow(
            List<TextToken> tokens, int rawLen, int iLeft, int iRight)
        {
            if (iLeft < 0 || iRight < 0 || tokens.Count == 0)
                return (0, rawLen);

            int visLeft    = tokens[iLeft].VisibleStart;
            int visRight   = tokens[iRight].VisibleStart + tokens[iRight].Normalized.Length;
            int winVisible = visRight - visLeft;

            // Total visible chars in the whole line.
            int totalVisible = tokens[tokens.Count - 1].VisibleStart
                               + tokens[tokens.Count - 1].Normalized.Length;

            // If the whole line fits within the budget, show it all — no truncation.
            if (totalVisible <= _snippetLength)
                return (0, rawLen);

            int sIdx, eIdx;

            if (winVisible >= _snippetLength)
            {
                // Window alone exceeds budget — centre-crop by token count.
                int centre = (iLeft + iRight) / 2;
                int half   = _snippetLength / 2;
                sIdx = System.Math.Max(0,              centre - half);
                eIdx = System.Math.Min(tokens.Count-1, centre + half);
            }
            else
            {
                int remaining = _snippetLength - winVisible;

                // Scale the margin: full margin for tight matches, near-zero for loose ones.
                int scaledMargin = (int)(_contextMargin * (1.0 - (double)winVisible / _snippetLength));
                int margin       = System.Math.Min(remaining / 2, scaledMargin);

                int targetLeft  = visLeft  - margin;
                int targetRight = visRight + margin;
                sIdx = BinarySearchLeft (tokens, targetLeft);
                eIdx = BinarySearchRight(tokens, targetRight);
            }

            int snapStart = tokens[sIdx].RawStart;
            int snapEnd   = eIdx + 1 < tokens.Count ? tokens[eIdx + 1].RawStart : rawLen;

            return (System.Math.Max(0, snapStart), System.Math.Min(rawLen, snapEnd));
        }

        /// <summary>First token index with VisibleStart >= target (or 0).</summary>
        private static int BinarySearchLeft(List<TextToken> tokens, int target)
        {
            int lo = 0, hi = tokens.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (tokens[mid].VisibleStart < target) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        /// <summary>Last token index with VisibleStart <= target (or tokens.Count-1).</summary>
        private static int BinarySearchRight(List<TextToken> tokens, int target)
        {
            int lo = 0, hi = tokens.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (tokens[mid].VisibleStart <= target) lo = mid;
                else hi = mid - 1;
            }
            return lo;
        }

        // ── Single-pass renderer from raw HTML ────────────────────────

        private string RenderFromRaw(
            string                      rawHtml,
            List<TextToken>             tokens,
            IReadOnlyCollection<string> queryTerms,
            int                         snapStart,
            int                         snapEnd)
        {
            _termSet.Clear();
            foreach (var t in queryTerms) _termSet.Add(t);

            _renderBuf.Clear();
            if (snapStart > 0) _renderBuf.Append('…');

            int pos = snapStart;

            foreach (var tok in tokens)
            {
                if (tok.RawEnd   <= snapStart) continue;
                if (tok.RawStart >= snapEnd)   break;
                if (!_termSet.Contains(tok.Normalized)) continue;

                AppendRawStripped(rawHtml, pos, tok.RawStart, snapEnd);
                pos = tok.RawStart;

                _renderBuf.Append(_preTag);
                int tokEnd = tok.RawEnd < snapEnd ? tok.RawEnd : snapEnd;
                _renderBuf.Append(rawHtml, tok.RawStart, tokEnd - tok.RawStart);
                _renderBuf.Append(_postTag);
                pos = tok.RawEnd;
            }

            AppendRawStripped(rawHtml, pos, snapEnd, snapEnd);

            if (snapEnd < rawHtml.Length) _renderBuf.Append('…');
            return _renderBuf.ToString();
        }

        /// <summary>
        /// Appends rawHtml[from..to) to _renderBuf, stripping HTML tags.
        /// If from lands mid-tag, scans backwards to detect and skip the partial tag.
        /// </summary>
        private void AppendRawStripped(string rawHtml, int from, int to, int limit)
        {
            if (to > limit) to = limit;

            bool inTag = false;
            for (int k = from - 1; k >= 0; k--)
            {
                if (rawHtml[k] == '>') break;
                if (rawHtml[k] == '<') { inTag = true; break; }
            }

            for (int i = from; i < to; i++)
            {
                char c = rawHtml[i];
                if (inTag)    { if (c == '>') inTag = false; continue; }
                if (c == '<') { inTag = true; continue; }
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
