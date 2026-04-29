# Book Catalog Search — How It Currently Works

This document describes exactly what the current search algorithm does, step by step, including known bugs and edge cases discovered through analysis against the real database. It is meant as a decision aid — read it, then decide what to change.

---

## Overview

Search runs in two phases. Phase 1 is synchronous and fires on every keystroke. Phase 2 is async, debounced, and only fires when Phase 1 returns nothing.

```
user types
    │
    ├─► Phase 1 (every keystroke, sync)
    │       normalize query
    │       filter all books in memory
    │       if results → show immediately, cancel any Phase 2 in flight
    │       if no results → clear results, let Phase 2 run
    │
    └─► Phase 2 (300ms debounce, async, only when Phase 1 found nothing)
            split query into "book words" + "toc words"
            fetch TOC rows from DB for matching books
            score TOC entries against toc words
            show results
```

---

## Phase 1 — Book title search

**Files:** `useBookCatalogSearch.ts`, `booksCategoryTree.ts`, `bookPathNormalizer.ts`, `bookQueryMatcher.ts`, `normalizeText.ts`

### Step 1: Query normalization

`toQueryWords(rawQuery)` applies two transformations in sequence:

1. `normalize(s)` — lowercases and strips quote characters: `"`, `'`, `״`, `׳`
2. `normalizeBookQuery(s)` — applies Hebrew-specific substitutions (see table below)
3. Split on whitespace, drop empty tokens

**Substitution rules** (applied symmetrically to both query and indexed titles):

| Input pattern | Canonical form | Notes |
|---|---|---|
| `שו"ע` / `שו״ע` / `שוע` | `שלחן ערוך` | Abbreviation expansion |
| `שולחן` | `שלחן` | Plene spelling normalization |

These are the only two rules. No other abbreviations or synonyms are handled.

**Quote stripping consequence:** `רש"י` → `רשי`, `רש׳י` → `רשי`. This means a user typing `רשי` (no quotes) correctly matches books titled `רש"י`. Zero false negatives for Rashi books.

### Step 2: Book index — what each book is indexed as

At catalog load time, `ensureBookSearchMetadata(book)` builds a `searchPath` and `searchWords` for each book:

```
searchPath = normalizeBookQuery(normalize(fullPath)) + " " + normalize(authors)
searchWords = searchPath.split(/\s+/)
```

`fullPath` is the full category path plus book title, e.g.:
```
תלמוד / בבלי / ראשונים על התלמוד / רש"י / רש"י על בבא קמא
```

After normalization this becomes:
```
תלמוד / בבלי / ראשונים על התלמוד / רשי / רשי על בבא קמא
```

And `searchWords` is every space-separated token from that string, including category names at every level.

**Key implication:** every word in every ancestor category name is a searchable token for every book under that category. A book under `מפרשים` has `מפרשים` in its `searchWords`.

### Step 3: Filtering — `filterBooksByWords`

Given query words `[w₁, w₂, ..., wₙ]`, matching runs in two passes.

**Match tiers** (per query word against a single token):

| Tier | Score | Condition |
|---|---|---|
| Exact | 3 | `token === word` |
| Prefix | 2 | `token.startsWith(word)` |
| Contains | 1 | `token.includes(word)` |
| None | 0 | no match |

**Pass 1 — catalog best:** for each query word, scan all books and find the highest tier any book achieves for that word. This becomes the required tier for that word.

**Pass 2 — filter and score:** each book must match every query word at its required tier or better. Books that fall short on any word are dropped. Survivors accumulate a total score (sum of per-word tiers).

**Results** are sorted by total score descending, then by `treeOrder` as a tiebreaker.

**Why this matters:** if any book matches a word exactly, all books that only prefix- or contains-match that word are dropped. So `רשי` returns only books with the exact token `רשי` (i.e. books titled `רש"י`, since quotes are stripped at index time) — not books that merely contain `רשי` as a substring inside a longer token like `מפרשים`. If no exact match exists anywhere in the catalog, the required tier drops to prefix, and so on.

This also means multi-word queries rank correctly: a book that exactly matches all words scores higher than one that prefix-matches some of them, and both appear only if they meet the per-word catalog-best threshold.

---

## Phase 2 — TOC heuristics

**Files:** `bookCatalogTocHeuristics.ts`, `tocSearchUtils.ts`

Phase 2 only runs when Phase 1 returns zero results. It interprets the query as `<book words> <toc words>` and searches TOC entries.

### Stage 1: Query split

`splitQueryIntoBookAndTocParts(words, matchesAnyBook)` tries every right-trimmed prefix of the query words, longest first, to find the longest prefix that matches at least one book via Phase 1's `filterBooksByWords`.

