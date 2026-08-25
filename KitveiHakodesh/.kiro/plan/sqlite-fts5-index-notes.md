# Building a SQLite FTS5 index over `seforim.db`

Notes on how we built a full-text index over the Otzaria `line` table
(~5.89M rows of Hebrew/Aramaic text, niqqud + cantillation present in some
books, absent in most) — what we tried, what broke, and the final recipe.

## The requirement

- Search must work whether the text has niqqud/teamim or not (or a mix).
- The only query shape needed: **"which lines contain all of these search
  terms"** — a plain AND over words, no phrase order required.
- Must support scoping a search to one `bookId`.
- Must not blow up disk usage relative to the ~7.8 GB source database.

## Step 1 — don't write a new tokenizer, reuse the real one

`line.content` is HTML, not plain text (`<h1>`, `<span>`, `<big>`, HTML
entities, mixed in with pointed Hebrew). Whatever strips it down to bare
search terms has to:

- decode HTML entities and strip tags,
- strip niqqud (`U+05B0`–`U+05C7`) and cantillation (`U+0591`–`U+05AF`),
- split on maqaf, keep intra-word geresh/gershayim (`רש"י` → `רשי`, not two
  tokens),
- know that some inline tags (`<b>`, `<small>`, `<span>`) sit **inside** a
  word (a drop-cap first letter, an emphasized letter) and must NOT break
  the word, while block tags (`<br>`, `<h1>`, `<div>`, …) must.

A hand-rolled regex version of this got the last point wrong on the first
try: `<big>בְּ</big>רֵאשִׁ֖ית` tokenized as two words (`ב`, `ראשית`)
instead of one (`בראשית`), because replacing every tag with a space splits
words that inline formatting merely decorates.

