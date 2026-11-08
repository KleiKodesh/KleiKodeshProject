using FtsLib.Tokenization;
using System.Collections.Generic;

namespace FtsLib.Snippets
{
    /// <summary>
    /// Locates the snippet window in a line of raw HTML for a prepared query and describes it
    /// as offsets — see <see cref="SnippetDescriptor"/>. Emits NO text: no HTML, no stripped
    /// copy, no string at all. Rendering is the consumer's job (the page).
    ///
    /// Single pass — the raw HTML is scanned exactly once:
    ///   1. <see cref="TokenStream"/> tokenizes it, producing tokens with raw character
    ///      positions AND cumulative visible-char offsets.
    ///   2. A sliding window finds the tightest token window covering all query groups.
    ///   3. ExpandWindow widens it by context words, reading the token list only — O(1) per
    ///      side, no re-scan of the raw string.
    ///   4. CollectMarkRanges walks the tokens inside the window and records the matched ones.
    ///
    /// Query state (the term→group map) lives in <see cref="PreparedQueryGroups"/> —
    /// immutable, built once per query, shared read-only across every line and thread. The
    /// locator holds only per-call scratch (token stream, group counters, mark buffer), reused
    /// across calls with zero per-call heap allocation; the descriptor it returns views that
    /// scratch. Per-line cost is independent of how many terms the query expanded to.
    /// Not thread-safe — one instance per thread.
    /// </summary>
    internal sealed class SnippetWindowLocator
    {
        private readonly int _contextWords;    // words of context on each side of the match

        // Safety ceiling in visible chars — only fires for pathologically long lines
        // (e.g. a single paragraph with no word breaks). Normal lines never hit this.
        private const int SafetyCeiling = 4000;

        // ── Per-call scratch (no query state — that is PreparedQueryGroups) ──

        private readonly TokenStream _tokenStream = new TokenStream();
        private          int[]       _groupCount  = new int[8];
        private readonly List<int>   _markRanges  = new List<int>(24);
        private          bool[]      _markMask    = new bool[32];   // occurrence marks (issue #4)
        private          int[]       _last        = new int[8];     // in order: latest chain start per group
        private          int[]       _starts      = new int[32];    // in order: per (group, token) prefix start
        private          int[]       _ends        = new int[32];    // in order: per (group, token) suffix end

        public SnippetWindowLocator(int contextWords = 8)
        {
            _contextWords = contextWords;
        }

        // ── Public API ───────────────────────────────────────────────

        /// <summary>
        /// The core locate. <paramref name="prepared"/> is the query's immutable term→group
        /// map — prepare it ONCE per query (not per line) and share it across every result
        /// and every thread. The returned descriptor's marks view this locator's scratch and
        /// are overwritten by the next call.
        /// </summary>
        public SnippetDescriptor Locate(
            string              rawHtml,
            PreparedQueryGroups prepared,
            bool                requireOrdered     = false,
            int                 originalGroupCount = 0,
            int                 maxWordDistance    = int.MaxValue)
        {
            if (string.IsNullOrEmpty(rawHtml) || prepared == null || prepared.IsEmpty)
                return SnippetDescriptor.NoMatch;

            var tokens = _tokenStream.Tokenize(rawHtml);
            if (tokens.Count == 0)
                return SnippetDescriptor.NoMatch;

            // In order: the best window is the tightest ORDERED covering window and the marks
            // the tokens of ordered occurrences; the order check is this window's existence.
            // Order off: unchanged.
            bool ordered = requireOrdered && prepared.GroupCount > 1;
            var (iLeft, iRight, score) = ordered ? OrderedCoverWindow(tokens, prepared) : (-1, -1, int.MaxValue);
            bool orderedFound = score != int.MaxValue;
            if (!orderedFound)
                (iLeft, iRight, score) = FindWindow(tokens, prepared);
            if (score == int.MaxValue)
                return SnippetDescriptor.NoMatch;

            var (snapStart, snapEnd, sIdx, eIdx) = ExpandWindow(tokens, rawHtml.Length, iLeft, iRight);

            // Visible words actually inside the window — lets the caller detect a "short"
            // snippet for free (no re-scan, no DB) and widen it over neighbouring lines.
            int windowWords = eIdx - sIdx + 1;

            // WordDistance = extra words between matched slots (0 = all consecutive).
            // originalGroupCount overrides so skipped wildcards still count as a slot.
            int denominator = originalGroupCount > 0 ? originalGroupCount : prepared.GroupCount;

            // Marks: the tokens of full occurrences (issue #4). Without a distance every
            // term token in the snippet is in a covering window, so only the caller's
            // distance filter narrows them.
            long maxSpan = (long)maxWordDistance + (denominator - 1);
            bool masked = orderedFound
                ? OrderedOccurrenceMarks(tokens, prepared, sIdx, eIdx, iLeft, iRight, maxSpan)
                : maxWordDistance != int.MaxValue
                  && OccurrenceMarks(tokens, prepared, sIdx, eIdx, iLeft, iRight, maxSpan);
            CollectMarkRanges(tokens, prepared, snapStart, snapEnd, masked ? sIdx : -1);
            // Words between the ends, by position (a paired reading's second reading shares
            // its first reading's position).
            int wordDist = tokens[iRight].Position - tokens[iLeft].Position - (denominator - 1);
            if (wordDist < 0) wordDist = 0;

            bool isMatch = !ordered || orderedFound;

            return new SnippetDescriptor(isMatch, score, wordDist, windowWords, snapStart, snapEnd,
                leadingEllipsis: snapStart > 0, trailingEllipsis: snapEnd < rawHtml.Length,
                markRanges: _markRanges, terms: prepared);
        }

