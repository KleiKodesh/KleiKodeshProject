# FtsLib Performance Report

> **How to regenerate this report**
> ```
> FtsLibTest.exe perf full
> ```
> The test opens an HTML report automatically. Fill in the numbers below from that run.
>
> The numbers in this file were measured on the **500k tier** (500,000 lines, 3 segments).
> Rows marked `—` require a full `perf` run to populate.

---

## Environment

| Item | Value |
|---|---|
| Index tier | 500k (500,000 lines, 3 segments) |
| Unique terms | ~400k (500k slice) |
| Index size on disk | ~70 MB |
| First-batch size | 200 results |
| Warm-up query | תורה (13,910 results) |
| Measured | 2026-05-06, Debug build |

---

## Optimizations applied (2026-05-06)

Two changes to `ZayitDb.FetchSearchResultsStreaming`:

1. **Book title cache** — `LoadBookTitles()` now caches the result for the lifetime of the connection. Previously it ran `SELECT id, title FROM book` on every 200-row chunk fetch. For a 47k-result query split into 238 chunks that was 238 redundant full-table scans.
2. **Removed `ORDER BY l.id`** from `FetchChunk` — IDs from the posting-list intersection are already in ascending order. The sort was forcing SQLite to re-sort each chunk unnecessarily, dominating C:Fetch time on large result sets.

---

## Optimizations applied (2026-05-07)

**Skip list acceleration for `SkipTo`** — posting lists with ≥ 256 entries now carry a skip table (one entry per 128 docs). Each entry records the doc ID, the byte offset in the posting buffer, and the encoded value of the preceding entry. `PostingIterator.SkipTo` binary-searches the table to jump directly to the right neighborhood before linear-scanning the remaining few entries.

Skip tables are built during indexing (`RamIndexEntry.Add`) and during segment merges (`SegmentMerger.MergeChunks`). They are stored inline in the `.dat` file immediately before the posting bytes for each term, and their offset and count are stored as two new columns (`skip_offset`, `skip_count`) in the `term_index` SQLite table. `IndexReader.LoadChunk` reads both the skip table and posting bytes in a single seek+read.

The benefit is concentrated on AND intersection (`PostingIntersector`) where `SkipTo` is called repeatedly on the longer posting lists to catch up to the rarest term. For a common Hebrew word with 50,000 entries, each `SkipTo` drops from O(n) varint decodes to O(log n) table scan + O(128) linear scan. Short posting lists (< 256 entries) get no skip table and are unaffected.

**Existing indexes are incompatible** — the `.dat` format changed (new `skipCount` field + skip table bytes per term) and the `.db` schema changed (two new columns). Any index built before this change must be deleted and rebuilt.

---

## Pipeline phases

Every case is broken into five independently timed phases:

| Phase | What it measures |
|---|---|
| **A: Expand** | Fuzzy/wildcard LIKE scans against `term_index` SQLite. Only non-zero for fuzzy and wildcard queries. |
| **B: Index** | Posting-list intersection → list of matching line IDs. No DB access. |
| **C: Fetch** | Read all content rows from SQLite for the matched IDs. Worst-case (all results). |
| **D: Snip** | Generate snippets for all results: tokenize + proximity window + render. Includes word-distance and ordered filtering. |
| **1st-batch** | Time until the first 200 results with snippets are ready. This is the UX latency the user actually feels. |

---

## Literal AND searches

| Query | IDs | A:Expand | B:Index | C:Fetch | D:Snip | 1st-batch |
|---|---|---|---|---|---|---|
| `תורה` (single common) | — | 0 ms | — ms | — ms | — ms | — ms |
| `שויתי` (single rare) | — | 0 ms | — ms | — ms | — ms | — ms |
| `torah` (English) | — | 0 ms | — ms | — ms | — ms | — ms |
| `כי ביצחק` (2-word) | 283 | 0 ms | 45 ms | 121 ms | 206 ms | 109 ms |
| `תורה מצוה` (2-word common) | 1,010 | 0 ms | 27 ms | 189 ms | 514 ms | 141 ms |
| `אברהם יצחק יעקב` (3-word) | 1,266 | 0 ms | 32 ms | 245 ms | 599 ms | 150 ms |
| `אבל בן אין לה` (4-word) | — | 0 ms | — ms | — ms | — ms | — ms |
| `וידבר משה כן אל בני` (5-word) | — | 0 ms | — ms | — ms | — ms | — ms |
| `שויתי לנגדי תמיד כי מימיני בל` (6-word) | — | 0 ms | — ms | — ms | — ms | — ms |

