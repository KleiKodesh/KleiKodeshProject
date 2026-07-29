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
├── Program.cs           ← Test runner entry point
├── DbQuery.cs           ← Database query helpers
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
| `WildcardDiag.cs` | Wildcard expansion diagnostics |
| `FstSizeDiag.cs` | FST size analysis |
| `KetivExpanderTest.cs` | Ketiv/qere expansion |
| `KetivQueryTest.cs` | Ketiv in queries |
| `WordDistanceTest.cs` | Proximity filtering |
| `OrderedSearchTest.cs` | Ordered matching tests |
| `SnippetTest.cs` | Snippet generation |
| `PerformanceTest.cs` | Performance benchmarks |
| `SpeedTest.cs` | Speed measurements |
| `MonitorTest.cs` | Resource monitoring |
| `VerifyTest.cs` | Index verification |
| `ExpandDiag.cs` | Expansion diagnostics |
| `FileLoadDiag.cs` | File loading diagnostics |
| `LookupDiag.cs` | Dictionary lookup diagnostics |
| `PrefixLenDiag.cs` | Prefix length analysis |
| `ProbeSearch.cs` | Search probing tool |
| `SnippetDiag.cs` | Snippet diagnostics |

### Shared/

Test utilities:

| File | Purpose |
|---|---|
| `TestHelpers.cs` | Common test utilities |
| `HtmlReport.cs` | Generate HTML test reports |
| `DiffIds.cs` | Compare document ID lists |

## Running Tests

### From Visual Studio

1. Set `FtsLibTest` as startup project
2. Run with F5 or Ctrl+F5

### From Command Line

```powershell
cd FtsLibTest
.\bin\Release\FtsLibTest.exe
```

### Individual Test Selection

Modify `Program.cs` to run specific tests:
```csharp
// Uncomment the test you want to run
// BuildFreshTest.Run();
// SearchTest.Run();
PerformanceTest.Run();
```

## Test Data

Tests expect:
- SQLite database at a configured path
- Index directory for test indexes
- Reference results for regression testing

Configure paths in `Program.cs` or `App.config`.

## Diagnostic Tools

Many `*Diag.cs` files are diagnostic tools rather than automated tests:
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

## Configuration

`App.config` contains:
- Database connection strings
- Index paths
- Test parameters

Example:
```xml
<appSettings>
    <add key="TestDbPath" value="C:\\TestData\\seforim.db" />
    <add key="TestIndexPath" value="C:\\TestData\\index" />
</appSettings>
```
