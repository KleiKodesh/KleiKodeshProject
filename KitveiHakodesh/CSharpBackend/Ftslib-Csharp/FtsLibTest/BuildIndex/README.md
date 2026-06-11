# BuildIndex/

Index building tests.

## Files

| File | Purpose |
|---|---|
| `BuildFreshTest.cs` | Build index from scratch |
| `BuildTest.cs` | Incremental builds and segment merging |

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

## Expected Behavior

- Build completes without errors
- Segment files are created
- Term dictionary contains all terms
- Posting lists are sorted and delta-encoded
- Merge reduces segment count over time