Example: `["בראשית", "פרק", "ד"]`
- Try `["בראשית", "פרק"]` — no book match
- Try `["בראשית"]` — book match → `bookWords = ["בראשית"]`, `tocWords = ["פרק", "ד"]`

Returns `null` if no prefix matches any book (Phase 2 produces no results).

### Stage 2: TOC fetch

Candidate books are capped at **50** (sorted by tree order, so the most prominent catalog entries are kept). TOC rows are fetched from the database in batches of `sqrt(n)` books per query.

After fetching, root TOC entries whose text is a title variant of the book title are stripped. The matching is fuzzy: strips quotes/maqaf, then checks that the shorter word-set is a subset of the longer one with a minimum overlap ratio of 0.6. A hardcoded set of 5 book IDs overrides the ratio rule for known edge cases.

The `isCancelled` callback is checked after every batch — if the user has typed something new, the fetch aborts immediately.

### Stage 3: TOC scoring — `SearchableTree`

Each TOC node is indexed as an array of **segments**, one per ancestor level plus the node's own level:

```
בראשית / פרק א / פסוק ד  →  [["בראשית"], ["פרק", "א"], ["פסוק", "ד"]]
```

Scoring matches query words as an **ordered subsequence** across segments:

- Each query word must match a token that starts with it (prefix match), in a segment at or after the previous match
- Score = sum of intra-segment token distances between consecutive matched query words that land in the same segment
- Cross-segment transitions cost `0` for the distance component, but add `(segmentDistance × 10)` as a penalty

After scoring, **bond detection** runs on the best result: consecutive query word pairs that landed in the same segment in the best result are "bonded" — all other results must also have those pairs in the same segment. This prevents `"פרק ד"` from matching `"פרק א / פסוק ד"` when `"פרק ד"` exists as a direct entry.

**Exact-vs-prefix two-pass:** the scorer first tries with the last word requiring an exact match. If that yields no results, it retries with prefix matching on the last word. This prevents `"פרק ל"` from surfacing `"פרק לא"` when an exact `"פרק ל"` entry exists.

**Ancestry deduplication:** if a node matched, all its descendants are suppressed. So matching `"פרק ד"` prevents `"פרק ד / פסוק א"`, `"פרק ד / פסוק ב"` etc. from also appearing.

### Stage 4: Result building

Matched TOC nodes are converted to `TocFsItem` objects with:
- `tocTitle` — the node's own text
- `tocPath` — the full display path (`"בראשית / פרק ד"`)
- `tocLineIndex` — the line in the book this TOC entry points to (used for navigation)

---

## Cancellation

A monotonically increasing `searchGeneration` counter prevents stale results:

- Phase 1 increments the counter on every keystroke and clears the spinner immediately
- Phase 2 captures the counter value at the start of each run and checks it after every `await` — if it has changed, the run exits without touching results

This means the user never sees results from a superseded search, and the spinner never gets stuck.

---

## What is not handled

- **No title-only matching.** The search path includes the full category hierarchy. A book titled `רש"י על בבא קמא` also matches queries for `תלמוד`, `בבלי`, `ראשונים`, etc. because those are in its path.
- **No ranking by relevance.** Phase 1 results are sorted only by catalog tree order. A book whose title exactly matches the query appears in the same position as a book that matched only via a category name.
- **No synonym expansion beyond the two hardcoded rules.** `תנ"ך` does not match `מקרא`. `ר"מ` does not match `רמב"ם`.
- **No fuzzy matching.** A typo produces zero results.
- **No author-only search.** Authors are included in `searchWords` but there is no way to search by author alone — the author tokens compete equally with title and category tokens.

---

## Source file map

| File | What it owns |
|---|---|
| `useBookCatalogSearch.ts` | Two-phase orchestration, debounce, cancellation, result state |
| `booksCategoryTree.ts` | Book index building (`ensureBookSearchMetadata`), `filterBooksByWords` |
| `bookCatalogTocHeuristics.ts` | Phase 2 pipeline: query split, DB fetch, result building |
| `tocSearchUtils.ts` | `SearchableTree` (scoring, bond detection, deduplication), `stripTocTitleRoots` |
| `bookPathNormalizer.ts` | Abbreviation substitutions (`normalizeBookPath`), חסר/מלא variant detection (`areHebrewSpellingVariants`, `decomposeHebrewWord`) |
| `bookPathMatcher.ts` | Token scoring (`scoreWordAgainstTokens`), match tier constants |
| `normalizeText.ts` | Base normalization: lowercase + strip quotes |
