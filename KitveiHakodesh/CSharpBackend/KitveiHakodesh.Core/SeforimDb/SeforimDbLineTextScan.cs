using System;
using System.Collections.Generic;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// The in-book / commentary search's fallback, natively: every occurrence of a query
    /// as a SUBSTRING of a line's text, with the SAME normalization the frontend's search
    /// and highlighter use (vue-frontend/src/utils/hebrewTextProcessing.ts:
    /// stripHtmlForSearch + removeDiacriticsForSearch) — tags skipped, an entity one
    /// character, Hebrew diacritics dropped, the maqaf a space, quote marks folded. The
    /// counts per line must equal the frontend's, because Enter walks occurrences by number.
    ///
    /// One pass per line: the HTML is walked once and each kept character feeds a KMP
    /// automaton; no stripped copy is built and nothing is kept past the call.
    /// </summary>
    public static class SeforimDbLineTextScan
    {
        /// <summary>A line holding the query, and how many times.</summary>
        public readonly struct LineHit
        {
            public LineHit(int lineIndex, int lineId, int count) { LineIndex = lineIndex; LineId = lineId; Count = count; }
            public int LineIndex { get; }
            public int LineId { get; }
            public int Count { get; }
        }

        /// <summary>The query as the search compares it: diacritics dropped, maqaf a space, quotes folded.</summary>
        public static string NormalizeQuery(string query)
        {
            var sb = new System.Text.StringBuilder(query.Length);
            foreach (char c in query.Trim())
            {
                if (IsIgnoredMark(c)) continue;
                sb.Append(FoldChar(c));
            }
            return sb.ToString();
        }

        /// <summary>A matcher for one normalized needle, reusable across lines.</summary>
        public sealed class Matcher
        {
            private readonly string _needle;
            private readonly int[] _fail;
            private int _state;
            public int Count { get; private set; }

            public Matcher(string normalizedNeedle)
            {
                _needle = normalizedNeedle;
                _fail = BuildFailure(normalizedNeedle);
            }

            public bool IsEmpty => _needle.Length == 0;

            public void Reset() { _state = 0; Count = 0; }

            /// <summary>Feeds one normalized character.</summary>
            public void Feed(char c)
            {
                while (_state > 0 && _needle[_state] != c) _state = _fail[_state - 1];
                if (_needle[_state] == c) _state++;
                if (_state == _needle.Length)
                {
                    Count++;
                    // Overlapping occurrences count, as indexOf(needle, at + 1) does.
                    _state = _fail[_state - 1];
                }
            }

            private static int[] BuildFailure(string p)
            {
                var fail = new int[p.Length];
                for (int i = 1, k = 0; i < p.Length; i++)
                {
                    while (k > 0 && p[i] != p[k]) k = fail[k - 1];
                    if (p[i] == p[k]) k++;
                    fail[i] = k;
                }
                return fail;
            }
        }

        /// <summary>Occurrences of the matcher's needle in one line's HTML — the one-pass walk.</summary>
        public static int CountIn(string html, Matcher matcher)
        {
            matcher.Reset();
            int i = 0, length = html.Length;
            while (i < length)
            {
                char ch = html[i];
                if (ch == '<')
                {
                    int close = html.IndexOf('>', i + 1);
                    if (close < 0) break;
                    i = close + 1;
                    continue;
                }
                if (ch == '>') { i++; continue; }   // a stray '>' outside a tag: dropped
                if (ch == '&')
                {
                    // An entity only when a ';' follows within 12 chars with no whitespace or tag.
                    int entityEnd = -1;
                    for (int j = i + 1; j < length && j <= i + 12; j++)
                    {
                        char c = html[j];
                        if (c == ';') { entityEnd = j; break; }
                        if (c == ' ' || c == '\t' || c == '\n' || c == '<') break;
                    }
                    if (entityEnd >= 0)
                    {
                        matcher.Feed(QuoteEntity(html, i, entityEnd - i + 1));
                        i = entityEnd + 1;
                        continue;
                    }
                    matcher.Feed('&');
                    i++;
                    continue;
                }
                if (!IsIgnoredMark(ch)) matcher.Feed(FoldChar(ch));
                i++;
            }
            return matcher.Count;
        }

        /// <summary>Hebrew diacritics and cantillation dropped by the search (the maqaf is kept).</summary>
        private static bool IsIgnoredMark(char c) => c >= '֑' && c <= 'ׇ' && c != '־';

        /// <summary>Maqaf → space; every double-quote glyph → '"'; every single-quote glyph → '\''.</summary>
        private static char FoldChar(char c)
        {
            switch (c)
            {
                case '־': return ' ';
                case '"': case '״': case '“': case '”': case '„': return '"';
                case '\'': case '׳': case '‘': case '’': case '‚': case '`': return '\'';
                default: return c;
            }
        }

        /// <summary>A quote entity becomes the folded quote; any other entity one sentinel char.</summary>
        private static char QuoteEntity(string html, int start, int count)
        {
            // &quot; &#34; &#x22; → '"'   &apos; &#39; &#x27; → '\''  (case-insensitive)
            string e = html.Substring(start, count).ToLowerInvariant();
            switch (e)
            {
                case "&quot;": case "&#34;": case "&#x22;": return '"';
                case "&apos;": case "&#39;": case "&#x27;": return '\'';
                default: return '\0';
            }
        }

        /// <summary>A hit with its book, for a scan over several books' ranges.</summary>
        public readonly struct RangeHit
        {
            public RangeHit(int bookId, int lineIndex, int lineId, int count) { BookId = bookId; LineIndex = lineIndex; LineId = lineId; Count = count; }
            public int BookId { get; }
            public int LineIndex { get; }
            public int LineId { get; }
            public int Count { get; }
        }

        /// <summary>Lines per piece of work: a book is read in pieces this long, two at a time.</summary>
        private const int PieceLines = 20000;
        /// <summary>At most this many reads run at once (the machine stays light).</summary>
        private const int Parallelism = 2;
        /// <summary>A book is read by listed indexes when its span holds this many times more lines than wanted.</summary>
        private const int SparseFactor = 4;

        /// <summary>
        /// Scans many (book, first, last) ranges — a commentary panel's lines — with ONE read
        /// per book over the span of its ranges, the hits filtered back to the exact ranges.
        /// Thousands of one-line ranges cost nothing extra. Output: books in first-seen order,
        /// each in reading order.
        /// </summary>
        public static List<RangeHit> ScanRanges(SeforimDbQueries queries, IReadOnlyList<(int BookId, int FirstLineIndex, int LastLineIndex)> ranges, int versionId, string query)
        {
            var result = new List<RangeHit>();
            string needle = NormalizeQuery(query);
            if (needle.Length == 0) return result;
            var bookOrder = new List<int>();
            var wanted = new Dictionary<int, List<(int First, int Last)>>();
            foreach (var (bookId, first, last) in ranges)
            {
                if (last < first) continue;
                if (!wanted.TryGetValue(bookId, out var list)) { list = new List<(int, int)>(); wanted[bookId] = list; bookOrder.Add(bookId); }
                list.Add((first, last));
            }
            // The work, read two pieces at a time: a book whose ranges cover most of its span is
            // read as a span cut into pieces; one whose ranges are a scattered few lines (a
            // commentary panel: hundreds of lines spread through a whole book) is read by the
            // listed line indexes instead, so the lines between them are never decoded.
            var pieces = new List<(int BookId, int First, int Last, List<int> Indexes)>();
            foreach (int bookId in bookOrder)
            {
                var list = wanted[bookId];
                list.Sort((a, b) => a.First.CompareTo(b.First));
                int spanFirst = list[0].First, spanLast = 0;
                long wantedLines = 0;
                foreach (var r in list) { if (r.Last > spanLast) spanLast = r.Last; wantedLines += r.Last - r.First + 1; }
                long spanLines = (long)spanLast - spanFirst + 1;
                if (wantedLines * SparseFactor < spanLines)
                {
                    var indexes = new List<int>((int)wantedLines);
                    foreach (var r in list) for (int li = r.First; li <= r.Last; li++) indexes.Add(li);
                    for (int at = 0; at < indexes.Count; at += PieceLines)
                        pieces.Add((bookId, 0, 0, indexes.GetRange(at, Math.Min(PieceLines, indexes.Count - at))));
                }
                else
                {
                    for (int at = spanFirst; at <= spanLast; at += PieceLines)
                        pieces.Add((bookId, at, Math.Min(spanLast, at + PieceLines - 1), null));
                }
            }
            var hitsPerPiece = new List<LineHit>[pieces.Count];
            System.Threading.Tasks.Parallel.For(0, pieces.Count, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Parallelism }, i =>
            {
                var (bookId, first, last, indexes) = pieces[i];
                hitsPerPiece[i] = indexes == null
                    ? Scan(queries, bookId, first, last, versionId, query)
                    : ScanIndexes(queries, bookId, indexes, versionId, query);
            });
            for (int i = 0; i < pieces.Count; i++)
            {
                int bookId = pieces[i].BookId;
                var list = wanted[bookId];
                int cursor = 0;
                foreach (var hit in hitsPerPiece[i])
                {
                    while (cursor < list.Count && list[cursor].Last < hit.LineIndex) cursor++;
                    if (cursor == list.Count) break;
                    if (list[cursor].First <= hit.LineIndex) result.Add(new RangeHit(bookId, hit.LineIndex, hit.LineId, hit.Count));
                }
            }
            return result;
        }

        /// <summary>Like <see cref="Scan"/> for a sparse list of line indexes (ascending).</summary>
        public static List<LineHit> ScanIndexes(SeforimDbQueries queries, int bookId, IReadOnlyList<int> lineIndexes, int versionId, string query)
        {
            var hits = new List<LineHit>();
            var matcher = new Matcher(NormalizeQuery(query));
            if (matcher.IsEmpty || lineIndexes.Count == 0) return hits;
            queries.ForEachLineByIndexes(bookId, lineIndexes, versionId, (id, lineIndex, content) =>
            {
                int count = CountIn(content, matcher);
                if (count > 0) hits.Add(new LineHit(lineIndex, id, count));
            });
            return hits;
        }

        /// <summary>
        /// Scans the lines of [firstLineIndex, lastLineIndex] of a book (through a version's
        /// overlay when versionId > 0) and returns the lines holding the query, in reading order.
        /// </summary>
        public static List<LineHit> Scan(SeforimDbQueries queries, int bookId, int firstLineIndex, int lastLineIndex, int versionId, string query)
        {
            var hits = new List<LineHit>();
            var matcher = new Matcher(NormalizeQuery(query));
            if (matcher.IsEmpty || lastLineIndex < firstLineIndex) return hits;
            queries.ForEachLineInIndexRange(bookId, firstLineIndex, lastLineIndex, versionId, (id, lineIndex, content) =>
            {
                int count = CountIn(content, matcher);
                if (count > 0) hits.Add(new LineHit(lineIndex, id, count));
            });
            return hits;
        }
    }
}
