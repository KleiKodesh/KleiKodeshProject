# FtsLib

## Repository setup

This folder exists in two git repos simultaneously:

- **KleiKodeshProject** — the main app repo. Committing from the workspace root includes these files.
- **FtsLib** (`github.com/KleiKodesh/FtsLib`) — a standalone repo for this library. The folder has its own `.git` pointing here.

When you change files in this folder, push to both repos separately:

1. From the workspace root — commits to KleiKodeshProject
2. From inside this folder (`CSharpBackend/Ftslib-Csharp`) — commits to the FtsLib repo

Both repos share the same local files. There is no automatic sync between them.

---

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
IEnumerable<SearchResult> results = index.Search(query, cap: 0, ct: ct);
IEnumerable<int>          ids     = index.SearchIds(query, ct: ct);
```

`Search` streams results lazily — index scan + DB fetch together. `SearchIds` skips the DB fetch; use it when you only need counts or will load content on demand.

**Filtering by line ID** — both methods accept an optional `filterIds` keep-set:

```csharp
// Only lines of one sefer / category can match:
var results = index.Search(query, filterIds: myLineIds);
var ids     = index.SearchIds(query, filterIds: myLineIds);
```

Semantics: `null` = search everything (default); an empty collection matches
nothing; duplicates and unknown IDs are fine (negative IDs are ignored).

The filter is applied *inside* the posting intersection, not on the output —
a small filter set becomes the driver of the intersection (candidate-driven
probing, same idea as Lucene's cost-sorted `FILTER` clause), so heavy
wildcard/fuzzy queries get **faster** when filtered, never slower than
post-filtering the results yourself. Verified with `FtsLibTest.exe filtertest full`:
`בני*` drops 343 ms → 62 ms with a ~1k-ID filter; `*כי* ביצחק` 6.7 s → 4.1 s
(the remainder is term expansion, which no filter can skip).

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

## Building

```powershell
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
& $msbuild FtsLib.slnx /p:Configuration=Release /v:minimal
```

`dotnet build` does not work — this solution uses classic `.csproj` format with WPF.
