# BuildIndex/

Index building tests.

## Files

| File | Purpose |
|---|---|
| `BuildFreshTest.cs` | Build index from scratch |
| `BuildTest.cs` | Incremental builds and segment merging |
| `CrashMergeTest.cs` | Harsh crash-recovery test for force merge — simulates every phase of the commit sequence |
| `InterruptTest.cs` | Interrupt-and-recover stress test — repeatedly builds index, cancels at random points |
| `MergeTest.cs` | Force-merge corruption diagnostic test — verifies correctness before and after merge |

## BuildFreshTest

Tests building a new index from an empty state:

- Validates segment file creation
- Verifies term dictionary correctness
- Checks posting list integrity
- Measures build time

Usage:
```csharp
BuildFreshTest.Run();
```

## BuildTest

Tests incremental index operations:

- Adding documents to existing index
- Segment merging behavior
- WAL recovery after crash simulation
- Delete set handling

Tests both:
- Fresh builds
- Incremental updates
- Background merging

## CrashMergeTest

Tests crash recovery during force merge by simulating every phase of the commit sequence:

- Killed before merge starts (no WAL, no tmp)
- Killed after WAL BEGIN_MERGE, before any file written
- Killed while writing .dat.tmp (partial/truncated file)
- Killed after .dat.tmp complete, before .db.tmp written
- Killed after .db.tmp complete, before File.Move
- Killed after .dat renamed, before .db renamed
- Killed after both renamed, before source deleted
- Killed after partial source deletion

Each scenario verifies recovery produces a correct index.

## InterruptTest

Stress test that repeatedly builds the index and cancels at random points. Supports three kill modes:

- **hard** — cancels token, abandons task after 50ms (simulates process kill)
- **soft** — cancels token, waits for full drain (clean cancel)
- **fixed** — cancels token, waits indefinitely (fixed StopAll)

Validates that cancellation at any point (mid-flush or mid-merge) never corrupts the index.

## MergeTest

Force-merge diagnostic test:

1. Builds a full index from scratch
2. Searches for "כי ביצחק" — verifies correctness before merge
3. Backs up the index directory
4. Force-merges all segments into one
5. Searches again — verifies correctness after merge

Reports PASS/FAIL with full details.

## Expected Behavior

- Build completes without errors
- Segment files are created
- Term dictionary contains all terms
- Posting lists are sorted and delta-encoded
- Merge reduces segment count over time
