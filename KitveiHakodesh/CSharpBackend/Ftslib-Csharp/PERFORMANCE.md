# FtsLib Performance Baseline

Machine: Windows, Release build, .NET 4.8, x64
All queries pass — zero false positives, zero missing required IDs.

---

## 500k tier (500,000 lines)

### Build
| Metric | Value |
|---|---|
| Lines indexed | 500,000 |
| Total time | 48.28s |
| Average rate | 10,357 lines/s |
| Index size on disk | 88.1 MB |

### Search — 2026-07-12 (RoaringBitmapIterator hybrid SkipTo)
Commit note: RoaringBitmapIterator hybrid SkipTo — O(distance)→O(log blocks) for cross-block jumps.
B:Index column is the posting-list intersection time (no DB); reflects the SkipTo improvement directly.

| Query | Type | Results | B:Index |
|---|---|---|---|
| כי ביצחק | literal | 481 | 22 ms |
| תורה מצוה | literal | 1,979 | 13 ms |
| אברהם יצחק יעקב | literal | 1,988 | 11 ms |
| משה* תורה | wildcard | 3,912 | 373 ms |
| *ישראל | wildcard | 64,118 | 264 ms |
| בני* | wildcard | 40,756 | 315 ms |
| כי יצחק~ | fuzzy | 6,426 | 221 ms |
| יסראל~2 | fuzzy | 63,944 | 246 ms |
| Warm-up (תורה) | literal | 22,473 | 1,617 ms (full pipeline) |

### Search — 2026-07-11 (skip-list binary search fix, previous baseline)
Commit note: skip-list binary search fix applied (PostingIterator.SkipTo O(k)→O(log k)).

| Query | Type | Results | Search time |
|---|---|---|---|
| כי ביצחק | literal | 481 | 77 ms |
| שויתי לנגדי תמיד | literal | 257 | 38 ms |
| תורה מצוה | literal | 1,979 | 228 ms |
| אברהם יצחק יעקב | literal | 1,988 | 269 ms |
| אבל בן אין לה | literal | 1,137 | 232 ms |
| וידבר משה כן אל בני | literal | 296 | 105 ms |
| משה* תורה | wildcard | 3,912 | 646 ms |
| *ישראל | wildcard | 64,118 | 3,554 ms |
| *אבר* | wildcard | 20,008 | 1,480 ms |
| בני* | wildcard | 40,756 | 3,096 ms |
| כי יצחק~ | fuzzy | 6,426 | 997 ms |
| תארה~ מצוה | fuzzy | 286 | 471 ms |
| אנב~ | fuzzy | 38,135 | 2,847 ms |
| יסראל~2 | fuzzy | 63,944 | 4,129 ms |
| כי ביצחק~ | fuzzy | 6,393 | 1,021 ms |
| Warm-up (תורה) | literal | 22,473 | 1,254 ms |

### Wildcard intersection stress queries (2026-07-11 baseline)
These queries combine a high-frequency wildcard (large OR expansion) with a second term,
stressing the AND intersection across many posting lists.

| Query | Results | Search time |
|---|---|---|
| כי *יצח* | 6,454 | 909 ms |
| *כי* ביצחק | 529 | 8,455 ms |
| *כי* *יצח* | 8,747 | 4,906 ms |

---

## Full tier (6,543,318 lines)

### Build
| Metric | Value |
|---|---|
| Lines indexed | 6,543,318 |
| Total time | 15m 26s |
| Average rate | 7,064 lines/s |
| Index size on disk | 730.4 MB |

### Search — 2026-07-11 (skip-list binary search fix, previous baseline)

| Query | Type | Results | Search time |
|---|---|---|---|
| כי ביצחק | literal | 2,179 | 878 ms |
| שויתי לנגדי תמיד | literal | 917 | 384 ms |
| תורה מצוה | literal | 21,147 | 6,565 ms |
| אברהם יצחק יעקב | literal | 9,109 | 3,249 ms |
| אבל בן אין לה | literal | 22,507 | 10,852 ms |
| וידבר משה כן אל בני | literal | 1,084 | 676 ms |
| משה* תורה | wildcard | 25,510 | 9,343 ms |
| *ישראל | wildcard | 504,333 | 96,740 ms |
| *אבר* | wildcard | 179,624 | 43,562 ms |
| בני* | wildcard | 385,820 | 72,556 ms |
| כי יצחק~ | fuzzy | 45,700 | 23,351 ms |
| תארה~ מצוה | fuzzy | 954 | 3,401 ms |
| אנב~ | fuzzy | 470,705 | 117,313 ms |
| יסראל~2 | fuzzy | 505,748 | 46,163 ms |
| כי ביצחק~ | fuzzy | 45,118 | 13,544 ms |
| Warm-up (תורה) | literal | 212,506 | 35,191 ms |

### Wildcard intersection stress queries (2026-07-11 baseline)

| Query | Results | Search time |
|---|---|---|
| כי *יצח* | 45,756 | 8,000 ms |
| *כי* ביצחק | 2,599 | 41,078 ms |
| *כי* *יצח* | 77,720 | 45,066 ms |
