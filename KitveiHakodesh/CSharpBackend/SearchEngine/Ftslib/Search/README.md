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
| `HebrewWildcardExpander.cs` | `HebrewWildcardExpander` | Expand `*` patterns |
| `FuzzyExpander.cs` | `FuzzyExpander` | Levenshtein expansion |
| `KetivExpander.cs` | `KetivExpander` | Hebrew ketiv/qere variants |
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
| `word~` | Edit distance 1 variants |
| `word~2` | Edit distance 2 variants |
| `word~3` | Edit distance 3 variants |
| `a \| b` | `a` OR `b` (within one AND slot) |

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

### FuzzyExpander

Levenshtein-based fuzzy matching up to distance 3.
