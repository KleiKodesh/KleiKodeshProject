// Regression guard for "an additional library's book titles are missing from book-name search".
//
// The catalog index is stamped (catalogtoc.ver) with a fingerprint of the sources it answers for.
// Two ways that stamp used to certify coverage the index did not have, which leaves a library's
// titles unsearchable until its file happens to change:
//
//   A. An index built by an OLDER build (one that dropped the library whole - e.g. because the
//      library has no alt_toc_structure table) carries the same stamp a current build computes for
//      the same files, so the current build trusted it and never rebuilt. Fixed by bumping
//      IndexFormatVersion: every pre-fix index is stale exactly once.
//   B. A build that could not read a library (file busy, unreadable table) still wrote the stamp
//      for the FULL library set, so the skip was cached as "complete". Fixed by stamping only the
//      libraries the build actually covered; the next start sees a mismatch and retries.
//
// Both use a schema-6+ shaped library (line_content sidecar, zstd-compressed with its own
// dictionary, NO alt_toc_structure/alt_toc_entry) - the shape of the real personal library this
// was reported against - and run the REAL coordinator/index/search, not a mock.
using System;
using System.IO;
using System.Linq;
using System.Threading;
using KitveiHakodesh.Core.SeforimDb;
using KitveiHakodesh.Core.Settings;
using KitveiHakodesh.Core.SeforimDbCatalog;
using Microsoft.Data.Sqlite;

namespace KitveiHakodeshService.Tests
{
    public static class CatalogCoverageVerify
    {
        private const string LibraryTitle = "ספר אישי דחוס";

        public static int Run()
        {
            int failures = 0;
            void Check(bool ok, string what, string? detail = null)
            {
                if (ok) Console.WriteLine("  OK: " + what);
                else { failures++; Console.Error.WriteLine("  FAIL: " + what + (detail is null ? "" : " - " + detail)); }
            }

            string dir = Path.Combine(Path.GetTempPath(), "catcoverage-" + Environment.ProcessId);
            Directory.CreateDirectory(dir);
            try
            {
                string primary = Path.Combine(dir, "seforim.db");
                string lib = Path.Combine(dir, "personal-compact.db");
                HardeningVerify.BuildLibrary(primary, "ראשי", "ספר ראשי", "טקסט ראשי", "פרק", HardeningVerify.Shape.Plain, altToc: true, regularToc: true);
                HardeningVerify.BuildLibrary(lib, "אישי", LibraryTitle, "טקסט אישי", "פרק", HardeningVerify.Shape.SplitCompressed, altToc: false, regularToc: true, dictSeed: 7);
                // Old enough that the readiness gate ("file written in the last few seconds") passes.
                var old = DateTime.UtcNow.AddHours(-2);
                File.SetLastWriteTimeUtc(primary, old);
                File.SetLastWriteTimeUtc(lib, old);

                var entries = new[] { new SeforimDbAdditionalLibraries.Entry(1, lib) };
                var libs = SeforimDbAttachedLibraries.Resolve(entries);
                Check(libs.Count == 1, "the compressed, alt-TOC-less library attaches", "resolved " + libs.Count);

                // ── 0. Baseline: a fresh build finds the library's title ────────────────────
                string idx0 = Path.Combine(dir, "idx0");
                Check(TitleFound(primary, idx0, entries), "baseline: fresh build finds the library's book title by name");

                // ── A. index from an older build, same stamp ────────────────────────────────
                string idxA = Path.Combine(dir, "idxA");
                {
                    // What an old build left behind: an index WITHOUT the library, stamped with the
                    // fingerprint of the full set under the format version that build used.
                    using var oldIndex = new SeforimDbCatalogIndex(idxA, primary);
                    oldIndex.BuildAndSwitch(SeforimDbContentFingerprint.ComputeAll(primary, libs, "v20"));
                }
                Check(TitleFound(primary, idxA, entries),
                    "A. an index stamped by the previous format version is rebuilt and then finds the library's title");

                // ── B. build that could not read the library must not certify it ───────────
                string idxB = Path.Combine(dir, "idxB");
                string fullHash = SeforimDbCatalogIndex.ComputeDbHash(primary, libs);
                {
                    using var index = new SeforimDbCatalogIndex(idxB, primary, libs);
                    var skipped = 0;
                    index.SourceSkipped = (_, _) => skipped++;
                    // Hold the library file exclusively for the duration of the build: it was
                    // attachable a moment ago (Resolve above) and is not readable now.
                    SqliteConnection.ClearAllPools(); // pooled handles from the runs above would keep it open
                    using (new FileStream(lib, FileMode.Open, FileAccess.Read, FileShare.None))
                        index.BuildAndSwitch(fullHash);
                    Check(!string.Equals(index.ActiveHash, fullHash, StringComparison.OrdinalIgnoreCase),
                        "B. a build that skipped the library does not stamp the full-set fingerprint",
                        "stamp was the full-set fingerprint although the library was not indexed (skipped callbacks: " + skipped + ")");
                }
                // The file is readable again and unchanged: the next process start must retry.
                Check(TitleFound(primary, idxB, entries),
                    "B. after the library becomes readable again, the next start rebuilds and finds its title");
            }
            finally
            {
                try { SqliteConnection.ClearAllPools(); Directory.Delete(dir, recursive: true); } catch { }
            }

            Console.WriteLine(failures == 0 ? "ALL OK" : failures + " FAILURE(S)");
            return failures;
        }

        /// <summary>Starts a coordinator exactly like the service does, lets it decide whether to
        /// (re)build, waits for it, and reports whether book-name search finds the library's book
        /// with its global id.</summary>
        private static bool TitleFound(string primary, string indexPath, SeforimDbAdditionalLibraries.Entry[] entries)
        {
            using var coordinator = new SeforimDbCatalogIndexCoordinator(() => primary, indexPath, (_, _, _) => { }, entries);
            var deadline = DateTime.UtcNow.AddSeconds(60);
            coordinator.EnsureIndex();
            Thread.Sleep(200);
            while (DateTime.UtcNow < deadline && (coordinator.IsIndexing || !coordinator.IsReady)) Thread.Sleep(100);
            var result = coordinator.Search(LibraryTitle);
            int expected = SeforimDbLibraryId.ToGlobal(1, 1);
            return result.Ready && (result.Results?.Any(h => h.Level == 0 && h.BookId == expected) ?? false);
        }
    }
}
