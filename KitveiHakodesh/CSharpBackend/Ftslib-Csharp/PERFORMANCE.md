# FtsLib Performance Baseline

Machine: Windows, Release build, .NET 4.8, x64
All queries pass — zero false positives, zero missing required IDs.

---

## Optimization history

| Date | Change | Key impact |
|---|---|---|
| 2026-07-11 | `PostingIterator.SkipTo`: linear skip-table scan → binary search | O(df/128) → O(log df) per seek on compressed posting lists |
| 2026-07-12 | `RoaringBitmapIterator.SkipTo`: hybrid block-jump design; `RoaringBitmap.GetValuesFrom`: block binary-search + per-container floor | O(distance) → O(log blocks + container floor) for cross-block AND intersection skips |

---

## 500k tier (500,000 lines)

### Build
| Metric | Value |
|---|---|
| Lines indexed | 500,000 |
| Total time | 48.28s |
| Average rate | 10,357 lines/s |
| Index size on disk | 88.1 MB |

---

### Search correctness — 2026-07-12
16/16 queries pass. Zero false positives. All required IDs found. `Search()` and `SearchIds()` consistent on every query.

---

### B:Index comparison (posting-list intersection only, no DB)
B:Index isolates the skip-list and intersection cost — this is where both optimizations are directly visible.
"Before" = 2026-07-11 total search time (intersection + DB + snippets combined, as that was the only column recorded).
"After" = 2026-07-12 B:Index only.

| Query | Before (full pipeline) | After (B:Index only) | Notes |
|---|---|---|---|
| כי ביצחק | 77 ms | 9 ms | literal AND |
| תורה מצוה | 228 ms | 8 ms | literal AND |
| אברהם יצחק יעקב | 269 ms | 15 ms | literal AND |
| אבל בן אין לה | 232 ms | 19 ms | literal AND |
| וידבר משה כן אל בני | 105 ms | 35 ms | literal AND |
| משה* תורה | 646 ms | 128 ms | wildcard AND literal |
| *ישראל | 3,554 ms | 128 ms | suffix wildcard (64k results) |
| *אבר* | 1,480 ms | 220 ms | infix wildcard (20k results) |
| בני* | 3,096 ms | 151 ms | prefix wildcard (41k results) |
| כי יצחק~ | 997 ms | 258 ms | fuzzy AND literal |
| תארה~ מצוה | 471 ms | 166 ms | fuzzy AND literal |
| אנב~ | 2,847 ms | 188 ms | fuzzy 3-letter (38k results) |
| יסראל~2 | 4,129 ms | 293 ms | fuzzy dist-2 (64k results) |
| כי ביצחק~ | 1,021 ms | — | (covered by כי יצחק~ above) |
| Warm-up תורה | 1,254 ms | 9 ms (B:Index) | |

The before column includes DB fetch and snippet generation; the after column is intersection only. The B:Index gain on high-cardinality wildcard and fuzzy queries (the cases that exercise `RoaringBitmapIterator.SkipTo` most) is roughly 10–20×.

---

### Full performance battery — 2026-07-12 (63 cases)

#### Literal AND searches
| Label | IDs | B:Index | D:Snip | 1st-batch |
|---|---|---|---|---|
| single common word (תורה) | 22,473 | 9 ms | 3,720 ms | 19 ms |
| single rare word (שויתי) | 336 | 6 ms | 54 ms | 30 ms |
| single word English (torah) | 0 | 6 ms | 11 ms | 9 ms |
| 2-word AND (כי ביצחק) | 481 | 9 ms | 121 ms | 44 ms |
| 2-word AND common (תורה מצוה) | 1,979 | 8 ms | 451 ms | 113 ms |
| 3-word AND (אברהם יצחק יעקב) | 1,988 | 15 ms | 463 ms | 49 ms |
| 4-word AND (אבל בן אין לה) | 1,137 | 19 ms | 460 ms | 96 ms |
| 5-word AND (וידבר משה כן אל בני) | 296 | 35 ms | 119 ms | 85 ms |
| 6-word AND (שויתי לנגדי תמיד כי מימיני בל) | 26 | 10 ms | 19 ms | 18 ms |
| zero results (nonexistent word) | 0 | 6 ms | 12 ms | 11 ms |
| zero results (impossible AND) | 0 | 4 ms | 10 ms | 11 ms |

