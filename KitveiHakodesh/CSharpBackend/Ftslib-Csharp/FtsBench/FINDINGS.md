# FtsLib number-types + SIMD experiment — findings

Branch: `perf/ftslib-numtypes-simd`  ·  Date: 2026-07-15
Goal: can we make **indexing faster & more compact** and **search faster** by trying
different integer representations / codecs and SIMD?

All measurements are on **real data** (the prebuilt `index_500k` / `index_full`
`.dat` files), run in an **isolated `net48` x64** bench (`FtsBench/`) that links the
*actual* production algorithm files so there is no code drift. Timings use warmup +
min-of-N; every candidate passes a correctness gate before it is timed.

---

## The single most important constraint

The library targets **.NET Framework 4.8**. That decides what "SIMD" can be:

| | Available on net48? | Consequence |
|---|---|---|
| `System.Runtime.Intrinsics` (AVX2/SSE intrinsics, `PSHUFB` shuffles) | **No** | The classic *SIMD varint decode* (StreamVByte / Masked-VByte) can't be ported — it needs shuffles. |
| `System.Numerics.Vector<T>` | **Yes** (pkg already referenced) | Elementwise vector ops only. Good for bitset AND/OR and popcount. |

Runtime probe on the dev box (`FtsBench.exe env`): `.NET Framework 4.8.9325`, x64,
`Vector.IsHardwareAccelerated = True`, **`Vector<ulong>.Count = 4` (256-bit / AVX2)**.
A recent Framework servicing evidently enabled 256-bit `Vector<T>`.
⚠️ **End-user machines vary**: without AVX2 they fall back to 128-bit (`Count = 2`) or
scalar. `Vector<T>` degrades gracefully, so SIMD code is still correct and faster there,
just less so. All SIMD numbers below are **best-case (AVX2)**.

---

## Findings at a glance

| Idea | Verdict | Measured | Status |
|---|---|---|---|
| **Inline varint encode** (drop per-posting delegate) | ✅ ship | **2.38× faster encode**, byte-identical | **implemented** |
| **Harley-Seal SIMD popcount** for `BitmapContainer.CountBits` | ✅ ship (free) | **3.3× popcount / 2.7× `OrWith`**, count-identical | **implemented** |
| **Format v2** = no-offset first value + varint the 20-byte term header | ✅ prototyped, results identical at full scale | **`.dat` −18% @500k, −7% @full** (measured rebuilds) | **prototyped on branch** |
| **Front-coded term dictionary** (replace SQLite `.db`) | ✅ prototyped — **the big lever** | **5.3× smaller `.db`, 55× exact lookup, 11× prefix**, correct | **prototyped on branch** |
| Allocation-free result iteration (`GetValuesInto`) | ⚠️ marginal | 5–15× isolated, but ~0–11% end-to-end; GC-churn only | wired (env-toggled) |
| GroupVarint codec | ❌ reject | **+8.6%/+11% bigger**, decode only 1.16× | rejected |
| BitPack-FOR / PForDelta codec | ❌ reject | +7–8% bigger (FOR); −1.3% (patched) not worth complexity | rejected |
| Narrower skip-table ints | ❌ skip | skip table is only 2.9–4.6% of `.dat` | rejected |
| SIMD / bulk-iter to speed the cold `*כי*` **infix** union | ❌ not applicable | bottleneck is term-expansion (`LIKE` scan), not vector math or iteration | — |

---

## 1. Compaction: the posting codec is already near-optimal

Real gap distribution (from `FtsBench.exe dist`): **70% of gaps fit in 1 byte, 26% in 2
bytes.** LEB128 is an excellent fit. Analytically re-encoding every real posting:

**index_full** (3.1M terms, 336M postings, `.dat` posting bytes = 461 MB):

| codec | size | vs current |
|---|---|---|
| LEB (current) | 461.5 MB | — |
| LEB no-offset | 457.6 MB | **−0.9%** |
| GroupVarint | 512.6 MB | +11.1% |
| BitPack-FOR/128 | 500.2 MB | +8.4% |
| BitPack-Patched/128 | 455.4 MB | −1.3% |