        /// <summary>
        /// Convenience for one-off literal-term locates (each term occurrence is its own AND
        /// slot). Prepares per call — use the <see cref="PreparedQueryGroups"/> overload when
        /// locating for many lines of one query.
        /// </summary>
        public SnippetDescriptor Locate(string rawHtml, IReadOnlyCollection<string> queryTerms)
            => Locate(rawHtml, PreparedQueryGroups.FromLiteralTerms(queryTerms));

        /// <summary>
        /// Convenience for one-off group locates. Prepares per call — use the
        /// <see cref="PreparedQueryGroups"/> overload when locating for many lines of one query.
        /// </summary>
        public SnippetDescriptor Locate(
            string                                     rawHtml,
            IReadOnlyList<IReadOnlyCollection<string>> queryGroups,
            bool                                       requireOrdered     = false,
            int                                        originalGroupCount = 0)
            => Locate(rawHtml, PreparedQueryGroups.FromGroups(queryGroups),
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
                if (prepared.TryGetGroups(tokens[R].NormSpan, out int[] rGroups))
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
                    if (prepared.TryGetGroups(tokens[L].NormSpan, out int[] lGroups))
                    {
                        foreach (int lg in lGroups)
                            if (--_groupCount[lg] == 0) covered--;
                    }
                    L++;
                }
            }