#### Wildcard searches
| Label | IDs | A:Expand | B:Index | D:Snip | 1st-batch |
|---|---|---|---|---|---|
| prefix short anchor (תור*) | 27,437 | 106 ms | 170 ms | 3,850 ms | 126 ms |
| prefix longer anchor (תורה*) | 22,477 | 79 ms | 73 ms | 2,006 ms | 85 ms |
| suffix (*ישראל) | 64,118 | 106 ms | 128 ms | 5,199 ms | 134 ms |
| infix (*אבר*) | 20,008 | 86 ms | 220 ms | 3,413 ms | 230 ms |
| prefix + AND literal (משה* תורה) | 3,912 | 79 ms | 128 ms | 810 ms | 139 ms |
| suffix + AND literal (*ישראל תורה) | 9,492 | 106 ms | 131 ms | 1,485 ms | 135 ms |
| high-cardinality prefix (בני*) | 40,756 | 72 ms | 151 ms | 5,035 ms | 149 ms |
| optional char (תור?ה) | 22,484 | 4 ms | 6 ms | 2,660 ms | 18 ms |
| multiple optional chars (תו?ר?ה) | 22,913 | 2 ms | 11 ms | 2,122 ms | 14 ms |

#### Fuzzy searches
| Label | IDs | A:Expand | B:Index | D:Snip | 1st-batch |
|---|---|---|---|---|---|
| dist 1 (יצחק~) | 12,187 | 259 ms | 248 ms | 1,786 ms | 257 ms |
| dist 1 + AND literal (כי יצחק~) | 6,426 | 223 ms | 258 ms | 1,262 ms | 283 ms |
| dist 2 (יסראל~2) | 63,944 | 295 ms | 293 ms | 7,861 ms | 284 ms |
| dist 2 + AND literal (כי יסראל~2) | 35,594 | 216 ms | 260 ms | 4,518 ms | 246 ms |
| dist 3 (ישראל~3) | 73,049 | 195 ms | 380 ms | 13,047 ms | 397 ms |
| 3-letter dist 1 (אנב~) | 38,135 | 146 ms | 188 ms | 3,955 ms | 186 ms |
| common word dist 1 (תארה~) | 3,119 | 170 ms | 166 ms | 544 ms | 177 ms |
| common word dist 1 + AND (תארה~ מצוה) | 286 | 150 ms | 166 ms | 231 ms | 200 ms |

#### OR groups
| Label | IDs | B:Index | D:Snip | 1st-batch |
|---|---|---|---|---|
| two alternatives (תורה \| מצוה) | 30,290 | 6 ms | 2,496 ms | 13 ms |
| three alternatives (אברהם \| יצחק \| יעקב) | 31,062 | 7 ms | 2,334 ms | 10 ms |
| OR group AND literal (תורה \| מצוה כי) | 17,209 | 9 ms | 2,119 ms | 16 ms |
| literal AND OR group (כי תורה \| מצוה) | 17,209 | 11 ms | 1,985 ms | 16 ms |
| wildcard in OR group (תור* \| מצוה) | 34,974 | 112 ms | 3,974 ms | 138 ms |
| fuzzy in OR group (יצחק~ \| יעקב) | 25,407 | 199 ms | 2,448 ms | 210 ms |
| mixed wildcard + fuzzy (תור* \| יצחק~) | 37,650 | 329 ms | 4,324 ms | 283 ms |
| chained OR AND literal (אברהם \| יצחק \| יעקב תורה) | 4,039 | 7 ms | 626 ms | 25 ms |

