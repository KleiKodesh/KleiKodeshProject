// Written before nullable annotations (ported from the DocumentLocator library, which built
// with these warnings suppressed); not annotated yet.
#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KitveiHakodesh.Core.LocalFiles
{
    /// <summary>
    /// Owns the user-defined excluded folders feature end to end: reading and writing
    /// the list, and applying it to search results.
    ///
    /// File: the SIBLING of the index directory — {parentOf(indexPath)}\excluded_folders.json.
    /// It deliberately does NOT live inside {indexPath}: a reindex deletes that directory
    /// recursively, and any config stored in there is one failed delete away from being
    /// lost for good. Keeping it outside means a rebuild physically cannot touch it.
    ///
    /// Format: a plain JSON array of absolute path strings, e.g.
    ///   ["C:\\Users\\Alice\\Downloads","D:\\Temp"]
    ///
    /// No external JSON library is used — the file format is simple enough to
    /// parse and emit with the same minimal hand-rolled JSON helpers already
    /// used in PipeProtocol. Keeping it dependency-free is deliberate: the file is
    /// human-readable and hand-editable, and the library compiles for both net48
    /// and net10.0-windows without dragging in a serializer.
    ///
    /// EVERY host must go through this class — both the net48 DocumentLocator.Service
    /// (named pipe) and the net10 KitveiHakodeshService (in-process, which also backs
    /// the Vue settings dialog). The exclusion list is applied at SEARCH time, never
    /// baked into the index, so an edit takes effect on the next query with no rebuild.
    /// Do not re-implement the prefix test in a host — a second copy will drift.
    ///
    /// Thread safety: Load/Save are called only from the service thread or from
    /// a dedicated handler thread — no concurrent access is expected. Callers
    /// that need thread safety must synchronise externally.
    /// </summary>
    public static class LocalFileIndexExcludedFolders
    {
        private const string FileName = "excluded_folders.json";

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the path to the excluded-folders file for a given index directory:
        /// the file sits NEXT TO that directory, not inside it. The file may or may not exist.
        ///
        /// Falls back to inside the index directory only when <paramref name="indexPath"/>
        /// has no parent (a drive root), which no real deployment uses.
        /// </summary>
        public static string FilePath(string indexPath)
        {
            string parent = ParentOf(indexPath);
            return Path.Combine(string.IsNullOrEmpty(parent) ? indexPath : parent, FileName);
        }

        /// <summary>
        /// The legacy location, inside the index directory (everything up to and including
        /// v9.0.4). Only read, never written — <see cref="Load"/> migrates it forward on
        /// first sight, so this can be retired once no supported install still has one.
        /// </summary>
        private static string LegacyFilePath(string indexPath)
            => Path.Combine(indexPath, FileName);

        /// <summary>
        /// Loads the excluded folders list from disk.
        /// Returns an empty list if the file does not exist or cannot be parsed.
        /// Never throws.
        ///
        /// Migrates a legacy list found inside the index directory to the current location
        /// on first read, so an upgrade keeps the user's configuration.
        /// </summary>
        public static List<string> Load(string indexPath)
        {
            string file = FilePath(indexPath);
            if (File.Exists(file)) return ReadOrEmpty(file);

            // Nothing at the current location — an older install may still have one
            // inside the index directory. Adopt it, then write it out where a reindex
            // cannot reach it. A failed migration write is non-fatal: we still return
            // the list, and the next Save puts it in the right place.
            string legacy = LegacyFilePath(indexPath);
            if (!File.Exists(legacy)) return new List<string>();

            var migrated = ReadOrEmpty(legacy);
            if (migrated.Count > 0)
            {
                try { Save(indexPath, migrated); } catch { /* best effort */ }
            }
            return migrated;
        }

        private static List<string> ReadOrEmpty(string file)
        {
            try
            {
                string json = File.ReadAllText(file, Encoding.UTF8);
                return ParseJsonStringArray(json);
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Saves the excluded folders list to disk as a UTF-8 JSON array.
        /// Creates the containing directory if it does not exist.
        /// Throws on I/O failure — callers should handle this.
        ///
        /// The write is atomic: the content lands in a sibling .tmp file that is only
        /// then moved over the real one, so an interrupted write can never leave the
        /// user with a truncated or empty list.
        /// </summary>
        public static void Save(string indexPath, IEnumerable<string> folders)
        {
            string file = FilePath(indexPath);

            // GetDirectoryName returns "" (not null) for a bare relative path, and
            // CreateDirectory("") throws — fall back to the index directory in that case.
            string folder = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            string json = BuildJsonStringArray(folders);

            // A unique temp name: two hosts saving at once must not write the same staging
            // file, or one would publish the other's content and the loser would then fail
            // on a .tmp that no longer exists.
            string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";

            File.WriteAllText(temp, json, Encoding.UTF8);
            try
            {
                if (!File.Exists(file))
                {
                    // First save — there is nothing to replace.
                    File.Move(temp, file);
                    return;
                }

                // File.Replace preserves the destination's identity and swaps atomically.
                // The .tmp is always a sibling of the target so the cross-volume case
                // cannot arise, but some network redirectors refuse Replace outright.
                try
                {
                    File.Replace(temp, file, null);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                                           || ex is PlatformNotSupportedException)
                {
                    // Fallback for hosts without Replace. Move the existing list ASIDE rather
                    // than deleting it: if the move-into-place then fails, deleting first
                    // would leave the user with NOTHING — the very data loss this whole file
                    // was restructured to prevent. The old list is only discarded once the
                    // new one is safely in place.
                    string backup = file + "." + Guid.NewGuid().ToString("N") + ".bak";
                    File.Move(file, backup);
                    try
                    {
                        File.Move(temp, file);
                    }
                    catch
                    {
                        try { File.Move(backup, file); } catch { /* restore failed — .bak remains */ }
                        throw;
                    }
                    try { File.Delete(backup); } catch { /* harmless leftover */ }
                }
            }
            catch
            {
                // Only clean up the staging file while the real one is still there. If `file`
                // is missing, this .tmp may be the only copy of the list left — keep it.
                try { if (File.Exists(file) && File.Exists(temp)) File.Delete(temp); }
                catch { /* best effort */ }
                throw;
            }
        }

        /// <summary>
        /// The directory containing <paramref name="indexPath"/>, or null at a drive root.
        /// Trailing separators are trimmed first so "…\filesystemindex\" and
        /// "…\filesystemindex" resolve to the same parent.
        /// </summary>
        private static string ParentOf(string indexPath)
        {
            if (string.IsNullOrEmpty(indexPath)) return null;
            try
            {
                string trimmed = indexPath.TrimEnd('\\', '/');
                if (trimmed.Length == 0) return null;
                return Path.GetDirectoryName(trimmed);
            }
            catch
            {
                return null;
            }
        }

        // ── Filtering ─────────────────────────────────────────────────────────────

        /// <summary>
        /// True when <paramref name="path"/> sits inside one of <paramref name="excludedFolders"/>.
        ///
        /// An excluded folder covers every descendant but NOT a sibling whose name merely
        /// starts with the same characters: excluding "C:\Foo" hides "C:\Foo\a.pdf" and
        /// "C:\Foo" itself, but never "C:\FooBar\a.pdf". That separator boundary check is
        /// the reason this belongs in one shared place — a naive StartsWith gets it wrong.
        ///
        /// Comparison is case-insensitive (Windows paths). Trailing separators on the
        /// configured folder are ignored, so "D:\Temp\" and "D:\Temp" behave identically.
        /// </summary>
        public static bool IsExcluded(string path, IList<string> excludedFolders)
        {
            if (string.IsNullOrEmpty(path) || excludedFolders == null) return false;

            for (int i = 0; i < excludedFolders.Count; i++)
            {
                string folder = excludedFolders[i];
                if (string.IsNullOrEmpty(folder)) continue;

                string prefix = folder.TrimEnd('\\', '/');
                if (prefix.Length == 0) continue;
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                // Exact match (the folder itself), or a real child under a separator.
                if (path.Length == prefix.Length) return true;
                char boundary = path[prefix.Length];
                if (boundary == '\\' || boundary == '/') return true;
            }
            return false;
        }

        /// <summary>
        /// Removes every entry whose path is excluded, and reduces <paramref name="total"/>
        /// by the number dropped so callers can still report "showing X of Y" honestly.
        ///
        /// Returns the input list unchanged when nothing is excluded (no allocation in the
        /// common case). <paramref name="total"/> never goes below the surviving count.
        /// </summary>
        public static List<PathEntry> Filter(
            List<PathEntry> entries, IList<string> excludedFolders, ref int total)
        {
            if (entries == null || entries.Count == 0) return entries;
            if (excludedFolders == null || excludedFolders.Count == 0) return entries;

            var kept = new List<PathEntry>(entries.Count);
            foreach (var entry in entries)
            {
                if (!IsExcluded(entry.Path, excludedFolders)) kept.Add(entry);
            }

            if (kept.Count == entries.Count) return entries; // nothing dropped

            total -= entries.Count - kept.Count;
            if (total < kept.Count) total = kept.Count;
            return kept;
        }

        // ── JSON helpers (no external library) ────────────────────────────────────

        /// <summary>
        /// Emits a JSON array of strings, e.g. ["C:\\foo","D:\\bar"].
        /// Each string is escaped for JSON (backslashes and double-quotes only —
        /// sufficient for Windows absolute paths).
        /// </summary>
        private static string BuildJsonStringArray(IEnumerable<string> items)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            bool first = true;
            foreach (string item in items)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"');
                sb.Append(EscapeJsonString(item));
                sb.Append('"');
            }
            sb.Append(']');
            return sb.ToString();
        }

        /// <summary>
        /// Parses a JSON string array produced by <see cref="BuildJsonStringArray"/>.
        /// Tolerates extra whitespace but does not handle nested arrays or objects.
        /// Returns an empty list on any parse error.
        /// </summary>
        private static List<string> ParseJsonStringArray(string json)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(json)) return result;

            json = json.Trim();
            if (json.Length < 2 || json[0] != '[') return result;

            int i = 1;
            while (i < json.Length)
            {
                // Skip whitespace and commas between elements
                while (i < json.Length && (json[i] == ' ' || json[i] == '\t' ||
                       json[i] == '\r' || json[i] == '\n' || json[i] == ','))
                    i++;

                if (i >= json.Length || json[i] == ']') break;

                if (json[i] != '"') { i++; continue; } // unexpected — skip

                // Read quoted string
                int start = i + 1;
                int end   = start;
                while (end < json.Length)
                {
                    if (json[end] == '\\') { end += 2; continue; }  // skip escaped char
                    if (json[end] == '"')  break;
                    end++;
                }

                if (end < json.Length)
                    result.Add(UnescapeJsonString(json.Substring(start, end - start)));

                i = end + 1;
            }

            return result;
        }

        private static string EscapeJsonString(string s)
        {
            if (s == null) return string.Empty;
            // Windows paths only need backslash and double-quote escaping.
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string UnescapeJsonString(string s)
        {
            if (s == null) return string.Empty;
            // Reverse the two escape sequences produced by EscapeJsonString.
            // Use a placeholder to avoid double-processing.
            return s.Replace("\\\\", "\x00").Replace("\\\"", "\"")
                    .Replace("\x00", "\\");
        }
    }
}
