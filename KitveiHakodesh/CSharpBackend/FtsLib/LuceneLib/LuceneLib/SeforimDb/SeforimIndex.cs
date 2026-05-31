using LuceneLib.Indexing;
using LuceneLib.Search;
using LuceneLib.Snippets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace LuceneLib.SeforimDb
{
    /// <summary>
    /// Public API for full-text search over the seforim database, backed by Lucene.NET.
    ///
    /// Provides the exact same interface as FtsLib.SeforimDb.SeforimIndex so that
    /// callers can switch implementations by changing only the using statement and
    /// the constructor name.
    ///
    /// Query syntax (handled by <see cref="HebrewQueryBuilder"/>):
    ///   word        — literal AND term
    ///   word*       — prefix wildcard
    ///   *word       — suffix wildcard
    ///   *word*      — infix wildcard
    ///   wor?d       — optional char: the char before '?' is optional
    ///   word~N      — fuzzy match, edit distance N (1–2, via Lucene FuzzyQuery)
    ///   a | b       — OR: lines matching a OR b satisfy this AND slot
    ///   %word       — grammar prefix expansion
    ///   word%       — grammar suffix expansion
    ///   %word%      — grammar prefix + suffix expansion
    ///   ~word       — כתיב חסר/מלא spelling variants
    ///
    /// Multiple tokens are AND-ed; '|'-separated tokens are OR-ed within one slot.
    /// </summary>
    public sealed class SeforimIndex
    {
        private readonly string _indexPath;
        private readonly string _dbPath;

        // Progress file written after each flush so a build can be resumed.
        private const string ProgressFileName = "build.progress";

        /// <summary>Default number of words of context shown on each side of the match.</summary>
        public const int DefaultContextWords = 8;

        /// <summary>Kept for binary compatibility with FtsLib callers.</summary>
        public const int DefaultSnippetLength = DefaultContextWords;

        public SeforimIndex(string indexPath, string dbPath)
        {
            if (string.IsNullOrWhiteSpace(indexPath))
                throw new ArgumentException("indexPath must not be empty.", nameof(indexPath));
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new ArgumentException("dbPath must not be empty.", nameof(dbPath));

            _indexPath = indexPath;
            _dbPath    = dbPath;

            if (!Directory.Exists(_indexPath))
                Directory.CreateDirectory(_indexPath);
        }

        // ── Build ─────────────────────────────────────────────────────

        /// <summary>
        /// Builds (or resumes) the Lucene index from the seforim database.
        ///
        /// If a <c>build.progress</c> file exists in the index directory, indexing
        /// resumes from the last committed row ID. Otherwise a fresh build is started.
        ///
        /// Returns true when at least one row was processed; false when the database
        /// was empty or the build was cancelled before any rows were indexed.
        /// </summary>
        /// <param name="limit">Maximum rows to index. 0 = all rows.</param>
        /// <param name="onProgress">Called after each row with the running count.</param>
        /// <param name="onFlush">Called after each Lucene commit (periodic checkpoint).</param>
        /// <param name="totalLines">Total rows in the database (for progress display).</param>
        /// <param name="resumeOffset">Unused — kept for API compatibility.</param>
        /// <param name="ct">Cancellation token.</param>
        public bool BuildIndex(
            int               limit        = 0,
            Action<long>      onProgress   = null,
            Action            onFlush      = null,
            long              totalLines   = 0,
            long              resumeOffset = 0,
            CancellationToken ct           = default)
        {
            int resumeLineId = ReadResumeLineId();
            if (resumeLineId > 0)
                Console.WriteLine($"[SeforimIndex] Resuming from line id {resumeLineId}");
            else
                Console.WriteLine("[SeforimIndex] Starting fresh build");

            // A fresh build deletes the existing index; a resume appends to it.
            bool deleteExisting = resumeLineId == 0;

            long n                 = 0;
            bool anyRowsProcessed  = false;
            int  lastWrittenLineId = resumeLineId;

            // Commit every this many rows so the progress file stays current.
            const long CommitInterval = 250_000;

            using (var db     = new ZayitDb(_dbPath))
            using (var writer = new LuceneIndexWriter(_indexPath,
                                    deleteExistingIndex: deleteExisting))
            {
                if (!db.IsOpen)
                {
                    Console.WriteLine("[SeforimIndex] Database not found — aborting.");
                    return false;
                }

                long effectiveTotal = totalLines > 0 ? totalLines : db.CountLines();

                var rows = resumeLineId > 0
                    ? db.ReadLinesFrom(resumeLineId, limit, ct)
                    : db.ReadLines(limit, ct);

                foreach (var (id, content) in rows)
                {
                    ct.ThrowIfCancellationRequested();
                    anyRowsProcessed  = true;
                    lastWrittenLineId = id;
                    n++;

                    writer.AddDocument(id, content ?? string.Empty);
                    onProgress?.Invoke(n);

                    // Periodic commit — makes progress visible to readers and
                    // updates the progress file so a crash loses at most CommitInterval rows.
                    if (n % CommitInterval == 0)
                    {
                        writer.Commit();
                        WriteProgressFile(lastWrittenLineId, effectiveTotal, resumeOffset + n);
                        Console.WriteLine($"[SeforimIndex] Committed at lineId={lastWrittenLineId} (n={n})");
                        onFlush?.Invoke();
                    }
                }

                // Final commit — flushes everything remaining.
                writer.Commit();
            }

            if (anyRowsProcessed)
            {
                WriteProgressFile(lastWrittenLineId, totalLines, resumeOffset + n);
                Console.WriteLine($"[SeforimIndex] Build complete — final lineId={lastWrittenLineId}");
            }
            else
            {
                Console.WriteLine("[SeforimIndex] No rows processed — progress file unchanged.");
            }

            return anyRowsProcessed;
        }

        // ── Resume helpers ────────────────────────────────────────────

        /// <summary>
        /// Returns the last committed line ID from a previous interrupted build,
        /// or 0 if no progress file exists.
        /// </summary>
        public int GetResumeLineId() => ReadResumeLineId();

        /// <summary>
        /// Returns (lastFlushedLineId, totalLines, resumeOffset) from the progress file.
        /// Any value is 0 if the file is absent or was written by an older build.
        /// </summary>
        public void GetResumeState(out int lineId, out long totalLines, out long resumeOffset)
            => ReadProgressFile(out lineId, out totalLines, out resumeOffset);

        /// <summary>
        /// Deletes the build progress file.
        /// Call this after a successful build to clear resume state.
        /// </summary>
        public void DeleteBuildProgressFile()
        {
            try
            {
                string path = Path.Combine(_indexPath, ProgressFileName);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        // ── Database helpers ──────────────────────────────────────────

        /// <summary>Returns the total number of lines in the database.</summary>
        public long CountLines()
        {
            using (var db = new ZayitDb(_dbPath))
                return db.CountLines();
        }

        /// <summary>Returns the number of lines with id &lt;= <paramref name="upToId"/>.</summary>
        public long CountLinesUpTo(int upToId)
        {
            using (var db = new ZayitDb(_dbPath))
                return db.CountLinesUpTo(upToId);
        }

        // ── Search ────────────────────────────────────────────────────

        /// <summary>
        /// Searches the index for lines matching the query and fetches their content
        /// from the database. Results are yielded lazily.
        /// </summary>
        /// <param name="query">Query string (see class docs for syntax).</param>
        /// <param name="cap">Maximum results to return. 0 = no cap.</param>
        /// <param name="expandKetiv">
        /// When true, adds כתיב חסר/מלא variants for every literal token.
        /// Equivalent to prefixing every plain token with '~'.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        public IEnumerable<SearchResult> Search(
            string            query,
            int               cap         = 0,
            bool              expandKetiv = false,
            CancellationToken ct          = default)
        {
            if (string.IsNullOrWhiteSpace(query)) yield break;

            string effectiveQuery = expandKetiv ? ApplyKetivPrefix(query) : query;

            // Build matched groups once — same for every result in this search.
            // Each AND slot becomes one group; OR alternatives within a slot form the collection.
            var matchedGroups = BuildMatchedGroups(effectiveQuery);

            using (var searcher = new LuceneSearcher(_indexPath))
            using (var db       = new ZayitDb(_dbPath))
            {
                if (!db.IsOpen) yield break;

                int yielded = 0;
                foreach (int rowId in searcher.Search(effectiveQuery))
                {
                    ct.ThrowIfCancellationRequested();

                    string content   = db.GetLineById(rowId);
                    string bookTitle = db.GetBookTitleByLineId(rowId);

                    yield return new SearchResult(
                        rowId,
                        bookTitle ?? string.Empty,
                        content   ?? string.Empty,
                        matchedGroups,
                        matchedGroups.Count);

                    yielded++;
                    if (cap > 0 && yielded >= cap) yield break;
                }
            }
        }

        /// <summary>
        /// Searches the index and returns only matching line IDs — no database fetch.
        /// Faster than <see cref="Search"/> when content is not needed.
        /// </summary>
        public IEnumerable<int> SearchIds(
            string            query,
            bool              expandKetiv = false,
            CancellationToken ct          = default)
        {
            if (string.IsNullOrWhiteSpace(query)) yield break;

            string effectiveQuery = expandKetiv ? ApplyKetivPrefix(query) : query;

            using (var searcher = new LuceneSearcher(_indexPath))
            {
                foreach (int rowId in searcher.Search(effectiveQuery))
                {
                    ct.ThrowIfCancellationRequested();
                    yield return rowId;
                }
            }
        }

        // ── Snippets ──────────────────────────────────────────────────

        /// <summary>
        /// Generates a highlighted HTML snippet for a line ID and query string.
        /// Fetches the line content from the database.
        /// </summary>
        public SnippetResult GenerateSnippet(int lineId, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return SnippetResult.NoMatch;

            using (var db = new ZayitDb(_dbPath))
            {
                if (!db.IsOpen) return SnippetResult.NoMatch;
                string content = db.GetLineById(lineId);
                if (content == null) return SnippetResult.NoMatch;
                return GenerateSnippetFromContent(content, query);
            }
        }

        /// <summary>
        /// Generates a highlighted HTML snippet from an already-fetched
        /// <see cref="SearchResult"/>. No database round-trip.
        /// </summary>
        /// <param name="result">The search result to highlight.</param>
        /// <param name="requireOrdered">
        /// When true, only snippets where query terms appear in left-to-right order
        /// are considered a match. Passed as <c>inOrder</c> to the span highlighter.
        /// </param>
        /// <param name="contextWords">
        /// Approximate number of words of context on each side of the match.
        /// Mapped to Lucene's fragment size (contextWords * 8 chars per word).
        /// </param>
        public SnippetResult GenerateSnippet(
            SearchResult result,
            bool         requireOrdered = false,
            int          contextWords   = DefaultContextWords)
        {
            if (result == null)                    return SnippetResult.NoMatch;
            if (result.MatchedGroups.Count == 0)   return SnippetResult.NoMatch;
            if (string.IsNullOrEmpty(result.Content)) return SnippetResult.NoMatch;

            // Reconstruct a query string from the matched groups so the highlighter
            // can re-parse it. Each group becomes one AND slot; alternatives within
            // a group are joined with ' | '.
            string reconstructedQuery = ReconstructQuery(result.MatchedGroups);
            return GenerateSnippetFromContent(result.Content, reconstructedQuery,
                requireOrdered, contextWords);
        }

        // ── Private helpers ───────────────────────────────────────────

        private SnippetResult GenerateSnippetFromContent(
            string content,
            string query,
            bool   requireOrdered = false,
            int    contextWords   = DefaultContextWords)
        {
            // Fragment size: contextWords words on each side of the match.
            // Approximate 8 visible chars per Hebrew word.
            int fragmentSize = contextWords * 8 * 2;

            int slop = requireOrdered ? contextWords : int.MaxValue;

            using (var searcher = new LuceneSearcher(_indexPath))
            {
                foreach (var (_, snippet) in searcher.SearchWithSnippets(
                    query,
                    _ => content,
                    fragmentSize: fragmentSize,
                    slop: slop,
                    inOrder: requireOrdered))
                {
                    // Compute WordDistance from the snippet HTML by counting the number
                    // of tokens between the first and last <mark> tag.
                    // This mirrors FtsLib's definition: tokens between leftmost and
                    // rightmost matched tokens (0 = adjacent).
                    int wordDistance = ComputeWordDistance(snippet.Html);
                    int score        = snippet.Html.Length; // proxy for raw char span

                    return new SnippetResult(snippet.Html, score, wordDistance, snippet.IsMatch);
                }
            }

            return SnippetResult.NoMatch;
        }

        /// <summary>
        /// Counts the number of whitespace-separated tokens between the first
        /// &lt;mark&gt; and the last &lt;/mark&gt; in the snippet HTML, minus the
        /// number of matched terms (so adjacent terms give WordDistance = 0).
        ///
        /// This is a close approximation of FtsLib's WordDistance metric, which
        /// counts tokens between the leftmost and rightmost matched tokens.
        /// </summary>
        private static int ComputeWordDistance(string html)
        {
            if (string.IsNullOrEmpty(html)) return int.MaxValue;

            int firstMark = html.IndexOf("<mark>", StringComparison.OrdinalIgnoreCase);
            int lastClose = html.LastIndexOf("</mark>", StringComparison.OrdinalIgnoreCase);
            if (firstMark < 0 || lastClose < 0) return int.MaxValue;

            // Extract the substring from first <mark> to end of last </mark>
            int windowEnd = lastClose + "</mark>".Length;
            string window = html.Substring(firstMark, windowEnd - firstMark);

            // Strip tags and count words in the window
            var sb = new System.Text.StringBuilder(window.Length);
            bool inTag = false;
            foreach (char c in window)
            {
                if (c == '<') { inTag = true; continue; }
                if (c == '>') { inTag = false; sb.Append(' '); continue; }
                if (!inTag) sb.Append(c);
            }

            // Count non-empty tokens
            int totalTokens = 0;
            int markCount   = 0;
            foreach (var tok in sb.ToString().Split(
                new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                totalTokens++;
            }

            // Count matched terms (number of <mark> tags)
            int pos = 0;
            while ((pos = html.IndexOf("<mark>", pos, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                markCount++;
                pos += 6;
            }

            int wordDistance = totalTokens - markCount;
            return wordDistance < 0 ? 0 : wordDistance;
        }

        /// <summary>
        /// Reconstructs a query string from matched groups.
        /// Each group is one AND slot; alternatives within a group are joined with ' | '.
        /// </summary>
        private static string ReconstructQuery(
            IReadOnlyList<IReadOnlyCollection<string>> groups)
        {
            var slots = new List<string>(groups.Count);
            foreach (var group in groups)
                slots.Add(string.Join(" | ", group));
            return string.Join(" ", slots);
        }

        /// <summary>
        /// Builds a MatchedGroups list from a query string by parsing its tokens.
        /// Each AND slot becomes one group; OR alternatives within a slot form the collection.
        /// </summary>
        private static IReadOnlyList<IReadOnlyCollection<string>> BuildMatchedGroups(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Array.Empty<IReadOnlyCollection<string>>();

            // Reuse HebrewQueryBuilder's slot-parsing logic by splitting on whitespace
            // and '|' ourselves — mirrors the same rules.
            query = query.Replace("|", " | ");
            var slots        = new List<IReadOnlyCollection<string>>();
            var pendingSlot  = new List<string>();
            bool lastWasPipe = false;

            foreach (var raw in query.Split(new[] { ' ', '\t', '\r', '\n' },
                                            StringSplitOptions.RemoveEmptyEntries))
            {
                bool isPipe = true;
                foreach (char c in raw) if (c != '|') { isPipe = false; break; }

                if (isPipe) { lastWasPipe = true; continue; }

                // Normalise: strip markers, nikud, etc. — same as HebrewQueryBuilder.Normalise
                string normalised = NormaliseToken(raw);
                if (normalised.Length == 0) continue;

                if (!lastWasPipe && pendingSlot.Count > 0)
                {
                    slots.Add(pendingSlot.ToArray());
                    pendingSlot = new List<string>();
                }

                pendingSlot.Add(normalised);
                lastWasPipe = false;
            }

            if (pendingSlot.Count > 0)
                slots.Add(pendingSlot.ToArray());

            return slots;
        }

        /// <summary>
        /// Strips query markers (~, %, *) and nikud from a token to get the bare
        /// normalised form used for highlighting.
        /// </summary>
        private static string NormaliseToken(string raw)
        {
            // Strip leading ~ and %
            int start = 0;
            while (start < raw.Length && (raw[start] == '~' || raw[start] == '%'))
                start++;
            // Strip trailing %
            int end = raw.Length;
            while (end > start && raw[end - 1] == '%')
                end--;

            var sb = new System.Text.StringBuilder(end - start);
            for (int i = start; i < end; i++)
            {
                char c = raw[i];
                if (c >= '\u0591' && c <= '\u05C7') continue; // nikud + cantillation
                if (c == '\u05F3' || c == '\u05F4' || c == '"') continue;
                if (c == '*' || c == '?') { sb.Append(c); continue; }
                if (c >= '\u05D0' && c <= '\u05EA') { sb.Append(c); continue; }
                if (c >= 'A' && c <= 'Z') { sb.Append((char)(c | 32)); continue; }
                if (c >= 'a' && c <= 'z') { sb.Append(c); continue; }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Prefixes every plain literal token in the query with '~' to trigger
        /// כתיב חסר/מלא expansion. Wildcard and already-marked tokens are left alone.
        /// </summary>
        private static string ApplyKetivPrefix(string query)
        {
            query = query.Replace("|", " | ");
            var parts = query.Split(new[] { ' ', '\t', '\r', '\n' },
                                    StringSplitOptions.RemoveEmptyEntries);
            var sb = new System.Text.StringBuilder();
            foreach (var part in parts)
            {
                if (sb.Length > 0) sb.Append(' ');
                bool isPipe    = true;
                foreach (char c in part) if (c != '|') { isPipe = false; break; }
                bool isMarked  = part.Length > 0 && (part[0] == '~' || part[0] == '%');
                bool isWild    = part.IndexOf('*') >= 0 || part.IndexOf('?') >= 0;

                if (!isPipe && !isMarked && !isWild)
                    sb.Append('~');
                sb.Append(part);
            }
            return sb.ToString();
        }

        // ── Progress file ─────────────────────────────────────────────

        private int ReadResumeLineId()
        {
            ReadProgressFile(out int lineId, out _, out _);
            return lineId;
        }

        private void ReadProgressFile(out int lineId, out long totalLines, out long resumeOffset)
        {
            lineId       = 0;
            totalLines   = 0;
            resumeOffset = 0;
            string path = Path.Combine(_indexPath, ProgressFileName);
            try
            {
                if (!File.Exists(path)) return;
                string[] lines = File.ReadAllText(path).Trim().Split('\n');
                if (lines.Length >= 1) int.TryParse(lines[0].Trim(),  out lineId);
                if (lines.Length >= 2) long.TryParse(lines[1].Trim(), out totalLines);
                if (lines.Length >= 3) long.TryParse(lines[2].Trim(), out resumeOffset);
            }
            catch { }
        }

        private void WriteProgressFile(int lineId, long totalLines, long resumeOffset)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(_indexPath, ProgressFileName),
                    lineId.ToString() + "\n" +
                    totalLines.ToString() + "\n" +
                    resumeOffset.ToString());
            }
            catch { }
        }
    }
}