            return (bestILeft, bestIRight, bestScore);
        }

        /// <summary>
        /// True when <paramref name="rawHtml"/> carries a quoted phrase: a word of each of
        /// <paramref name="phrase"/>'s groups at consecutive positions, in group order
        /// (positions as the tokenizer gives them, so a paired reading's second reading
        /// stands at its first reading's position).
        /// </summary>
        public bool ContainsPhrase(string rawHtml, PreparedQueryGroups phrase)
        {
            if (string.IsNullOrEmpty(rawHtml) || phrase == null || phrase.IsEmpty) return false;
            var tokens = _tokenStream.Tokenize(rawHtml);
            int n = phrase.GroupCount;
            var at = new HashSet<int>[n];
            for (int g = 0; g < n; g++) at[g] = new HashSet<int>();
            foreach (var t in tokens)
                if (phrase.TryGetGroups(t.NormSpan, out int[] groups))
                    foreach (int g in groups) at[g].Add(t.Position);
            foreach (int p in at[0])
            {
                int g = 1;
                while (g < n && at[g].Contains(p + g)) g++;
                if (g == n) return true;
            }
            return false;
        }

        private void EnsureGroupCount(int required)
        {
            if (_groupCount.Length < required)
                _groupCount = new int[required * 2];
        }

        // ── Ordered-match validation ──────────────────────────────────

        /// <summary>
        /// In order: the tightest (iLeft, iRight, raw-char span) window holding one token per
        /// group in query order (by token index, a token in one slot), the earliest on a tie;
        /// score int.MaxValue without one. Per group the latest chain start so far; a chain
        /// ending at a token of the last group starts there. One pass, O(tokens x groups).
        /// </summary>
        private (int iLeft, int iRight, int score) OrderedCoverWindow(
            List<TextToken> tokens, PreparedQueryGroups prepared)
        {
            int k = prepared.GroupCount;
            if (_last.Length < k) _last = new int[k * 2];
            for (int g = 0; g < k; g++) _last[g] = -1;
            int bestL = -1, bestR = -1, bestScore = int.MaxValue;
            for (int t = 0; t < tokens.Count; t++)
            {
                if (!prepared.TryGetGroups(tokens[t].NormSpan, out int[] gs)) continue;
                if (_starts.Length < gs.Length) _starts = new int[gs.Length * 2];
                // read the previous group's state before this token updates it
                for (int i = 0; i < gs.Length; i++) _starts[i] = gs[i] == 0 ? t : _last[gs[i] - 1];
                for (int i = 0; i < gs.Length; i++)
                {
                    int g = gs[i], v = _starts[i];
                    if (v < 0) continue;
                    if (g == k - 1)
                    {
                        int span = tokens[t].RawEnd - tokens[v].RawStart;
                        if (span < bestScore) { bestScore = span; bestL = v; bestR = t; }
                    }
                    else if (v > _last[g]) _last[g] = v;
                }
            }
            return (bestL, bestR, bestScore);
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
                int visEnd   = tokens[eIdx].VisibleStart + tokens[eIdx].NormLen;
                if (visEnd - visStart <= SafetyCeiling) break;
                bool canTrimLeft  = sIdx < iLeft;
                bool canTrimRight = eIdx > iRight;
                if (!canTrimLeft && !canTrimRight) break; // match itself exceeds ceiling — show it anyway
                int trimLeft  = canTrimLeft  ? tokens[sIdx + 1].VisibleStart - visStart : int.MaxValue;
                int trimRight = canTrimRight ? visEnd - (tokens[eIdx - 1].VisibleStart + tokens[eIdx - 1].NormLen) : int.MaxValue;
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

        // ── Mark ranges ───────────────────────────────────────────────

        /// <summary>
        /// Records every query-term token inside [snapStart, snapEnd) as a flat
        /// (rawStart, rawLength, termOrdinal) triple in <see cref="_markRanges"/>, document
        /// order, the length clipped to snapEnd, the ordinal being WHICH query term the token
        /// matched (<see cref="PreparedQueryGroups.TermAt"/>) — so a hit can name its own
        /// matched terms instead of the whole expansion. A token's raw span may cross inline
        /// tags (e.g. word-transparent formatting inside a word); the renderer strips those —
        /// the offsets here are over the raw text, untouched.
        /// </summary>
        private void CollectMarkRanges(
            List<TextToken>     tokens,
            PreparedQueryGroups prepared,
            int                 snapStart,
            int                 snapEnd,
            int                 maskStart)
        {
            _markRanges.Clear();
            for (int t = 0; t < tokens.Count; t++)
            {
                var tok = tokens[t];
                if (tok.RawEnd   <= snapStart) continue;
                if (tok.RawStart >= snapEnd)   break;
                if (!prepared.TryGetTerm(tok.NormSpan, out var term)) continue;
                // only tokens of a full occurrence inside the window (issue #4)
                if (maskStart >= 0 && (t < maskStart || t - maskStart >= _markMask.Length || !_markMask[t - maskStart]))
                    continue;

                int tokEnd = tok.RawEnd < snapEnd ? tok.RawEnd : snapEnd;
                _markRanges.Add(tok.RawStart);
                _markRanges.Add(tokEnd - tok.RawStart);
                _markRanges.Add(term.Ordinal);
            }
        }

        // ── Occurrence marks (issue #4) ───────────────────────────────

        /// <summary>
        /// In order: marks in <see cref="_markMask"/> every term token of sIdx..eIdx in an
        /// ordered chain (one token per group, query order) lying in sIdx..eIdx whose ends are
        /// at most <paramref name="maxSpan"/> positions apart: t in slot g qualifies when the
        /// latest prefix chain start ending at t and the earliest suffix chain end starting at
        /// t are within the span. False when every term token qualifies anyway (none outside
        /// the best window, exactly one per group: the best window's own chain).
        /// </summary>
        private bool OrderedOccurrenceMarks(
            List<TextToken> tokens, PreparedQueryGroups prepared,
            int sIdx, int eIdx, int iLeft, int iRight, long maxSpan)
        {
            int k = prepared.GroupCount, nTerms = 0;
            bool outside = false;
            for (int t = sIdx; t <= eIdx; t++)
                if (prepared.TryGetGroups(tokens[t].NormSpan, out _))
                {
                    nTerms++;
                    if (t < iLeft || t > iRight) outside = true;
                }
            if (!outside && nTerms == k) return false;

            int w = eIdx - sIdx + 1;
            if (_markMask.Length < w) _markMask = new bool[w * 2];
            System.Array.Clear(_markMask, 0, _markMask.Length);
            if (_starts.Length < k * w) _starts = new int[k * w * 2];
            if (_ends.Length < k * w) _ends = new int[k * w * 2];
            for (int i = 0; i < k * w; i++) { _starts[i] = -1; _ends[i] = -1; }
            if (_last.Length < k) _last = new int[k * 2];

            for (int g = 0; g < k; g++) _last[g] = -1;
            for (int t = sIdx; t <= eIdx; t++)
            {
                if (!prepared.TryGetGroups(tokens[t].NormSpan, out int[] gs)) continue;
                foreach (int g in gs) _starts[g * w + t - sIdx] = g == 0 ? t : _last[g - 1];
                foreach (int g in gs)
                {
                    int v = _starts[g * w + t - sIdx];
                    if (v > _last[g]) _last[g] = v;
                }
            }
            for (int g = 0; g < k; g++) _last[g] = -1;
            for (int t = eIdx; t >= sIdx; t--)
            {
                if (!prepared.TryGetGroups(tokens[t].NormSpan, out int[] gs)) continue;
                foreach (int g in gs) _ends[g * w + t - sIdx] = g == k - 1 ? t : _last[g + 1];
                foreach (int g in gs)
                {
                    int v = _ends[g * w + t - sIdx];
                    if (v >= 0 && (_last[g] < 0 || v < _last[g])) _last[g] = v;
                }
            }
            for (int t = sIdx; t <= eIdx; t++)
            {
                if (!prepared.TryGetGroups(tokens[t].NormSpan, out int[] gs)) continue;
                foreach (int g in gs)
                {
                    int a = _starts[g * w + t - sIdx], z = _ends[g * w + t - sIdx];
                    if (a >= 0 && z >= 0 && tokens[z].Position - tokens[a].Position <= maxSpan)
                    {
                        _markMask[t - sIdx] = true;
                        break;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Marks in <see cref="_markMask"/> (index t - sIdx) every term token of tokens
        /// sIdx..eIdx inside a full occurrence lying in sIdx..eIdx: a token range covering
        /// every group whose ends are at most <paramref name="maxSpan"/> positions apart
        /// (maxWordDistance + groups - 1). False when every term token qualifies anyway
        /// (all of them inside the best window iLeft..iRight): no mask, the common hit
        /// stays free. Per right end r (a term token) the covering windows [l', r] are
        /// those with l' &lt;= L - 1 (the latest covering left end); their union within
        /// the span is [lo, r].
        /// </summary>
        private bool OccurrenceMarks(
            List<TextToken> tokens, PreparedQueryGroups prepared,
            int sIdx, int eIdx, int iLeft, int iRight, long maxSpan)
        {
            bool outside = false;
            for (int t = sIdx; t <= eIdx && !outside; t++)
                if ((t < iLeft || t > iRight) && prepared.TryGetGroups(tokens[t].NormSpan, out _))
                    outside = true;
            if (!outside) return false;

            int w = eIdx - sIdx + 1;
            if (_markMask.Length < w) _markMask = new bool[w * 2];
            System.Array.Clear(_markMask, 0, _markMask.Length);
            int k = prepared.GroupCount;
            EnsureGroupCount(k);
            for (int i = 0; i < k; i++) _groupCount[i] = 0;

            int covered = 0, L = sIdx, lo = sIdx, markedTo = sIdx;
            for (int r = sIdx; r <= eIdx; r++)
            {
                bool rTerm = prepared.TryGetGroups(tokens[r].NormSpan, out int[] rGroups);
                if (rTerm)
                    foreach (int g in rGroups)
                        if (_groupCount[g]++ == 0) covered++;
                while (covered >= k)
                {
                    if (prepared.TryGetGroups(tokens[L].NormSpan, out int[] lGroups))
                        foreach (int g in lGroups)
                            if (--_groupCount[g] == 0) covered--;
                    L++;
                }
                if (!rTerm || L == sIdx) continue;
                int x = L - 1;
                while (tokens[r].Position - tokens[lo].Position > maxSpan) lo++;
                if (lo <= x)
                {
                    for (int t = System.Math.Max(lo, markedTo); t <= r; t++) _markMask[t - sIdx] = true;
                    markedTo = r + 1;
                }
            }
            return true;
        }
    }
}
