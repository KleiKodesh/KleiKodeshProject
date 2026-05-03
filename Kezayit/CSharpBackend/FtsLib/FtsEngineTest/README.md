# FtsEngine Test Suite

Comprehensive performance testing suite for the FtsEngine full-text search library with incremental test sizes, progress logging, RAM tracking, and results comparison.

## Test Sizes

- **quick** (500k lines) — ~1 minute
- **medium** (1M lines) — ~2 minutes  
- **large** (3M lines) — ~6 minutes
- **full** (5.4M lines) — ~17 minutes
- **all** — runs all tests sequentially

## Usage

```bash
# Run a specific test
FtsEngineTest.exe quick
FtsEngineTest.exe medium
FtsEngineTest.exe large
FtsEngineTest.exe full

# Run all tests
FtsEngineTest.exe all
```

## Output

### Console Output
- Real-time progress with elapsed time, throughput, and ETA
- Memory usage tracking (RAM before/after indexing)
- Search performance metrics (min/avg/max times)
- Sample results from the search query

### File Output
Results are written to `test_results/` directory:

1. **Detailed logs** — `ftsengine_test_<size>_<timestamp>.txt`
   - Full test output with all progress messages
   - Search results with book titles and references

2. **Summary CSV** — `ftsengine_summary.csv`
   - One row per test run
   - Columns: Timestamp, Test, Lines, Terms, BuildTime(ms), RAM(MB), AvgSearch(ms), Results
   - Useful for comparing performance across runs

## Metrics Tracked

### Indexing
- **Lines indexed** — total documents processed
- **Unique terms** — vocabulary size
- **Build time** — total indexing duration (ms)
- **Throughput** — lines/sec during indexing
- **RAM used** — peak memory delta (MB)

### Search
- **Query** — search terms used
- **Results** — number of matching lines
- **Search time** — min/avg/max across 5 runs (ms)
- **Cold vs warm** — first run vs subsequent runs

## Comparison with FtsLib

The FtsEngine test suite mirrors the structure of FtsLibTest for easy comparison:

| Metric | FtsLib | FtsEngine |
|--------|--------|-----------|
| Index build | 13.7 min | ? |
| Peak RAM | 816 MB | ? |
| Search (warm) | 27-34 ms | ? |
| Results | 1,750 | ? |

Run the tests and check `test_results/ftsengine_summary.csv` to populate the FtsEngine column.

## Database

Tests use the seforim database (5.4M lines of Hebrew/Aramaic text) located at:
```
%APPDATA%\io.github.kdroidfilter.seforimapp\databases\seforim.db
```

The database path is resolved via the Windows registry (ZayitApp settings).

## Implementation Notes

- **Sort-Based Index Construction (SBIC)** — FtsEngine uses a 100 MB buffer that flushes to disk when full, then k-way merges all runs
- **Streaming search** — DiskIndexReader loads only posting bytes for queried terms, no full index load
- **AND semantics** — returns line IDs containing ALL search terms
- **Progress events** — IndexBuilder reports progress via event callbacks
- **Memory tracking** — uses Win32 `GetProcessMemoryInfo` for accurate working set size

## Troubleshooting

### "Database not found"
- Ensure the seforim database exists at the expected path
- Check registry: `HKEY_CURRENT_USER\Software\ZayitApp\Database\Path`

### Out of memory
- Run smaller tests first (quick, medium)
- Close other applications
- Increase system virtual memory

### Slow performance
- Check disk I/O (SSD vs HDD)
- Verify database is not on network drive
- Monitor CPU usage during indexing

## Files

- `Program.cs` — entry point with test selection
- `FullDbTest.cs` — main test logic with progress logging and metrics
- `README.md` — this file
