using FtsLib.Tokenization;
using System.Collections.Generic;
using System.Text;

namespace FtsLib.Snippets
{
    /// <summary>
    /// Builds a highlighted HTML snippet from raw HTML content and a prepared query.
    ///
    /// Single-pass design — the raw HTML is scanned exactly once:
    ///   1. <see cref="TokenStream"/> tokenizes the raw HTML, producing tokens with
    ///      raw character positions AND cumulative visible-char offsets.
    ///   2. A sliding-window algorithm finds the tightest token window covering all terms.
    ///   3. ExpandWindow binary-searches the token list to add context — O(log n),
    ///      no re-scanning of the raw string.
    ///   4. The renderer walks only the snippet range, stripping tags inline.
    ///
    /// Query state (the term→group map) lives in <see cref="PreparedQueryGroups"/> —
    /// immutable, built once per query, shared read-only across every line and every
    /// thread. The builder itself holds only pure per-call scratch buffers (token
    /// stream, group counters, render buffer), reused across calls with zero
    /// per-call heap allocation. Per-line cost is therefore independent of how many
    /// terms the query expanded to. Not thread-safe — one instance per thread.
    /// </summary>
    internal sealed class SnippetBuilder
    {
        private readonly string _preTag;
        private readonly string _postTag;
        private readonly int    _contextWords;    // words of context on each side of the match

        // Safety ceiling in visible chars — only fires for pathologically long lines
        // (e.g. a single paragraph with no word breaks). Normal lines never hit this.
        private const int SafetyCeiling = 4000;

        // ── Per-call scratch (no query state — that is PreparedQueryGroups) ──

        private readonly TokenStream   _tokenStream = new TokenStream();
        private          int[]         _groupCount  = new int[8];
        private readonly StringBuilder _renderBuf   = new StringBuilder(512);

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

        /// <summary>
        /// The core build. <paramref name="prepared"/> is the query's immutable
        /// term→group map — prepare it ONCE per query (not per line) and share it
        /// across every result and every thread.
        /// </summary>
        public SnippetResult Build(
            string              rawHtml,
            PreparedQueryGroups prepared,
            bool                requireOrdered     = false,
            int                 originalGroupCount = 0)
        {
            if (string.IsNullOrEmpty(rawHtml) || prepared == null || prepared.IsEmpty)
                return new SnippetResult(Encode(rawHtml ?? string.Empty), int.MaxValue, int.MaxValue, false);

            var tokens = _tokenStream.Tokenize(rawHtml);
            if (tokens.Count == 0)
                return new SnippetResult(Encode(rawHtml), int.MaxValue, int.MaxValue, false);

            var (iLeft, iRight, score) = FindWindow(tokens, prepared);
            if (score == int.MaxValue)
                return new SnippetResult(Encode(rawHtml), int.MaxValue, int.MaxValue, false);

            var (snapStart, snapEnd, sIdx, eIdx) = ExpandWindow(tokens, rawHtml.Length, iLeft, iRight);
            string html = RenderFromRaw(rawHtml, tokens, prepared, snapStart, snapEnd);

            // Visible words actually shown in the window — lets the caller detect a
            // "short" snippet for free (no re-scan, no DB), e.g. to embellish it with
            // surrounding lines when it spans fewer words than the requested context.
            int windowWords = eIdx - sIdx + 1;

            // WordDistance = extra words between matched slots (0 = all consecutive).
            // originalGroupCount overrides so skipped wildcards still count as a slot.
            int denominator = originalGroupCount > 0 ? originalGroupCount : prepared.GroupCount;
            int wordDist = iRight - iLeft - (denominator - 1);
            if (wordDist < 0) wordDist = 0;

            if (requireOrdered && prepared.GroupCount > 1 && !HasOrderedMatch(tokens, prepared))
                return new SnippetResult(html, score, wordDist, false, windowWords);

            return new SnippetResult(html, score, wordDist, true, windowWords);
        }

        /// <summary>
        /// Convenience for one-off literal-term builds (each term occurrence is its
        /// own AND slot). Prepares per call — use the <see cref="PreparedQueryGroups"/>
        /// overload when building snippets for many lines of one query.
        /// </summary>
        public SnippetResult Build(string rawHtml, IReadOnlyCollection<string> queryTerms)
            => Build(rawHtml, PreparedQueryGroups.FromLiteralTerms(queryTerms));

