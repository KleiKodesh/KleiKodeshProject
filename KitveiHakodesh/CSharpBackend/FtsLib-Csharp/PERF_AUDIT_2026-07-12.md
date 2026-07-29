# FtsLib Search-Time Performance Audit — 2026-07-12

**Goal:** reduce *search time* — query → matching IDs → first visible results
(stage A:Expand + stage B:Index + first-batch). The snippet-all-results numbers
(D:Snip) in PERFORMANCE.md are a test-harness worst case and were explicitly out
of scope.

**How this was produced:** a 47-agent audit. One agent re-measured the real
full-tier index with a purpose-built driver (Appendix A); five agents each read
one hot subsystem in full; 39 raw findings were merged to 18 (F01–F18); each
merged finding was then adversarially verified by two independent agents — one
checking the claim against the actual code line-by-line, one checking the impact
against measured numbers (several compiled micro-benchmarks from the real
sources to do it). Findings below are grouped by verification outcome.
10 verification agents died on a session usage limit near the end — those
findings are listed under "Unverified" rather than silently dropped.

**Verified binaries:** measurements ran against Release builds confirmed to
match HEAD (`ffcff879`). Result counts matched PERFORMANCE.md's pre-optimization
full-tier counts *exactly* on every overlapping query — zero correctness drift
from the 2026-07-11/12 optimizations.

> **Note:** PERFORMANCE.md's "Full tier" section is pre-optimization and stale.
> Section 1 below is the current truth for full tier.

---

## 1. Current state — fresh full-tier measurements (2026-07-12)

Environment: `index_full` = 4 live segments (`seg_1_25` 105 MB, `seg_1_30` 112 MB,
`seg_1_35` 83 MB, `seg_2_20` 289 MB), 3,109,353 term_index rows total,
1,565,252 distinct terms; `index_500k` = 3 segments; DB 6.58 GB; 8 logical
cores, 15.8 GB RAM (index fits in OS file cache; DB does not — but B:Index
never touches the content DB). Warm-up `SearchIds("תורה")` before each run.

**B:Index = full `SearchIds()` enumeration** (parse + expand + union/intersect,
no DB content, no snippets — note SearchIds *re-runs expansion internally*, so
B includes a second A). P1 = first pass after warm-up, P2 = warm repeat.

| Query | IDs | A:Expand P1/P2 | B:Index P1/P2 | Pre-opt full pipeline (ref) |
|---|---|---|---|---|
| `כי *יצח*` | 45,756 | 366 / 1,050 ms | 596 / 1,392 ms | 8.0 s |
| `*כי* ביצחק` | 2,599 | 412 / 1,000 ms | **43,297 / 31,169 ms** | 41.1 s |
| `*כי* *יצח*` | 77,720 | 2,415 / 1,647 ms | **36,890 / 33,573 ms** | 45.1 s |
| `*ישראל` | 504,333 | 820 / 858 ms | 1,209 / 1,033 ms | 96.7 s |
| `יסראל~2` | 505,748 | 2,114 / 1,985 ms | 2,797 / 2,579 ms | 46.2 s |
| `בני*` | 385,820 | 630 / 582 ms | 1,791 / 1,455 ms | 72.6 s |
| `תורה מצוה` | 21,147 | — | 106 / 27 ms | 6.6 s |

Decomposition of the pathology:

| Query (alone) | Expanded terms | Union IDs | B:Index |
|---|---|---|---|
| `*כי*` | 27,543 | 2,392,083 | 12,475 ms |
| `*יצח*` | 199 | 112,818 | 400 ms |

500k spot-check validated the methodology against PERFORMANCE.md (same ballpark
on every query: `*ישראל` A=72/B=108–241 vs PERF 106/128; `בני*` 74/110–118 vs
72/151; `יסראל~2` 168/208–323 vs 295/293; `תורה מצוה` B=12–17 vs 8).

**Conclusions:**

1. The 2026-07-11/12 skip-list + roaring optimizations transferred to full tier
   for most stress shapes (10–90× vs the pre-opt column).
