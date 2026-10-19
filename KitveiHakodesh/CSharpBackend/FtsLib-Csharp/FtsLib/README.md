# FtsLib/

Full-text search over Hebrew/Aramaic seforim, on SQLite FTS5.

## Structure

```
FtsLib/
├── Fts5/            ← The engine: Fts5SeforimIndex (public entry), Fts5Index (build),
│                      Fts5SearchEngine (search), term dictionaries + FM-index sidecar
├── Indexing/        ← FtsLog, IndexWriteLock, BackgroundPriorityScope, SegmentHandle
│                      (read handle over a term dictionary: terms.db + terms.dat)
├── Search/          ← QueryParser, expanders (wildcard, fuzzy, grammar, ketiv),
│                      TrigramIndex sidecar, Levenshtein
├── SeforimDb/       ← SearchResult, SnippetDescriptorPipeline
├── Snippets/        ← Snippet window location (offsets, no HTML)
├── Tokenization/    ← HTML text tokenization
└── IFtsCorpus*.cs   ← The corpus seam: the caller supplies the documents
```

The FTS5 engine is net10.0 only (`#if NET6_0_OR_GREATER`); the net48 leg builds the shared
helpers only.

The old custom segment engine (SeforimIndex, IndexWriter, segment store/merger, posting
iterators, RoaringBitmap) was removed once the app moved to FTS5 — see git history.

## Dependencies

- `Microsoft.Data.Sqlite` (10.0.9) + `SQLitePCLRaw.lib.e_sqlite3`

## Building

```
dotnet build FtsLib.csproj
```
