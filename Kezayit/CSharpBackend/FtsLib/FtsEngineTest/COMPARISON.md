# FtsEngine vs FtsLib Performance Comparison

## Baseline (FtsLib) — Full DB (5.4M lines)

From `FtsLibTest/Specs.md`:

| Metric | Value |
|--------|-------|
| Lines indexed | 5,444,192 |
| Unique terms | 1,409,819 |
| Index build time | 13.7 min (820 sec) |
| Peak RAM (index in memory) | 816 MB |
| RAM delta from baseline | +759 MB |
| Save to disk | 14.7 sec |
| postings.bin size | 367 MB |
| index.db size | 40 MB |
| Search (cold) | 68 ms |
| Search (warm) | 27-34 ms |
| Search results | 1,750 |

## FtsEngine Results

Run the test suite and update this table:

```bash
FtsEngineTest.exe full
```

| Metric | Value | vs FtsLib |
|--------|-------|----------|
| Lines indexed | ? | ? |
| Unique terms | ? | ? |
| Index build time | ? | ? |
| Peak RAM | ? | ? |
| RAM delta | ? | ? |
| postings.bin size | ? | ? |
| index.db size | ? | ? |
| Search (cold) | ? | ? |
| Search (warm) | ? | ? |
| Search results | ? | ? |

## Key Differences

### Architecture
- **FtsLib**: In-memory dictionary + skip list (loads entire index into RAM)
- **FtsEngine**: Sort-Based Index Construction (SBIC) with streaming merge (100 MB buffer)

### Index Format
- **FtsLib**: Binary index.db with skip list metadata
- **FtsEngine**: postings.bin (compressed posting lists) + index.db (term dictionary)

### Search Strategy
- **FtsLib**: PostingIterator with skip list acceleration
- **FtsEngine**: DiskIndexReader with lazy posting list loading

### Expected Tradeoffs
- **FtsEngine should use less peak RAM** (100 MB buffer vs 816 MB in-memory index)
- **FtsEngine build time may be longer** (disk I/O for run files + merge overhead)
- **FtsEngine search may be slower** (disk seeks for posting bytes vs in-memory access)
- **FtsEngine index files may be larger** (separate postings.bin + index.db)

## Test Increments

Use incremental tests to understand scaling behavior:

```bash
FtsEngineTest.exe quick    # 500k lines
FtsEngineTest.exe medium   # 1M lines
FtsEngineTest.exe large    # 3M lines
FtsEngineTest.exe full     # 5.4M lines
```

Plot build time vs lines to identify:
- Linear scaling (ideal)
- Superlinear scaling (merge overhead)
- Memory pressure (GC pauses)

## CSV Summary

After running tests, check `test_results/ftsengine_summary.csv`:

```
Timestamp,Test,Lines,Terms,BuildTime(ms),RAM(MB),AvgSearch(ms),Results
2026-05-03_14-30-45,500k,500000,123456,45000,250,15,1750
2026-05-03_14-32-30,1M,1000000,234567,95000,350,18,1750
2026-05-03_14-39-00,3M,3000000,678901,285000,550,22,1750
2026-05-03_14-56-00,full,5444192,1409819,820000,816,27,1750
```

## Notes

- Search query: "כי ביצחק" (Hebrew: "for Isaac")
- Expected results: 1,750 matching lines
- All times in milliseconds
- RAM in megabytes
- Search times are averages of 5 runs
