# Shared/

Test utilities and helpers.

## Files

| File | Purpose |
|---|---|
| `TestHelpers.cs` | Common test utilities |
| `HtmlReport.cs` | Generate HTML test reports |

## TestHelpers

Provides common functionality for tests:

### Methods

| Method | Purpose |
|---|---|
| `GetTestDbPath()` | Get configured test database path |
| `GetTestIndexPath()` | Get configured test index path |
| `EnsureTestDataExists()` | Validate test data availability |
| `CreateTempIndex()` | Create temporary index directory |
| `CleanUpTempIndex()` | Remove temporary index |
| `TimeOperation()` | Measure operation duration |
| `AssertEqual<T>()` | Type-safe equality assertion |

### Example Usage

```csharp
var dbPath = TestHelpers.GetTestDbPath();
var indexPath = TestHelpers.CreateTempIndex();

try {
    // Run test
    var duration = TestHelpers.TimeOperation(() => {
        index.BuildIndex();
    });
    Console.WriteLine($"Build took {duration}");
}
finally {
    TestHelpers.CleanUpTempIndex(indexPath);
}
```

## HtmlReport

Generates HTML reports from test results:

### Features
- Test result summary
- Performance charts
- Comparison tables
- Export to file

### Usage
```csharp
var report = new HtmlReport();
report.AddSection("Query Performance", queryData);
report.AddChart("Latency Distribution", latencies);
report.Save("results.html");
```

## Conventions

- All helpers are static for easy access
- Clean up resources in `finally` blocks
- Use temp directories for isolated tests
- Report timing for performance-sensitive operations
