# FtsLibTest

Tests and benchmarks for FtsLib (SQLite FTS5 engine). net10.0 console app; the first
argument names the command, an unknown one prints the list.

```
FtsLibTest/
├── SearchIndex/   ← parser, ketiv and word-distance tests
├── Fts5Engine/    ← fts5engine build | resume | live
├── Benchmarks/    ← tokenizer, trigram sidecar, FM-index, fuzzy, FTS5 compare harnesses
├── Corpus/        ← reader for the seforim database used as test corpus (ZayitDb)
├── Shared/        ← TestHelpers, QueryCases
└── Program.cs     ← command dispatch
```

The tests and benchmarks of the old custom segment engine (build/merge/crash tests,
search/perf/snippet batteries) were removed with that engine; see git history.

```
dotnet run -c Release -- fts5engine build [dbPath] [outDir] [limit]
```
