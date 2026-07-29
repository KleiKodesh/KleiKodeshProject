# FtsLib/

Core C# full-text search library.

## Overview

Production-ready implementation of the FtsLib search engine. Provides fast full-text search over Hebrew/Aramaic seforim databases.

## Structure

```
FtsLib/
├── Indexing/        ← Segment-based index construction
├── Search/          ← Query parsing & execution
├── SeforimDb/       ← Public API (SeforimIndex)
├── Snippets/        ← Snippet generation
├── Tokenization/    ← HTML text tokenization
├── FtsLib.csproj    ← Project file
└── packages.config  ← NuGet dependencies
```

## Modules

### Indexing/
LSM-tree style index building:
- **IndexWriter** — Main API for building indexes
- **SegmentStore** — Segment file lifecycle management
- **SegmentWriter** — Write posting lists to disk (delta+varint)
- **SegmentReader** — Read posting lists from segments
- **SegmentMerger** — Background segment compaction

### Search/
Query execution engine:
- **QueryParser** — Parse query syntax (AND, OR, wildcards, fuzzy)
- **IndexReader** — Read term dictionaries and posting lists
- **PostingIntersector** — Fast AND intersection with skip lists
- **HebrewWildcardExpander** — Hebrew wildcard pattern expansion
- **FuzzyExpander** — Levenshtein distance term expansion
- **RoaringBitmap** — Compressed bitmap for document sets

### SeforimDb/
Public API facade — see `SeforimDb/README.md` for detailed API documentation.

Key classes:
- **SeforimIndex** — Main entry point (build, search, snippet)
- **SearchResult** — Search result with content
- **SnippetResult** — Highlighted snippet

### Snippets/
- **SnippetBuilder** — Generate highlighted snippets
- **SnippetResult** — Snippet data

### Tokenization/
- **HtmlWordScanner** — Scan words from HTML
- **TokenStream** — Token iterator
- **Tokenizer** — Main tokenization API

## Dependencies

From `packages.config`:
- `System.Data.SQLite` — SQLite database access
- `System.Data.SQLite.Core` — SQLite native bindings
- `System.Data.SQLite.Linq` — LINQ support

## Building

```powershell
msbuild FtsLib.csproj /p:Configuration=Release
```

## Usage

See parent folder `README.md` for quick start examples, or `SeforimDb/README.md` for full API reference.
