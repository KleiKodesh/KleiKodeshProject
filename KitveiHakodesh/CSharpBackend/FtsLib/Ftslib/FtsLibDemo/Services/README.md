# Services/

Business logic services for the WPF demo.

## Overview

Services encapsulate business logic and external dependencies, following the dependency inversion principle.

## Files

| File | Interface | Implementation | Purpose |
|---|---|---|---|
| `ISearchService.cs` | `ISearchService` | — | Search contract |
| `SearchService.cs` | — | `SearchService` | Execute searches |
| `IIndexService.cs` | `IIndexService` | — | Index build contract |
| `IndexService.cs` | — | `IndexService` | Build indexes |
| `IDbService.cs` | `IDbService` | — | DB access contract |
| `DbService.cs` | — | `DbService` | Database operations |
| `ISettingsService.cs` | `ISettingsService` | — | Settings contract |
| `SettingsService.cs` | — | `SettingsService` | Persist settings |
| `IResultsHtmlService.cs` | `IResultsHtmlService` | — | HTML formatting contract |
| `ResultsHtmlService.cs` | — | `ResultsHtmlService` | Format results as HTML |

## Service Descriptions

### ISearchService / SearchService

Executes full-text searches:

```csharp
interface ISearchService
{
    Task<IEnumerable<SearchResult>> SearchAsync(
        string indexPath,
        string query,
        int maxWordDistance,
        bool ordered,
        CancellationToken ct);
}
```

Features:
- Opens existing index
- Parses query
- Streams results lazily
- Applies word distance filter

### IIndexService / IndexService

Builds search indexes:

```csharp
interface IIndexService
{
    Task BuildIndexAsync(
        string dbPath,
        string indexPath,
        Action<int> onProgress,
        CancellationToken ct);
}
```

Features:
- Creates SeforimIndex
- Builds from SQLite DB
- Reports progress
- Supports cancellation

### IDbService / DbService

Database access:

```csharp
interface IDbService
{
    Task<bool> ValidateDbAsync(string dbPath);
    Task<long> CountLinesAsync(string dbPath);
}
```

### ISettingsService / SettingsService

Persists user preferences:

```csharp
interface ISettingsService
{
    Task<string> LoadLastDbPathAsync();
    Task SaveLastDbPathAsync(string path);
}
```

Stores in `%AppData%` or registry.

### IResultsHtmlService / ResultsHtmlService

Formats results as HTML for display:

```csharp
interface IResultsHtmlService
{
    string FormatResults(IEnumerable<SearchResultItem> results);
}
```

Wraps snippets in styled HTML container.

## Dependency Injection

Services are injected into ViewModels via constructor:

```csharp
public MainViewModel(
    ISearchService searchService,
    IIndexService indexService,
    ISettingsService settingsService,
    ...)
```

This enables:
- Unit testing with mocks
- Swapping implementations
- Clear dependency graph
