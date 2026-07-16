# Tokenization/

HTML text tokenization for indexing and search.

## Overview

Converts HTML content into searchable word tokens while preserving context for snippet generation.

## Files

| File | Class | Purpose |
|---|---|---|
| `HtmlWordScanner.cs` | `HtmlWordScanner` | Scan words from HTML |
| `TokenStream.cs` | `TokenStream` | Iterator-style token access |
| `Tokenizer.cs` | `Tokenizer` | Main tokenization API |
| `HtmlBlockTags.cs` | — | List of block-level HTML tags |

## HtmlWordScanner

Low-level HTML parser that:
1. Parses HTML character-by-character
2. Skips tags and attributes
3. Extracts text content
4. Identifies word boundaries
5. Handles HTML entities (`&nbsp;`, `&amp;`, etc.)

Features:
- State machine-based parsing
- Minimal memory allocation
- Position tracking for snippets

## TokenStream

Provides iterator-style access to tokens:
```csharp
var stream = new TokenStream(html);
while (stream.MoveNext())
{
    Console.WriteLine(stream.Current.Word);
}
```

Properties:
- `Current` — Current token
- `Position` — Character position in document
- `IsHtmlTag` — Whether token is inside a tag

## Tokenizer

High-level API for common operations:

```csharp
// Get all words
var words = Tokenizer.Tokenize(html);

// With positions
var tokens = Tokenizer.TokenizeWithPositions(html);
```

## HTML Block Tags

`HtmlBlockTags.cs` defines elements that should be treated as word boundaries:
- `<p>`, `<div>`, `<br>` — Paragraph breaks
- `<table>`, `<tr>`, `<td>` — Table elements
- `<h1>` through `<h6>` — Headings

This ensures content like `word1</p><p>word2` isn't concatenated into `word1word2`.

## Hebrew Handling

The tokenizer is Hebrew-aware:
- Handles RTL text direction
- Preserves Hebrew punctuation
- Correctly identifies Hebrew word boundaries

## Usage Example

```csharp
var html = "<p>שלום <b>עולם</b></p>";
var tokens = Tokenizer.TokenizeWithPositions(html);

foreach (var token in tokens)
{
    Console.WriteLine($"{token.Word} at position {token.Position}");
}
// Output:
// שלום at position 3
// עולם at position 9
```
