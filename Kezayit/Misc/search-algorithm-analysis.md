# Book Catalog Search Algorithm Analysis & Improvement Suggestions

## Current Architecture Overview

The book catalog search uses a **two-phase hybrid approach**:

### Phase 1: Instant Book Title Match (Synchronous)
- Runs on every keystroke
- Filters in-memory book catalog by query words
- Results appear immediately with no loading state
- Cancels any in-flight Phase 2 search

**Algorithm:**
```
filterBooksByWords(allBooks, words):
  exactWords = words[:-1]
  prefixWord = words[-1]
  
  for each book:
    pathWords = book.searchPath.split()
    
    # All but last word must match exactly
    exactMatch = all(word in pathWords for word in exactWords)
    
    # Last word can be a prefix match
    prefixMatch = any(word.startsWith(prefixWord) for word in pathWords)
    
    if exactMatch AND prefixMatch:
      include book
  
  sort by treeOrder
```

**Characteristics:**
- Time: O(n × m) where n = books, m = avg words per book path
- Space: O(1) auxiliary
- Deterministic, no false negatives for exact book titles
- Prefix matching on final word enables incremental typing

### Phase 2: TOC Heuristics Fallback (Asynchronous, Debounced)
- Runs 300ms after user stops typing
- Only triggers when Phase 1 finds no books
- Interprets query as `<book words> <toc words>`
- Capped at 50 candidate books to prevent DB overload

**Four-stage pipeline:**

**Stage 1: Query Split**
```
splitQueryIntoBookAndTocParts(words):
  for trimCount in 1..len(words)-1:
    bookWords = words[:-trimCount]
    if any book matches bookWords:
      return { bookWords, tocWords: words[-trimCount:] }
  return null
```
- Tries every right-trimmed prefix to find longest book match
- Time: O(k × n) where k = query length, n = books

**Stage 2: TOC Fetch**
- Loads all TOC entries for candidate books from DB
- Batches queries: batch size = sqrt(num_books)
- Strips redundant root entries (titles matching book title)
- Sorts results by book tree order

**Stage 3: TOC Search**
- Builds a `SearchableTree` from flat TOC rows
- Runs a sophisticated scoring algorithm:
  - Matches query words as ordered subsequence across segments
  - Scores based on intra-segment token distance
  - Cross-segment transitions cost 0
  - Detects "bonded" word pairs (consecutive words in same segment)
  - Filters results to enforce bonded pair constraints
  - Deduplicates by ancestry (if parent matched, suppress children)

**Stage 4: Result Building**
- Converts matched TOC nodes to UI items
- Preserves display paths for breadcrumb rendering

---

## Current Strengths

