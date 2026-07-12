# FtsLib Index Corruption / Merge Hang — Diagnosis

**Date:** 2026-07-12
**Symptoms investigated:**

1. Search is blocked while merging (`IndexMergingException`), unlike Lucene which searches safely during indexing/merging.
2. Sporadic, unreproducible index corruption at index time — results from **early segments** silently disappear.
3. Full merge works in tests but fails/corrupts in production (full index only, never the 500k index).
4. Closing the app mid-merge hangs the process.

**Conclusion:** all four symptoms trace back to one design decision that differs from Lucene (source-file deletion is part of the merge commit, and the directory listing is the manifest), plus two concrete bugs. Every claim below was verified against the code and against Lucene's actual source (`SegmentInfos.java`, `IndexFileDeleter.java`, `IndexWriter.java` from apache/lucene) — nothing is guessed.

---

## Bug 1 — Crash recovery deletes good data (root cause of the corruption)

The merge commit sequence in `FtsLib/Indexing/SegmentMerger.cs` (lines ~146–203) is:

1. Rename `.tmp` files → final names (`seg_L+1_ID.dat` / `.db`)
2. Delete the source segments **one by one**
3. Write `END_MERGE` to the WAL

Now consider a kill **in the middle of step 2** — e.g. sources `seg_1_0` and `seg_1_1` already deleted, `seg_1_2` and `seg_1_3` still on disk.

Recovery (`SegmentStore.Recover`, `FtsLib/Indexing/SegmentStore.cs:279`) only treats the target as complete when **all** sources are gone:

```csharp
if (targetExists && !sourcesExist)   // Case B — register target
```

With *some* sources surviving, it falls through to the re-run path (`SegmentStore.cs:305–333`) which:

- **deletes the fully-merged target** — the only file still containing the data of the already-deleted sources — and
- re-runs the merge from **only the surviving sources**.

The data of every source deleted before the kill is **permanently lost**. Since sources are deleted roughly in segment-ID order, it is the **earliest segments** that vanish — exactly the observed symptom ("results from early segments stop showing up").

**Worse sub-variant:** if only *one* source survived, the re-run `MergeLevel` hits the `segIds.Count < 2` early-return (`SegmentMerger.cs:55`), does nothing, and the `finally` clears the WAL anyway — silent data loss with no trace at all.

**The logic is backwards.** Because the `File.Move` renames in step 1 happen strictly before any deletion in step 2, *a target that exists at its final (non-`.tmp`) path is always complete*. Recovery should trust it (generalized Case B: register target, delete leftover sources) — never delete it.

### Why CrashMergeTest passes anyway

`FtsLibTest/BuildIndex/CrashMergeTest.cs` scenario **H** simulates exactly this crash state — but:

- The probe only checks that line id **548** for `"כי ביצחק"` appears, and that line lives in the big L2 backup segment, which is **never one of the deleted sources** in any scenario.
- The scenarios plant a *copy of a source file* as the fake target, so the "lost" data still exists elsewhere on disk.

The harness literally cannot observe this bug. A proper probe would diff the **full result set** against a known-good index (`FtsLibTest/Shared/DiffIds.cs` already exists for this).

---

## Bug 2 — Hang on close → users kill the process mid-merge (production trigger #1)

There is **no cancellation inside a merge**. The k-way merge loop (`SegmentMerger.WriteMergedDat`, `SegmentMerger.cs:230–295`) never checks a token. On the full index, high-level cascade merges run for **minutes**.

On close, `FtsIndexState.StopAll()` (`KitveiHakodeshLib/Search/FtsIndexState.cs:301`) waits **indefinitely** for the build task, which in `IndexWriter.Dispose` → `SegmentStore.WaitForMerge` waits for the in-flight merge to finish. That is the hang.

Users then kill the process from Task Manager — landing the kill at an arbitrary point in the commit sequence, which feeds Bug 1. **The hang and the corruption are the same bug seen twice.**

---

## Bug 3 — Recovery stomps a live merge from another process (production trigger #2)

`SearchHandler.ExecuteOnDbReady` (`KitveiHakodeshLib/Search/SearchHandler.cs:126–127`) constructs `new SeforimIndex(...)`, whose constructor runs `_store.Recover()` on the index directory — **before** the `IsAnotherProcessBuilding()` check, which only happens later in `StartBuildOrWatch`. `Recover()` is guarded by neither `write.lock` nor the build mutex.

So if instance A is mid-merge and the user opens instance B:

1. B's recovery deletes A's in-flight `.tmp` files (`SegmentStore.cs:180`).
2. B sees A's pending `BEGIN_MERGE` in the WAL, deletes A's target, and **re-runs the merge itself** while A is still writing — two writers on the same files.

This corrupts with **zero crashes involved**, which is why it feels like corruption "without rhyme or reason." The `FileShare.ReadWrite` on the WAL (`SegmentWal.cs:43`) makes the collision silent instead of an error.

### Why the 500k index never breaks

Few segments, merges finish in seconds — the kill window is tiny, the cascades shallow, and close never hangs long enough for users to reach for Task Manager.