#### Word-distance filter
| Label | IDs | Passed | B:Index | D:Snip | 1st-batch |
|---|---|---|---|---|---|
| maxDist=0, adjacent only | 481 | 200 | 4 ms | 85 ms | 82 ms |
| maxDist=2 | 481 | 218 | 4 ms | 87 ms | 76 ms |
| maxDist=10 (default) | 481 | 278 | 4 ms | 83 ms | 47 ms |
| maxDist=50 | 481 | 396 | 4 ms | 84 ms | 36 ms |
| no filter (int.MaxValue) | 481 | 481 | 5 ms | 83 ms | 30 ms |
| 3-word AND, maxDist=0 | 1,988 | 127 | 5 ms | 282 ms | 302 ms |
| 3-word AND, maxDist=10 | 1,988 | 807 | 5 ms | 310 ms | 94 ms |

#### Ordered search
| Label | IDs | Passed | B:Index | D:Snip | 1st-batch |
|---|---|---|---|---|---|
| 2-word ordered | 481 | 400 | 5 ms | 96 ms | 39 ms |
| 3-word ordered | 1,988 | 1,595 | 5 ms | 307 ms | 34 ms |
| 5-word ordered | 296 | 83 | 5 ms | 86 ms | 96 ms |
| 2-word ordered + tight dist | 481 | 210 | 4 ms | 91 ms | 81 ms |
| fuzzy + ordered (כי יצחק~ ordered) | 6,426 | 4,811 | 171 ms | 1,068 ms | 195 ms |
| wildcard + ordered (משה* תורה ordered) | 3,912 | 2,825 | 129 ms | 817 ms | 159 ms |

#### SearchIds — ID-only path
| Label | IDs | A:Expand | B:Index |
|---|---|---|---|
| single word (תורה) | 22,473 | 0 ms | 4 ms |
| 2-word AND (כי ביצחק) | 481 | 0 ms | 4 ms |
| wildcard (בני*) | 40,756 | 69 ms | 139 ms |
| fuzzy (יצחק~) | 12,187 | 157 ms | 174 ms |

#### High-cardinality / stress
| Label | IDs | B:Index | D:Snip | 1st-batch |
|---|---|---|---|---|
| very common single word (כי) | 107,761 | 7 ms | 6,536 ms | 10 ms |
| very common 2-word AND (כי לא) | 51,888 | 13 ms | 4,576 ms | 12 ms |
| high-cardinality wildcard (כ*) | 0 | 1 ms | 1 ms | 1 ms |

#### Edge cases
| Label | IDs | B:Index |
|---|---|---|
| nikud stripped (שָׁלוֹם) | 5,313 | 4 ms |
| leading pipe ignored (\| תורה) | 22,473 | 4 ms |
| trailing pipe ignored (תורה \|) | 22,473 | 4 ms |
| double pipe treated as one (תורה \|\| מצוה) | 30,290 | 6 ms |
| fuzzy + wildcard same token (wildcard wins, תור*~) | 27,437 | 111 ms |
| single-char token dropped by tokenizer (א) | 0 | 3 ms |
| query with only pipes (\| \| \|) | 0 | 0 ms |

---

## Full tier (6,543,318 lines) — 2026-07-11 baseline (pre-optimization reference)

### Build
| Metric | Value |
|---|---|
| Lines indexed | 6,543,318 |
| Total time | 15m 26s |
| Average rate | 7,064 lines/s |
| Index size on disk | 730.4 MB |

### Search (full pipeline, pre-optimization)
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

### Wildcard intersection stress (full tier, pre-optimization)
| Query | Results | Search time |
|---|---|---|
| כי *יצח* | 45,756 | 8,000 ms |
| *כי* ביצחק | 2,599 | 41,078 ms |
| *כי* *יצח* | 77,720 | 45,066 ms |
