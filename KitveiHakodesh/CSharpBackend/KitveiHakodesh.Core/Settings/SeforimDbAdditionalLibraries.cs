using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KitveiHakodesh.Core.SeforimDb;

namespace KitveiHakodesh.Core.Settings
{
    /// <summary>
    /// Extra seforim-shaped libraries the user has added ALONGSIDE the one
    /// <see cref="SeforimDbPathResolver"/> resolves — a personal library built offline, a second
    /// edition, anything that shares the official book/category/line schema but is not itself
    /// seforim.db.
    ///
    /// These are additive, never a replacement: the primary database keeps working exactly as
    /// before, through the exact same registry value, whether or not any additional library is
    /// configured. A machine that has never touched this setting behaves identically to one
    /// that predates it.
    ///
    /// EVERY ENTRY CARRIES A PERSISTED INDEX, assigned once by <see cref="Add"/> and never
    /// recomputed. That index is what <see cref="SeforimDbAttachedLibraries"/> uses as the
    /// library's id-space slot (see <see cref="SeforimDbLibraryId"/>) — NOT the entry's position
    /// in the list. Deriving the slot from list position was the original design and had a
    /// quiet but serious bug: removing (or merely losing — a disconnected network drive) an
    /// EARLIER library shifted every LATER library's position, which shifted its id-space slot,
    /// which silently reassigned every book id in it. A user's existing highlights and bookmarks
    /// are keyed on those global ids forever (see UserAnnotationStore), so a shifted slot does
    /// not fail loudly — it makes an annotation point at a different library's book after the
    /// next restart. Assigning the index once, at Add time, from a counter that only ever
    /// increases (<see cref="NextIndexValueName"/>) and is never reused even after a Remove,
    /// makes that impossible: a library's slot is fixed for as long as its path stays configured,
    /// full stop.
    ///
    /// Stored as two registry values: the entries (<c>"index|path"</c> pairs joined by ';' — the
    /// same separator Windows itself uses for PATH) and the next index to hand out. A path
    /// legitimately containing ';' or '|' cannot be represented; that is an accepted limit, not a
    /// bug, and <see cref="Add"/> refuses such a path rather than silently truncating it.
    /// </summary>
    public static class SeforimDbAdditionalLibraries
    {
        private const string RegistryApp = "KitveiHakodesh";
        private const string RegistrySection = "Database";
        private const string RegistryValueName = "AdditionalPaths";
        private const string NextIndexValueName = "AdditionalPathsNextIndex";
        private const char Separator = ';';
        private const char IndexPathSeparator = '|';

        /// <summary>One configured additional library: its persisted, never-reused id-space slot
        /// and its path. See the class remarks for why the index is not just "position in the
        /// list".</summary>
        public readonly struct Entry
        {
            public Entry(int index, string path)
            {
                Index = index;
                Path = path;
            }

            public int Index { get; }
            public string Path { get; }
        }

        /// <summary>Every configured additional library, exactly as stored — no existence check.
        /// For a settings UI that wants to show a broken entry rather than hide it.</summary>
        public static IReadOnlyList<Entry> GetAllEntriesRaw() =>
            ParseEntries(AppSettingsRegistry.Get(RegistryApp, RegistrySection, RegistryValueName, ""));

        /// <summary>Every configured path, exactly as stored (no existence check), for callers
        /// that only need to show/manage paths and do not care about the id-space slot.</summary>
        public static IReadOnlyList<string> GetAllRaw() =>
            GetAllEntriesRaw().Select(e => e.Path).ToArray();

        /// <summary>The configured entries whose path currently exists on disk — what
        /// <see cref="SeforimDbQueries"/> actually attempts to ATTACH. A path that has moved or
        /// been deleted is skipped here exactly as the primary path is skipped by
        /// <see cref="SeforimDbPathResolver"/> — silently dropping a missing library is safer
        /// than failing every catalog read over one bad entry. Crucially, skipping it here does
        /// NOT free its index: see the class remarks.</summary>
        public static IReadOnlyList<Entry> GetAllEntries() =>
            GetAllEntriesRaw().Where(e => FileExists(e.Path)).ToArray();

        /// <summary>Same as <see cref="GetAllEntries"/>, paths only, for callers that do not need
        /// the id-space slot.</summary>
        public static IReadOnlyList<string> GetAll() =>
            GetAllEntries().Select(e => e.Path).ToArray();

