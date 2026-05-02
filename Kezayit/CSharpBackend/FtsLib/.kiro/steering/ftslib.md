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

## DB schema (key tables)
- `line(id, bookId, lineIndex, content, heRef)` — one row per line of text
- `book(id, title)` — book metadata
- Search results are fetched back from DB by matched line IDs after the index search.