1. **Responsive UX** — Phase 1 is instant, no loading state for common queries
2. **Smart fallback** — TOC heuristics handle "בראשית פרק ד" style queries
3. **Cancellation** — Debounce + generation counter prevents stale results
4. **Batch optimization** — sqrt(n) batching balances round-trips vs query size
5. **Ancestry deduplication** — Prevents redundant parent/child results
6. **Bonded pair detection** — Prevents "פרק ד" from matching "פרק א / פסוק ד"
7. **Normalization** — Handles abbreviations (שו"ע → שלחן ערוך) and spelling variants

---

## Issues & Improvement Opportunities

### 1. **Phase 1: Inefficient Word Matching on Every Keystroke**

**Problem:**
- Every keystroke re-splits `book.searchPath` into words
- `ensureBookSearchMetadata()` is called per book per keystroke
- No caching of split words — O(n × m) work repeated

**Current code:**
```typescript
export function filterBooksByWords(allBooks: BookRow[], words: string[]): BookRow[] {
  return allBooks.filter((book) => {
    ensureBookSearchMetadata(book)  // ← splits searchPath every time
    const pathWords = book.searchWords ?? []
    // ... matching logic
  })
}
```

**Impact:** On 10,000 books with avg 5 words each, this is ~50,000 string splits per keystroke.

**Recommendation:**
- Pre-compute `searchWords` at catalog load time (already done in `assignFullPaths`)
- Verify `ensureBookSearchMetadata` is truly idempotent (it is — checks `if (book.searchPath && book.searchWords) return`)
- Consider: move the idempotency check outside the filter loop

**Suggested fix:**
```typescript
export function filterBooksByWords(allBooks: BookRow[], words: string[]): BookRow[] {
  if (!words.length) return []
  
  // Ensure all books have metadata pre-computed (should be done at load time)
  for (const book of allBooks) {
    ensureBookSearchMetadata(book)
  }
  
  const exactWords = words.slice(0, -1)
  const prefixWord = words[words.length - 1]!
  
  return allBooks
    .filter((book) => {
      const pathWords = book.searchWords ?? []
      const exactWordsMatch = exactWords.every((queryWord) =>
        pathWords.some((pathWord) => pathWord === queryWord),
      )
      const prefixWordMatch = pathWords.some((pathWord) => 
        pathWord.startsWith(prefixWord)
      )
      return exactWordsMatch && prefixWordMatch
    })
    .sort((a, b) => (a.treeOrder ?? 0) - (b.treeOrder ?? 0))
}
```

---

### 2. **Phase 1: Quadratic Matching Logic**

**Problem:**
- For each book, for each exact word, scan all path words: O(n × m × k)
- For each book, for each prefix word, scan all path words: O(n × m × k)
- No early exit when a word is not found

**Current code:**
```typescript
const exactWordsMatch = exactWords.every((queryWord) =>
  pathWords.some((pathWord) => pathWord === queryWord)  // ← O(m) per word
)
```

**Recommendation:**
- Convert `pathWords` to a Set for O(1) exact lookups
- Keep prefix matching as-is (must scan all words)

**Suggested fix:**
```typescript
export function filterBooksByWords(allBooks: BookRow[], words: string[]): BookRow[] {
  if (!words.length) return []
  
  const exactWords = words.slice(0, -1)
  const prefixWord = words[words.length - 1]!
  
  return allBooks
    .filter((book) => {
      const pathWords = book.searchWords ?? []
      const pathWordSet = new Set(pathWords)  // ← O(m) once per book
      
      // Exact words: O(k) with Set lookup
      const exactWordsMatch = exactWords.every((queryWord) => 
        pathWordSet.has(queryWord)
      )
      
      // Prefix words: O(m) scan (unavoidable)
      const prefixWordMatch = pathWords.some((pathWord) => 
        pathWord.startsWith(prefixWord)
      )
      
      return exactWordsMatch && prefixWordMatch
    })
    .sort((a, b) => (a.treeOrder ?? 0) - (b.treeOrder ?? 0))
}
```

**Complexity improvement:** O(n × m × k) → O(n × m)

---

### 3. **Phase 2: Redundant Root Entry Stripping**

**Problem:**
- `stripTocTitleRoots()` is called per batch, but the logic is repeated
- Fuzzy title matching (ratio-based) is O(m) per root entry
- No caching of which books have redundant roots

**Current code:**
```typescript
function stripRedundantRootEntriesPerBook(rows: TocRow[], bookTitles: Map<number, string>): TocRow[] {
  const rowsByBook = new Map<number, TocRow[]>()
  for (const row of rows) {
    const group = rowsByBook.get(row.bookId) ?? []
    group.push(row)
    rowsByBook.set(row.bookId, group)
  }

  const stripped: TocRow[] = []
  for (const [bookId, group] of rowsByBook) {
    const title = bookTitles.get(bookId) ?? ''
    stripped.push(...stripTocTitleRoots(group, title, { bookId }))  // ← called per book
  }
  return stripped
}
```

**Recommendation:**
- Pre-compute which books have redundant roots at catalog load time
- Cache the result in a Set<bookId>
- At search time, only strip for books in the cache

**Suggested fix:**
```typescript
// At catalog load time (in booksDataStore or similar):
const booksWithRedundantRoots = new Set<number>()
for (const book of allBooks) {
  const roots = tocEntries.filter(e => e.bookId === book.id && e.parentId === null)
  if (roots.some(r => isTitleVariant(book.title, r.text))) {
    booksWithRedundantRoots.add(book.id)
  }
}

// At search time:
function stripRedundantRootEntriesPerBook(
  rows: TocRow[], 
  bookTitles: Map<number, string>,
  booksWithRedundantRoots: Set<number>
): TocRow[] {
  const rowsByBook = new Map<number, TocRow[]>()
  for (const row of rows) {
    if (!booksWithRedundantRoots.has(row.bookId)) {
      // No redundant roots for this book, keep all rows
      rowsByBook.set(row.bookId, [row])
    } else {
      const group = rowsByBook.get(row.bookId) ?? []
      group.push(row)
      rowsByBook.set(row.bookId, group)
    }
  }

  const stripped: TocRow[] = []
  for (const [bookId, group] of rowsByBook) {
    if (booksWithRedundantRoots.has(bookId)) {
      const title = bookTitles.get(bookId) ?? ''
      stripped.push(...stripTocTitleRoots(group, title, { bookId }))
    } else {
      stripped.push(...group)
    }
  }
  return stripped
}
```

---

### 4. **Phase 2: TOC Search Scoring — Segment Crossing Penalty Too High**

**Problem:**
- `SEGMENT_CROSSING_PENALTY = 10` is arbitrary
- A query like "פרק ד" that crosses one segment boundary gets penalized heavily
- May suppress valid results like "פרק ד / פסוק א" when "פרק ד" exists as a root

**Current code:**
```typescript
const SEGMENT_CROSSING_PENALTY = 10
let score = 0
for (let i = 1; i < words.length; i++) {
  if (segIndices[i] === segIndices[i - 1]) {
    score += tokenIndices[i]! - tokenIndices[i - 1]!
  } else {
    score += (segIndices[i]! - segIndices[i - 1]!) * SEGMENT_CROSSING_PENALTY  // ← 10x penalty
  }
}
```

**Recommendation:**
- Tune the penalty based on real query data
- Consider: penalty should be proportional to segment distance, not fixed
- Consider: use a logarithmic penalty instead of linear

**Suggested fix:**
```typescript
// Logarithmic penalty: crossing 1 segment = 2, crossing 2 = 3, crossing 3 = 4, etc.
const segmentDistance = segIndices[i]! - segIndices[i - 1]!
const penalty = Math.log2(segmentDistance + 1) * 2  // ← tunable multiplier
score += penalty
```

---

### 5. **Phase 2: No Prefix Matching in TOC Search**

**Problem:**
- TOC search requires exact token matches (after normalization)
- Query "פרק" won't match "פרקים" or "פרקי"
- User must type the full word to get results

**Current code:**
```typescript
private _score(nodeId: number, words: string[], lastWordExact = false): ... {
  for (let wi = 0; wi < words.length; wi++) {
    const w = words[wi]!
    const exact = lastWordExact && wi === words.length - 1
    for (let si = segFrom; si < segs.length; si++) {
      for (let ti = 0; ti < seg.length; ti++) {
        if (exact ? seg[ti] === w : seg[ti]!.startsWith(w)) {  // ← prefix only on last word
          // ...
        }
      }
    }
  }
}
```

**Recommendation:**
- Allow prefix matching on all words, not just the last one
- This matches Phase 1 behavior and improves discoverability

**Suggested fix:**
```typescript
private _score(nodeId: number, words: string[]): ... {
  for (let wi = 0; wi < words.length; wi++) {
    const w = words[wi]!
    const isLastWord = wi === words.length - 1
    for (let si = segFrom; si < segs.length; si++) {
      for (let ti = 0; ti < seg.length; ti++) {
        // Exact match on all words except last (which can be prefix)
        const matches = isLastWord 
          ? seg[ti]!.startsWith(w)
          : seg[ti] === w
        if (matches) {
          // ...
        }
      }
    }
  }
}
```

---

### 6. **Phase 2: No Fuzzy Matching**

**Problem:**
- Typos and misspellings get zero results
- "פרק" vs "פרק" (different Unicode normalization) may not match
- No Levenshtein distance or similar

**Recommendation:**
- Add optional fuzzy matching for the last word (Levenshtein distance ≤ 1)
- Only enable when exact match yields no results
- Use a simple algorithm: allow 1 character insertion/deletion/substitution

**Suggested implementation:**
```typescript
function levenshteinDistance(a: string, b: string): number {
  const m = a.length, n = b.length
  const dp: number[][] = Array(m + 1).fill(null).map(() => Array(n + 1).fill(0))
  for (let i = 0; i <= m; i++) dp[i][0] = i
  for (let j = 0; j <= n; j++) dp[0][j] = j
  for (let i = 1; i <= m; i++) {
    for (let j = 1; j <= n; j++) {
      dp[i][j] = Math.min(
        dp[i - 1][j] + 1,      // deletion
        dp[i][j - 1] + 1,      // insertion
        dp[i - 1][j - 1] + (a[i - 1] !== b[j - 1] ? 1 : 0)  // substitution
      )
    }
  }
  return dp[m][n]
}

// In search: try exact first, then fuzzy on last word if empty
const exactMatches = search(nodes, query)
if (!exactMatches.length && words.length > 0) {
  const lastWord = words[words.length - 1]!
  const fuzzyMatches = nodes.filter(node => {
    const segs = this.segments.get(node.id) ?? []
    const lastSeg = segs[segs.length - 1] ?? []
    return lastSeg.some(token => levenshteinDistance(token, lastWord) <= 1)
  })
  return fuzzyMatches.slice(0, limit)
}
```

---

### 7. **Phase 2: No Result Ranking by Relevance**

**Problem:**
- Results are sorted by score, but score only reflects match tightness
- No consideration of book popularity, frequency, or user history
- "פרק א" (first chapter) appears same as "פרק ק" (100th chapter)

**Recommendation:**
- Add a secondary sort key: book popularity or frequency
- Boost results from recently-opened books
- Consider: TF-IDF style scoring for TOC entries

**Suggested fix:**
```typescript
// In runTocHeuristics, after building items:
const items = buildTocResultItems(matchedNodes, bookMap, tree)

// Sort by: (1) score, (2) book popularity, (3) TOC depth
items.sort((a, b) => {
  const scoreA = matchedNodes.find(n => n.id === a.tocEntryId)?.score ?? Infinity
  const scoreB = matchedNodes.find(n => n.id === b.tocEntryId)?.score ?? Infinity
  
  if (scoreA !== scoreB) return scoreA - scoreB
  
  // Secondary: book popularity (e.g., number of links, frequency in searches)
  const popA = bookPopularity.get(a.book.id) ?? 0
  const popB = bookPopularity.get(b.book.id) ?? 0
  if (popA !== popB) return popB - popA
  
  // Tertiary: TOC depth (prefer shallower entries)
  const depthA = tree.getDepth(a.tocEntryId)
  const depthB = tree.getDepth(b.tocEntryId)
  return depthA - depthB
})
```

---

### 8. **No Query Expansion or Synonym Handling**

**Problem:**
- "תנ"ך" won't match "מקרא" (both mean "Bible")
- "שו"ע" expands to "שלחן ערוך" but no other synonyms are handled
- User must know the canonical name

**Recommendation:**
- Extend `bookQueryNormalizer.ts` with more synonym rules
- Consider: build a synonym map from the catalog itself (e.g., "also known as" metadata)

**Suggested rules to add:**
```typescript
const TITLE_VARIANTS: [RegExp, string][] = [
  // Existing
  [/שו["״]?ע/g, 'שלחן ערוך'],
  [/שולחן/g, 'שלחן'],
  
  // New suggestions
  [/תנ["״]?ך/g, 'מקרא'],  // Bible
  [/ש["״]?ס/g, 'שולחן ערוך'],  // Alternative abbreviation
  [/ר["״]?מ["״]?ב/g, 'רמב"ם'],  // Maimonides
  [/ר["״]?י["״]?ף/g, 'ריף'],  // Rif
  [/ר["״]?ש["״]?י/g, 'רשי'],  // Rashi
]
```

---

### 9. **No Analytics on Search Behavior**

**Problem:**
- No data on which queries are common, which return no results
- Can't identify gaps in the catalog or normalization rules
- Can't optimize penalty weights based on real usage

**Recommendation:**
- Log search queries and result counts (anonymized)
- Track zero-result queries to identify missing normalization rules
- Use data to tune `SEGMENT_CROSSING_PENALTY` and other constants

**Suggested logging:**
```typescript
// In useBookCatalogSearch:
watch(debouncedQuery, async (rawQuery) => {
  // ... existing search logic ...
  
  // Log for analytics
  if (results.value.length === 0 && rawQuery.trim()) {
    logSearchEvent({
      query: rawQuery,
      phase: 'toc',
      resultCount: 0,
      timestamp: Date.now()
    })
  }
})
```

---

### 10. **Phase 2: Batch Size Heuristic Not Validated**

**Problem:**
- Batch size = sqrt(num_books) is a guess, not validated
- No measurement of DB round-trip time vs query size
- May be suboptimal for different DB backends (SQLite vs PostgreSQL)

**Recommendation:**
- Measure actual query times for different batch sizes
- Consider: adaptive batching based on DB response time
- Document the heuristic with measurements

**Suggested measurement script:**
```python
import sqlite3
import time

db = sqlite3.connect('data.db')
cursor = db.cursor()

# Measure query time for different batch sizes
for batch_size in [1, 5, 10, 20, 50, 100]:
    book_ids = list(range(1, batch_size + 1))
    placeholders = ','.join('?' * len(book_ids))
    query = f'SELECT * FROM tocEntry WHERE bookId IN ({placeholders})'
    
    start = time.time()
    for _ in range(100):
        cursor.execute(query, book_ids)
        cursor.fetchall()
    elapsed = time.time() - start
    
    print(f"Batch size {batch_size}: {elapsed/100*1000:.2f}ms per query")
```

---

## Summary of Recommendations (Priority Order)

| Priority | Issue | Impact | Effort |
|----------|-------|--------|--------|
| **High** | Phase 1: Quadratic matching (Set lookup) | 10-50% faster Phase 1 | 30 min |
| **High** | Phase 2: Prefix matching in TOC | Better discoverability | 1 hour |
| **Medium** | Phase 1: Redundant metadata computation | Cleaner code, marginal perf | 30 min |
| **Medium** | Phase 2: Fuzzy matching on typos | Better UX | 2 hours |
| **Medium** | Phase 2: Result ranking by popularity | Better relevance | 1 hour |
| **Low** | Phase 2: Tune segment crossing penalty | Marginal scoring improvement | 1 hour |
| **Low** | Phase 2: Redundant root caching | Marginal perf on broad queries | 1 hour |
| **Low** | Query expansion / synonyms | Better discoverability | 2 hours |
| **Low** | Search analytics | Data-driven optimization | 3 hours |
| **Low** | Batch size validation | Confidence in heuristic | 2 hours |

---

## Implementation Notes

### For Phase 1 (Set Lookup):
- Minimal risk, high confidence
- Can be done in isolation
- Measurable performance improvement

### For Phase 2 (Prefix Matching):
- Requires changes to `SearchableTree._score()`
- May need tuning of `SEGMENT_CROSSING_PENALTY`
- Test with real queries to ensure no regression

### For Fuzzy Matching:
- Consider: use a library like `fuse.js` instead of hand-rolling Levenshtein
- Only enable as fallback when exact match yields nothing
- Measure performance impact on large TOC trees

### For Analytics:
- Ensure compliance with privacy policy
- Consider: store only aggregated stats, not individual queries
- Use for tuning, not for user tracking

