# FtsEngine Test Suite — Implementation Summary

## What Was Created

A comprehensive performance testing suite for the FtsEngine full-text search library with incremental test sizes, real-time progress logging, memory tracking, and results comparison against FtsLib baseline.

## Files Added/Modified

### New Files
1. **FullDbTest.cs** — Main test logic
   - Incremental test sizes: 500k, 1M, 3M, 5.4M lines
   - Progress logging with elapsed time, throughput, ETA
   - Memory tracking (Win32 GetProcessMemoryInfo)
   - Search performance metrics (5 runs, min/avg/max)
   - Results written to text files and CSV summary

2. **README.md** — User guide
   - Usage instructions
   - Test sizes and expected durations
   - Output format explanation
   - Metrics tracked
   - Troubleshooting guide

3. **COMPARISON.md** — Performance baseline
   - FtsLib baseline metrics from Specs.md
   - Template for FtsEngine results
   - Expected tradeoffs between architectures
   - CSV format for tracking runs

4. **TEST_SUITE_SUMMARY.md** — This file

### Modified Files
1. **Program.cs** — Entry point
   - Test selection menu (quick, medium, large, full, all)
   - Sequential test runner for "all" option

2. **FtsEngineTest.csproj** — Project configuration
   - Added FullDbTest.cs to compilation
   - Added FtsLib project reference (for Tokenizer)
   - Added Microsoft.VisualBasic reference (for registry access)

## Key Features

### Progress Logging
- Real-time console output with color-coded messages
- Elapsed time, throughput (lines/sec), ETA
- Memory usage at each milestone
- Phase transitions (Indexing → Flushing → Merging → Complete)

### Performance Metrics
- **Indexing**: lines, terms, build time, throughput, RAM delta
- **Search**: query terms, result count, min/avg/max times (5 runs)
- **Comparison**: vs FtsLib baseline

### Output Files
- **Detailed logs** — `test_results/ftsengine_test_<size>_<timestamp>.txt`
  - Full console output with all progress messages
  - Search results with book titles and references
  
- **Summary CSV** — `test_results/ftsengine_summary.csv`
  - One row per test run
  - Columns: Timestamp, Test, Lines, Terms, BuildTime(ms), RAM(MB), AvgSearch(ms), Results
  - Useful for comparing performance across runs and architectures

### Memory Tracking
- Uses Win32 `GetProcessMemoryInfo` for accurate working set size
- Tracks RAM before/after indexing
- Reports peak memory delta
- Monitors memory during progress milestones

### Search Verification
- Runs 5 search iterations to measure cold vs warm performance
- Fetches results from database by matched line IDs
- Displays first 10 results with book titles and references
- Verifies search results match expected count (1,750 for "כי ביצחק")

## Architecture Differences

### FtsEngine (SBIC — Sort-Based Index Construction)
- **Build**: Stream → 100 MB buffer → sort → flush to run file → k-way merge
- **Index**: postings.bin (compressed posting lists) + index.db (term dictionary)
- **Search**: DiskIndexReader loads only posting bytes for queried terms
- **Expected**: Lower peak RAM, potentially slower search (disk I/O)

### FtsLib (In-Memory Dictionary)
- **Build**: Stream → dictionary with skip list → save to disk
- **Index**: Single index.db with skip list metadata
- **Search**: PostingIterator with skip list acceleration
- **Baseline**: 816 MB peak RAM, 27-34 ms warm search

## Test Execution

### Quick Test (500k lines)
```bash
FtsEngineTest.exe quick
```
Expected: ~1 minute

### Medium Test (1M lines)
```bash
FtsEngineTest.exe medium
```
Expected: ~2 minutes

### Large Test (3M lines)
```bash
FtsEngineTest.exe large
```
Expected: ~6 minutes

### Full Test (5.4M lines)
```bash
FtsEngineTest.exe full
```
Expected: ~17 minutes

### All Tests
```bash
FtsEngineTest.exe all
```
Runs quick → medium → large → full sequentially with prompts between tests.

## Results Analysis

After running tests, analyze results:

1. **Check console output** for real-time progress
2. **Review detailed log** in `test_results/ftsengine_test_<size>_<timestamp>.txt`
3. **Compare CSV summary** in `test_results/ftsengine_summary.csv`
4. **Update COMPARISON.md** with FtsEngine results

### CSV Format
```
Timestamp,Test,Lines,Terms,BuildTime(ms),RAM(MB),AvgSearch(ms),Results
2026-05-03_14-30-45,500k,500000,123456,45000,250,15,1750
2026-05-03_14-32-30,1M,1000000,234567,95000,350,18,1750
2026-05-03_14-39-00,3M,3000000,678901,285000,550,22,1750
2026-05-03_14-56-00,full,5444192,1409819,820000,816,27,1750
```

## Verification Checklist

- [x] Code compiles without errors
- [x] Project references configured (FtsEngine, FtsLib)
- [x] Progress logging implemented
- [x] Memory tracking implemented
- [x] Search verification implemented
- [x] Results written to files
- [x] CSV summary created
- [x] Documentation complete

## Next Steps

1. Run the test suite: `FtsEngineTest.exe full`
2. Monitor progress in console
3. Check results in `test_results/` directory
4. Compare metrics with FtsLib baseline
5. Update COMPARISON.md with results
6. Analyze performance tradeoffs

## Notes

- Database path resolved from Windows registry (ZayitApp settings)
- Search query: "כי ביצחק" (Hebrew: "for Isaac")
- Expected results: 1,750 matching lines
- All times in milliseconds, RAM in megabytes
- Search times are averages of 5 runs
- Results directory created automatically if it doesn't exist
