using System;
using System.Collections.Generic;
using System.IO;

namespace SearchEngine.SeforimDb
{
    /// <summary>
    /// Matched-group building, token normalisation, query reconstruction,
    /// ketiv prefix application, and build progress-file I/O for <see cref="SeforimIndex"/>.
    /// </summary>
    public sealed partial class SeforimIndex
    {
        // Progress file written after each commit so a build can be resumed.
        private const string ProgressFileName = "build.progress";

        // ── Resume helpers ────────────────────────────────────────────

        public int GetResumeLineId() => ReadResumeLineId();

        public void GetResumeState(out int lineId, out long totalLines, out long resumeOffset)
            => ReadProgressFile(out lineId, out totalLines, out resumeOffset);

        public void DeleteBuildProgressFile()
        {
            try
            {
                string path = Path.Combine(_indexPath, ProgressFileName);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        private int ReadResumeLineId()
        {
            ReadProgressFile(out int lineId, out _, out _);
            return lineId;
        }

        private void ReadProgressFile(
            out int lineId, out long totalLines, out long resumeOffset)
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
                    lineId.ToString()       + "\n" +
                    totalLines.ToString()   + "\n" +
                    resumeOffset.ToString());
            }
            catch { }
        }

        // ── Matched-group building ────────────────────────────────────

        /// <summary>
        /// Parses a query string into groups of OR-alternative normalised tokens,
        /// one group per AND slot.  Used to populate <see cref="SearchResult.MatchedGroups"/>.
        /// </summary>
        private static IReadOnlyList<IReadOnlyCollection<string>> BuildMatchedGroups(
            string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Array.Empty<IReadOnlyCollection<string>>();

            if (query.IndexOf('|') >= 0)
                query = query.Replace("|", " | ");

            var slots        = new List<IReadOnlyCollection<string>>();
            var pendingSlot  = new List<string>();
            bool lastWasPipe = false;

            foreach (var raw in query.Split(
                new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                bool isPipe = true;
                foreach (char c in raw) if (c != '|') { isPipe = false; break; }
                if (isPipe) { lastWasPipe = true; continue; }

                string normalised = NormaliseToken(raw);
                if (normalised.Length == 0) continue;

                if (!lastWasPipe && pendingSlot.Count > 0)
                {
                    slots.Add(pendingSlot.ToArray());
                    pendingSlot.Clear(); // reuse the list rather than allocating a new one
                }

                pendingSlot.Add(normalised);
                lastWasPipe = false;
            }

            if (pendingSlot.Count > 0)
                slots.Add(pendingSlot.ToArray());

            return slots;
        }

        /// <summary>
        /// Reconstructs a flat query string from a list of OR-group token collections.
        /// Used to re-run snippet generation from a <see cref="SearchResult"/>.
        /// </summary>
        private static string ReconstructQuery(
            IReadOnlyList<IReadOnlyCollection<string>> groups)
        {
            var slots = new List<string>(groups.Count);
            foreach (var group in groups)
                slots.Add(string.Join(" | ", group));
            return string.Join(" ", slots);
        }

        // ── Token normalisation ───────────────────────────────────────

        /// <summary>
        /// Strips operator markers (leading ~, %, trailing %, trailing ~N fuzzy suffix)
        /// and then strips nikud, cantillation, and geresh from the remaining text.
        /// Used to extract the bare display token for <see cref="SearchResult.MatchedGroups"/>.
        /// </summary>
        private static string NormaliseToken(string raw)
        {
            int start = 0;
            while (start < raw.Length && (raw[start] == '~' || raw[start] == '%'))
                start++;
            int end = raw.Length;
            while (end > start && raw[end - 1] == '%')
                end--;

            // Strip trailing ~N fuzzy suffix.
            for (int i = end - 1; i >= start; i--)
            {
                if (raw[i] == '~')
                {
                    string suffix = raw.Substring(i + 1, end - i - 1);
                    bool valid = suffix.Length == 0
                              || (suffix.Length == 1 && suffix[0] >= '1' && suffix[0] <= '9');
                    if (valid) { end = i; break; }
                }
            }

            var sb = new System.Text.StringBuilder(end - start);
            for (int i = start; i < end; i++)
            {
                char c = raw[i];
                if (c >= '\u0591' && c <= '\u05C7') continue; // nikud + cantillation
                if (c == '\u05F3' || c == '\u05F4' || c == '"') continue; // geresh/gershayim
                if (c == '*' || c == '?') { sb.Append(c); continue; }
                if (c >= '\u05D0' && c <= '\u05EA') { sb.Append(c); continue; } // Hebrew
                if (c >= 'A' && c <= 'Z') { sb.Append((char)(c | 32)); continue; }
                if (c >= 'a' && c <= 'z') { sb.Append(c); continue; }
            }
            return sb.ToString();
        }

        // ── Ketiv prefix application ──────────────────────────────────

        /// <summary>
        /// Prepends '~' to every non-pipe, non-already-marked, non-wildcard token
        /// in the query so the search engine applies ketiv חסר/מלא expansion.
        /// </summary>
        public static string ApplyKetivExpansion(string query)
        {
            if (query.IndexOf('|') >= 0)
                query = query.Replace("|", " | ");
            var parts = query.Split(
                new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new System.Text.StringBuilder(query.Length + parts.Length * 2);
            foreach (var part in parts)
            {
                if (sb.Length > 0) sb.Append(' ');
                bool isPipe   = true;
                foreach (char c in part) if (c != '|') { isPipe = false; break; }
                bool isMarked = part.Length > 0 && (part[0] == '~' || part[0] == '%');
                bool isWild   = part.IndexOf('*') >= 0 || part.IndexOf('?') >= 0;
                if (!isPipe && !isMarked && !isWild) sb.Append('~');
                sb.Append(part);
            }
            return sb.ToString();
        }
    }
}
