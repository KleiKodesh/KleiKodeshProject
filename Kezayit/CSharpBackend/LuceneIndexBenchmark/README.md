# LuceneIndexBenchmark

Console app that benchmarks Lucene.NET indexing and search against the Zayit SQLite database.
Tests the high-RAM single-flush strategy to see if Lucene can match Bloom filter index build times.

## Setup

1. Open `Kezayit.slnx` in Visual Studio.
2. Right-click the `LuceneIndexBenchmark` project → **Manage NuGet Packages** → restore.
   Packages needed (already in `packages.config`):
   - `Lucene.Net` 4.8.0-beta00016
   - `Lucene.Net.Analysis.Common` 4.8.0-beta00016
   - `Stub.System.Data.SQLite.Core.NetFramework` 1.0.119.0 (already in solution packages folder)
3. Build the project.

## Usage

```
LuceneIndexBenchmark.exe build  <db-path> [index-dir] [ram-mb]
LuceneIndexBenchmark.exe search <index-dir>
LuceneIndexBenchmark.exe all    <db-path> [index-dir] [ram-mb]
```

**Build only** (measure index time):
```
LuceneIndexBenchmark.exe build C:\data\zayit.db C:\data\lucene-index 512
```

**Search only** (index must already exist):
```
LuceneIndexBenchmark.exe search C:\data\lucene-index
```

**Build then search** in one run:
```
LuceneIndexBenchmark.exe all C:\data\zayit.db C:\data\lucene-index 512
```

## RAM Buffer Tuning

The `ram-mb` argument controls `IndexWriterConfig.RAMBufferSizeMB`.
The goal is to set it high enough that Lucene never flushes a segment mid-build —
only one flush happens at the very end via `ForceMerge(1)`.

Rough guide for 6M lines:
- 256 MB — likely 2–3 intermediate flushes
- 512 MB — likely 0–1 intermediate flushes (recommended starting point)
- 1024 MB — guaranteed zero flushes if you have the RAM

Watch the console output: if you see no "merging" messages before the final
`ForceMerge` line, the buffer was large enough.

## What It Measures

- **Index build time** — total wall time including ForceMerge and Commit
- **Index size on disk** — printed after build completes
- **Search latency** — single-term, multi-term AND, and phrase queries
- **Phrase search** — demonstrates the key advantage over Bloom filters

## Architecture

| File | Role |
|---|---|
| `Program.cs` | CLI entry point, argument parsing |
| `LuceneIndexBuilder.cs` | Producer/consumer pipeline, IndexWriter config |
| `LuceneSearcher.cs` | Query execution and benchmark runner |
| `HebrewTextNormalizer.cs` | Strips nikud/teamim, tokenizes Hebrew text |
| `SqliteLineReader.cs` | Streams rows from SQLite with minimal overhead |

The pipeline mirrors `BloomFilterIndexer.cs` exactly:
reader thread → worker threads (normalize + build docs) → writer thread (IndexWriter).
