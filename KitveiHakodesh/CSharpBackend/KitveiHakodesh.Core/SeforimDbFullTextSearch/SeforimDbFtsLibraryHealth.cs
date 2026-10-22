using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace KitveiHakodesh.Core.SeforimDbFullTextSearch
{
    /// <summary>
    /// Which additional libraries the full-text index left OUT because they are damaged, and why.
    ///
    /// A library can pass the cheap attach probe (tables, columns, one decodable row) and still be
    /// corrupt or truncated further into the file. Reading it end to end is the only proof, and the
    /// index coordinator does that BEFORE building (see <see cref="SeforimDbFtsLibraryValidator"/>);
    /// the verdicts are recorded here so the settings page can tell the user why a library is not
    /// searchable instead of the library silently contributing nothing. Process-wide and in-memory:
    /// it is a diagnostic, never an input to a decision.
    /// </summary>
    public static class SeforimDbFtsLibraryHealth
    {
        private static readonly ConcurrentDictionary<string, string> Skipped =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void MarkSkipped(string path, string reason) => Skipped[path] = reason;

        public static void MarkHealthy(string path) => Skipped.TryRemove(path, out _);

        /// <summary>The reason the full-text index skipped <paramref name="path"/>, or null when it
        /// has not been skipped.</summary>
        public static string? TrySkipReason(string path) =>
            Skipped.TryGetValue(path, out var reason) ? reason : null;
    }

    /// <summary>
    /// Proves an additional library can be read END TO END by the very read the index build
    /// performs (<see cref="SeforimDbFtsCorpus.ReadDocumentsInResultOrder"/>). A cheap probe that
    /// decodes one row cannot see a damaged page in the middle of the file or a truncated tail, and
    /// such a library used to fail the whole build with "database disk image is malformed" — taking
    /// the primary library's index down with it.
    ///
    /// The verdict is cached per (path, length, last-write time): an unchanged file is read once per
    /// process, a replaced one is re-checked. <c>force</c> bypasses the cache (used after a build
    /// failed, when the cached "healthy" verdict is exactly what is in doubt).
    /// </summary>
    internal static class SeforimDbFtsLibraryValidator
    {
        private static readonly ConcurrentDictionary<string, (long Length, DateTime WriteUtc, string? Reason)> Cache =
            new ConcurrentDictionary<string, (long, DateTime, string?)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Null when the whole library reads cleanly, else why it cannot be indexed.
        /// Never throws except <see cref="OperationCanceledException"/>.</summary>
        public static string? Check(string path, CancellationToken ct, bool force = false)
        {
            long length;
            DateTime writeUtc;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return "file not found";
                length = info.Length;
                writeUtc = info.LastWriteTimeUtc;
            }
            catch (Exception ex) { return "file not accessible: " + ex.GetType().Name + ": " + ex.Message; }

            if (!force && Cache.TryGetValue(path, out var cached) && cached.Length == length && cached.WriteUtc == writeUtc)
                return cached.Reason;

            string? reason = ReadEverything(path, ct);
            Cache[path] = (length, writeUtc, reason);
            return reason;
        }

        private static string? ReadEverything(string path, CancellationToken ct)
        {
            try
            {
                using var corpus = new SeforimDbFtsCorpus(path);
                corpus.CountDocuments();
                foreach (var _ in corpus.ReadDocumentsInResultOrder(0, ct)) { }
                return null;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return "damaged or unreadable: " + ex.GetType().Name + ": " + ex.Message;
            }
        }
    }
}