2. The one remaining pathology is **short-anchor infix wildcards (`*כי*`) in
   AND queries: 31–43 s**, essentially unimproved.
3. A:Expand (SQLite LIKE scans of the vocabulary) is now a co-dominant cost for
   everything else: 0.3–0.9 s per wildcard pattern, ~2 s per fuzzy term at full
   tier — and it is paid **twice** per measured search (once in A, again inside
   SearchIds).
4. Measurement caveat: A:Expand run-to-run variance is ±50% (fresh
   SegmentHandles + SQLite scans each query; P2 was sometimes *slower* than P1).
   B numbers were stable within ~25%.

### 1.1 Open question — profile before optimizing further

`*כי*` alone (union build + drain) costs 12.5 s, and its resolve component is
~110k SQLite point SELECTs ≈ 6–10 s (see F01). But `*כי* ביצחק` costs 31–43 s —
**~20–30 s are not accounted for** by the verified component costs (the
intersect itself should be milliseconds). Hypotheses: per-SkipTo overhead
multiplied across child iterators on some path that bypasses the roaring
materialization, or another hidden multiplier. **Step zero of any follow-up:
run a profiler (or add stage stopwatches) over `SearchIds("*כי* ביצחק")` at
full tier and attribute those seconds.** The fixes below are verified
regardless, but this gap may hide an additional cheap win.

---

## 2. Roadmap — confirmed findings, in recommended order

Every finding here survived both adversarial checks (code-truth + impact).
"Impact" figures are the *verifier's revised* estimates, not the finder's
original claims.

### Tier 1 — quick wins, no index-format changes

