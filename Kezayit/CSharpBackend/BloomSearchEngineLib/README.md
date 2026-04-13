# BloomSearchEngineLib — Bloom Filter Full-Text Search Engine

A .NET class library that builds and queries a Bloom filter index over the seforim (Jewish texts) SQLite database, enabling fast full-text search across millions of lines of text.

## How It Integrates with the VSTO Add-in

`KezayitLib.SearchHandler` owns an instance of `BloomFilterSearcher` and `BloomFilterIndexer`. It is called from the VSTO task pane via the WebView2 message bridge when the user performs a search or triggers an index rebuild.

## How It Works

### Indexing

1. `BloomFilterIndexer` reads every line from the seforim database via `ZayitDbManager`.
2. For each line, it extracts search terms with `TermExtractor` and normalises Hebrew text with `TextNormalizer`.
3. Terms are inserted into a `BloomFilter` (one filter per chunk of lines).
4. Completed chunks are written to disk by `BloomFilterCollectionWriter` as `BloomFilters/lines.dat`.
5. After each committed chunk, a sentinel file (`BloomFilters/indexing.lock`) is updated with `lastLineId:chunkCount` so indexing can resume after a crash.
6. On successful completion the sentinel is deleted and the current app version is written to `BloomFilters/lines.ver`.

### Searching

1. `BloomFilterSearcher.Search(query)` splits the query into terms.
2. `BloomFilterCollectionReader` loads the index and runs each term through the Bloom filters to find candidate line IDs.
3. Candidate lines are hydrated from the database and scored by `SearchEngineMatcher` (proximity, exact match, partial match).
4. Perfect matches are streamed to the caller immediately; top-100 partial matches follow.

### Version Detection

On startup, `SearchHandler.OnDbReady()` compares the installed app version (from `HKEY_CURRENT_USER\SOFTWARE\KleiKodesh` → `Version`) with the version stamped in `BloomFilters/lines.ver`. If they differ, a `bloomIndexVersionMismatch` push event is sent to the Vue app, which shows a confirm dialog asking the user whether to rebuild.

## File Structure

```
BloomSearchEngineLib/
├── BloomFilter.cs                  — Single Bloom filter (bit array + hash functions)
├── BloomFilterCollectionReader.cs  — Reads the on-disk filter collection
├── BloomFilterCollectionWriter.cs  — Writes the on-disk filter collection
├── BloomFilterIndexer.cs           — Builds the index from the database; resume-on-crash
├── BloomFilterSearcher.cs          — Queries the index; streams results
├── BloomFilterSearchModels.cs      — SearchResult / SearchResultItem data models
├── BloomIndexingCoordinator.cs     — Prevents concurrent indexing across processes
├── SearchEngineMatcher.cs          — Scores and ranks candidate lines
├── TermExtractor.cs                — Splits query/text into normalised search terms
├── TextNormalizer.cs               — Strips Hebrew diacritics and normalises characters
└── ZayitDbManager.cs               — SQLite queries for line hydration during search
```

## On-Disk Artefacts

| File                         | Purpose                                          |
| ---------------------------- | ------------------------------------------------ |
| `BloomFilters/lines.dat`     | The Bloom filter index                           |
| `BloomFilters/lines.ver`     | App version stamp written after successful build |
| `BloomFilters/indexing.lock` | Resume sentinel (deleted on clean completion)    |
