# Snippets/

Locates the snippet window for a search hit and describes it as OFFSETS. No HTML is
produced on the search path — the consumer (the page) renders from the descriptor.

## Files

| File | Class | Purpose |
|---|---|---|
| `SnippetWindowLocator.cs` | `SnippetWindowLocator` | Finds the tightest window, expands it by context words, records mark ranges |
| `SnippetDescriptor.cs` | `SnippetDescriptor` | The result: window offsets, mark ranges, ellipsis flags, scores |
| `SnippetHtmlRenderer.cs` | `SnippetHtmlRenderer` | Reference rendering of a descriptor to `<mark>` HTML — tests/diagnostics only |
| `PreparedQueryGroups.cs` | `PreparedQueryGroups` | The query's term→group map, built once per query |

## Algorithm

1. **Tokenize** the line content once (`TokenStream`, raw char positions kept)
2. **Find tightest window** containing at least one token of every query group
3. **Expand window** by `contextWords` tokens on each side (safety ceiling in visible chars)
4. **Collect mark ranges** — every query-term token inside the window
5. **Score** — character span and word distance

## SnippetWindowLocator

```csharp
var locator = new SnippetWindowLocator(contextWords: 8);
var prepared = PreparedQueryGroups.FromGroups(result.MatchedGroups);   // once per query
SnippetDescriptor descriptor = locator.Locate(line.Html, prepared);
```

Holds only scratch buffers; zero per-call allocation. One instance per thread.

## SnippetDescriptor

| Property | Type | Description |
|---|---|---|
| `IsMatch` | `bool` | False = index false positive, drop the hit |
| `SnapStart` / `SnapEnd` | `int` | Window as raw char offsets into the source |
| `MarkRanges` | `IReadOnlyList<int>` | Flat (start, length, termOrdinal) triples (`MarkStride` = 3) of highlighted tokens; `MatchedTermAt(ordinal)` names the term; a VIEW of locator scratch, copy before the next `Locate` |
| `LeadingEllipsis` / `TrailingEllipsis` | `bool` | "…" before / after the window |
| `WindowWordCount` | `int` | Words in the window — below `contextWords` means "short", extend over neighbour lines |
| `Score` | `int` | Char span of the tightest window (smaller = better) |
| `WordDistance` | `int` | Tokens between leftmost and rightmost matches |

## Rendering

The page rebuilds the HTML from the source text and the descriptor
(`vue-frontend/.../fullTextSearchSnippets.ts`). `SnippetHtmlRenderer.Render(source, descriptor)`
is the reference implementation the port must match: window with tags and `{X}` paragraph
markers stripped, each mark range wrapped in `<mark>`, "…" on each cut side.

```html
…השלום <mark>בית</mark> של <mark>תורה</mark>…
```
