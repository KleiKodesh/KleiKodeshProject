# Search/

Query parsing and search execution engine.

## Overview

The search module handles the complete query pipeline from parsing to result streaming.

## Pipeline

```
Query String
      ↓
[QueryParser] → Token tree
      ↓
[Expander] → Literal terms (wildcard/fuzzy expansion)
      ↓
[IndexReader] → Posting lists
      ↓
[PostingIntersector] → AND intersection
      ↓
[Result Stream] → Document IDs
```

## Files

### Core Search

| File | Class | Purpose |
|---|---|---|
| `QueryParser.cs` | `QueryParser` | Parse query syntax |
| `IndexReader.cs` | `IndexReader` | Read term dictionaries/postings |
| `PostingIntersector.cs` | `PostingIntersector` | Fast AND intersection |
| `PostingIterator.cs` | `PostingIterator` | Iterate posting list |
| `PostingStream.cs` | `PostingStream` | Buffered posting I/O |

### Iterators

| File | Class | Purpose |
|---|---|---|
| `ConcatIterator.cs` | `ConcatIterator` | Concatenate multiple iterators |
| `UnionIterator.cs` | `UnionIterator` | Sorted union of iterators |
| `FilteringIterator.cs` | `FilteringIterator` | Filter with predicate |

### Term Expansion

| File | Class | Purpose |
|---|---|---|
| `HebrewWildcardExpander.cs` | `HebrewWildcardExpander` | Expand `*`, `?` patterns |
| `FuzzyExpander.cs` | `FuzzyExpander` | Levenshtein expansion |
| `KetivExpander.cs` | `KetivExpander` | Hebrew ketiv/qere variants |
| `GrammarExpander.cs` | `GrammarExpander` | Grammatical prefix/suffix expansion (`%`) |
| `Levenshtein.cs` | — | Distance calculation |

### Bitmap

| File | Class | Purpose |
|---|---|---|
| `RoaringBitmap.cs` | `RoaringBitmap` | Compressed bitmap |
| `RoaringBitmapIterator.cs` | `RoaringBitmapIterator` | Bitmap iterator |

### Utilities

| File | Purpose |
|---|---|
| `VarInt.cs` | Variable-length integer encoding |
| `ProximityWindow.cs` | Term proximity tracking |

## Query Syntax

| Pattern | Expansion |
|---|---|
| `word` | Literal term |
| `word*` | All terms with prefix `word` |
| `*word` | All terms with suffix `word` |
| `w*rd` | All terms matching pattern |
| `wor?d` | `word` or `wod` (optional char) |
| `%word` | All grammatical prefix forms of `word` (קידומות דקדוקיות) |
| `word%` | All grammatical suffix forms of `word` (סיומות דקדוקיות) |
| `%word%` | All prefix, suffix, and prefix+suffix forms of `word` |
| `word~` | Edit distance 1 variants |
| `word~2` | Edit distance 2 variants |
| `word~3` | Edit distance 3 variants |
| `a \| b` | `a` OR `b` (within one AND slot) |

**Operator precedence / interactions:**
- `*` overrides `%`: a token with `*` is treated as a plain wildcard; `%` is ignored.
- `?` is compatible with `%`: optional-char variants are unrolled first, then grammar expansion is applied to each resulting base word.
- `%` and `~` on the same token: fuzzy wins, `%` is ignored (same rule as `*`).

## Key Classes

### IndexReader

```csharp
var reader = new IndexReader(store);
var postings = reader.GetPostings(term);
```

### PostingIntersector

Skip-list accelerated AND intersection. Efficiently finds documents containing all terms.

### HebrewWildcardExpander

Hebrew-aware wildcard expansion handling:
- RTL text direction
- Hebrew character ranges
- Optional character patterns (`?`)

### FuzzyExpander

Levenshtein-based fuzzy matching up to distance 3.
