# FtsEngine Test Suite — Quick Start

## Build

```bash
dotnet build FtsEngineTest/FtsEngineTest.csproj
```

## Run Tests

### Option 1: Run a Single Test

```bash
# 500k lines (~1 min)
FtsEngineTest.exe quick

# 1M lines (~2 min)
FtsEngineTest.exe medium

# 3M lines (~6 min)
FtsEngineTest.exe large

# Full 5.4M lines (~17 min)
FtsEngineTest.exe full
```

### Option 2: Run All Tests Sequentially

```bash
FtsEngineTest.exe all
```

This runs: quick → medium → large → full with prompts between each test.

## What to Expect

### Console Output
```
[00:00.00]   » Started  : 2026-05-03 14:30:45
[00:00.05]   » PID      : 12345
[00:00.05]   » RAM      : 150 MB

  ── Database ──
[00:00.10]   » Path : C:\Users\...\seforim.db
[00:00.10]   » Size : 2500.5 MB  (modified 2026-05-03 10:00)
[00:00.15]   ✓ Connected in 45 ms

  ── Row Count ──
[00:00.20]   ✓ 5,444,192 rows  (50 ms)

  ── Index Build ──
[00:00.25]   » Streaming all rows...
[00:05.30]   · 500,000 / 5,444,192  (9.2%)  ETA 00:47  {rate} lines/s  RAM 350 MB
[00:10.45]   · 1,000,000 / 5,444,192  (18.4%)  ETA 00:42  {rate} lines/s  RAM 450 MB
...
[13:45.00]   ✓ Lines      : 5,444,192
[13:45.05]   ✓ Terms      : 1,409,819
[13:45.10]   ✓ Time       : 825,000 ms
[13:45.15]   · Throughput : 6,599 lines/sec
[13:45.20]   ✓ RAM after  : 816 MB  (Δ +759 MB)

  ── Search ──
[13:45.30]   » Query : "כי ביצחק"
[13:45.35]   » Terms : [כי, ביצחק]
[13:45.40]   » Running 5 iterations...
[13:45.45]   · Run 1: 68 ms  →  1,750 results
[13:45.50]   · Run 2: 28 ms  →  1,750 results
[13:45.55]   · Run 3: 27 ms  →  1,750 results
[13:46.00]   · Run 4: 29 ms  →  1,750 results
[13:46.05]   · Run 5: 30 ms  →  1,750 results
[13:46.10]   ✓ Results : 1,750  |  min=27 ms  avg=36.4 ms  max=68 ms

  ── Results ──
[13:46.15]   ✓ Fetched 1,750 rows in 125 ms

  [1] Bereishit  Genesis 21:12
      And God said unto Abraham: 'Let it not be grievous in thy sight...

  [2] Bereishit  Genesis 26:4
      And I will make thy seed to multiply as the stars of the heaven...

  ... and 1,748 more results

  ── Summary ──
[13:46.30]   » DB size        : 2500.5 MB
[13:46.35]   » Lines indexed  : 5,444,192
[13:46.40]   » Unique terms   : 1,409,819
[13:46.45]   » Index time     : 825,000 ms
[13:46.50]   » Search (avg)   : 36.4 ms
[13:46.55]   » Results found  : 1,750
[13:47.00]   » RAM used       : 759 MB
[13:47.05]   ✓ Total time     : 13:47.05
[13:47.10]   ✓ Results written to: test_results\ftsengine_test_full_2026-05-03_14-30-45.txt
[13:47.15]   ✓ Summary appended to: test_results\ftsengine_summary.csv
```

### Output Files

After the test completes, check:

1. **Detailed log** — `test_results/ftsengine_test_full_2026-05-03_14-30-45.txt`
   - Full console output
   - Search results with references

2. **Summary CSV** — `test_results/ftsengine_summary.csv`
   ```
   Timestamp,Test,Lines,Terms,BuildTime(ms),RAM(MB),AvgSearch(ms),Results
   2026-05-03_14-30-45,full,5444192,1409819,825000,759,36.4,1750
   ```

## Compare with FtsLib Baseline

FtsLib results (from `FtsLibTest/Specs.md`):

| Metric | FtsLib | FtsEngine |
|--------|--------|-----------|
| Lines indexed | 5,444,192 | 5,444,192 |
| Unique terms | 1,409,819 | ? |
| Build time | 13.7 min (820 sec) | ? |
| Peak RAM | 816 MB | ? |
| Search (warm) | 27-34 ms | ? |
| Results | 1,750 | ? |

Update the FtsEngine column with your test results.

## Troubleshooting

### "Database not found"
The test looks for the seforim database at:
```
%APPDATA%\io.github.kdroidfilter.seforimapp\databases\seforim.db
```

If not found, check:
1. Is the Zayit app installed?
2. Has the database been downloaded?
3. Check registry: `HKEY_CURRENT_USER\Software\ZayitApp\Database\Path`

### Out of Memory
- Run smaller tests first (quick, medium)
- Close other applications
- Check available disk space (temp files for run files)

### Slow Performance
- Check disk I/O (SSD vs HDD)
- Verify database is not on network drive
- Monitor CPU usage (should be high during indexing)

### Results Don't Match
- Verify search query: "כי ביצחק"
- Check database hasn't changed
- Ensure no other processes are modifying the database

## Files

- `Program.cs` — Entry point
- `FullDbTest.cs` — Test logic
- `README.md` — Full documentation
- `COMPARISON.md` — Performance baseline
- `QUICKSTART.md` — This file
- `TEST_SUITE_SUMMARY.md` — Implementation details

## Next Steps

1. Build: `dotnet build FtsEngineTest/FtsEngineTest.csproj`
2. Run: `FtsEngineTest.exe full`
3. Wait for completion (~17 minutes)
4. Check results in `test_results/` directory
5. Compare with FtsLib baseline
6. Update `COMPARISON.md` with results