---

## Bug 4 — Failed flush is swallowed; build stamps itself complete with missing data

If a background segment write throws, `SegmentStore.WaitForMerge` (`SegmentStore.cs:519–525`) swallows the `AggregateException` as "non-fatal". The final progress write (`IndexingPipeline.cs:312`) then uses `lastWrittenLineId` — so the build completes, the version stamp is written, and the index is marked done while an **entire batch of lines was never indexed**.

---

## How Lucene actually does it (verified from apache/lucene source)

The fundamental difference: in FtsLib, *deleting source files is part of the commit* and *the directory listing is the manifest* (`SegmentLiveState.RebuildFromDisk` scans `seg_*.dat`). In Lucene, neither is true.

1. **The manifest is a file, committed atomically.** `SegmentInfos.prepareCommit()` writes `pending_segments_N` (generation-numbered, never overwritten in place), fsyncs it via `directory.sync(...)`; `finishCommit()` renames it to `segments_N` and calls `dir.syncMetaData()`. A checksum footer (`CodecUtil.writeFooter`) makes partial writes detectable. Readers find the highest valid `segments_N` (`FindSegmentsFile` retry loop). A crash at any instant leaves either the old commit or the new one — never an ambiguous state that recovery must "interpret."

2. **Deletion is garbage collection, never part of commit.** `IndexFileDeleter` reference-counts every file (`incRef`/`decRef`, `checkpoint()`: "We simply incref the files referenced by the new SegmentInfos and decref the files we had previously seen"). Merged-away sources are decRef'd only *after* the new `segments_N` referencing the merged segment is durable, and physically deleted only when no commit point and no open reader references them. At startup it deletes whatever the manifest doesn't reference. There is no case analysis, no "re-run the merge," and no state in which committed data must be reconstructed — precisely why Lucene has no equivalent of the Case A/B/C/D ambiguity.

3. **Search during merge.** Merge work runs on background threads *outside* the writer lock (`ConcurrentMergeScheduler`); only `commitMerge` (a SegmentInfos pointer swap + `checkpoint()`) is synchronized, and it is microseconds. Readers are point-in-time snapshots; NRT readers incRef the files they use ("we incRef all files when we return an NRT reader from IW"), so a merge can retire segments while searches on them continue untouched. FtsLib instead holds the write lock for the entire merge and makes searches throw `IndexMergingException` (`SegmentStore.cs:105`). Note: the existing `SearchLease` mechanism is already ~80% of Lucene's refcounting idea.

4. **Close during merge.** `IndexWriter.close()` waits via `waitForMerges()`; `rollback()` *aborts* running merges quickly. Aborting is always safe in their design: the sources are still committed, and the half-written merged files are unreferenced garbage cleaned at next startup.

---

## Recommended fixes, in order

| # | Fix | Size | Effect |
|---|-----|------|--------|
| 1 | **Recovery: trust a final-path target.** If `targetDat` and `targetDb` both exist at final paths, treat as Case B regardless of surviving sources — register target, delete leftover sources, clear WAL. Never delete a final-path target. Also validate the target fully (scan to end / verify `.db` offsets against `.dat` length) — `ValidateAllSegments` currently reads only the first record. | small | stops the data-loss path |
| 2 | **Guard recovery.** Acquire the build mutex / `write.lock` *before* running `Recover()`; skip recovery entirely when another process holds it (become the watcher first, recover after takeover). | small | stops cross-process stomp |
| 3 | **Merge cancellation.** Thread a `CancellationToken` into `MergeIfNeeded`/`WriteMergedDat` (check every N terms). With fix 1 in place, aborting mid-merge is always safe — sources intact, `.tmp` cleaned at next startup. | small | fixes the close hang |
| 4 | **Manifest redesign (Lucene's model, replaces the WAL).** Generation-numbered `segments.manifest` listing live segments, written tmp → fsync → rename. Commit order: write merged files → fsync → commit new manifest → *then* delete sources as best-effort GC. Recovery = read newest valid manifest, delete everything unreferenced. Eliminates the entire Case A–D analysis; kills at any point are harmless. | medium | the real cure |
| 5 | **Search during merge.** With fix 4: hold the write lock only for the manifest swap; replace `TryEnterReadLock(0)`-throw with a blocking `EnterReadLock` (only ever blocks for the swap instant); delete old sources only once outstanding `SearchLease`s drain. Search then works continuously through indexing and merging, like Lucene. | medium | removes `IndexMergingException` UX |
| 6 | **Don't swallow flush failures.** Surface pipeline faults from `WaitForMerge` so a failed segment write fails the build instead of stamping a complete index with a missing batch. | small | closes silent-gap hole |

Suggested sequence: **1–3 first** (surgical, stops the bleeding), then **4–5** as the proper redesign, with 6 alongside.

### Test-coverage gaps to close

- Probe must diff the **full result set** vs a known-good index, not one line id.
- Scenario H (partial source deletion) with a *real* merged target must be asserted to keep all source data.
- Add: kill during recovery's own re-run merge; two processes where one runs recovery while the other is merging; single-surviving-source recovery.