**Conclusion:** fancy codecs *lose* on this data. Don't chase them.
The only clean posting win is removing the `int.MinValue` offset (the first value of
every term currently always costs the full 5 varint bytes): **−4.2% @500k, −0.9% @full**
(shrinks with scale because it's amortised over more postings/term).

### The real compaction target is the term header, not the postings

`.dat` composition at full tier: **postings 78.3%, term header 10.6%, term text 6.5%,
skip 4.6%.** The header is **20 fixed bytes/term** (`termByteLen`, `chunkByteLen`,
`docCount`, `lastEncoded`, `skipCount` — all int/uint32). 40% of terms are singletons,
so most of those 20 bytes are tiny values stored fat. Varint-ing them ≈ halves the header:

- @full: 62 MB → ~30 MB (**~−5% of `.dat`**)
- @500k: 13 MB → ~6 MB (**~−13% of `.dat`**)

Combined with no-offset, a coordinated format change could cut the `.dat` by roughly
**5–15%** depending on tier — *and* speed up merge (fewer header bytes to read/write).
It requires `SegmentWriter`/`SegmentReader`/`SegmentMerger` changes, a format-version
bump, and a **full index rebuild** on end-user machines, so it's proposed, not shipped.

---

## 2. Indexing speed: inline the varint write ✅ IMPLEMENTED

`PostingStream.Add` called `VarInt.Write(toWrite, WriteByte)` — passing an instance
method as `Action<byte>` **allocates a delegate on every posting** (~336M allocations
across a full build) and does a capacity check per byte. Inlining the base-128 write
straight into the buffer (one capacity check per posting):

```
PostingStream (delegate, old)  :  78.0 M postings/s
PostingStream (inline, new)    : 186.0 M postings/s   → 2.38× ,  byte-for-byte identical
```

At full scale (336M postings) the encode step drops ~4.3s → ~1.8s **and** removes ~336M
short-lived allocations (GC relief). Output is byte-identical (verified on all 667k real
500k lists), so **the on-disk index is unchanged** — no rebuild needed, no format bump.

---

## 3. Search speed: Harley-Seal SIMD popcount ✅ IMPLEMENTED

`BitmapContainer.OrWith` did a **scalar** Hamming-weight recount of all 1024 words after
every merge, and that recount *dominated* `OrWith`. Replacing `CountBits` with a
Harley-Seal carry-save-adder tree over `Vector<ulong>` (folds 16 vectors/pass, ~1024
scalar popcounts → ~80; **no shuffle intrinsics, so it runs on net48**):

```
popcount   : scalar 464 Mwords/s → Harley-Seal 1440 Mwords/s   → 3.3×
Vector OR  : 1.75× over scalar   (already in the codebase; validated)
full OrWith: 388 Mwords/s → 949 Mwords/s                        → 2.7×
```

Produces bit-for-bit the same cardinality (verified on 4096 random containers + edge
cases + the real `RoaringBitmap.Or` path).

**Honest scope of this win:** `CountBits` is *only* called from `OrWith`, and `OrWith`
only fires on `bitmap.Or(otherBitmap)` — merging **already-materialized** bitmaps
(cached/repeated wildcard expansions, nested materialized groups). A **cold** single-group
union like `*כי*` resolves each term to a raw posting list drained via `DrainInto` (the
`Add` path), which keeps cardinality incrementally and never popcounts. So this is a
**free** win — 2.7× on the bitmap-merge path, **zero cost everywhere else, never hurts** —
that benefits cached/repeat/pagination queries, but does **not** speed the cold `*כי*`
pathology.

### Decode codec: not worth changing
GroupVarint decodes only **1.16×** faster than LEB while being **+11% bigger**. LEB decode
is already ~157–180 M postings/s. Leave it.

---

## End-to-end validation (old vs new binary, same index_500k)

Result-identity via `dumpids` (byte-diff of sorted id sets), min-of-5 timings:

| query | ids | old | new | results |
|---|---|---|---|---|
| תורה | 22,473 | 4 ms | 4 ms | **IDENTICAL** |
| בני* | 40,756 | 17 ms | 17 ms | **IDENTICAL** |
| כי ביצחק | 481 | 5 ms | 6 ms | **IDENTICAL** |
| *כי* | 183,959 | 193 ms | 189 ms | **IDENTICAL** |

500k timings are noise-level (the cold union path isn't `OrWith`-bound, as explained).
Result-identity is the point here, and it holds.

---

## 4. Format v2 compaction PROTOTYPE (no-offset + varint header) — real rebuild A/B

Implemented on the branch and validated by rebuilding a fresh 500k index and diffing
against the v1 index (same corpus, same deterministic 3-segment layout):

**Two coordinated codec changes:**
- **No-offset postings** — `PostingStream.Encode` dropped the `int.MinValue` rebase, so the
  first value of every term is the actual doc id (varint ~3-4 B) instead of a fixed 5 B.
  Rippled through decode (`PostingIterator`), the merge decode/skip-rebuild, and the skip seed.
- **Varint term header** — the five fixed header scalars (were 20 B of int32/uint32) are
  varint-encoded in `SegmentWriter` + `SegmentMerger`, read back in `SegmentReader`. Search
  never touches this header (it uses `.db` offsets), so only merge + file size are affected.

**Results (fresh rebuilds, v2 vs v1, identical segment layout at each tier):**

| tier | v1 `.dat` | v2 `.dat` | delta | results |
|---|---|---|---|---|
| 500k | 55.4 MB | 45.4 MB | **−18.0%** | byte-identical (7 queries) |
| **full** | **589.3 MB** | **548.2 MB** | **−7.0% (−39.2 MB)** | **identical (8-query headless A/B + 17/18 suite)** |

Correctness proven four ways: a synthetic v2 codec round-trip (MoveNext + DrainInto + SkipTo,
±skip table, 12 cases), a clean same-toolchain baseline build, result-identity A/Bs at both tiers,
and — at full — a fresh build that **exercised the v2 merge path** (LSM cascade merges to levels 1–2)
plus the `search full` comprehensive suite (content + snippet + `Search`/`SearchIds` consistency),
17/18 queries all-pass (the 18th `*כי* *יצח*` was already proven identical by the headless A/B and its
content-validation is pathologically slow, so it was stopped). `.db` term-index size unchanged (the
compaction is entirely in `.dat`). Full build: 6.5M lines in 9m36s.

The win shrinks with scale (−18% @500k → −7% @full) because both the fixed 20-byte header and the
5-byte first-posting offset are amortised over more postings/term as the corpus grows.

**Build time is within noise** — a clean master baseline built in ~22.5 s vs v2's ~20.5 s (the
earlier apparent 1.7× was background I/O interference on the baseline runs). The encode step is
a tiny fraction of total build time, exactly as the microbenchmark predicts; the real prize here
is **size, not build speed**.

**⚠️ Shipping cost:** format v2 is a *breaking* on-disk change. It needs a format-version bump
and a **full index rebuild** on every end-user machine (old indexes read with v2 code, or v2
indexes read with old code, would be silently wrong). The prototype builds fresh into an
isolated dir and does not add a version guard — that's the main remaining work before shipping.
At full tier the header is a smaller share of `.dat` (10.6% vs 24% at 500k), so expect a smaller
percentage there (roughly −6-9%); still worth it for a one-time rebuild if disk footprint matters.

## 5. Allocation-free result iteration (`GetValuesInto`) — ⚠️ MARGINAL

The pipeline drains a result `RoaringBitmap` through layered `IEnumerable<int>` (`RoaringBitmap.
GetValues` → `Container.GetValues`, both `yield`) into a `List<int>`. Added a bulk
`RoaringBitmap.GetValuesInto(int[])` (tight loops, no yield) and hooked `PostingIntersector.
DrainStarted` to use it when the terminal iterator is a `RoaringBitmapIterator` (env toggle
`FTS_NOBULK=1` forces the legacy path for A/B).

**Microbench (`FtsBench.exe iter`):** bulk is **5–15× faster** than `yield`→`List` and does **0
Gen0 GCs** (vs ~1 per materialization). Pre-sizing the `List` barely helped (~1.1×) → the cost is the
`yield` state machines + virtual `MoveNext`, not `List` growth — the article's "abstraction overhead",
confirmed.

**End-to-end (`SearchIds` A/B, full index, min-of-5):**

| query | ids | yield | bulk | Δ |
|---|---|---|---|---|
| `בני*` (cheap prefix expansion) | 386k | 71 ms | 63 ms | **−11%** |
| `*ישראל` | 504k | 444 ms | 446 ms | noise |
| `*כי*` | 2.39M | 1052 ms | 1054 ms | noise |
| `תורה מצוה` (AND control) | 21k | 20 ms | 20 ms | — (path not taken) |

**Verdict:** the 5–15× is real but *isolated*; materialization is a small slice of total query time
(term-expansion + union-build dominate), so end-to-end it's **~11% only when expansion is cheap
(prefix wildcards), noise otherwise**. The durable benefit is **eliminating per-query enumerator
allocations** (smoother GC under sustained querying). Zero-risk, never slower — a cheap polish, not a
headline. Left on the branch, env-toggled, pending a keep/drop-toggle decision.

## 6. Front-coded term dictionary — ✅ PROTOTYPED, the biggest remaining lever

The SQLite `.db` term_index is **~176 MB at full ≈ 24% of the whole index** — larger than any `.dat`
saving. It stores every term twice (table + `UNIQUE INDEX`) and can't exploit that Hebrew terms share
**82.3% of their prefix bytes**. Prototype (`FtsLibTest.exe termdict full`,
`FtsLibTest/SearchIndex/TermDictProto.cs`): a **front-coded block dictionary** — 16-term blocks, head
term stored full, the rest as (shared-prefix-len, suffix); the 5 outputs per term with the monotonic
`.dat` offsets **delta-coded**. Measured on the real largest full segment (`seg_2_20`, 1.19M terms) vs
its 64.8 MB SQLite `.db`:

| metric | SQLite `.db` | front-coded | result |
|---|---|---|---|
| **size** | 64.8 MB | **12.3 MB** | **5.3× smaller (18.9%)** |
| **exact lookup** (20k random terms) | 10,884/s | 596,358/s | **54.8×** ✓ outputs byte-identical |
| **prefix scan** (1k 3-char anchors, both materialize terms) | 2,442/s | 27,820/s | **11.4×** ✓ counts match |

Extrapolated to the full `.db` (176 MB → ~33 MB, **~−143 MB**), total index drops from ~765 MB (v1) to
~581 MB — **≈ −24%**. This *beats* the `fstsize` FST estimate (25.7%) because of the offset delta-coding,
and a front-coded dict is **far simpler than a real FST** — the pragmatic choice. Being disk-backed /
mmap-able it stays off the process heap (aligns with the earlier veto on an *in-memory* term dict).

**Scope / cost (why it's not shipped):**
- Accelerates **exact + prefix** lookups (and **suffix** if a reversed dict is added). It does **not**
  help the **infix** `*כי*` pathology — substring matching needs an n-gram index, a separate structure.
- Shipping is a **large project**, not a patch: the `.db` is queried in `IndexReader.LookupTerm` (exact)
  **and all four expanders** (`HebrewWildcardExpander`, `GrammarExpander`, `FuzzyExpander`'s trigram
  prefilter, range scans). All of that moves from SQLite to the front-coded structure, plus
  `SegmentWriter`/`SegmentReader` format work.

## What was implemented on this branch

**Shipped, no format change (safe to merge as-is):**
- `FtsLib/Search/PostingStream.cs` — inline varint write (removed the per-posting
  `Action<byte>` delegate + dead `WriteByte`).
- `FtsLib/Search/RoaringBitmap.cs` — `BitmapContainer.CountBits` → Harley-Seal SIMD popcount.

**Prototype, breaking format change (needs a version bump + rebuild before shipping):**
- `FtsLib/Search/PostingStream.cs` + `PostingIterator.cs` — no-offset postings.
- `FtsLib/Indexing/SegmentWriter.cs` + `SegmentReader.cs` + `SegmentMerger.cs` +
  `FtsLib/Search/VarInt.cs` — varint term header.

**Marginal polish (env-toggled `FTS_NOBULK`):**
- `FtsLib/Search/RoaringBitmap.cs` `GetValuesInto` + `PostingIntersector.cs` `DrainStarted` bulk path.

**Prototype only, NOT wired into production (bench/diagnostic):**
- `FtsLibTest/SearchIndex/TermDictProto.cs` — front-coded term dictionary vs SQLite.
- `FtsBench/` — the whole isolated net48 measurement harness.

All compile-verified (production `FtsLib.dll` + `FtsLibTest.exe` via VS MSBuild),
correctness-verified, and end-to-end result-identity confirmed at 500k **and full** tier.

## Recommended next steps (by ROI)
1. **Merge the two safe wins** (inline encode + Harley-Seal popcount) — no format change, pure upside.
2. **The front-coded term dictionary is the top lever** — ~−24% total index size + 10–55× faster
   term/prefix lookup. It's a multi-week project (replace SQLite `.db` in the reader + all four
   expanders), so scope it in phases. Does not help infix `*כי*` (that needs a separate n-gram index).
3. **Decide on format v2** (−18% `.dat` @500k, −7% @full, results identical). If yes, add a
   format-version stamp + old-index rejection/auto-rebuild. One-time rebuild cost on end-user machines.
4. **Keep-or-drop** the allocation-free iteration (marginal; zero-risk).
5. **Test on a non-AVX2 machine** to confirm the `Vector<T>` fallback (128-bit / scalar) still helps.

## Reproduce
```
cd KitveiHakodesh/CSharpBackend/Ftslib-Csharp/FtsBench
dotnet build -c Release
bin/Release/net48/FtsBench.exe env       # runtime / SIMD probe
bin/Release/net48/FtsBench.exe dist      # real-data distribution + codec size estimates
bin/Release/net48/FtsBench.exe codec     # encode/decode throughput + correctness
bin/Release/net48/FtsBench.exe bitmap    # popcount / OR / OrWith + correctness
bin/Release/net48/FtsBench.exe roundtrip # format-v2 codec self-consistency
bin/Release/net48/FtsBench.exe iter      # result-iteration (yield vs bulk)

# classic project (VS MSBuild), needs the seforim DB + a built index:
FtsLibTest.exe termdict full             # front-coded term dict vs SQLite .db
```
