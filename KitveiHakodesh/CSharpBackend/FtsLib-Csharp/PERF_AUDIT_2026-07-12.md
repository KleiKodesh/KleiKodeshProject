# FtsLib expander performance notes (from the 2026-07-12 audit)

> Trimmed: the original audit covered the removed custom segment engine (posting lists,
> RoaringBitmap unions, segment merges). Only the findings about the term-expansion layer
> (HebrewWildcardExpander, FuzzyExpander, the trigram/n-gram ideas) are kept. Measurements
> were taken on that engine's segments; file/line links to removed files are historical.

#### F06 — Prefix wildcards: LIKE → BINARY index range scan *(effort: small)*
- **Files:** [HebrewWildcardExpander.cs:143-152](FtsLib/Search/HebrewWildcardExpander.cs#L143-L152), [SegmentWriter.cs:127-133](FtsLib/Indexing/SegmentWriter.cs#L127-L133)
- **Today:** `ExpandStar` runs `term LIKE @p` for all shapes. Parameterized LIKE
  never uses `idx_term` (needs `case_sensitive_like=ON`, never set), so `בני*`
  full-scans 1.19M keys per segment. EXPLAIN QUERY PLAN confirmed: LIKE =
  `SCAN ... COVERING INDEX`, range = `SEARCH`. Measured: 304 ms → 1.4 ms at full
  tier. Hidden multiplier: each `?`-unrolled sub-pattern pays its own full scan
  (up to 16 scans × 4 segments).
- **Change:** when the pattern doesn't start with `*`, add
  `term >= @lo AND term < @hi` (leading literal run; `@hi` = run with last char
  incremented — safe, alphabet is only U+05D0–U+05EA and a–z) **while keeping
  the original LIKE as a residual predicate** — result set provably identical.
  Also apply to `?`-unrolled sub-patterns ending in `*`.
- **Verified impact:** 75–120× on stage A for prefix patterns. 500k: `בני*`
  A+B+1st 372→~160 ms (−57%), `תור*` −72%, `משה* תורה` −65%. Full tier: −40 to
  −60% on prefix-anchored queries. 0% on suffix/infix/fuzzy.

#### F02 — In-memory per-segment term dictionary + character n-gram index *(effort: large)*
- **Files:** [HebrewWildcardExpander.cs:141-153](FtsLib/Search/HebrewWildcardExpander.cs#L141-L153), [FuzzyExpander.cs:104-136](FtsLib/Search/FuzzyExpander.cs#L104-L136), [SeforimIndex.cs:223-233](FtsLib/SeforimDb/SeforimIndex.cs#L223-L233), [SegmentWriter.cs:134-163](FtsLib/Indexing/SegmentWriter.cs#L134-L163)
- **Today:** every wildcard pattern = full LIKE table scan per segment; every
  fuzzy term = full scan with up to 4 OR'd `LIKE '%ngram%'`. Nothing cached — a
  fresh IndexReader + SegmentHandles + SQLite connections per `Search()`.
  Measured warm: wildcard scans 319–372 ms/pattern, fuzzy 674–2,919 ms/term at
  full tier. The Levenshtein confirm phase is ≤30 ms — the scan is everything.
- **Change:** per-segment `TermDictionary` cache keyed by segment path, owned by
  SegmentStore (invalidation = live-segment snapshot; segments are write-once
  with never-reused IDs): sorted term array (terms are already Ordinal-sorted on
  disk — `SegmentWriter.cs:30`, `SegmentMerger` merges via CompareOrdinal), a
  lazily built reversed-order permutation, and trigram→int[] / bigram→int[]
  maps (ASCII-folded keys to replicate LIKE's ASCII case-insensitivity).
  Serve: prefix = binary-search range; suffix = binary search on reversed
  permutation; infix = rarest-bigram candidate list + `IndexOf` verify + the
  existing affix-budget filter; fuzzy = union of query n-gram posting lists +
  existing Levenshtein confirm. Optionally persist as a sidecar table at
  build/merge time (+5–8% index size) to kill the first-query build cost.
  Store F01's five metadata ints per term (~+45 MB full tier) → stage B does
  **zero** SQLite calls.
- **Prototype evidence (real index):** **byte-identical** fuzzy candidate sets
  in 0.11–6.8 ms vs 0.7–2.9 s; build 7.2 s dump + 8.5 s build, 166 MB naive
  (compactable ~60–80 MB); only 1,777 distinct Hebrew bigrams over 1.57M terms;
  team's own FstSizeDiag/FileLoadDiag already concluded a ~20 MB in-RAM
  structure "costs nothing per search".
- **Verified impact (500k, A+B+1st):** `*ישראל` −86%, `יסראל~2` −90%, `אנב~`
  −81%, `בני*` −58%, `ישראל~3` −58%, `*אבר*` −42%. Full tier: expansion fraction
  is larger, so ~50–90% per wildcard/fuzzy stress query (multi-second absolute).
  Removes only the expansion component of the `*כי*` AND stress (rest is
  F01/F03).
- **Implementation caveats found by verification:** (a) mid-star (`אב*גד`) and
  fragmented multi-star (`*א*ב*`) shapes have no contiguous bigram — need a
  linear-scan fallback over the in-memory array (~20–60 ms, still ≫ faster than
  SQLite); (b) 1-char fuzzy terms need a unigram list or the same fallback;
  (c) `SELECT term FROM term_index` must add `ORDER BY rowid` or re-sort;
  (d) supersedes F06/F07/F13/F14 when adopted.

#### F07 — Reversed-term column for suffix wildcards *(effort: medium; skip if F02 adopted)*
- **Files:** [SegmentWriter.cs:134-163](FtsLib/Indexing/SegmentWriter.cs#L134-L163), [HebrewWildcardExpander.cs:141-190](FtsLib/Search/HebrewWildcardExpander.cs#L141-L190)
- **Today:** `LIKE '%ישראל'` scans 3.11M rows at full tier (366 ms) to keep ~100
  budget-surviving terms. No reversed-term structure exists. All segment .db
  writes funnel through the single `WriteMetaDb` helper → one-site schema
  change.
- **Change:** add `rterm TEXT` column (char-reverse in C#; safe — BMP-only
  alphabet) + `idx_rterm`; route `*abc` patterns to a range scan on the reversed
  anchor, reverse results back; keep the existing affix filter → byte-identical
  results. Needs rebuild, or lazy fallback via `PRAGMA table_info` detection,
  or one-time ALTER+UPDATE migration. ~10–15% .db growth.
- **Verified impact:** `*ישראל` search time ~2.2–2.5× at full tier (~4–5× at
  500k). 0% on infix/fuzzy. **Mutually exclusive with F02 — pick one.**

#### Other expander findings (unverified at the time)

- **F14** *(small)* — SQLite stopgaps if F02 is deferred: `instr()` instead of
  LIKE for pure-Hebrew n-grams (measured 213→91 ms on `אנב~` scans at 500k;
  **caution:** LIKE is ASCII-case-insensitive and terms are never lowercased on
  the ASCII range — only safe when the n-gram has no ASCII letters);
  `WITHOUT ROWID` clustered term_index (single B-tree probe, half the file,
  needs rebuild); `PRAGMA cache_size/mmap_size` on read connections.
- **F18** *(small — correctness boundary, read before touching FuzzyExpander)* —
  the fuzzy n-gram prefilter *provably misses* 40–92% of terms genuinely within
  the edit distance at stress term lengths (requiring ≥1 shared n-gram only
  guarantees recall when `(L−q+1) − d·q ≥ 1`; measured vs exact banded
  Levenshtein at 500k: `יצחק~1` finds 25/41 true neighbors, `יסראל~2` 75/187,
  `ישראל~3` 498/6,221). The test suite cannot see this (it validates against the
  filter's own output). **Current recall is the de-facto spec**: every
  optimization must replicate candidates byte-identically (F02's prototype
  does). "Fixing" recall is a product decision — it would inflate `ישראל~3` from
  498 to 15,369 terms and multiply downstream cost.

## Correctness constraint

- Fuzzy candidate sets must stay **byte-identical** to the current n-gram
  prefilter output (see F18: current recall is the spec).
