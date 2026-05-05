# SeforimIndex — Public API

Full-text search over the seforim SQLite database. Three methods, no internals exposed.

## Setup

```csharp
var index = new SeforimIndex(indexPath, dbPath);
```

Both paths are caller-supplied. `indexPath` is the directory where segment files are stored. `dbPath` is the seforim SQLite database file.

---

## BuildIndex

```csharp
index.BuildIndex(
    limit:      0,               // 0 = all lines; pass e.g. 500_000 for a partial build
    onProgress: n => { ... });   // optional — receives running line count
```

Blocking. Reads every line from the DB, tokenizes it, and writes the index to disk. Roughly 17 minutes for the full 5.4M-line database.

---

## Search

```csharp
// Full search — returns content + book title for each result.
// Use when you need to display or process the line content.
IEnumerable<SearchResult> results = index.Search(query, cap: 0);

// ID-only search — no DB fetch, much faster for large result sets.
// Use when you only need to count results or load content on demand.
IEnumerable<int> ids = index.SearchIds(query);
```

`Search` returns results lazily. `cap: 0` means no limit. Each `SearchResult` has:

| Property | Type | Description |
|---|---|---|
| `LineId` | `int` | Line ID in the seforim DB |
| `BookTitle` | `string` | Book the line belongs to |
| `Content` | `string` | Raw HTML content of the line |
| `MatchedGroups` | `IReadOnlyList<IReadOnlyCollection<string>>` | Per-token expanded term groups (OR within each group). Pass the result to `GenerateSnippet(result)` for accurate highlighting and proximity scoring. |

**Query syntax:**

| Token | Meaning |
|---|---|
| `word` | Literal AND term |
| `word*` | Wildcard — prefix, infix, or suffix |
| `wor?d` | Optional char — the character before `?` is optional; matches both `word` and `wrd` |
| `word~` | Fuzzy — edit distance 1 (default) |
| `word~2` | Fuzzy — edit distance 2 |
| `word~3` | Fuzzy — edit distance 3 (maximum) |
| `a \| b` | OR — lines matching `a` OR `b` satisfy this AND slot |

Multiple tokens are AND-ed. `|`-separated tokens are OR-ed within one AND slot. Wildcard/fuzzy tokens are OR-expanded internally; OR groups merge all their expansions.

**OR (`|`) rules:**
- `|` must appear as a standalone whitespace-delimited token: `word1 | word2`.
- Consecutive pipes chain: `a | b | c word` → `(a OR b OR c) AND word`.
- Each alternative in an OR group can independently be a literal, wildcard, or fuzzy term: `word* | word~` expands both and merges the results.
- A leading or trailing `|`, or `||`, is silently ignored (the missing side is treated as absent).
- Parentheses have no special meaning — grouping is determined solely by `|` placement.

`?` edge cases: a `?` at the start of a token, or immediately after `*` or another `?`, is silently dropped (no preceding letter to make optional). Up to 4 `?` operators per token are supported (2⁴ = 16 variants); patterns with more are rejected.

---

## GenerateSnippet

Two overloads:

```csharp
// Preferred — uses pre-computed matched terms from the search result.
// Correctly highlights expanded forms (e.g. ביצחק when query was יצחק~).
SnippetResult snippet = index.GenerateSnippet(result);

// Fallback — re-parses the query and uses only the base word forms.
// Use when you have a lineId but no SearchResult (e.g. direct lookup).
SnippetResult snippet = index.GenerateSnippet(lineId, query);

if (snippet.IsMatch)
    Console.WriteLine(snippet.Html);   // highlighted HTML, ready to render
```

Each `SnippetResult` has:

| Property | Type | Description |
|---|---|---|
| `Html` | `string` | Highlighted HTML with `<mark>` tags around matched terms |
| `Score` | `int` | Character span of the tightest window covering all terms — smaller is better |
| `IsMatch` | `bool` | False = index false positive, filter this result out |

---

## CountLines

```csharp
long total = index.CountLines();
```

Returns the total number of lines in the database. Useful for computing build progress percentage.

---

## Folder layout

```
Seforim/
  SeforimIndex.cs     ← public facade
  SearchResult.cs     ← public result type
  SnippetResult.cs    ← public snippet result type
  Internal/
    IndexingPipeline.cs   ← build logic
    SearchPipeline.cs     ← query parsing + search execution
    SnippetPipeline.cs    ← snippet generation
```
