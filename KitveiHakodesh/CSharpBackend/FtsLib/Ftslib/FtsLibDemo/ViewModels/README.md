# ViewModels/

MVVM ViewModels for the WPF demo application.

## Overview

Implements the ViewModel layer of the MVVM pattern. ViewModels expose data and commands that the View (XAML) binds to.

## Files

| File | Class | Purpose |
|---|---|---|
| `MainViewModel.cs` | `MainViewModel` | Main window logic |
| `RelayCommand.cs` | `RelayCommand` | Synchronous command |
| `AsyncRelayCommand.cs` | `AsyncRelayCommand` | Asynchronous command |
| `ViewModelBase.cs` | `ViewModelBase` | Base with INotifyPropertyChanged |
| `SearchResultItem.cs` | `SearchResultItem` | Result for binding |

## MainViewModel

The primary ViewModel coordinating all UI operations.

### Properties

| Property | Type | Description |
|---|---|---|
| `Query` | `string` | Search query text |
| `Results` | `ObservableCollection<SearchResultItem>` | Search results |
| `IsSearching` | `bool` | Search in progress |
| `IsIndexing` | `bool` | Index build in progress |
| `IndexProgress` | `double` | Build progress (0-100) |
| `IndexProgressText` | `string` | Status text |
| `MaxWordDistance` | `int` | Filter setting |
| `OrderedMatching` | `bool` | Search option |

### Commands

| Command | Action |
|---|---|
| `SearchCommand` | Execute search |
| `BuildIndexCommand` | Start index build |
| `CancelIndexCommand` | Cancel build |
| `BrowseDbCommand` | Select DB file |
| `ShowHelpCommand` | Show syntax help |

### Methods

**Search**
```csharp
async Task SearchAsync()
```
- Validates query
- Calls ISearchService
- Streams results to Results collection

**BuildIndex**
```csharp
async Task BuildIndexAsync()
```
- Validates paths
- Calls IIndexService with progress callbacks
- Updates progress properties

## RelayCommand

Standard ICommand implementation:
```csharp
public RelayCommand(Action execute, Func<bool> canExecute = null)
```

## AsyncRelayCommand

Async ICommand implementation:
```csharp
public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute = null)
```

Handles busy state automatically.

## ViewModelBase

Base class providing:
- `INotifyPropertyChanged` implementation
- `SetProperty()` helper for change notification

```csharp
public class ViewModelBase : INotifyPropertyChanged
{
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null);
}
```

## SearchResultItem

Bindable result item:
```csharp
public class SearchResultItem
{
    public int LineId { get; set; }
    public string BookTitle { get; set; }
    public string SnippetHtml { get; set; }
    public int WordDistance { get; set; }
}
```
