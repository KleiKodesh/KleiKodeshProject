using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
    /// Stored as one registry value, paths joined with ';' — the same separator Windows itself
    /// uses for PATH, and simpler than a JSON array for something only this class ever parses.
    /// A path legitimately containing ';' cannot be represented; that is an accepted limit, not
    /// a bug, and <see cref="Add"/> refuses such a path rather than silently truncating it.
    /// </summary>
    public static class SeforimDbAdditionalLibraries
    {
        private const string RegistryApp = "KitveiHakodesh";
        private const string RegistrySection = "Database";
        private const string RegistryValueName = "AdditionalPaths";
        private const char Separator = ';';

        /// <summary>Every configured additional path, exactly as stored — no existence check.
        /// For a settings UI that wants to show a broken entry rather than hide it.</summary>
        public static IReadOnlyList<string> GetAllRaw()
        {
            string value = AppSettingsRegistry.Get(RegistryApp, RegistrySection, RegistryValueName, "");
            if (string.IsNullOrWhiteSpace(value)) return Array.Empty<string>();

            return value
                .Split(Separator)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToArray();
        }

        /// <summary>The configured additional paths that currently exist on disk — what
        /// <see cref="KitveiHakodesh.Core.SeforimDb.SeforimDbQueries"/> actually attaches.
        /// A path that has moved or been deleted is skipped here exactly as the primary path
        /// is skipped by <see cref="SeforimDbPathResolver"/> — silently dropping a missing
        /// library is safer than failing every catalog read over one bad entry.</summary>
        public static IReadOnlyList<string> GetAll() =>
            GetAllRaw().Where(FileExists).ToArray();

        /// <summary>Adds <paramref name="path"/> if it is not already present (case-insensitive,
        /// by full path) and does not contain the separator. Does not require the file to exist
        /// yet — a user may add a path before restoring the file, exactly like the primary path
        /// setting.</summary>
        public static bool Add(string? rawPath)
        {
            string path = SeforimDbLibrarySwitcher.NormalizePath(rawPath);
            if (path.Length == 0 || path.IndexOf(Separator) >= 0) return false;

            string full = TryFullPath(path);
            var current = GetAllRaw();
            if (current.Any(p => string.Equals(TryFullPath(p), full, StringComparison.OrdinalIgnoreCase)))
                return false;

            Save(current.Append(path));
            return true;
        }

        /// <summary>Removes a configured path (case-insensitive, by full path). Returns false
        /// when it was not present.</summary>
        public static bool Remove(string? rawPath)
        {
            string path = SeforimDbLibrarySwitcher.NormalizePath(rawPath);
            string full = TryFullPath(path);
            var current = GetAllRaw();
            var remaining = current.Where(p => !string.Equals(TryFullPath(p), full, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (remaining.Length == current.Count) return false;

            Save(remaining);
            return true;
        }

        /// <summary>Forgets every additional library.</summary>
        public static void Clear() => AppSettingsRegistry.Set(RegistryApp, RegistrySection, RegistryValueName, "");

        private static void Save(IEnumerable<string> paths) =>
            AppSettingsRegistry.Set(RegistryApp, RegistrySection, RegistryValueName, string.Join(Separator, paths));

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
