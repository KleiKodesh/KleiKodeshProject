# FtsLib Performance Report

> **How to regenerate this report**
> ```
> FtsLibTest.exe perf full
> ```
> The test opens an HTML report automatically. Fill in the numbers below from that run.

---

## Environment

| Item | Value |
|---|---|
| Index tier | full (5,444,192 lines) |
| Unique terms | 1,409,819 |
| Index size on disk | postings.bin 367 MB + index.db 40 MB |
| First-batch size | 200 results |
| Warm-up query | תורה |

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

| Query | IDs | Passed | A:Expand | B:Index | C:Fetch | D:Snip | 1st-batch |
|---|---|---|---|---|---|---|---|
| `תורה` (single common) | — | — | 0 ms | — ms | — ms | — ms | — ms |
| `שויתי` (single rare) | — | — | 0 ms | — ms | — ms | — ms | — ms |
| `torah` (English) | — | — | 0 ms | — ms | — ms | — ms | — ms |
| `כי ביצחק` (2-word) | — | — | 0 ms | — ms | — ms | — ms | — ms |
| `תורה מצוה` (2-word common) | — | — | 0 ms | — ms | — ms | — ms | — ms |
| `אברהם יצחק יעקב` (3-word) | — | — | 0 ms | — ms | — ms | — ms | — ms |
| `אבל בן אין לה` (4-word) | — | — | 0 ms | — ms | — ms | — ms | — ms |
| `וידבר משה כן אל בני` (5-word) | — | — | 0 ms | — ms | — ms | — ms | — ms |
| `שויתי לנגדי תמיד כי מימיני בל` (6-word) | — | — | 0 ms | — ms | — ms | — ms | — ms |

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

| Query | IDs | Passed | A:Expand | B:Index | D:Snip | 1st-batch |
|---|---|---|---|---|---|---|
| `תור*` (prefix, short anchor) | — | — | — ms | — ms | — ms | — ms |
| `תורה*` (prefix, longer anchor) | — | — | — ms | — ms | — ms | — ms |
| `*ישראל` (suffix) | — | — | — ms | — ms | — ms | — ms |
| `*אבר*` (infix) | — | — | — ms | — ms | — ms | — ms |
| `משה* תורה` (prefix + AND) | — | — | — ms | — ms | — ms | — ms |
| `*ישראל תורה` (suffix + AND) | — | — | — ms | — ms | — ms | — ms |
| `בני*` (high-cardinality prefix) | — | — | — ms | — ms | — ms | — ms |
| `תור?ה` (optional char) | — | — | — ms | — ms | — ms | — ms |
| `תו?ר?ה` (two optional chars) | — | — | — ms | — ms | — ms | — ms |

**Expected profile:** A:Expand is the bottleneck for wildcards — it runs LIKE scans against the term index. Short anchors (`תור*`) expand to many terms and are slower than long anchors (`תורה*`). Infix wildcards (`*אבר*`) are the most expensive. The AND term in `משה* תורה` dramatically reduces the final result set.

**Edge case — very short anchor:** A single-character prefix like `כ*` may expand to thousands of terms. The expander has a minimum anchor length guard; queries below it are silently skipped (zero results). This is intentional — not a bug.

---

## Fuzzy searches

| Query | IDs | Passed | A:Expand | B:Index | D:Snip | 1st-batch |
|---|---|---|---|---|---|---|
| `יצחק~` (dist 1) | — | — | — ms | — ms | — ms | — ms |
| `כי יצחק~` (dist 1 + AND) | — | — | — ms | — ms | — ms | — ms |
| `יסראל~2` (dist 2) | — | — | — ms | — ms | — ms | — ms |
| `כי יסראל~2` (dist 2 + AND) | — | — | — ms | — ms | — ms | — ms |
| `ישראל~3` (dist 3) | — | — | — ms | — ms | — ms | — ms |
| `אנב~` (3-letter word, dist 1) | — | — | — ms | — ms | — ms | — ms |
| `תארה~` (common word, dist 1) | — | — | — ms | — ms | — ms | — ms |
| `תארה~ מצוה` (dist 1 + AND) | — | — | — ms | — ms | — ms | — ms |

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