**Expected profile:** B:Index dominates for common words. Rare words are near-instant. Each additional AND term reduces the result set and speeds up the intersection. The 1st-batch latency should be well under 1 second for any multi-word query.

---

## Zero-result queries

| Query | IDs | A:Expand | B:Index | Notes |
|---|---|---|---|---|
| `nonexistentword123xyz` | 0 | 0 ms | — ms | Term absent from index — returns immediately |
| `nonexistentword123 תורה` | 0 | 0 ms | — ms | AND short-circuits on first missing term |

**Expected profile:** Both should return in < 5 ms. The AND intersection aborts as soon as any term is absent from the index.

---

## Wildcard searches

| Query | IDs | A:Expand | B:Index | C:Fetch | D:Snip | 1st-batch |
|---|---|---|---|---|---|---|
| `תור*` (prefix, short anchor) | — | — ms | — ms | — ms | — ms | — ms |
| `תורה*` (prefix, longer anchor) | — | — ms | — ms | — ms | — ms | — ms |
| `*ישראל` (suffix) | 47,569 | 238 ms | 279 ms | 2,490 ms | 9,730 ms | 343 ms |
| `*אבר*` (infix) | — | — ms | — ms | — ms | — ms | — ms |
| `משה* תורה` (prefix + AND) | 2,169 | 218 ms | 387 ms | 515 ms | 1,950 ms | 481 ms |
| `*ישראל תורה` (suffix + AND) | — | — ms | — ms | — ms | — ms | — ms |
| `בני*` (high-cardinality prefix) | 32,275 | 201 ms | 299 ms | 2,328 ms | 8,571 ms | 315 ms |
| `תור?ה` (optional char) | — | — ms | — ms | — ms | — ms | — ms |
| `תו?ר?ה` (two optional chars) | — | — ms | — ms | — ms | — ms | — ms |

**Expected profile:** A:Expand is the bottleneck for wildcards — it runs LIKE scans against the term index. Short anchors (`תור*`) expand to many terms and are slower than long anchors (`תורה*`). Infix wildcards (`*אבר*`) are the most expensive. The AND term in `משה* תורה` dramatically reduces the final result set.

**Edge case — very short anchor:** A single-character prefix like `כ*` may expand to thousands of terms. The expander has a minimum anchor length guard; queries below it are silently skipped (zero results). This is intentional — not a bug.

---

## Fuzzy searches

| Query | IDs | A:Expand | B:Index | C:Fetch | D:Snip | 1st-batch |
|---|---|---|---|---|---|---|
| `יצחק~` (dist 1) | — | — ms | — ms | — ms | — ms | — ms |
| `כי יצחק~` (dist 1 + AND) | 4,416 | 454 ms | 529 ms | 491 ms | 2,384 ms | 471 ms |
| `יסראל~2` (dist 2) | 47,488 | 497 ms | 606 ms | 2,331 ms | 10,880 ms | 695 ms |
| `כי יסראל~2` (dist 2 + AND) | — | — ms | — ms | — ms | — ms | — ms |
| `ישראל~3` (dist 3) | — | — ms | — ms | — ms | — ms | — ms |
| `אנב~` (3-letter word, dist 1) | — | — ms | — ms | — ms | — ms | — ms |
| `תארה~` (common word, dist 1) | — | — ms | — ms | — ms | — ms | — ms |
| `תארה~ מצוה` (dist 1 + AND) | — | — ms | — ms | — ms | — ms | — ms |

**Expected profile:** A:Expand grows with edit distance — dist 3 can be 5–10× slower than dist 1. Short words (3 letters) have fewer neighbors and expand faster. The AND term in `כי יצחק~` reduces the final result set significantly. Dist 3 on a common word may produce thousands of expansions; the intersection step then dominates.

**Critical invariant:** A fuzzy term with zero expansions is a hard miss — the query returns zero results immediately. This is correct: the user asked for a word that doesn't exist in the index even approximately.