#### F04 — Force-merge the serving index *(operational, effort: small)*
- **Files:** [ForceMerger.cs:49-77](FtsLib/Indexing/ForceMerger.cs#L49-L77), [SeforimIndex.cs:193-219](FtsLib/SeforimDb/SeforimIndex.cs#L193-L219)
- **Today:** `index_full` has 4 live segments (3.1M term rows vs 1.19M in the
  largest — 2.6× duplication); `index_500k` has 3. `ForceMerge()` exists but was
  never run. Every LookupTerm is ×4 SELECTs, every LIKE scan ×4 table scans,
  every posting read stitched from 2–4 chunks.
- **Change:** run `SeforimIndex.ForceMerge()` after full builds (or build with
  `forceMergeOnComplete=true`). Add a perf-harness regression check asserting
  single-segment-per-level before measuring.
- **Verified impact:** LIKE scans divide by a *measured* 1.85–2.23× (not the
  claimed 2.6–4×), resolve SELECTs by 4, chunk loads by ~2. Net **~30–45%** off
  wildcard/fuzzy search time at full tier.

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

#### F13 — Parallelize per-segment expansion scans *(effort: small; stopgap)*
- **Files:** [HebrewWildcardExpander.cs:141-153](FtsLib/Search/HebrewWildcardExpander.cs#L141-L153), [FuzzyExpander.cs:119-133](FtsLib/Search/FuzzyExpander.cs#L119-L133), [SearchPipeline.cs:63-82](FtsLib/SeforimDb/SearchPipeline.cs#L63-L82)
- **Today:** expanders iterate segments in a serial `foreach`; SearchPipeline
  expands groups one at a time. A:Expand = SUM of segment scans; largest segment
  is only ~38% of total. Each SegmentHandle owns its own SQLiteConnection and
  expanders create their own commands, so cross-segment parallelism has no
  shared-connection hazard (the shared `seg.Lookup` command is stage-B-only).
- **Change:** `Parallel.ForEach` over segments with thread-local HashSets merged
  at the end (set union is order-independent → byte-identical). Skip when 1
  segment. Optionally parallelize group expansion in Search/SearchIds too.
- **Verified impact:** measured 2.6× on stage A at full tier (suffix scan
  317→121 ms; fuzzy trigram scan 819→314 ms). End-to-end ~15–28% on stress
  queries; time-to-first-results ~2–2.6×. Largely superseded once F02 lands;
  mostly redundant with F04 (merge to 1 segment removes the parallelism).

### Tier 2 — the single biggest lever

#### F01 — Stop re-fetching term metadata term-by-term during resolve *(effort: medium)*
- **Files:** [IndexReader.cs:156-183](FtsLib/Search/IndexReader.cs#L156-L183), [HebrewWildcardExpander.cs:143-153](FtsLib/Search/HebrewWildcardExpander.cs#L143-L153), [FuzzyExpander.cs:104-136](FtsLib/Search/FuzzyExpander.cs#L104-L136), [SearchPipeline.cs:160-228](FtsLib/SeforimDb/SearchPipeline.cs#L160-L228)
- **Today:** the expander scans exactly the right `term_index` rows but selects
  only `term`, discarding `skip_offset/skip_count/offset/length/count`. Then
  `ResolveIterator → LookupTerm` re-fetches those same rows with one prepared
  point SELECT **per term per segment** (measured 53–92 µs each, ADO.NET
  overhead dominates), probing all 4 segments even when expansion already knows
  which segments hold the term. Scale: `*כי*` = 27,543 unique terms × 4 segments
  = **110,172 SELECTs ≈ 6–10 s per query**; `*אבר*` = 5,416 SELECTs ≈ 450–500 ms
  (measured); `ישראל~3` ≈ 5.4k SELECTs ≈ 300–450 ms. `GrammarExpander.Verify`
  ([GrammarExpander.cs:262-281](FtsLib/Search/GrammarExpander.cs#L262-L281)) has
  the same pattern.
- **Change:** expansion SELECTs become
  `SELECT term, skip_offset, skip_count, offset, length, count`; return
  `List<(string term, SegmentChunk[] chunks)>` tagged with SegmentHandle; plumb
  through `SearchPipeline.ExpandGroup` so the resolve delegate consumes
  pre-resolved chunks for expanded terms (fallback to LookupTerm for plain
  literals); batch GrammarExpander verification the same way. Post-filters
  (affix budget, Levenshtein) only inspect the term string → zero risk.
  Expansion and resolve share one immutable segment snapshot (SearchLease) →
  no staleness. Keep the deletes FilteringIterator wrap on the new path.
- **Verified impact:** `*כי* ביצחק` / `*כי* *יצח*` shed the resolve component
  (~6–10 s); `*אבר*` −35–40%; fuzzy stress −20–30%; 500k wildcard/fuzzy B:Index
  roughly halves. **The single biggest verified lever for the worst queries.**
- **Corrections vs the original claim:** resolve is ~110k SELECTs (~6–10 s),
  not ~220k/~19 s (finder double-counted per-segment rows before dedup).
  The `getCount`-sort memoization sub-idea is moot: SearchPipeline routes
  through `MixedSearch`, which never calls `getCount`.

### Tier 3 — structural (largest ceilings)

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

#### F03 — Candidate-driven AND for {rare literal} × {huge OR group} *(effort: large; depends on F01)*
- **Files:** [PostingIntersector.cs:94-101,147-173](FtsLib/Search/PostingIntersector.cs#L94-L173), [IndexReader.cs:205-243](FtsLib/Search/IndexReader.cs#L205-L243)
- **Today:** `MixedSearch` materializes every ≥20-term group into a full
  RoaringBitmap up front regardless of the AND partner. For `*כי* ביצחק`
  (2,599 results; ביצחק ≈ 2.6k docs) it resolves, disk-loads and drains all
  27.5k `*כי*` terms — millions of postings — before intersecting. `כי` alone
  covers 2,179 of the 2,599 final ids, so a term-by-term filter would exhaust
  the candidate set after a handful of high-df terms.
- **Change:** when one group is small (docs below an *adaptive* threshold —
  small-side count ≪ estimated union size; don't hardcode 10–50k) and another
  has ≥ RoaringOrThreshold terms: materialize the small side as a sorted int[];
  sort the huge group's terms by Count descending (metadata free once F01
  lands); SkipTo-probe each term's postings against the still-unmatched
  candidates; **stop consuming terms the moment every candidate is matched**.
  Semantics exact: candidate passes iff present in ≥1 term, and all terms are
  consulted until the candidate set is empty (never exclude early).
- **Verified impact:** with F01, `*כי* ביצחק` stage B → ~0.1–1 s best case
  (early exit fires), ~1–5 s worst case; **10–25× end-to-end** on the worst
  query in the suite. Standalone (no F01) only ~2×, because per-term SQLite
  lookups dominate. Coverage: of the named stress set only `*כי* ביצחק`
  qualifies (`*כי* *יצח*` has no small side; `כי` at ~1M docs is not small).
- **Trap flagged by verification:** a candidate that matches *no* group term
  forces consulting *all* terms (correctness requires it) — the win degrades
  toward 3–10× if df(ביצחק) is meaningfully above 2,599; still never worse than
  today.

#### F05 — Bulk union build: `DrainInto` + `AddAscending` *(effort: medium)*
- **Files:** [PostingIntersector.cs:167-171](FtsLib/Search/PostingIntersector.cs#L167-L171), [RoaringBitmap.cs:63-96,244-302](FtsLib/Search/RoaringBitmap.cs#L63-L302), [PostingIterator.cs:41-56](FtsLib/Search/PostingIterator.cs#L41-L56), [VarInt.cs:41-53](FtsLib/Search/VarInt.cs#L41-L53)
- **Today:** `while (it.MoveNext()) bitmap.Add(it.Current)` — per posting: 1–2
  virtual MoveNexts (Concat/Filtering wrappers), byte-at-a-time varint with
  bounds check, `FindKey` binary search over ~100 block keys, virtual
  Container.Add, ArrayContainer binary search. Postings are ascending →
  consecutive docs nearly always hit the same 64K block, so FindKey is redundant
  almost every call. Benchmark: raw decode of `*ישראל`'s 656k postings into a
  flat bitmap = 2 ms — the layered per-doc calls are ~all the overhead
  (~30–60 ns/posting).
- **Change:** `PostingIterator.DrainInto(RoaringBitmap)` (ConcatIterator
  forwards to children; FilteringIterator tests deletes inline) decoding the
  varint buffer in one tight non-virtual loop, paired with
  `RoaringBitmap.AddAscending` caching (lastHighKey, lastContainer) + an
  ArrayContainer append fast-path. Identical values inserted → zero risk.
- **Verified impact (standalone, full tier):** `בני*` 25–40%, `*אבר*` 30–40%,
  `ישראל~3` 30–40%, `*ישראל` 15–25%, `*כי*` union drains 0.4–1.5 s. At 500k:
  ~8–30% of first-batch.

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

#### F10 — Parallel union build *(effort: large; do after F01/F05)*
- **Files:** [PostingIntersector.cs:147-173](FtsLib/Search/PostingIntersector.cs#L147-L173), [SegmentHandle.cs:80-117](FtsLib/Indexing/SegmentHandle.cs#L80-L117), [RoaringBitmap.cs:108-160,399-420](FtsLib/Search/RoaringBitmap.cs#L108-L160)
- **Today:** `BuildRoaringIterator` is strictly sequential. The SIMD
  `RoaringBitmap.Or/OrWith` merge path is *dead code* today (resolve never
  returns a RoaringBitmapIterator). Thread-safety blockers: the shared prepared
  Lookup command and the lock-serialized FileStream in SegmentHandle.
- **Change:** partition expanded terms across N=min(cores,4–8) workers; each
  worker gets its own connection/FileStream (or needs no SQLite at all once F01
  metadata is prefetched) and drains into a thread-local bitmap via F05's bulk
  path; merge with the existing SIMD OrWith. OR is commutative/idempotent →
  bit-identical, deterministic.
- **Verified impact:** realistic 2–4× on union build (not the claimed 3–6×).
  Full tier: 35–55% end-to-end on union-dominated queries *conditional on
  F01/F05 landing first* (otherwise SQLite resolve dominates and parallelism
  mostly parallelizes waiting).

### Suggested sequencing

```
PR1 (quick):      F04 (ops) + F06 + F01      → *כי* ביצחק ~40s → ~2s; wildcard/fuzzy B halves
PR2 (medium):     F05 + F13 (if F02 deferred)
PR3 (structural): F02 (subsumes F06/F07/F13/F14)  → all A:Expand → ms
PR4 (structural): F03 (needs F01) + F10 (needs F01/F05)
```

Expected end state at full tier: every stress query ≈ 1–2 s or less; simple
wildcards/fuzzy well under 1 s; the `*כי*` family bounded by union drain
(~0.5–2 s) instead of 31–43 s. Subject to resolving the §1.1 unknown.

---

## 3. Unverified findings (verification agents died — session limit)

Treat as plausible but unchecked; re-verify before investing.

- **F14** *(small)* — SQLite stopgaps if F02 is deferred: `instr()` instead of
  LIKE for pure-Hebrew n-grams (measured 213→91 ms on `אנב~` scans at 500k;
  **caution:** LIKE is ASCII-case-insensitive and terms are never lowercased on
  the ASCII range — only safe when the n-gram has no ASCII letters);
  `WITHOUT ROWID` clustered term_index (single B-tree probe, half the file,
  needs rebuild); `PRAGMA cache_size/mmap_size` on read connections.
- **F16** *(small, code-CONFIRMED, impact unverified)* — `SearchPipeline.Search`
  does `using (var db = new ZayitDb(dbPath))` per query
  ([SearchPipeline.cs:80](FtsLib/SeforimDb/SearchPipeline.cs#L80),
  [ZayitDb.cs:28-40](FtsLib/SeforimDb/ZayitDb.cs#L28-L40)): ctor
  Console.WriteLine, fresh connection to the 6.7 GB DB, `journal_mode=WAL`
  (a write op) executed every search, 64 MB page cache discarded on close, no
  `Read Only`/`Pooling` flags (SegmentHandle does it right). Keep one long-lived
  read-only pooled ZayitDb per SeforimIndex.
- **F17** *(small)* — first visible result waits for a full 200-ID chunk in
  `ZayitDb.FetchSearchResultsStreaming` **and** another 200-item buffer in
  `SearchService` (demo app) before anything is shown; word-distance filtering
  can discard ~40%, pushing the real requirement past 300 IDs. Geometric ramp
  (25→50→100→200) in both places; same rows, same order.
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

---

## 4. Rejected findings — verified dead ends (don't redo)

Each was code-verified as *factually real* but **refuted on impact** by
measurement/Amdahl. Revisit only after Tier 1–3 land.

- **F08 — Reorder AND groups / bitmap-native And/Contains.** getCount *is* dead
  code in MixedSearch and groups *do* stay in query order — but measured 500k
  deltas show the whole intersect phase is ~2–4% of B (e.g. `*ישראל` 128 ms vs
  `*ישראל תורה` 131 ms). 7 of 11 stress queries are single-group (no intersect
  at all). Materialization dominates; intersect cost ≤ build cost structurally.
  The SIMD And/Contains pieces are worth doing *opportunistically* after F01/F05.
- **F09 — Direct roaring cursor** (replace nested IEnumerator state machines).
  Mechanism confirmed (~20 ns/value vs ~4 ns direct; measured via compiled
  micro-benchmark of the actual sources, bit-identical outputs) — but
  F09-addressable time is 5–55 ms per stress query ≈ 1–4% of search time.
  Follow-up only, after the dominant costs are gone.
- **F11 — LoadChunk double-alloc/copy, per-int skip parse, per-chunk locked
  reads.** Real, but the addressable slice is ~4–8 µs of a ~53 µs per-chunk cost
  dominated by the SQLite point SELECT (= F01's territory). Fold the
  alloc/copy elimination and a `needSkipTable` flag into F01 as hygiene
  (~5–15 ms/query at 500k). Note: `needSkipTable=false` is only safe for the
  roaring-union drain path — never for iterators that reach
  `PostingMatcher.Intersect`/`UnionIterator` (they call SkipTo).
- **F12 — Cross-query caching (expansions, union bitmaps, ID lists).** Zero
  caching exists (confirmed; the roaring fast-path at
  `PostingIntersector.cs:159-166` is dead code) — but every proposed cache is a
  cold miss on the first execution, which is exactly what the stress benchmark
  and the stated goal measure. **Two correctness traps if ever built:** cache
  keys must include the deletes.bin version (not just the livePaths hash), and
  a cached IndexReader holding its SearchLease blocks merges indefinitely.
  **Worth re-filing narrowly:** the demo app's load-more re-runs the entire
  search AND re-snippets already-shown rows
  ([SearchService.cs:54-60](FtsLibDemo/Services/SearchService.cs#L54-L60) —
  snippet call precedes the skip check); resuming from a cached ID list turns a
  tens-of-seconds re-search into a ~10–50 ms offset fetch. Pagination/UX fix,
  not a cold-search fix.
- **F15 — SkipTo resume + gallop.** Premise correct (full-range binary search
  restarts every call; targets are monotone) but most stress queries never
  execute `PostingIterator.SkipTo` (roaring path), and on the AND trio it totals
  ~10–30 ms. Noise (~0–2%).

---

## 5. Unexplored avenues (critic flagged, agents never ran)

Three structurally distinct ideas no finding covers — candidates for a future
audit round:

1. **First-batch early-exit contract:** serve the first visible page from a
   lazy streaming heap-union + streaming intersect while the full roaring union
   (needed for total count / load-more) completes on a background task.
2. **Build-time precomputed affix-bucket union bitmaps** (Lucene-style
   auto-prefix/suffix terms with the Hebrew wildcard budget baked in): prefix/
   suffix wildcards resolve to ONE stored RoaringBitmap instead of a union over
   2.5k–30k posting lists. (Potentially the only real fix for `*כי*`-class
   unions beyond F01/F05/F10.)
3. **Pipeline overlap of stages A and B:** stream expanded terms (with chunk
   metadata) into the union builders as the dictionary scan produces them,
   instead of completing ALL expansion before reading a single posting byte.

---

## 6. Correctness constraints (non-negotiable, from the test suite)

- Zero false positives, zero missing required IDs
  ([SearchTest.cs](FtsLibTest/SearchIndex/SearchTest.cs), `MaxFalsePositiveRate = 0.0`).
- `Search()` and `SearchIds()` must return identical ID sets (both flow through
  `ExpandGroup`/`MixedSearch` — keep it that way).
- Fuzzy candidate sets must stay **byte-identical** to the current n-gram
  prefilter output (see F18 — current recall is the spec).
- Ascending doc-ID output order (ConcatIterator contract) and deletes filtering
  must be preserved on any new resolve/drain path.
- Segment snapshots: expansion + resolve must share one SearchLease; any cache
  keys on the live-segment snapshot must also account for deletes.bin.

---

## Appendix A — Measurement rig (reproduction)

No existing CLI command times A/B per arbitrary query without content/snippets
(SpeedTest snips ALL results; QueryTest/DiffIds run full Search()). The audit
used a minimal driver compiled **as `FtsLibTest.exe`** (to satisfy
`InternalsVisibleTo("FtsLibTest")`), referencing the Release `FtsLib.dll` with
its SQLite/System.* dependencies alongside.

Usage: `FtsLibTest.exe <indexDir> <dbPath> <queriesFile> [passes]` — first
query line is the warm-up; output is TSV (`ids / A_expand_ms / B_searchids_ms`
per query per pass). Compile with `csc /platform:x64 /optimize+` against the
Release output folder.

```csharp
using FtsLib.Indexing;
using FtsLib.Search;
using FtsLib.SeforimDb;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

// Measurement driver. Mirrors FtsLibTest SpeedTest.cs phases A and B exactly:
//   A:Expand = fuzzy/wildcard term expansion only (fresh SegmentHandles, disposed after)
//   B:Index  = full SearchIds(query) enumeration (parse + expand + posting intersection, no DB content fetch)
// Assembly must be named FtsLibTest so InternalsVisibleTo("FtsLibTest") grants access
// to internal QueryParser / FuzzyExpander / HebrewWildcardExpander / SegmentHandle.
internal static class Driver
{
    private static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length < 3)
        {
            Console.WriteLine("usage: FtsLibTest.exe <indexDir> <dbPath> <queriesFile> [passes]");
            return;
        }

        string indexDir    = args[0];
        string dbPath      = args[1];
        string queriesFile = args[2];
        int    passes      = args.Length > 3 ? int.Parse(args[3]) : 1;

        var queries = new List<string>();
        foreach (var line in File.ReadAllLines(queriesFile, Encoding.UTF8))
        {
            string t = line.Trim();
            if (t.Length > 0) queries.Add(t);
        }

        var swInit = Stopwatch.StartNew();
        var index = new SeforimIndex(indexDir, dbPath);
        swInit.Stop();
        Console.WriteLine("INIT\t" + swInit.ElapsedMilliseconds + " ms\tindex=" + indexDir);

        // Warm-up: literal SearchIds on the first query line (by convention).
        string warm = queries[0];
        var swW = Stopwatch.StartNew();
        int wc = 0;
        foreach (var _ in index.SearchIds(warm)) wc++;
        swW.Stop();
        Console.WriteLine("WARMUP\t" + warm + "\tids=" + wc + "\tB=" + swW.ElapsedMilliseconds + " ms");
        Console.WriteLine();
        Console.WriteLine("pass\t#\tquery\tids\tA_expand_ms\tB_searchids_ms");

        for (int p = 1; p <= passes; p++)
        {
            for (int qi = 1; qi < queries.Count; qi++)
            {
                string query = queries[qi];

                // -- Phase A: expansion only (identical to SpeedTest.cs) --
                var parsed = QueryParser.Parse(query);
                var datFiles = Directory.GetFiles(indexDir, "seg_*.dat");
                Array.Sort(datFiles);
                var segments = new List<SegmentHandle>();
                foreach (var dat in datFiles)
                {
                    string db2 = Path.ChangeExtension(dat, ".db");
                    if (File.Exists(db2)) segments.Add(new SegmentHandle(dat, db2));
                }

                var termCounts = new StringBuilder();
                var swA = Stopwatch.StartNew();
                foreach (var group in parsed.Groups)
                {
                    if (group.IsFuzzy)
                    {
                        var t = FuzzyExpander.Expand(group.Pattern, group.FuzzyDistance, segments);
                        termCounts.Append(group.Pattern).Append("=").Append(t.Count).Append("terms ");
                    }
                    else if (group.IsWildcard)
                    {
                        var t = HebrewWildcardExpander.Expand(group.Pattern, segments);
                        termCounts.Append(group.Pattern).Append("=").Append(t.Count).Append("terms ");
                    }
                }
                swA.Stop();
                long expandMs = swA.ElapsedMilliseconds;
                foreach (var s in segments) s.Dispose();

                // -- Phase B: SearchIds (ids only, no DB content) --
                var swB = Stopwatch.StartNew();
                int n = 0;
                foreach (var id in index.SearchIds(query)) n++;
                swB.Stop();

                Console.WriteLine("P" + p + "\t" + qi + "\t" + query + "\t" + n +
                                  "\t" + expandMs + "\t" + swB.ElapsedMilliseconds +
                                  "\t" + termCounts.ToString().Trim());
            }
        }

        Console.WriteLine("DONE");
    }
}
```

Query files used: `queries_full.txt` = `תורה` (warm-up), `כי *יצח*`,
`*כי* ביצחק`, `*כי* *יצח*`, `*ישראל`, `יסראל~2`, `בני*`, `תורה מצוה`;
`queries_decomp.txt` = `תורה`, `*יצח*`, `*כי*`; `queries_500k.txt` = `תורה`,
`*ישראל`, `בני*`, `יסראל~2`, `תורה מצוה`. Run with `passes=2` (P1 ≈ cold-ish,
P2 = warm).

---

*Audit artifacts (session-local, may be cleaned up): full findings JSON with
both verifier verdicts per finding, measurement logs (`full_run.log`,
`decomp_run.log`), and the compiled micro-benchmarks (`Bench.cs`) lived under
the Claude session scratchpad for session `f229b20e`, workflow
`wf_23bf94dd-232`. Everything needed to resume is in this document.*