The KleiKodesh project already has a production-grade tokenizer
(`FtsLib/Tokenization/HtmlWordScanner.cs`) that had already hit and fixed
this exact bug (see its comments — "flushing on every `<` indexed those
words as unfindable fragments"). Rather than re-debug the same edge cases,
the index build calls that tokenizer directly from .NET:

```csharp
using FtsLib.Tokenization;   // FtsLib.csproj is InternalsVisibleTo("FtsLibTest")

var tok = new Tokenizer();
HashSet<string> terms = tok.Extract(rawHtmlContent);   // niqqud/teamim-free, deduped
```

(A faithful Python port of the same scanner was also written and
unit-tested against the tricky cases — inline-tag mid-word joins, HTML
entities as separators, maqaf, intra-word quotes — for use in contexts
without a .NET runtime. The logic is a straight line-for-line port of
`HtmlWordScanner.cs` / `HebrewChars.cs` / `HtmlBlockTags.cs`.)

## Step 2 — pick the FTS5 detail level

SQLite FTS5 has three `detail=` levels:

| Level | Stores | Enables |
|---|---|---|
| `full` (default) | positions | phrase queries, `snippet()`/`highlight()`, `bm25()` |
| `column` | column presence only | ~nothing extra with a single column — barely smaller than `full` |
| `none` | just "term occurs in this row" | plain AND/OR of terms, smallest index |

Since the actual requirement is "AND of terms, no phrase order," `detail=none`
was the right call — smallest index, and FTS5's default `MATCH 'term1 term2'`
is already an implicit AND, so no query-syntax changes were needed either.

## Step 3 — the mistake, and the fix

FTS5's "external content" mode (`content='some_table'`) is the standard way
to avoid FTS5 storing a second copy of your text — the index only stores the
inverted structure, and defers to your own table for the source text.

The first working build used exactly that:

```sql
CREATE TABLE line_search (
  lineId       INTEGER PRIMARY KEY,
  bookId       INTEGER NOT NULL,
  content_bare TEXT NOT NULL          -- the deduped, space-joined terms
);
CREATE VIRTUAL TABLE line_fts USING fts5(
  content_bare,
  content='line_search',
  content_rowid='lineId',
  detail=none
);
```

Result over all 5.89M lines: **3,448.6 MB**. That looked like FTS5 itself
being bloated — until it was checked directly. `line_search.content_bare`
(a literal, human-readable, space-joined copy of every line's term set)
was **3,001.2 MB** all by itself; the actual FTS5 inverted index was only
**447.5 MB**. The "external content" table still needs *something* to hold
the text FTS5 is built from, and storing the already-tokenized term string
there was functionally a second copy of exactly what the index encodes —
paid for in plain, uncompressed UTF-8 text.

Fix: for this use case there is no need to ever read the term string back
out of the FTS5 table (the real content is fetched from `seforim.db`'s
`line` table directly, by `lineId`, when a result needs to be shown) — so
use a genuinely **contentless** table (`content=''`) and a companion
metadata-only table for `bookId` filtering:

```sql
CREATE TABLE line_meta (
  lineId INTEGER PRIMARY KEY,     -- = line.id in seforim.db
  bookId INTEGER NOT NULL
);
CREATE INDEX idx_line_meta_book ON line_meta(bookId);

CREATE VIRTUAL TABLE line_fts USING fts5(
  content_bare,
  content='',                     -- contentless: no source-text copy at all
  detail=none
);
```

Build (streaming from the source DB, one row at a time):

```python
for line_id, book_id, raw_html in rows_from_seforim_db:
    terms = extract_terms(raw_html)              # the ported/real tokenizer
    bare  = ' '.join(terms)
    cur.execute('INSERT INTO line_meta (lineId, bookId) VALUES (?,?)', (line_id, book_id))
    cur.execute('INSERT INTO line_fts (rowid, content_bare) VALUES (?,?)', (line_id, bare))
```

Query (AND of terms, scoped to one book):

```sql
SELECT line_fts.rowid
FROM line_fts
JOIN line_meta ON line_meta.lineId = line_fts.rowid
WHERE line_fts MATCH 'term1 term2'
  AND line_meta.bookId = ?;
```

This was verified directly: same match results, same bookId scoping, and
`content_bare` cannot be read back from `line_fts` (contentless — `NULL` on
any attempt), which is the point.

## One data-quality caveat baked into the build

66 rows in the source corpus (all in one book, `bookId=3780`, "שלח תשלח")
are 1–4.8 million characters long — almost certainly a line-splitting
defect in that one sefer's import, not real "lines." Left alone, tokenizing
them fully would dominate build time for no useful search benefit (a hit
inside a multi-megabyte "line" isn't a meaningful result anyway). The build
truncates any row's raw HTML to the first 20,000 characters before
tokenizing — generous for any real verse/paragraph, and irrelevant for the
99.999% of normal-length rows.

## Results

Over all 5,888,636 lines:

| | Value |
|---|---|
| Unique terms | 1,473,218 |
| (term, line) pairs | 285,214,385 |
| **Final index size** (`line_fts` alone) | **447.5 MB** |
| `line_meta` (bookId lookup table) | small — a few tens of MB, plain integers |
| Source DB size | 7.86 GB |

Query timing (ids only, no content fetch — i.e. exactly the "which lines
match" cost), warm:

| Query | Hits | Time |
|---|---|---|
| כי ביצחק | 1,908 | 22–31 ms |
| שויתי לנגדי תמיד | 745 | 1–2 ms |
| תורה מצוה | 18,044 | 25–39 ms |
| אברהם יצחק יעקב | 7,899 | 8–16 ms |
| וידבר משה כן אל בני | 955 | 17–19 ms |

## How this compares to the project's existing custom index (`FtsLib`)

The KleiKodesh project already has a purpose-built engine for this exact
corpus (`FtsLib` — a custom LSM-style segment index, delta+varint compressed
posting lists, skip-list intersection). Measured head-to-head on the same
machine, same terms, ids-only:

| | FtsLib (custom index) | SQLite FTS5 (`detail=none`, contentless) |
|---|---|---|
| Index size | 659.6 MB | **447.5 MB** |
| כי ביצחק | 123–133 ms | 22–31 ms |
| תורה מצוה | 72–77 ms | 25–39 ms |
| אברהם יצחק יעקב | 35–36 ms | 8–16 ms |

For the narrow "AND of terms, no phrase/fuzzy/wildcard" job, bare SQLite
FTS5 came out both smaller and faster.

## What this design does **not** do

This index only answers "which lines contain all these exact (niqqud-free)
terms." It does not have, and would need real additional work to get:

- wildcard search beyond a plain prefix (`word*` is native to FTS5; infix
  `*word*` and suffix `*word` are not),
- fuzzy / edit-distance matching,
- Hebrew ketiv/kri (spelling-variant) expansion,
- phrase queries, relevance ranking, or `snippet()`/`highlight()` — all of
  which need `detail=full` (positions), which brings back the size cost
  `detail=none` was chosen to avoid.

`FtsLib` already has all of the above built and working for this same
corpus (`KetivExpander`, `FuzzyExpander`, `HebrewWildcardExpander`,
`SnippetBuilder`, …). This SQLite-FTS5 index is a valid, smaller/faster
answer to the specific "plain AND search, bookId-scoped" question — not a
drop-in replacement for the rest of what `FtsLib` does.
