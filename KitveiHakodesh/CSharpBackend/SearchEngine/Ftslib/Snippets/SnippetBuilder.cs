using FtsLib.Tokenization;
using System.Collections.Generic;
using System.Text;

namespace FtsLib.Snippets
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
        private readonly int    _contextWords;    // words of context on each side of the match

        // Safety ceiling in visible chars — only fires for pathologically long lines
        // (e.g. a single paragraph with no word breaks). Normal lines never hit this.
        private const int SafetyCeiling = 4000;

        private readonly TokenStream _tokenStream = new TokenStream();

        // ── Reused per-call state ─────────────────────────────────────

        private readonly Dictionary<string, int> _termToGroup =
            new Dictionary<string, int>(System.StringComparer.Ordinal);
        private int[] _groupCount = new int[8];

        private readonly HashSet<string> _termSet    = new HashSet<string>(System.StringComparer.Ordinal);
        private readonly HashSet<string> _allTerms   = new HashSet<string>(System.StringComparer.Ordinal);
        private readonly StringBuilder   _renderBuf  = new StringBuilder(512);

        public SnippetBuilder(
            string preTag       = "<mark>",
            string postTag      = "</mark>",
            int    contextWords = 8)
        {
            _preTag       = preTag;
            _postTag      = postTag;
            _contextWords = contextWords;
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
        /// Expands the match window (given as token indices iLeft..iRight) by
        /// <see cref="_contextWords"/> tokens on each side.
        ///
        /// The visible-char span is read directly from the already-built token list
        /// (tokens[sIdx].VisibleStart and tokens[eIdx].VisibleStart + word length) —
        /// no second pass over the source string. A safety ceiling of
        /// <see cref="SafetyCeiling"/> visible chars guards against pathological lines
        /// that have no word breaks; it never fires on normal Hebrew text.
        ///
        /// Returns raw character positions (snapStart, snapEnd).
        /// </summary>
        private (int snapStart, int snapEnd) ExpandWindow(
            List<TextToken> tokens, int rawLen, int iLeft, int iRight)
        {
            if (iLeft < 0 || iRight < 0 || tokens.Count == 0)
                return (0, rawLen);

            // Expand by word count on each side — exact, reads from the token list.
            int sIdx = System.Math.Max(0,              iLeft  - _contextWords);
            int eIdx = System.Math.Min(tokens.Count-1, iRight + _contextWords);

            // Safety ceiling: trim from the outside only when the expanded window
            // exceeds SafetyCeiling visible chars. Reads token positions — no rescan.
            // Never trims past the match boundaries (iLeft..iRight must stay inside).
            while (sIdx < eIdx)
            {
                int visStart = tokens[sIdx].VisibleStart;
                int visEnd   = tokens[eIdx].VisibleStart + tokens[eIdx].Normalized.Length;
                if (visEnd - visStart <= SafetyCeiling) break;
                bool canTrimLeft  = sIdx < iLeft;
                bool canTrimRight = eIdx > iRight;
                if (!canTrimLeft && !canTrimRight) break; // match itself exceeds ceiling — show it anyway
                int trimLeft  = canTrimLeft  ? tokens[sIdx + 1].VisibleStart - visStart : int.MaxValue;
                int trimRight = canTrimRight ? visEnd - (tokens[eIdx - 1].VisibleStart + tokens[eIdx - 1].Normalized.Length) : int.MaxValue;
                if (trimLeft <= trimRight) sIdx++;
                else                      eIdx--;
            }

            // snapStart: if we're showing from the first token of the line, start at 0
            // so no ellipsis is prepended and no leading tag/whitespace is skipped.
            // If we're mid-line, start at the first letter of the first context word.
            int snapStart = sIdx == 0 ? 0 : tokens[sIdx].RawStart;

            // snapEnd: use RawEnd of the last context token (includes its trailing
            // separator chars) rather than RawStart of the next token, which would
            // cut the gap between the last word and whatever follows it.
            int snapEnd = eIdx + 1 < tokens.Count ? tokens[eIdx].RawEnd : rawLen;

            return (System.Math.Max(0, snapStart), System.Math.Min(rawLen, snapEnd));
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
        /// HTML entities (e.g. &amp;nbsp;) are passed through as-is — the raw HTML
        /// already contains valid entities and the output is rendered via v-html.
        /// Paragraph markers of the form {X} where X is a Hebrew letter are stripped.
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
                if (inTag) { if (c == '>') inTag = false; continue; }
                if (c == '<') { inTag = true; continue; }

                // Strip {X} paragraph markers where X is a Hebrew letter (U+05D0–U+05EA).
                if (c == '{' && i + 2 < to && rawHtml[i + 2] == '}')
                {
                    char inner = rawHtml[i + 1];
                    if (inner >= '\u05D0' && inner <= '\u05EA') { i += 2; continue; }
                }

                // Pass through as-is — raw HTML already contains valid entities.
                _renderBuf.Append(c);
            }
        }

        /// <summary>
        /// Strips HTML tags and {X} paragraph markers from a raw HTML string,
        /// returning plain renderable text. Used for the no-match fallback path.
        /// </summary>
        private static string Encode(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            bool inTag = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (inTag) { if (c == '>') inTag = false; continue; }
                if (c == '<') { inTag = true; continue; }
                if (c == '{' && i + 2 < s.Length && s[i + 2] == '}')
                {
                    char inner = s[i + 1];
                    if (inner >= '\u05D0' && inner <= '\u05EA') { i += 2; continue; }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
