# SearchEngine — Lucene-Based Full-Text Search

A .NET class library implementing Lucene-based full-text indexing and search for seforim (Jewish texts).

## Overview

The search engine indexes all line content from the seforim database, building an inverted index that supports:

- Prefix, infix, and suffix wildcards (`word*`, `*word*`, `*word`)
- Fuzzy matching (Levenshtein edit distance)
- Hebrew-specific features: spelling variant expansion (ketiv/chaseir), abbreviation expansion
- Ordered and unordered multi-term queries with configurable word distance limits
- Direct Lucene query syntax for advanced users

## Architecture

The index is stored in a Lucene-format directory on disk. Each indexed document contains:

- `lineId` (stored, indexed): line ID for fetching line content from SQLite at search time
- `bookId` (stored, indexed): book ID for filtering results
- `content` (indexed only): full line text (not stored — fetched from DB on result retrieval)
- `title` (stored): book title from the `book` table
- `toc` (stored): TOC path hierarchy for display in search results
- `bookIds` (stored): comma-separated book IDs for all linked books (for related-books navigation)

## Folder Structure

```
SearchEngine/
├── SearchEngine/
│   ├── IndexBuilder.cs       — Builds index from seforim DB; manages full and incremental rebuilds
│   ├── IndexSearcher.cs      — Executes queries against the index
│   ├── QueryBuilder.cs       — Constructs Lucene query objects from user input
│   └── DocumentMapper.cs     — Maps database rows to Lucene documents
└── SearchEngineTest/
    └── SearchEngineTest.exe  — Console test harness
```

## Integration with KitveiHakodeshLib

`KitveiHakodeshLib/Search/SearchHandler.cs` owns the index lifecycle:

1. On app startup, checks if a valid index exists for the current DB
2. Manages full and incremental index rebuilds (triggered by `ResetFtsIndex` bridge action or automatic detection of DB changes)
3. Streams search results to the frontend in batches (starting at 1 result, doubling up to 16, then switching to 150ms timer flush)
4. Enriches each batch with TOC paths and snippet context via SQL queries

Bridge actions:

| Action                 | Purpose                                           |
| ---------------------- | ------------------------------------------------- |
| `FtsSearchStart`       | Start a search; returns `searchId` for batching   |
| `FtsSearchCancel`      | Cancel an in-progress search                      |
| `GetFtsIndexingProgress` | Poll indexing progress (percentage + status)    |
| `ResetFtsIndex`        | Delete index and trigger full rebuild             |

Push events:

| Event                 | Purpose                                          |
| --------------------- | ------------------------------------------------ |
| `ftsIndexProgress`    | Indexing progress tick (% complete, ETA)        |
| `ftsIndexInvalidated` | Index corrupt or missing; rebuild auto-started  |

## Query Syntax

- Multiple words are AND-ed by default: `word1 word2` matches lines with both
- Wildcards: `word*` (prefix), `*word` (suffix), `*word*` (infix), `word?` (optional character)
- Fuzzy: `word~1` or `word~2` for edit distance matching
- OR: `word1 | word2` matches either
- Expansion: `%word` (prefix expansion), `word%` (suffix), `%word%` (both)
- Spelling variants: `~word` to match Hebrew ketiv/chaseir variants

Frontend wrapping modes (auto-applied to user queries):

- `searchWildcardWrap` — wraps each term with `*...*` for infix search
- `searchGrammarWrap` — wraps each term with `%...%` for grammar expansion

Frontend parameters:

- `searchMaxWordDistance` — max token distance between terms in a line (default 10)
- `searchRequireOrdered` — whether terms must appear in query order

## Index Format

Lucene binary index format. Location: OS-specific temp directory (managed by Lucene internally).

Index is automatically invalidated and rebuilt when:

- The seforim database modification time changes
- The index file is corrupt or missing
- User explicitly requests a rebuild via `ResetFtsIndex`

