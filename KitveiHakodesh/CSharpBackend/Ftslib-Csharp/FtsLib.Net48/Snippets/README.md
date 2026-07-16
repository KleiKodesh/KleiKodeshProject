# Snippets/

Snippet generation with search term highlighting.

## Overview

Generates highlighted text excerpts showing search results in context. Finds the tightest window containing all query terms and wraps matches in `<mark>` tags.

## Files

| File | Class | Purpose |
|---|---|---|
| `SnippetBuilder.cs` | `SnippetBuilder` | Main snippet generation logic |
| `SnippetResult.cs` | `SnippetResult` | Result data structure |

## Algorithm

1. **Tokenize** the line content using `HtmlWordScanner`
2. **Locate** all occurrences of each search term
3. **Find tightest window** containing at least one occurrence of each term
4. **Extend window** with context (neighboring words)
5. **Render HTML** with `<mark>` tags around matched terms
6. **Calculate scores** — Character span and word distance

## SnippetBuilder

```csharp
var builder = new SnippetBuilder();
var result = builder.Build(
    htmlContent: line.Html,
    queryTerms: matchedGroups,
    lineId: line.LineId
);
```

## SnippetResult

| Property | Type | Description |
|---|---|---|
| `Html` | `string` | Highlighted HTML with `<mark>` tags |
| `Score` | `int` | Character span of tightest window (smaller = better) |
| `IsMatch` | `bool` | False = index false positive |

## Scoring

- **Score** — Number of characters in the tightest window covering all terms. Smaller scores indicate terms are closer together.

- **WordDistance** — Number of tokens between leftmost and rightmost matches (stored in `SearchResult`).

These scores help rank results by relevance.

## HTML Output

Example:
```html
...השלום <mark>בית</mark> של <mark>תורה</mark>...
```

Matched terms are wrapped in `<mark>` elements for easy styling.

## False Positives

`IsMatch` may be `false` when:
- The index contained the term but the document doesn't (rare, due to concurrent modifications)
- All terms were removed by HTML tag stripping

Always check `IsMatch` before displaying:
```csharp
if (snippet.IsMatch)
    Display(snippet.Html);
```

## Context Extension

The builder extends the tightest window by:
- Adding words before the first match
- Adding words after the last match
- Stopping at sentence boundaries when possible

This provides readable context while keeping snippets concise.
