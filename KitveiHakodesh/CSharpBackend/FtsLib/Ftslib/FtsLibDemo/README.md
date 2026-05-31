# FtsLibDemo

WPF (Windows Presentation Foundation) demo application for FtsLib.

## Overview

A desktop application demonstrating the full-text search capabilities of FtsLib. Mirrors the Flutter demo feature-for-feature but for Windows desktop.

## Features

- **Index Building** — Select SQLite DB and build search index with progress
- **Search** — Live query with streaming results
- **Query Syntax** — Full support for wildcards, fuzzy, OR operators
- **Results** — Highlighted snippets with book titles
- **Settings** — Persist last-used DB path

## Project Structure

```
FtsLibDemo/
├── ViewModels/          ← MVVM pattern
├── Services/            ← Business logic
├── MainWindow.xaml      ← Main UI (XAML)
├── MainWindow.xaml.cs   ← Code-behind
├── Converters.cs        ← Value converters
├── App.xaml             ← App resources
└── [project files]
```

## Architecture

Uses the MVVM (Model-View-ViewModel) pattern:

### ViewModels/
| Class | Purpose |
|---|---|
| `MainViewModel.cs` | Main window logic, search, indexing |
| `RelayCommand.cs` | Command implementation |
| `AsyncRelayCommand.cs` | Async command implementation |
| `ViewModelBase.cs` | Base class with INotifyPropertyChanged |
| `SearchResultItem.cs` | Result item for binding |

### Services/
| Class | Purpose |
|---|---|
| `ISearchService` / `SearchService` | Execute searches |
| `IIndexService` / `IndexService` | Build indexes |
| `IDbService` / `DbService` | Database access |
| `ISettingsService` / `SettingsService` | Persist settings |
| `IResultsHtmlService` / `ResultsHtmlService` | Format results as HTML |

## Dependencies

From `packages.config`:
- `System.Data.SQLite` — Database access
- WPF framework assemblies

## Building

```powershell
msbuild FtsLibDemo.csproj /p:Configuration=Release
```

## Running

```powershell
.\bin\Release\FtsLibDemo.exe
```

Or run from Visual Studio with F5.

## UI Layout

```
┌─────────────────────────────────────────┐
│  Search: [____________________] [Search] │
│  [i] Syntax Help  Max distance: [---]  │
│  [x] Ordered matching                  │
│  ─────────────────────────────────────  │
│  Results (live streaming):              │
│  ┌─────────────────────────────────┐  │
│  │ Book Title                       │  │
│  │ ...<b>matched</b> text...        │  │
│  └─────────────────────────────────┘  │
│  ┌─────────────────────────────────┐  │
│  │ ...                              │  │
│  └─────────────────────────────────┘  │
│  ─────────────────────────────────────  │
│  [Build Index]  Progress: [████████]  │
│  Status: 1,234,567 lines indexed       │
└─────────────────────────────────────────┘
```

## RTL Support

The UI flows right-to-left for Hebrew text:
```xml
FlowDirection="RightToLeft"
```