        /// <summary>
        /// Convenience for one-off group builds. Prepares per call — use the
        /// <see cref="PreparedQueryGroups"/> overload when building snippets for
        /// many lines of one query.
        /// </summary>
        public SnippetResult Build(
            string                                     rawHtml,
            IReadOnlyList<IReadOnlyCollection<string>> queryGroups,
            bool                                       requireOrdered     = false,
            int                                        originalGroupCount = 0)
            => Build(rawHtml, PreparedQueryGroups.FromGroups(queryGroups),
                     requireOrdered, originalGroupCount);

        // ── Window finding ────────────────────────────────────────────
        // Returns (iLeft, iRight, score) — token indices, not raw positions.

        private (int iLeft, int iRight, int score) FindWindow(
            List<TextToken>     tokens,
            PreparedQueryGroups prepared)
        {
            int required = prepared.GroupCount;
            EnsureGroupCount(required);
            for (int i = 0; i < required; i++) _groupCount[i] = 0;

            int covered    = 0;
            int bestILeft  = -1, bestIRight = -1, bestScore = int.MaxValue;
            int L = 0;

            for (int R = 0; R < tokens.Count; R++)
            {
                if (prepared.TryGetGroups(tokens[R].Normalized, out int[] rGroups))
                {
                    foreach (int rg in rGroups)
                        if (_groupCount[rg]++ == 0) covered++;
                }

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
                    if (prepared.TryGetGroups(tokens[L].Normalized, out int[] lGroups))
                    {
                        foreach (int lg in lGroups)
                            if (--_groupCount[lg] == 0) covered--;
                    }
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
        /// Uses a greedy forward scan: O(n) in token count.
        /// </summary>
        private static bool HasOrderedMatch(List<TextToken> tokens, PreparedQueryGroups prepared)
        {
            int numGroups = prepared.GroupCount;
            if (numGroups <= 1) return true; // single group — order is trivially satisfied

            // Try every starting token that belongs to group 0.
            for (int start = 0; start < tokens.Count; start++)
            {
                if (!prepared.TryGetGroups(tokens[start].Normalized, out int[] startGroups)
                    || System.Array.IndexOf(startGroups, 0) < 0)
                    continue;

                // Greedily advance through groups 1..numGroups-1.
                int pos       = start + 1;
                int nextGroup = 1;
                while (nextGroup < numGroups && pos < tokens.Count)
                {
                    if (prepared.TryGetGroups(tokens[pos].Normalized, out int[] tGroups)
                        && System.Array.IndexOf(tGroups, nextGroup) >= 0)
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
        /// Returns raw character positions (snapStart, snapEnd) plus the token
        /// indices (sIdx, eIdx) of the window bounds so the caller can report how
        /// many words the window spans.
        /// </summary>
        private (int snapStart, int snapEnd, int sIdx, int eIdx) ExpandWindow(
            List<TextToken> tokens, int rawLen, int iLeft, int iRight)
        {
            if (iLeft < 0 || iRight < 0 || tokens.Count == 0)
                return (0, rawLen, 0, tokens.Count > 0 ? tokens.Count - 1 : 0);

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

            return (System.Math.Max(0, snapStart), System.Math.Min(rawLen, snapEnd), sIdx, eIdx);
        }

        // ── Single-pass renderer from raw HTML ────────────────────────

        private string RenderFromRaw(
            string              rawHtml,
            List<TextToken>     tokens,
            PreparedQueryGroups prepared,
            int                 snapStart,
            int                 snapEnd)
        {
            _renderBuf.Clear();
            if (snapStart > 0) _renderBuf.Append('…');

            int pos = snapStart;

            foreach (var tok in tokens)
            {
                if (tok.RawEnd   <= snapStart) continue;
                if (tok.RawStart >= snapEnd)   break;
                if (!prepared.ContainsTerm(tok.Normalized)) continue;

                AppendRawStripped(rawHtml, pos, tok.RawStart, snapEnd);
                pos = tok.RawStart;

                _renderBuf.Append(_preTag);
                int tokEnd = tok.RawEnd < snapEnd ? tok.RawEnd : snapEnd;
                // The token's raw span can cross inline tags (word-transparent
                // formatting, e.g. ורא<b>ה</b>) — strip them instead of copying
                // raw: a pair that closes beyond the token would emit an
                // unbalanced opener inside <mark> and bleed formatting over the
                // rest of the snippet. Nikud and letters pass through untouched.
                AppendRawStripped(rawHtml, tok.RawStart, tokEnd, snapEnd);
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
