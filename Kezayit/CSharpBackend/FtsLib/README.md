# FtsLib

A full-text search library for a Hebrew/Aramaic seforim database (~5.4M lines, SQLite). Answers one question: **which lines contain all the search terms?**

Built on a custom LSM-style segment index with delta+varint compressed posting lists and skip-list accelerated intersection.

---

## Quick start

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

---

## Query syntax

| Token | Meaning |
|---|---|
| `word` | Literal AND term |
| `word*` | Wildcard — prefix, infix, or suffix |
| `wor?d` | Optional char — the char before `?` is optional; matches `word` and `wrd` |
| `word~` | Fuzzy — edit distance 1 |
| `word~2` | Fuzzy — edit distance 2 |
| `word~3` | Fuzzy — edit distance 3 (max) |
| `a \| b` | OR — lines matching `a` OR `b` satisfy this AND slot |

Multiple tokens are AND-ed. `|`-separated tokens are OR-ed within one AND slot. Wildcard/fuzzy tokens are expanded internally before intersection.

---

## API — `SeforimIndex`

### `BuildIndex`

```csharp
index.BuildIndex(limit: 0, onProgress: n => { ... });
```

`limit: 0` = all lines. Blocking. Crash-safe (WAL recovery on restart).

### `Search`

```csharp
IEnumerable<SearchResult> results = index.Search(query, cap: 0, ct);
IEnumerable<int>          ids     = index.SearchIds(query, ct);
```

`Search` streams results lazily — index scan + DB fetch together. `SearchIds` skips the DB fetch; use it when you only need counts or will load content on demand.

**`SearchResult` properties:**

| Property | Type | Description |
|---|---|---|
| `LineId` | `int` | Line ID in the seforim DB |
| `BookTitle` | `string` | Book the line belongs to |
| `Content` | `string` | Raw HTML content of the line |
| `MatchedGroups` | `IReadOnlyList<IReadOnlyCollection<string>>` | Expanded term groups per query token — pass to `GenerateSnippet(result)` |

### `GenerateSnippet`

```csharp
// Preferred — uses pre-computed matched terms (no extra DB fetch)
SnippetResult snippet = index.GenerateSnippet(result);

// Fallback — re-parses query; use when you have a lineId but no SearchResult
SnippetResult snippet = index.GenerateSnippet(lineId, query);
```

**`SnippetResult` properties:**

| Property | Type | Description |
|---|---|---|
| `Html` | `string` | Highlighted HTML with `<mark>` tags around matched terms |
| `Score` | `int` | Character span of the tightest window — smaller is better |
| `WordDistance` | `int` | Token count between leftmost and rightmost matched terms |
| `IsMatch` | `bool` | False = index false positive; filter this result out |

### `CountLines`

```csharp
long total = index.CountLines();
```

Total lines in the DB. Useful for progress percentage during build.

---

## Project structure

```
FtsLib/
  Core/           ← index engine (internal)
  Seforim/        ← public API facade + result types
  Misc/           ← SQLite DB access (ZayitDb)
FtsLibDemo/       ← WPF demo application
```

---

## Building

```powershell
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
& $msbuild FtsLib.slnx /p:Configuration=Release /v:minimal
```

`dotnet build` does not work — this solution uses classic `.csproj` format with WPF.