        /// <summary>Adds <paramref name="path"/> if it is not already present (case-insensitive,
        /// by full path) and does not contain either separator. Does not require the file to
        /// exist yet — a user may add a path before restoring the file, exactly like the primary
        /// path setting. The new entry's index comes from the ever-increasing counter, never from
        /// "one more than however many are configured right now" — see the class remarks.</summary>
        public static bool Add(string? rawPath)
        {
            string path = SeforimDbLibrarySwitcher.NormalizePath(rawPath);
            if (path.Length == 0 || path.IndexOf(Separator) >= 0 || path.IndexOf(IndexPathSeparator) >= 0)
                return false;

            string full = TryFullPath(path);
            var current = GetAllEntriesRaw();
            if (current.Any(e => string.Equals(TryFullPath(e.Path), full, StringComparison.OrdinalIgnoreCase)))
                return false;

            int index = ReadNextIndex();
            if (index > SeforimDbLibraryId.MaxLibraryIndex)
                return false; // this install has handed out every slot it can ever address — see SeforimDbLibraryId

            AppSettingsRegistry.Set(RegistryApp, RegistrySection, NextIndexValueName,
                (index + 1).ToString(CultureInfo.InvariantCulture));
            Save(current.Append(new Entry(index, path)));
            return true;
        }

        /// <summary>Removes a configured path (case-insensitive, by full path). Returns false
        /// when it was not present. Its index is retired, not freed — a later <see cref="Add"/>
        /// of a different path (or even the same path again) gets a NEW index, never this one.</summary>
        public static bool Remove(string? rawPath)
        {
            string path = SeforimDbLibrarySwitcher.NormalizePath(rawPath);
            string full = TryFullPath(path);
            var current = GetAllEntriesRaw();
            var remaining = current.Where(e => !string.Equals(TryFullPath(e.Path), full, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (remaining.Length == current.Count) return false;

            Save(remaining);
            return true;
        }

        /// <summary>Forgets every additional library AND resets the index counter. Unlike
        /// <see cref="Remove"/>, this deliberately allows index reuse — it is a full reset of the
        /// feature's state, equivalent to a fresh install, not the removal of one library.</summary>
        public static void Clear()
        {
            AppSettingsRegistry.Set(RegistryApp, RegistrySection, RegistryValueName, "");
            AppSettingsRegistry.Set(RegistryApp, RegistrySection, NextIndexValueName, "");
        }

        private static int ReadNextIndex()
        {
            string raw = AppSettingsRegistry.Get(RegistryApp, RegistrySection, NextIndexValueName, "");
            return NextIndexFrom(raw);
        }

        /// <summary>The counter logic alone, without touching the registry — so it can be unit
        /// tested directly. An unparsable or sub-1 value means "never assigned one before",
        /// i.e. the first slot.</summary>
        internal static int NextIndexFrom(string? storedCounter) =>
            int.TryParse(storedCounter, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 1
                ? n
                : 1;

        /// <summary>Parses the stored <c>"index|path;index|path"</c> value. A segment that is
        /// not well-formed (missing the separator, a non-positive or unparsable index, an empty
        /// path) is dropped rather than failing the whole read — one corrupted entry must not
        /// hide every other configured library.</summary>
        internal static IReadOnlyList<Entry> ParseEntries(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<Entry>();

            var list = new List<Entry>();
            foreach (var segment in raw.Split(Separator))
            {
                string s = segment.Trim();
                if (s.Length == 0) continue;

                int at = s.IndexOf(IndexPathSeparator);
                if (at <= 0 || at == s.Length - 1) continue;

                if (!int.TryParse(s.AsSpan(0, at), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) || index < 1)
                    continue;

                string path = s[(at + 1)..];
                list.Add(new Entry(index, path));
            }
            return list;
        }

        /// <summary>The inverse of <see cref="ParseEntries"/>.</summary>
        internal static string SerializeEntries(IEnumerable<Entry> entries) =>
            string.Join(Separator, entries.Select(e => e.Index.ToString(CultureInfo.InvariantCulture) + IndexPathSeparator + e.Path));

        private static void Save(IEnumerable<Entry> entries) =>
            AppSettingsRegistry.Set(RegistryApp, RegistrySection, RegistryValueName, SerializeEntries(entries));

        private static string TryFullPath(string path)
        {
            try { return Path.GetFullPath(path); }
            catch { return path; }
        }

        private static bool FileExists(string path)
        {
            // Same defensiveness as SeforimDbPathResolver: a disconnected network drive or a
            // removed USB stick can make File.Exists throw rather than return false.
            try { return File.Exists(path); }
            catch { return false; }
        }
    }
}