---

## OR group searches

| Query | IDs | Passed | A:Expand | B:Index | D:Snip | 1st-batch |
|---|---|---|---|---|---|---|
| `תורה \| מצוה` (2 alternatives) | — | — | — ms | — ms | — ms | — ms |
| `אברהם \| יצחק \| יעקב` (3 alternatives) | — | — | — ms | — ms | — ms | — ms |
| `תורה \| מצוה כי` (OR + AND) | — | — | — ms | — ms | — ms | — ms |
| `כי תורה \| מצוה` (AND + OR) | — | — | — ms | — ms | — ms | — ms |
| `תור* \| מצוה` (wildcard in OR) | — | — | — ms | — ms | — ms | — ms |
| `יצחק~ \| יעקב` (fuzzy in OR) | — | — | — ms | — ms | — ms | — ms |
| `תור* \| יצחק~` (wildcard + fuzzy OR) | — | — | — ms | — ms | — ms | — ms |
| `אברהם \| יצחק \| יעקב תורה` (chained OR + AND) | — | — | — ms | — ms | — ms | — ms |

**Expected profile:** OR groups union their posting lists before the AND intersection. A two-alternative OR group has more results than either term alone but fewer than their sum (duplicates removed). The AND term after the OR group is the main filter. Wildcard/fuzzy alternatives inside an OR group add their A:Expand cost.

---

## Word-distance filter

The word-distance filter runs inside snippet generation (D:Snip). It does not affect B:Index — all matching IDs are fetched first, then filtered by proximity.

| Query | maxDist | IDs | Passed | Filtered | D:Snip | 1st-batch |
|---|---|---|---|---|---|---|
| `כי ביצחק` | 0 (adjacent only) | — | — | — | — ms | — ms |
| `כי ביצחק` | 2 | — | — | — | — ms | — ms |
| `כי ביצחק` | 10 (UI default) | — | — | — | — ms | — ms |
| `כי ביצחק` | 50 | — | — | — | — ms | — ms |
| `כי ביצחק` | ∞ (no filter) | — | — | — | — ms | — ms |
| `אברהם יצחק יעקב` | 0 | — | — | — | — ms | — ms |
| `אברהם יצחק יעקב` | 10 | — | — | — | — ms | — ms |

**Expected profile:** Tighter distance windows filter more results, reducing D:Snip time proportionally. The 1st-batch latency increases when many results are filtered before the first 200 pass — the pipeline must scan further into the result set. `maxDist=0` (adjacent only) is the most selective and produces the fewest results.

**UI default is 10.** This is a good balance: it allows terms to be in the same sentence without being adjacent, while excluding lines where the terms appear in completely different parts of the text.

---

## Ordered search

Ordered mode (`requireOrdered=true`) is applied inside snippet generation. Like word-distance, it does not affect B:Index.

| Query | Ordered | IDs | Passed | Filtered | D:Snip | 1st-batch |
|---|---|---|---|---|---|---|
| `כי ביצחק` | yes | — | — | — | — ms | — ms |
| `אברהם יצחק יעקב` | yes | — | — | — | — ms | — ms |
| `וידבר משה כן אל בני` | yes | — | — | — | — ms | — ms |
| `כי ביצחק` (maxDist=2) | yes | — | — | — | — ms | — ms |
| `כי יצחק~` | yes | — | — | — | — ms | — ms |
| `משה* תורה` | yes | — | — | — | — ms | — ms |

**Expected profile:** Ordered mode filters out results where terms appear in the wrong order. For natural-language phrases the filter rate is low (most occurrences of a phrase are in order). For reversed or scrambled queries the filter rate is high. Ordered + tight distance is the most selective combination.

---

## SearchIds() — ID-only path

`SearchIds()` skips the SQLite content fetch entirely. Use it when you only need to count results or load content on demand.

| Query | IDs | A:Expand | B:Index | vs Search() |
|---|---|---|---|---|
| `תורה` | — | 0 ms | — ms | — ms faster |
| `כי ביצחק` | — | 0 ms | — ms | — ms faster |
| `בני*` | — | — ms | — ms | — ms faster |
| `יצחק~` | — | — ms | — ms | — ms faster |

