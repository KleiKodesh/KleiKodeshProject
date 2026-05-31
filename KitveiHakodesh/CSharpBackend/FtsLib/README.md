# FtsLib

A full-text search library for Hebrew/Aramaic seforim databases, implemented in both C# and Dart.

## Overview

FtsLib provides fast full-text search over large Hebrew/Aramaic text databases (~5.4M lines, SQLite). It answers the core question: **which lines contain all the search terms?**

Built on a custom LSM-style segment index with delta+varint compressed posting lists and skip-list accelerated intersection.

## Repository Structure

```
FtsLib/
├── Ftslib-Csharp/     ← C# implementation (.NET Framework, WPF)
│   ├── FtsLib/        ← Core library
│   ├── FtsLibDemo/    ← WPF demo application
│   └── FtsLibTest/    ← Test suite & diagnostics
│
└── FtsLib-Dart/       ← Dart implementation (Flutter)
    ├── FtsDartLib/    ← Dart library (AOT-compilable)
    └── FtsDartLibFlutterDemo/  ← Flutter demo app
```

## Quick Start

### C#
```csharp
var index = new SeforimIndex(indexPath, dbPath);

// Build once (~17 min for full DB)
index.BuildIndex(onProgress: n => Console.WriteLine($"{n} lines indexed"));

// Search
foreach (var result in index.Search("שלום תורה"))
    Console.WriteLine($"{result.BookTitle}: {result.Content}");

// Snippet with highlighting
var snippet = index.GenerateSnippet(result);
if (snippet.IsMatch)
    Console.WriteLine(snippet.Html);
```

### Dart
```dart
final index = SeforimIndex(indexPath, dbPath);

// Build
await index.buildIndex(onProgress: (n) => print('$n lines indexed'));

// Search
for (final result in index.search("שלום תורה")) {
    final snippet = index.generateSnippet(result);
    if (snippet.isMatch) print(snippet.html);
}
```

## Query Syntax

| Token | Meaning |
|---|---|
| `word` | Literal AND term |
| `word*` | Wildcard — prefix, infix, or suffix |
| `wor?d` | Optional char — the char before `?` is optional |
| `word~` | Fuzzy — edit distance 1 |
| `word~2` | Fuzzy — edit distance 2 |
| `word~3` | Fuzzy — edit distance 3 (max) |
| `a \| b` | OR — lines matching `a` OR `b` satisfy this AND slot |

Multiple tokens are AND-ed. `|`-separated tokens are OR-ed within one AND slot.

## Features

- **Full-text indexing** — LSM-style segment-based index with background merging
- **Compressed storage** — Delta+varint encoded posting lists
- **Fast intersection** — Skip-list accelerated AND queries
- **Hebrew/Aramaic aware** — Handles RTL text, ketiv/qere variants
- **Wildcard search** — Prefix, infix, suffix wildcards (`*`)
- **Fuzzy matching** — Levenshtein distance up to 3
- **OR queries** — Multiple alternatives per AND slot
- **Snippet generation** — Highlighted excerpts with proximity scoring
- **Crash-safe** — WAL-based recovery

## Documentation

- `Ftslib-Csharp/` — C# implementation details
- `Ftslib-Csharp/FtsLib/SeforimDb/` — Public API documentation
- `FtsLib-Dart/FtsDartLibFlutterDemo/` — Flutter demo guide

## Building

### C# (requires MSBuild, not dotnet CLI)
```powershell
$msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
& $msbuild Ftslib-Csharp/FtsLib.slnx /p:Configuration=Release
```

### Dart
```bash
cd FtsLib-Dart/FtsDartLib
dart pub get
```

## Repository Setup

This folder exists in two git repos simultaneously:
- **KleiKodeshProject** — main app repo (commit from workspace root)
- **FtsLib** (`github.com/KleiKodesh/FtsLib`) — standalone library repo (commit from this folder)

Push to both repos separately when making changes.
