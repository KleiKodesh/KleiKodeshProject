# FtsLib Project Guidelines

## What this project is
A full-text search library for a Hebrew/Aramaic seforim database (SQLite, ~5.4M lines). The search engine answers one question: **which lines contain all the search terms?** (AND semantics). Nothing more.

## Project structure
- `FtsLib/` — the library
  - `Tokenizer.cs` — extracts terms from HTML/Hebrew/English text (nikud removal, HTML tag/entity handling)
  - `Codec/PostingCodec.cs` — delta + varint compression for posting lists (do not delete or rewrite)
  - `Codec/PostingStream.cs` — holds one term's compressed posting list (do not delete or rewrite)
  - `Index/RamIndex.cs` — `Dictionary<string, PostingStream>`, maps term → compressed list of line IDs
  - `Index/IndexManager.cs` — public API: `Add(term, lineId)` and `Search(terms) → lineIds`
  - `DbManger.cs` — opens the SQLite DB and streams rows
- `FtsLibTest/` — test + benchmark console app
  - `TokenizerTests.cs` — unit tests for the tokenizer (call `TokenizerTests.RunAll()` to run)
  - `LiveDbTest.cs` — original 100k-line live test (do not modify)
  - `FullDbTest.cs` — full DB benchmark: indexes all lines, searches, renders HTML report
  - `Program.cs` — entry point, calls `FullDbTest.Run()`

## Core design rules
- **Keep the codec**. `PostingCodec` + `PostingStream` are intentional — delta+varint compression keeps the index small. Never delete or replace them with `HashSet<int>` or similar.
- **Keep it simple**. The search logic is AND-intersection of posting lists. No ranking, no scoring, no phrase search.
- **Single-threaded indexing**. Parallel indexing breaks the ascending-ID requirement of the codec. Do not introduce parallel workers unless the merge strategy is explicitly agreed on.
- **No over-engineering**. Do not introduce arenas, memory-mapped structures, or custom allocators without being asked.
- **Tokenizer is allocation-aware**. It reuses internal buffers (`_terms`, `_buffer`, `_tagName`). Do not add per-call allocations.
- **Tests live in `TokenizerTests.cs`**. Do not add tests to `Program.cs`. Do not auto-add tests unless asked.
- **`LiveDbTest.cs` is read-only**. Never modify it.

## Codec invariants — do not break these

### What the codec indexes
The codec stores **line IDs only** (`line.id`). Book IDs (`book.id`) are never stored in the index — they only appear when fetching result rows back from the DB after search. So the negative-ID fix below applies to line IDs; book IDs are unaffected.

### Negative and full int-range line IDs are supported
`PostingCodec` maps the full `int` range to `uint` before encoding using:
```csharp
Encode(value) = (uint)(value - (long)int.MinValue)
Decode(value) = (int)(value  + (long)int.MinValue)
```
This is a bijection — every distinct `int` maps to a distinct `uint`, preserving sort order. Do not remove or simplify this mapping. Do not revert `WriteVarInt`/`ReadVarInt` to operate on `int` — they must operate on `uint`.

### Entry IDs must arrive in ascending order
`PostingStream.Add` enforces strictly ascending order and throws `ArgumentException` if violated. Do not remove this guard.

The indexing query in `FullDbTest.BuildIndex` uses `ORDER BY id` to guarantee this at the DB level:
```csharp
cmd.CommandText = "SELECT id, content FROM line ORDER BY id";
```
Do not remove the `ORDER BY id`. Without it, SQLite may return rows in any order, causing out-of-order IDs to reach the codec, which would corrupt the posting lists silently.

## DB schema (key tables)
- `line(id, bookId, lineIndex, content, heRef)` — one row per line of text
- `book(id, title)` — book metadata
- Search results are fetched back from DB by matched line IDs after the index search.