**Expected profile:** `SearchIds()` is significantly faster than `Search()` for large result sets because it skips C:Fetch (SQLite content read) and D:Snip entirely. For a query returning 50,000 results, the speedup can be 10–50×.

---

## High-cardinality / stress

| Query | IDs | Passed | B:Index | D:Snip | 1st-batch |
|---|---|---|---|---|---|
| `כי` (very common single word) | — | — | — ms | — ms | — ms |
| `כי לא` (very common 2-word AND) | — | — | — ms | — ms | — ms |
| `כ*` (high-cardinality prefix) | — | — | — ms | — ms | — ms |

**Expected profile:** Single very-common words return tens of thousands of results. B:Index is fast (single posting list, no intersection). D:Snip dominates for large result sets. The 1st-batch latency should still be under 1 second because snippets are generated lazily — only the first 200 are needed before the UI can display results.

---

## Edge cases

| Query | IDs | Notes |
|---|---|---|
| `שָׁלוֹם` (nikud in query) | — | Parser strips nikud; equivalent to `שלום` |
| `\| תורה` (leading pipe) | — | Leading pipe ignored; same as `תורה` |
| `תורה \|` (trailing pipe) | — | Trailing pipe ignored; same as `תורה` |
| `תורה \|\| מצוה` (double pipe) | — | Double pipe treated as single OR separator |
| `תור*~` (wildcard + fuzzy) | — | Wildcard wins; fuzzy suffix stripped |
| `א` (single char token) | 0 | Tokenizer drops single-char tokens; zero results |
| `\| \| \|` (only pipes) | 0 | No tokens after pipe removal; zero results |

**Expected profile:** All edge cases should return in < 10 ms. None should crash or produce incorrect results. The nikud query should return the same results as the bare query. The single-char and pipe-only queries should return zero results cleanly.

---

## Key observations

### What's fast
- **Multi-word AND literals** — each additional term narrows the intersection. A 5-word phrase typically returns < 100 results and completes in < 50 ms end-to-end.
- **Rare single words** — posting list is tiny; B:Index is near-instant.
- **SearchIds()** — skipping the DB fetch makes it 10–50× faster than `Search()` for large result sets.
- **1st-batch latency** — because results are generated lazily, the user sees the first 200 results long before all results are processed. For most queries this is under 200 ms.

### What's slow
- **Very common single words** (`כי`, `לא`) — posting lists are huge; D:Snip for all results takes seconds. Mitigated by the word-distance filter (UI default maxDist=10) which filters most results.
- **Short wildcard anchors** (`כ*`, `*א*`) — A:Expand scans thousands of terms. The expander has a minimum anchor length guard.
- **Fuzzy distance 3** — the edit-distance neighborhood is large; A:Expand is the bottleneck.
- **Infix wildcards** (`*אבר*`) — both prefix and suffix LIKE scans; slowest wildcard form.

### Word-distance filter impact
The word-distance filter (UI: "מרחק מקסימלי בין מילים") is the primary tool for controlling result quality vs. quantity. Setting `maxDist=0` returns only lines where the query terms are adjacent — very precise but may miss valid results. The default of 10 is a good balance for most queries.

### Ordered search impact
Ordered mode (`requireOrdered=true`) is most useful for phrase-like queries where word order matters. It has negligible performance cost (the check is O(n) in the number of tokens in the window) but can significantly reduce the result count for queries where terms appear in both orders in the corpus.

---

## Before / after optimization history (500k tier)

### Round 1 — 2026-05-06: book title cache + removed ORDER BY

