# FtsLib

## Repository setup

This folder exists in two git repos simultaneously:

- **KleiKodeshProject** — the main app repo. Committing from the workspace root includes these files.
- **FtsLib** (`github.com/KleiKodesh/FtsLib`) — a standalone repo for this library. The folder has its own `.git` pointing here.

When you change files in this folder, push to both repos separately:

1. From the workspace root — commits to KleiKodeshProject
2. From inside this folder (`CSharpBackend/FtsLib-Csharp`) — commits to the FtsLib repo

Both repos share the same local files. There is no automatic sync between them.

---

A full-text search library for a Hebrew/Aramaic seforim database (~5.4M lines, SQLite). Answers one question: **which lines contain all the search terms?**

Built on SQLite FTS5 (`FtsLib/Fts5/`): a line table indexed by FTS5, plus a term dictionary with an FM-index and trigram sidecar for wildcard and fuzzy expansion. The older custom segment engine (`SeforimIndex`) was removed; see git history.

---

## Quick start

```csharp
var index = new Fts5SeforimIndex(indexPath, () => new MyCorpus(dbPath)); // IFtsCorpus

// Build once
index.BuildIndex(onProgress: n => Console.WriteLine($"{n} lines indexed"));

// Search
foreach (var result in index.Search("שלום תורה"))
    Console.WriteLine($"{result.BookTitle}: {result.Content}");

// Snippet window (offsets only; the consumer renders)
var window = index.LocateSnippetWindow(result);
if (window.IsMatch)
    Console.WriteLine(window.SnapStart + ".." + window.SnapEnd);
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

## API — `Fts5SeforimIndex`

### `BuildIndex`

```csharp
index.BuildIndex(limit: 0, onProgress: n => { ... });
```

`limit: 0` = all lines. Blocking. An interrupted build resumes after the last committed line.

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


**`SearchResult` properties:**

| Property | Type | Description |
|---|---|---|
| `LineId` | `int` | Line ID in the seforim DB |
| `BookTitle` | `string` | Book the line belongs to |
| `Content` | `string` | Raw HTML content of the line |
| `MatchedGroups` | `IReadOnlyList<IReadOnlyCollection<string>>` | Expanded term groups per query token — pass to `LocateSnippetWindow(result)` |

### `LocateSnippetWindow`

Returns a `SnippetDescriptor` — where the snippet is, as offsets into the line text. No HTML
is produced; the consumer renders (see `FtsLib/Snippets/README.md`).

```csharp
// Preferred — uses pre-computed matched terms (no extra DB fetch)
SnippetDescriptor window = index.LocateSnippetWindow(result);
```

**`SnippetDescriptor` properties:**

| Property | Type | Description |
|---|---|---|
| `SnapStart` / `SnapEnd` | `int` | Window as raw char offsets into the source text |
| `MarkRanges` | `IReadOnlyList<int>` | Flat (start, length, termOrdinal) triples to highlight (`MatchedTermAt` names the term); a view of per-thread scratch — copy before the next locate |
| `LeadingEllipsis` / `TrailingEllipsis` | `bool` | "…" before / after the window |
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
dotnet build FtsLib.slnx -c Release
```

Both projects are SDK-style. `FtsLib` builds net48 and net10.0; `FtsLibTest` is a net10.0 console app.
