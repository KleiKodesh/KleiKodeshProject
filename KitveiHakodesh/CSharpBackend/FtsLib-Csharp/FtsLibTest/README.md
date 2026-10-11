# FtsLibTest

Test suite and diagnostics for FtsLib.

## Overview

Contains unit tests, integration tests, and diagnostic tools for validating the search engine. Organized into build tests, search tests, and shared utilities.

## Structure

```
FtsLibTest/
├── BuildIndex/          ← Index building tests
├── SearchIndex/         ← Search and query tests
├── Shared/              ← Test utilities
├── Benchmarks/          ← Micro-benchmarks and the trigram/sidecar harnesses
├── Corpus/              ← Reader for the seforim database used as test corpus
├── Program.cs           ← Test runner entry point (command dispatch)
├── FtsLibTest.csproj    ← net10.0 console app
└── Specs.md             ← Test specifications
```

## Test Categories

### BuildIndex/

Tests for index construction:

| File | Purpose |
|---|---|
| `BuildFreshTest.cs` | Build index from scratch |
| `BuildTest.cs` | Incremental build, merge tests |

### SearchIndex/

Tests for search functionality:

| File | Purpose |
|---|---|
| `SearchTest.cs` | Basic search validation |
| `QueryTest.cs` | Query parsing tests |
| `QueryParserTest.cs` | Parser edge cases |
| `KetivExpanderTest.cs` | Ketiv/qere expansion |
| `KetivQueryTest.cs` | Ketiv in queries |
| `WordDistanceTest.cs` | Proximity filtering |
| `OrderedSearchTest.cs` | Ordered matching tests |
| `SnippetTest.cs` | Snippet generation |
| `PerformanceTest.cs` | Performance benchmarks |
| `SpeedTest.cs` | Speed measurements |
| `MonitorTest.cs` | Resource monitoring |
| `VerifyTest.cs` | Index verification |
| `ProbeSearch.cs` | Search probing tool |
| `SnippetDiag.cs` | Snippet diagnostics |

### Shared/

Test utilities:

| File | Purpose |
|---|---|
| `TestHelpers.cs` | Common test utilities |
| `HtmlReport.cs` | Generate HTML test reports |

## Running Tests

### From Command Line

```powershell
dotnet build FtsLibTest -c Release
.\FtsLibTest\bin\Release\net10.0\FtsLibTest.exe <command> [args]
```

The first argument picks the test (see the `switch` in `Program.cs`). An unknown
command prints the list. Test indexes are written next to the exe
(`bin\Release\net10.0\index_<tier>`).

## Test Data

Tests expect:
- SQLite database at a configured path
- Index directory for test indexes
- Reference results for regression testing

The seforim database path is resolved in `BuildTest.ResolveDbPath()`; index directories in `Shared/TestHelpers.cs`.

## Diagnostic Tools

The `*Diag.cs` files and `Benchmarks/` are diagnostic tools rather than automated tests:
- Output statistics about index structure
- Analyze query expansion behavior
- Measure performance characteristics
- Generate reports for analysis

Run these to investigate issues or understand performance.

## Specs.md

Test specifications and expected behaviors:
- Query syntax requirements
- Performance targets
- Output formats