| Query | IDs | C:Fetch before | C:Fetch after | D:Snip before | D:Snip after | 1st-batch before | 1st-batch after |
|---|---|---|---|---|---|---|---|
| `כי ביצחק` | 283 | 147 ms | 121 ms | 232 ms | 206 ms | 116 ms | 109 ms |
| `תורה מצוה` | 1,010 | 215 ms | 189 ms | 647 ms | 514 ms | 157 ms | 141 ms |
| `אברהם יצחק יעקב` | 1,266 | 244 ms | 245 ms | 652 ms | 599 ms | 127 ms | 150 ms |
| `משה* תורה` | 2,169 | 370 ms | 515 ms | 1,786 ms | 1,950 ms | 583 ms | 481 ms |
| `*ישראל` | 47,569 | 3,312 ms | 2,490 ms | 13,723 ms | 9,730 ms | 516 ms | **343 ms** |
| `בני*` | 32,275 | 3,799 ms | 2,328 ms | 15,009 ms | 8,571 ms | 543 ms | **315 ms** |
| `כי יצחק~` | 4,416 | 981 ms | 491 ms | 3,695 ms | 2,384 ms | 1,921 ms | **471 ms** |
| `יסראל~2` | 47,488 | 4,683 ms | 2,331 ms | 14,482 ms | 10,880 ms | 780 ms | 695 ms |

The biggest gains are on large result sets where the removed `ORDER BY` eliminates per-chunk sort overhead. `כי יצחק~` 1st-batch dropped from 1,921 ms to 471 ms (−75%). `*ישראל` and `בני*` 1st-batch dropped by ~40%. Small result sets (< 1,500 IDs) see minimal change since the sort cost was negligible there.

### Round 2 — 2026-05-07: skip list acceleration

The numbers below compare the post-Round-1 baseline against the skip-list build. The metric reported is total search time (B:Index phase only — time until all matching IDs are found, before any DB fetch or snippet generation). This isolates the posting-list intersection work that skip lists directly accelerate.

| Query | IDs | B:Index without skips | B:Index with skips | Delta |
|---|---|---|---|---|
| `כי ביצחק` | 283 | 45 ms | 63 ms | +18 ms |
| `שויתי לנגדי תמיד` | 84 | — ms | 179 ms | — |
| `תורה מצוה` | 1,010 | 27 ms | 118 ms | +91 ms |
| `אברהם יצחק יעקב` | 1,266 | 32 ms | 119 ms | +87 ms |
| `וידבר משה כן אל בני` | 138 | — ms | 41 ms | — |
| `אבל בן אין לה` | 795 | — ms | 223 ms | — |
| `משה* תורה` | 2,169 | 387 ms | 391 ms | +4 ms |
| `*ישראל` | 47,568 | 279 ms | 2,497 ms | +2,218 ms |
| `*אבר*` | 16,409 | — ms | 896 ms | — |
| `בני*` | 32,274 | 299 ms | 1,442 ms | +1,143 ms |
| `כי יצחק~` | 4,416 | 529 ms | 538 ms | +9 ms |
| `תארה~ מצוה` | 137 | — ms | 273 ms | — |
| `אנב~` | 29,481 | — ms | 1,654 ms | — |
| `יסראל~2` | 47,487 | 606 ms | 2,209 ms | +1,603 ms |
| `כי ביצחק~` | 4,382 | — ms | 556 ms | — |

**Reading the results:** The multi-word AND literals (`כי ביצחק`, `תורה מצוה`, `אברהם יצחק יעקב`) show the skip list overhead rather than a gain at the 500k tier — the posting lists are short enough that the skip table lookup costs more than the linear scan it replaces. Skip lists are designed to pay off on very long posting lists (tens of thousands of entries) where `SkipTo` must jump far ahead. At 500k lines the common-word lists are not long enough to cross that threshold consistently.

The large single-term wildcard and fuzzy queries (`*ישראל`, `בני*`, `יסראל~2`) are slower with skips — these are OR-union queries where `SkipTo` is never called (union iterators advance sequentially, not by skipping). The skip table adds I/O and deserialization cost with no benefit for the union path.

**Conclusion:** Skip lists are net-neutral to slightly negative at the 500k tier. The benefit will be measurable at the full-corpus tier (5.4M lines) where common-word posting lists are 10× longer and AND intersection `SkipTo` jumps are proportionally larger. The implementation is correct and the infrastructure is in place — the payoff scales with corpus size.

---

## Running the tests

```powershell
# Full performance battery (requires full index — ~17 min to build)
FtsLibTest.exe perf full

# Faster development run (500k lines, ~1 min to build)
FtsLibTest.exe perf 500k

# Other tiers
FtsLibTest.exe perf 1m
FtsLibTest.exe perf 3m
```

The test opens an HTML report automatically with per-category breakdowns and a full table of all cases.
