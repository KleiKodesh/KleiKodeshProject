# FtsLib Project Guidelines

## What this project is
A full-text search library for a Hebrew/Aramaic seforim database (SQLite, ~5.4M lines). The search engine answers one question: **which lines contain all the search terms?** (AND semantics). Nothing more.

---

## Project structure

```
FtsLib/                         ← the library
  Codec/
    PostingCodec.cs             ← delta+varint codec (read path only — see note below)
    PostingStream.cs            ← per-term compressed byte buffer
  Index/
    RamIndex.cs                 ← Dictionary<term, Entry> + PostingIterator with skip list
    IndexManager.cs             ← public API: Add(), Search()
  DbManger.cs                   ← opens SQLite DB, streams rows
  StopWords.cs                  ← Hebrew/Aramaic stop word filter
  Tokenizer.cs                  ← HTML-aware, nikud-stripping tokenizer

FtsLibTest/                     ← test + benchmark console app
  TokenizerTests.cs             ← tokenizer unit tests
  SkipListTest.cs               ← skip list correctness tests (run before full DB)
  LiveDbTest.cs                 ← original 100k-line test (DO NOT MODIFY)
  QuickTest.cs                  ← 500k-line dev test (~1 min)
  FullDbTest.cs                 ← full 5.4M-line benchmark (~17 min)
  Program.cs                    ← entry point
```

---

## How the index works (current implementation)

### Write path
1. `IndexManager.Add(term, lineId)` — called once per term per line, IDs must be **strictly ascending** (enforced by `PostingStream`)
2. `RamIndex` maps each term to an `Entry` object containing:
   - A `PostingStream` — a raw `byte[]` holding delta+varint compressed line IDs
   - A skip list — `int[]` of `(docId, byteOffset, prevEncodedValue)` triples, one entry every **128 doc IDs**
3. `PostingStream.Add(lineId)` encodes `(lineId - prevLineId)` as a varint and appends to the buffer
4. The skip list entry is written **before** each 128th entry, storing:
   - `Skip[n]`   = the docId of the skip target
   - `Skip[n+1]` = byte offset in the buffer where that entry's varint starts
   - `Skip[n+2]` = the **previous** entry's encoded value (needed to resume delta decoding after a jump)

### Read / Search path
1. `IndexManager.Search(terms)` sorts terms by frequency ascending (rarest first)
2. `MergeIntersect` creates one `PostingIterator` per term
3. The rarest list drives the outer loop; for each candidate ID, `SkipTo(target)` is called on all other iterators
4. `SkipTo` binary-searches the skip table to find the largest skip entry with `docId < target`, jumps the byte cursor there, then linear-scans at most 127 entries
5. Results are yielded lazily — **zero heap allocation** during search

### Codec invariant — CRITICAL
The delta codec requires doc IDs to be added in **strictly ascending order**. The SQL query in `FullDbTest.BuildIndex` uses `ORDER BY id` for this reason. **Never remove `ORDER BY id`** — without it, IDs come back in arbitrary order, deltas become negative (wrap as huge unsigned values), and the iterator reads garbage, producing millions of false results.

### Skip list invariant — CRITICAL
The skip entry stores `prevEncodedValue` = `Stream.LastEncoded` at the time the skip is written (i.e., the encoded value of the entry **before** the skip target). In `SkipTo`, after jumping to `bestOffset`:
```
_encoded = bestPrevEncoded;   // restore state to "just before" the skip entry
_encoded += ReadVarInt();     // read the delta → lands exactly on the skip entry's docId
```
If you change the skip write logic, you must update `SkipTo` to match, and vice versa. The `SkipListTest` suite must pass before any full DB run.

---

## Tokenizer behaviour

- **Hebrew letters**: U+05D0–U+05EA (alef–tav), kept as-is including final forms (ך ם ן ף ץ)
- **Nikud** (U+05B0–U+05C7): stripped silently — `שָׁלוֹם` → `שלום`
- **English**: lowercased, a–z only
- **HTML tags**: block-level tags (`<p>`, `<div>`, `<br>`, `<h1>`–`<h6>`, `<li>`, `<tr>`, `<td>`, etc.) act as word separators; inline tags (`<b>`, `<i>`, `<span>`, etc.) are invisible
- **HTML entities**: whitespace entities (`&nbsp;`, `&ensp;`, `&emsp;`) act as word separators; all others (`&amp;`, `&shy;`, etc.) are invisible
- The tokenizer **reuses internal buffers** (`_terms`, `_buffer`, `_tagName`) — it is not thread-safe and must not be shared across threads

---

## Stop words
`StopWords.cs` contains common Hebrew/Aramaic words filtered at index time. Currently **not applied** — `IndexManager.Add` does not call `StopWords.IsStopWord`. The class exists for future use. Do not silently re-enable stop word filtering without updating search to handle the case where a query term is a stop word (it won't be in the index).

---

## Core design rules

- **Keep the codec**. `PostingCodec` + `PostingStream` are intentional — delta+varint compression keeps the index small. Never replace with `HashSet<int>` or `List<int>`.
- **Keep it simple**. AND-intersection only. No ranking, scoring, phrase search, or prefix matching.
- **Single-threaded indexing**. The codec requires ascending IDs. Parallel indexing requires partitioning by ID range and merging in order — do not attempt without explicit agreement.
- **No over-engineering**. No arenas, memory-mapped structures, or custom allocators unless asked.
- **`LiveDbTest.cs` is read-only**. Never modify it.
- **Tests live in `TokenizerTests.cs` and `SkipListTest.cs`**. Do not add tests to `Program.cs`.
- **Run `SkipListTest` before any full DB run** — it catches skip list bugs in seconds rather than 17 minutes.

---

## DB schema (key tables)
- `line(id, bookId, lineIndex, content, heRef)` — one row per line of text; `id` is the doc ID used in the index
- `book(id, title)` — book metadata
- Search results are fetched back from DB by matched line IDs after the index search

## Running tests
```
QuickTest.Run()        // 500k lines, ~1 min — use for development
FullDbTest.Run()       // all 5.4M lines, ~17 min
FullDbTest.Run(N)      // first N lines
```
