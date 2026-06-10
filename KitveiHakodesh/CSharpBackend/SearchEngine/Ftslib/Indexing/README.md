# Indexing/

Index construction and segment management.

## Overview

Implements an LSM-tree (Log-Structured Merge-tree) architecture for building and maintaining the full-text index.

## Architecture

```
Database Lines
      ↓
[Tokenizer] → Words + Positions
      ↓
[RAM Index] → In-memory hash map
      ↓
[Segment Writer] → Immutable segment files
      ↓
[Background Merge] → Compaction
```

## Files

| File | Class | Purpose |
|---|---|---|
| `IndexWriter.cs` | `IndexWriter` | Main API for building indexes |
| `SegmentStore.cs` | `SegmentStore` | Segment file lifecycle |
| `SegmentWriter.cs` | `SegmentWriter` | Write posting lists (delta+varint) |
| `SegmentReader.cs` | `SegmentReader` | Read posting lists |
| `SegmentMerger.cs` | `SegmentMerger` | Merge segments |
| `SegmentLiveState.cs` | `SegmentLiveState` | Segment metadata |
| `SegmentHandle.cs` | `SegmentHandle` | Reference-counted access |
| `SegmentWal.cs` | `SegmentWal` | Write-ahead log |
| `RamIndex.cs` | `RamIndex` | In-memory buffer |
| `RamIndexEntry.cs` | `RamIndexEntry` | RAM index entry |
| `DeleteSet.cs` | `DeleteSet` | Deleted document tracking |
| `IndexDirectory.cs` | — | Directory layout |
| `IndexWriteLock.cs` | `IndexWriteLock` | Exclusive write lock |
| `SearchLease.cs` | `SearchLease` | Allow reads during writes |
| `CorruptIndexException.cs` | — | Exception type |
| `IndexMergingException.cs` | — | Exception type |

## Key Concepts

**Segment:** Immutable index file containing term→postings mapping. Once written, never modified.

**Posting List:** Sorted list of document IDs for a term, encoded as:
- Delta encoding (differences between consecutive IDs)
- VarInt (variable-length integer) compression

**WAL (Write-Ahead Log):** Crash-safe buffer. If process crashes during indexing, recovery replays the WAL.

**Merge Policy:** Small segments are merged into larger ones to:
- Reduce file count
- Improve query performance
- Reclaim space from deleted documents

## Usage

```csharp
using (var store = new SegmentStore(indexPath))
using (var writer = new IndexWriter(store, dbConnection))
{
    writer.BuildIndex(
        limit: 0,  // 0 = all lines
        onProgress: n => Console.WriteLine($"{n} lines")
    );
}
```

## Thread Safety

- `IndexWriter` — Single-threaded for building
- `SegmentStore` — Thread-safe for concurrent searches
- `SegmentMerger` — Background thread for merging

## File Format

Segment files are binary with:
1. Header (version, metadata)
2. Term dictionary (sorted terms)
3. Posting lists (delta+varint encoded)
4. Footer (checksums)
