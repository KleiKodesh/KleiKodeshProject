using FtsLib.Indexing;
using FtsLib.SeforimDb;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace FtsLibTest
{
    /// <summary>
    /// Validates the doc_source (docId→corpus) mapping end to end:
    ///
    ///   U — DocSourceMap unit tests (clip / coalesce / resolve / split)
    ///   B — fresh build: every segment persists an identity row; rows tile
    ///       the built doc range contiguously; streaming search unaffected
    ///   L — legacy fallback: a segment with the table dropped behaves as
    ///       library-identity (mixed old/new index)
    ///   M — force merge: rows carried through the merge (incl. the legacy
    ///       gap) and coalesced; search results byte-identical to pre-merge
    ///   P — purge: delete + purge rewrites segments without losing rows
    ///   I — interrupt/resume: rows still tile with no overlap after a
    ///       cancelled and resumed build (the resume-overlap canary)
    ///
    /// Usage: FtsLibTest.exe docsource [limit]     (default 600000 lines)
    /// </summary>
    internal static class DocSourceTest
    {
        private static string IndexDirA =>
            Path.Combine(Path.GetTempPath(), "FtsDocSourceTestIndex");
        private static string IndexDirB =>
            Path.Combine(Path.GetTempPath(), "FtsDocSourceTestIndex_Resume");

        private static int _passed, _failed;

        // Set by phase L: true when the table was dropped from the ONLY live
        // segment (single-segment LSM timing — common on the slower net48
        // runtime), so the later force merge is an ALL-legacy merge and its
        // target legitimately carries no doc_source table at all.
        private static bool _droppedAllSegments;

        public static void Run(string[] args)
        {
            int limit = args.Length > 1 && int.TryParse(args[1], out int l) ? l : 600_000;

            string dbPath = BuildTest.ResolveDbPath();
            if (!File.Exists(dbPath))
            {
                Console.WriteLine("[DocSourceTest] DB not found: " + dbPath);
                return;
            }

            Console.WriteLine();
            Console.WriteLine("╔══ DOC_SOURCE TEST ═════════════════════════════════════════════════");
            Console.WriteLine($"║  DB     : {dbPath}");
            Console.WriteLine($"║  Index  : {IndexDirA}");
            Console.WriteLine($"║  Limit  : {limit:N0} lines");
            Console.WriteLine("╠════════════════════════════════════════════════════════════════════");

            _passed = 0; _failed = 0;

            UnitTests();

            WipeDir(IndexDirA);
            WipeDir(IndexDirB);

            try
            {
                var baselineIds = BuildAndVerify(dbPath, limit);       // B
                LegacyFallback(dbPath, baselineIds);                   // L
                MergeCarry(dbPath, baselineIds);                       // M
                PurgeCarry(dbPath, baselineIds);                       // P
                InterruptResume(dbPath, limit);                        // I
            }
            catch (Exception ex)
            {
                Fail("unhandled exception: " + ex);
            }

            Console.WriteLine("╠════════════════════════════════════════════════════════════════════");
            Console.WriteLine($"║  RESULT: {_passed} passed, {_failed} failed");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════════");

            if (_failed == 0)
            {
                // Leave dirs behind on failure for inspection; clean on success.
                WipeDir(IndexDirA);
                WipeDir(IndexDirB);
            }

            Environment.ExitCode = _failed == 0 ? 0 : 1;
        }

        // ── U: unit tests ─────────────────────────────────────────────

        private static void UnitTests()
        {
            Section("U: DocSourceMap unit tests");

            // Identity clip → single library-identity row over the batch.
            var clip = DocSourceMap.Identity.Clip(5, 100);
            Check("U1 identity clip", clip.Count == 1 &&
                clip[0].DocLo == 5 && clip[0].DocHi == 100 &&
                clip[0].Source == 0 && clip[0].SrcLo == 5);

            // Adjacent same-affine rows coalesce; a gap keeps them apart.
            var m1 = DocSourceMap.FromRows(new[]
            {
                new DocSourceRange(11, 20, 0, 11),
                new DocSourceRange(1, 10, 0, 1),
                new DocSourceRange(25, 30, 0, 25),
            });
            Check("U2 coalesce adjacent, keep gap", m1.Rows.Count == 2 &&
                m1.Rows[0].DocLo == 1 && m1.Rows[0].DocHi == 20 &&
                m1.Rows[1].DocLo == 25 && m1.Rows[1].DocHi == 30);

            // Overlapping duplicates that agree merge silently.
            var m2 = DocSourceMap.FromRows(new[]
            {
                new DocSourceRange(1, 10, 0, 1),
                new DocSourceRange(5, 15, 0, 5),
            });
            Check("U3 agreeing overlap merges", m2.Rows.Count == 1 &&
                m2.Rows[0].DocLo == 1 && m2.Rows[0].DocHi == 15);

            // Different corpora never coalesce; resolve applies the affine rule.
            const int BASE = 1_000_000_000;
            var m3 = DocSourceMap.FromRows(new[]
            {
                new DocSourceRange(1, 6_543_318, 0, 1),
                new DocSourceRange(BASE + 1, BASE + 42, 1, 1),
            });
            m3.Resolve(BASE + 5, out int src, out int sid);
            Check("U4 corpus resolve", m3.Rows.Count == 2 && src == 1 && sid == 5);
            m3.Resolve(123, out src, out sid);
            Check("U5 library resolve", src == 0 && sid == 123);
            m3.Resolve(900_000_000, out src, out sid); // uncovered → identity
            Check("U6 uncovered → identity", src == 0 && sid == 900_000_000);

            // Split: pure library ids → null (fast path).
            var libIds = new List<int> { 3, 800, 6_000_000 };
            Check("U7 split fast path", m3.SplitBySource(libIds) == null);

            // Split: mixed ids → two runs with correct offsets.
            var mixed = new List<int> { 3, 800, BASE + 2, BASE + 40 };
            var runs = m3.SplitBySource(mixed);
            Check("U8 split runs", runs != null && runs.Count == 2 &&
                runs[0].Source == 0 && runs[0].Start == 0 && runs[0].Count == 2 && runs[0].Offset == 0 &&
                runs[1].Source == 1 && runs[1].Start == 2 && runs[1].Count == 2 &&
                runs[1].Offset == 1L - (BASE + 1));

            // Identity map never splits.
            Check("U9 identity never splits", DocSourceMap.Identity.SplitBySource(mixed) == null);

            // Conflict: disagreeing overlap — first row wins, tail clipped.
            var m4 = DocSourceMap.FromRows(new[]
            {
                new DocSourceRange(1, 10, 0, 1),
                new DocSourceRange(5, 20, 1, 500),
            });
            m4.Resolve(7, out src, out sid);
            bool firstWins = src == 0 && sid == 7;
            m4.Resolve(15, out src, out sid);
            Check("U10 conflict first-wins + tail clip", firstWins && src == 1 && sid == 510);
        }

        // ── B: fresh build ────────────────────────────────────────────

        /// <summary>Builds a fresh index and verifies segment rows + search. Returns
        /// the baseline SearchIds result for the probe queries (used by later phases).</summary>
        private static Dictionary<string, List<int>> BuildAndVerify(string dbPath, int limit)
        {
            Section($"B: fresh build ({limit:N0} lines)");

            var index = new SeforimIndex(IndexDirA, dbPath);
            index.BuildIndex(limit: limit);
            index.DeleteBuildProgressFile(); // completed build — finalized state

            var seforim = new SeforimIndex(IndexDirA, dbPath);
            VerifySegmentRows(seforim, "B1");

            // Rows across all live segments must tile [1..N] exactly (library
            // line ids are dense from 1, so a completed limit-N build covers 1..N).
            var allRows = CollectAllRows(seforim);
            var tiled   = DocSourceMap.FromRows(allRows);
            Check("B2 rows tile [1..N]", tiled.Rows.Count == 1 &&
                tiled.Rows[0].DocLo == 1 && tiled.Rows[0].DocHi == limit &&
                tiled.Rows[0].Source == 0 && tiled.Rows[0].SrcLo == 1,
                $"rows={FormatRows(tiled.Rows)} expected [1..{limit}]");

            // Streaming search: ascending ids, matches SearchIds, resolver identity.
            var baseline = new Dictionary<string, List<int>>();
            foreach (var q in ProbeQueries)
            {
                var ids = seforim.SearchIds(q).ToList();
                baseline[q] = ids;

                var streamed = new List<int>();
                foreach (var r in seforim.Search(q)) streamed.Add(r.LineId);

                bool ascending = IsStrictlyAscending(streamed);
                Check($"B3 stream==ids \"{q}\"", ascending && streamed.SequenceEqual(ids),
                    $"streamed={streamed.Count} ids={ids.Count} ascending={ascending}");
            }

            // Resolver: identity for sampled hits.
            var sample = baseline.Values.First().Take(50).ToList();
            bool allIdentity = sample.Count > 0;
            foreach (var id in sample)
            {
                seforim.TryResolveDocId(id, out int src, out int sid);
                if (src != 0 || sid != id) { allIdentity = false; break; }
            }
            Check("B4 resolver identity", allIdentity);

            // Filtered search returns exactly the filter ∩ results, in order.
            var q0    = ProbeQueries[0];
            var full  = baseline[q0];
            var keep  = full.Where((_, i) => i % 3 == 0).ToList();
            var fids  = seforim.SearchIds(q0, filterIds: keep).ToList();
            Check("B5 filterIds subset", fids.SequenceEqual(keep),
                $"got {fids.Count}, expected {keep.Count}");

            return baseline;
        }

        // ── L: legacy fallback ────────────────────────────────────────

        private static void LegacyFallback(string dbPath, Dictionary<string, List<int>> baseline)
        {
            Section("L: legacy fallback (doc_source dropped from first segment)");

            // Drop the table from the FIRST segment — simulates a segment written
            // before doc_source existed sharing the index with new segments.
            // The post-build segment count is LSM-timing-dependent (the slower
            // net48 runtime often converges to a single segment): with ≥2
            // segments this sets up a MIXED index; with exactly 1 it sets up an
            // ALL-legacy index — both are real upgrade states worth covering,
            // and phase M adapts its merged-rows expectation accordingly.
            var seforim = new SeforimIndex(IndexDirA, dbPath);
            List<(string dat, string db)> live;
            using (seforim.AcquireSearchLease(out live)) { }
            _droppedAllSegments = live.Count == 1;
            var firstDb = live.OrderBy(p => p.dat, StringComparer.Ordinal).First().db;
            SegmentStore.DropDocSourceTableForTest(firstDb);
            Console.WriteLine($"║    dropped doc_source from {Path.GetFileName(firstDb)}" +
                (_droppedAllSegments ? " (only segment — ALL-legacy mode)" : ""));

            var reopened = new SeforimIndex(IndexDirA, dbPath);

            // Search results must be UNCHANGED — the dropped span resolves
            // through the identity fallback, which is the same mapping.
            foreach (var q in ProbeQueries)
            {
                var ids = reopened.SearchIds(q).ToList();
                Check($"L1 search unchanged \"{q}\"", ids.SequenceEqual(baseline[q]),
                    $"got {ids.Count}, expected {baseline[q].Count}");

                var streamed = new List<int>();
                foreach (var r in reopened.Search(q)) streamed.Add(r.LineId);
                Check($"L2 stream unchanged \"{q}\"", streamed.SequenceEqual(baseline[q]));
            }

            // Resolver still identity everywhere (covered + fallback spans).
            reopened.TryResolveDocId(1, out int s1, out int i1);
            reopened.TryResolveDocId(baseline[ProbeQueries[0]].Last(), out int s2, out int i2);
            Check("L3 resolve via fallback", s1 == 0 && i1 == 1 && s2 == 0 &&
                i2 == baseline[ProbeQueries[0]].Last());
        }

        // ── M: force merge carry ──────────────────────────────────────

        private static void MergeCarry(string dbPath, Dictionary<string, List<int>> baseline)
        {
            Section("M: force merge carries doc_source (incl. legacy gap)");

            var seforim = new SeforimIndex(IndexDirA, dbPath);
            seforim.ForceMerge();

            List<(string dat, string db)> live;
            using (seforim.AcquireSearchLease(out live)) { }
            Check("M1 single segment", live.Count == 1, $"got {live.Count}");

            // The merged segment's rows cover everything EXCEPT the span of the
            // segment whose table was dropped in phase L (that span rides the
            // identity fallback). All surviving rows are identity, so search
            // results must still match the baseline exactly. When phase L
            // dropped the ONLY segment's table, this is an all-legacy merge:
            // the target legitimately has no doc_source at all and everything
            // rides the fallback.
            var rows = SegmentStore.ReadDocSourceRows(live[0].db);
            if (_droppedAllSegments)
            {
                Check("M2 all-legacy merge carries no rows (full fallback)",
                    rows.Count == 0, FormatRows(rows));
            }
            else
            {
                bool allIdentityRows = rows.Count > 0;
                foreach (var r in rows)
                    if (r.Source != 0 || r.SrcLo != r.DocLo) allIdentityRows = false;
                Check("M2 merged rows present + identity", allIdentityRows,
                    FormatRows(rows));
            }

            foreach (var q in ProbeQueries)
            {
                var ids = seforim.SearchIds(q).ToList();
                Check($"M3 post-merge search \"{q}\"", ids.SequenceEqual(baseline[q]),
                    $"got {ids.Count}, expected {baseline[q].Count}");
            }

#if NET10_0_OR_GREATER
            // SearchParallel path (net10 only) — same results as streaming.
            var par = seforim.SearchParallel(ProbeQueries[0]).Select(r => r.LineId).ToList();
            Check("M4 SearchParallel parity", par.SequenceEqual(baseline[ProbeQueries[0]]));
#endif
        }

        // ── P: purge carry ────────────────────────────────────────────

        private static void PurgeCarry(string dbPath, Dictionary<string, List<int>> baseline)
        {
            Section("P: purge rewrites keep doc_source");

            var q0     = ProbeQueries[0];
            var toKill = baseline[q0].Take(50).ToList();

            using (var writer = new IndexWriter(IndexDirA))
            {
                foreach (var id in toKill) writer.Delete(id);
                writer.Purge();
            }

            var seforim = new SeforimIndex(IndexDirA, dbPath);
            var ids     = seforim.SearchIds(q0).ToList();
            Check("P1 deleted ids gone", !ids.Any(toKill.Contains) &&
                ids.Count == baseline[q0].Count - toKill.Count,
                $"got {ids.Count}, expected {baseline[q0].Count - toKill.Count}");

            List<(string dat, string db)> live;
            using (seforim.AcquireSearchLease(out live)) { }
            if (_droppedAllSegments)
            {
                // All-legacy index: there were no rows before the purge, so the
                // rewrite must not INVENT any — full-fallback state persists.
                bool noRows = live.Count > 0;
                foreach (var (dat, db) in live)
                    if (SegmentStore.ReadDocSourceRows(db).Count != 0) noRows = false;
                Check("P2 purge keeps all-legacy state (no rows)", noRows);
            }
            else
            {
                bool anyRows = live.Count > 0;
                foreach (var (dat, db) in live)
                    if (SegmentStore.ReadDocSourceRows(db).Count == 0) anyRows = false;
                Check("P2 rows survive purge rewrite", anyRows);
            }

            // Rows may over-cover purged docIds — that is by design (they map
            // docId→source, not docId→existence). Resolution stays identity.
            seforim.TryResolveDocId(toKill[0], out int src, out int sid);
            Check("P3 resolve still identity", src == 0 && sid == toKill[0]);
        }

        // ── I: interrupt / resume ─────────────────────────────────────

        private static void InterruptResume(string dbPath, int limit)
        {
            Section("I: interrupt + resume keeps rows tiled, no overlap");

            // Phase 1: cancel after the first flush completes.
            var cts   = new CancellationTokenSource();
            var index = new SeforimIndex(IndexDirB, dbPath);
            try
            {
                index.BuildIndex(limit: limit, onFlush: () => cts.Cancel(), ct: cts.Token);
                Fail("I0 build was not interrupted (limit too small for a mid-build flush?)");
                return;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("║    build cancelled after first flush — resuming...");
            }

            // Phase 2: resume to completion (fresh SeforimIndex, like a restart).
            // `limit` counts lines read THIS session, so the resume pass must ask
            // only for the remainder — library line ids are dense from 1, so the
            // resume line id equals the number of lines already indexed.
            var resumed  = new SeforimIndex(IndexDirB, dbPath);
            int resumeAt = resumed.GetResumeLineId();
            Check("I0b resume point recorded", resumeAt > 0 && resumeAt < limit,
                $"resumeAt={resumeAt}");
            resumed.BuildIndex(limit: limit - resumeAt);
            resumed.DeleteBuildProgressFile();

            var reopened = new SeforimIndex(IndexDirB, dbPath);
            VerifySegmentRows(reopened, "I1");

            var allRows = CollectAllRows(reopened);
            allRows.Sort((a, b) => a.DocLo.CompareTo(b.DocLo));

            // The canary below is vacuous with fewer than two rows — make that
            // visible instead of silently passing.
            Check("I2a resume produced ≥2 segments", allRows.Count >= 2,
                $"rows={allRows.Count} (overlap canary needs ≥2)");

            // No overlap between any two segment rows (resume-overlap canary):
            // overlapping doc ranges are the exact corruption mode the resume
            // floor exists to prevent.
            bool noOverlap = true;
            for (int i = 1; i < allRows.Count; i++)
                if (allRows[i].DocLo <= allRows[i - 1].DocHi) noOverlap = false;
            Check("I2 no doc-range overlap across segments", noOverlap, FormatRows(allRows));

            var tiled = DocSourceMap.FromRows(allRows);
            Check("I3 rows tile [1..N] after resume", tiled.Rows.Count == 1 &&
                tiled.Rows[0].DocLo == 1 && tiled.Rows[0].DocHi == limit,
                $"rows={FormatRows(tiled.Rows)} expected [1..{limit}]");

            // Probe search sanity on the resumed index.
            var ids = reopened.SearchIds("כי ביצחק").ToList();
            Check("I4 probe search has known id 548", ids.Contains(548),
                $"got {ids.Count} ids");
        }

        // ── Helpers ───────────────────────────────────────────────────

        private static readonly string[] ProbeQueries =
        {
            "תורה",        // common literal — thousands of hits
            "כי ביצחק",    // 2-word AND
            "בראשית ברא",  // phrase-ish AND
        };

        /// <summary>Every live segment must carry a doc_source table whose rows are
        /// library-identity and whose span matches the postings' actual min/max docId.</summary>
        private static void VerifySegmentRows(SeforimIndex seforim, string label)
        {
            List<(string dat, string db)> live;
            using (seforim.AcquireSearchLease(out live)) { }

            Check($"{label} live segments exist", live.Count > 0, "no live segments");

            foreach (var (dat, db) in live)
            {
                var rows = SegmentStore.ReadDocSourceRows(db);
                var (minDoc, maxDoc) = ScanPostingBounds(dat);

                bool ok = rows.Count == 1 &&
                          rows[0].Source == 0 &&
                          rows[0].SrcLo == rows[0].DocLo &&
                          rows[0].DocLo == minDoc &&
                          rows[0].DocHi == maxDoc;
                Check($"{label} {Path.GetFileName(dat)} identity row == postings [{minDoc}..{maxDoc}]",
                    ok, FormatRows(rows));
            }
        }

        private static List<DocSourceRange> CollectAllRows(SeforimIndex seforim)
        {
            List<(string dat, string db)> live;
            using (seforim.AcquireSearchLease(out live)) { }
            var all = new List<DocSourceRange>();
            foreach (var (dat, db) in live)
                all.AddRange(SegmentStore.ReadDocSourceRows(db));
            return all;
        }

        /// <summary>Min/max docId actually present in a segment's postings — decoded
        /// from the .dat alone (provider-free). Ground truth for the row spans.</summary>
        private static (int min, int max) ScanPostingBounds(string datPath)
        {
            int min = int.MaxValue, max = int.MinValue;
            using (var reader = new SegmentReader(datPath))
            {
                while (reader.MoveNext())
                {
                    var chunk = reader.CurrentChunk;
                    int pos   = 0;
                    uint first = FtsLib.Search.VarInt.Read(chunk, ref pos, reader.CurrentChunkLen);
                    int firstDoc = (int)((long)first + int.MinValue);
                    int lastDoc  = (int)((long)reader.CurrentLastEncoded + int.MinValue);
                    if (firstDoc < min) min = firstDoc;
                    if (lastDoc  > max) max = lastDoc;
                }
            }
            return (min, max);
        }

        private static bool IsStrictlyAscending(List<int> ids)
        {
            for (int i = 1; i < ids.Count; i++)
                if (ids[i] <= ids[i - 1]) return false;
            return true;
        }

        private static string FormatRows(IReadOnlyList<DocSourceRange> rows) =>
            "[" + string.Join(", ", rows.Select(r => r.ToString())) + "]";

        private static void Section(string name)
        {
            Console.WriteLine("╠── " + name);
        }

        private static void Check(string name, bool ok, string detail = null)
        {
            if (ok) { _passed++; Console.WriteLine($"║    PASS  {name}"); }
            else
            {
                _failed++;
                Console.WriteLine($"║    FAIL  {name}" +
                    (detail == null ? "" : $"  → {detail}"));
            }
        }

        private static void Fail(string msg)
        {
            _failed++;
            Console.WriteLine("║    FAIL  " + msg);
        }

        private static void WipeDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch { }
        }
    }
}
