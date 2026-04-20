# BloomSearchEngineLib — Bloom Filter Full-Text Search Engine

A .NET class library that builds and queries a Bloom filter index over the seforim (Jewish texts) SQLite database, enabling fast full-text search across millions of lines of text.

## How It Integrates with the App

`KezayitLib.SearchHandler` owns the `BloomFilterSearcher` and `BloomFilterIndexer`. It is called from the Vue frontend via the WebView2 message bridge when the user performs a search or triggers an index rebuild.

## How It Works

### Indexing

1. `BloomFilterIndexer` reads every line from the seforim database via `ZayitDbManager`.
2. For each line, `TermExtractor` extracts search terms and `TextNormalizer` strips Hebrew diacritics.
3. Terms are inserted into a `BloomFilter` — one filter per chunk of lines (default 100 lines/chunk).
4. Completed chunks are written to disk by `BloomFilterCollectionWriter` as `BloomFilters/lines.dat`.
5. The header is patched on every flush so the file is always self-consistent — a hard kill loses at most one unflushed buffer, and indexing can resume from the last committed chunk.
6. On successful completion the current app version is written to `BloomFilters/lines.ver`.

### Searching

1. `BloomFilterSearcher.Search(query)` splits the query into terms, then sorts them longest-first so the rarest term is checked first.
2. `BloomFilterCollectionReader` loads the entire index into memory and scans all filters in parallel across all CPU cores. For multi-term queries, scanning short-circuits on the first missing term (AND semantics).
3. Hit chunks are processed one at a time: lines are fetched from the DB, normalised, and scored by `SearchEngineMatcher`. Metadata for all matching lines in a chunk is fetched in a single batch query.
4. Perfect matches (all query terms present) are streamed to the caller immediately as each chunk is processed. Partial matches are accumulated and yielded after all perfect matches.
5. `proximityScore` is computed for every result (perfect and partial) and included in the response. The frontend can use it to re-sort results client-side if desired.

### Bloom Filter Design — Split Block Bloom Filter (SBBF)

The filter uses the same structure as DuckDB/Apache Parquet:

- The bit array is divided into 256-bit blocks (8 × uint32 words). Each probe touches exactly one block — one cache line — regardless of filter size. The old classic Bloom filter touched up to k scattered cache lines per probe.
- Hash function is xxHash64. The upper 32 bits select the block; the lower 32 bits drive 8 intra-block probes via the Parquet salt multipliers. The two halves are statistically independent, giving a true ~1% FP rate at ~10.5 bits/item. The old FNV-1a + rotl(h,16) double-hash derived both values from the same bits, inflating the real FP rate to ~2–3%.
- `hashFunctions = 8` and `bitCount % 256 == 0` are the format invariants. The load constructor throws loudly if either is violated — a stale or mismatched `.dat` file fails immediately rather than silently producing wrong results.

### Chunk Size

The chunk size is 100 lines (set in `SearchHandler` and as the default in `BloomFilterIndexer`). Each chunk becomes one Bloom filter. The tradeoff:

- Smaller chunks → more filters to scan, but fewer lines to hydrate per false-positive hit.
- Larger chunks → fewer filters to scan (faster Bloom pass), but more lines to hydrate per hit.

For the ~6M line corpus this gives ~60,000 filters. Empirically tested at 250 (24,000 filters) — slower, which confirms that hydration dominates over scan cost for this corpus. The bottleneck is the DB work after a Bloom hit, not the Bloom pass itself. Going smaller than 100 could help further if hydration remains the bottleneck, but has not been tested.

### Scoring and Ranking

`SearchEngineMatcher` scores each candidate line:

- Finds all positions of each query term in the normalised text.
- Runs a sliding-window minimum-span algorithm to find the tightest cluster of all terms.
- `proximityScore = 1 / (1 + span / 100)` — ranges from ~1.0 (terms adjacent) to near 0 (terms far apart). Results with score below 0.2 are discarded.
- Results are currently returned in chunk-processing order (corpus order). `proximityScore` is included in every result so the frontend can sort by it if needed.

### Version Detection

On startup, `SearchHandler.OnDbReady()` compares the installed app version (from `HKEY_CURRENT_USER\SOFTWARE\KleiKodesh` → `Version`) with the version stamped in `BloomFilters/lines.ver`. If they differ, a `bloomIndexVersionMismatch` push event is sent to the Vue app, which shows a confirm dialog asking the user whether to rebuild.

## File Structure

```
BloomSearchEngineLib/
├── BloomFilter.cs                  — SBBF filter (xxHash64, 256-bit blocks, Parquet salts)
├── BloomFilterCollectionReader.cs  — Loads index into memory; parallel scan with early exit
├── BloomFilterCollectionWriter.cs  — Writes index to disk; patches header on every flush
├── BloomFilterIndexer.cs           — Builds the index from the database; resume-on-crash
├── BloomFilterSearcher.cs          — Queries the index; streams results chunk by chunk
├── BloomFilterSearchModels.cs      — SearchResultItem / IndexProgressChangedEventArgs models
├── BloomIndexingCoordinator.cs     — Prevents concurrent indexing across processes (Mutex)
├── SearchEngineMatcher.cs          — Proximity scoring and snippet extraction
├── TermExtractor.cs                — Splits text into normalised Hebrew/Latin search terms
├── TextNormalizer.cs               — Strips HTML, decodes entities, removes diacritics
└── ZayitDbManager.cs               — SQLite queries; GetLineMetadataBatch for batch hydration
```

## On-Disk Artefacts

| File                     | Purpose                                          |
| ------------------------ | ------------------------------------------------ |
| `BloomFilters/lines.dat` | The Bloom filter index                           |
| `BloomFilters/lines.ver` | App version stamp written after successful build |

## Known Improvement Opportunities

- **Bigram indexing** — index adjacent word pairs alongside unigrams. A bigram "שלחן_ערוך" in the filter would distinguish "these two words appear adjacent" from "both words appear somewhere in the chunk", improving phrase query precision.
- **Global proximity sort** — currently results are in corpus order within each streamed batch. Collecting all perfect matches and sorting globally by `proximityScore` before returning would give better overall ranking, at the cost of waiting for all chunks to finish before the first result is yielded.
- **Batch `GetLineContent` in partial hydration** — `HydratePartialMatches` still fetches line content one row at a time. Low priority since partial hydration handles at most ~100 lines total and only runs when there are fewer than 100 perfect matches.
- **Term frequency statistics** — sorting query terms by actual corpus frequency (not just length) before the Bloom scan would give better early-exit behaviour for common short words.
