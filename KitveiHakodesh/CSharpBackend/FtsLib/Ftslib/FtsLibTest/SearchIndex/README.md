# SearchIndex/

Search and query tests.

## Files

### Core Tests

| File | Purpose |
|---|---|
| `SearchTest.cs` | Basic search functionality |
| `QueryTest.cs` | Query execution tests |
| `QueryParserTest.cs` | Query parsing edge cases |
| `KetivExpanderTest.cs` | Ketiv/qere expansion |
| `KetivQueryTest.cs` | Ketiv in search queries |
| `WordDistanceTest.cs` | Proximity filtering |
| `OrderedSearchTest.cs` | Ordered term matching |
| `SnippetTest.cs` | Snippet generation |

### Performance Tests

| File | Purpose |
|---|---|
| `PerformanceTest.cs` | Comprehensive benchmarks |
| `SpeedTest.cs` | Raw speed measurements |
| `MonitorTest.cs` | Resource usage monitoring |

### Diagnostics

| File | Purpose |
|---|---|
| `WildcardDiag.cs` | Wildcard expansion analysis |
| `FstSizeDiag.cs` | Term dictionary size analysis |
| `ExpandDiag.cs` | Term expansion diagnostics |
| `FileLoadDiag.cs` | Segment loading analysis |
| `LookupDiag.cs` | Dictionary lookup diagnostics |
| `PrefixLenDiag.cs` | Prefix length statistics |
| `ProbeSearch.cs` | Search behavior probing |
| `SnippetDiag.cs` | Snippet generation diagnostics |
| `VerifyTest.cs` | Index integrity verification |

## Test Coverage

### SearchTest
- Simple term search
- Multi-term AND search
- Result count validation
- Content matching

### QueryTest
- Query parsing
- Term extraction
- Boolean logic

### QueryParserTest
- Wildcard parsing (`*`)
- Fuzzy parsing (`~`)
- OR parsing (`|`)
- Escaping and edge cases

### KetivExpanderTest / KetivQueryTest
- Ketiv/qere variant expansion
- Hebrew text handling
- Traditional spelling variants

### WordDistanceTest
- Proximity filtering
- Distance calculation
- Filter effectiveness

### OrderedSearchTest
- Sequential term matching
- Order validation
- Performance with ordering

### SnippetTest
- Snippet generation
- Highlight accuracy
- Score calculation
- False positive detection

### PerformanceTest
- Query latency
- Throughput measurements
- Memory usage
- Scaling characteristics

## Running

Most tests can be run individually:
```csharp
SearchTest.Run();
PerformanceTest.Run();
SnippetTest.Run();
```

Or all together via `Program.cs`.
