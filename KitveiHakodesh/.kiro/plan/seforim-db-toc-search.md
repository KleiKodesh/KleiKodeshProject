# Seforim DB TOC Search Plan

A design for full-text search over the Seforim database's book catalog and
tables of contents. The plan is engine-agnostic: it can be implemented on any
Lucene-style full-text library (Lucene.NET, Lucene, Tantivy, etc.) — anything
that supports inverted-index term matching, per-field indexing, stored fields,
and a custom tokenizer.

## Index

-   Disk-based inverted index.
-   Rebuild automatically when the Seforim database changes. The change
    stamp is a plain composite string of **file-system metadata only** — no
    file content is ever read (~0.4 ms even on a 7 GB file):

    ```text
    formatVersion | path | size | mtime | ntfsChangeTime | fileUSN | fileId | wal=<size:ctime:usn>
    ```

    The fields deliberately cover each other's blind spots, so there are no
    practical misses:

    -   **size + mtime** — every normal write.
    -   **NTFS ChangeTime (ctime)** — a same-size in-place edit with mtime
        restored: the very call that restores mtime is a metadata change
        and bumps ctime.
    -   **per-file USN** — assigned monotonically by NTFS on every change
        record; applications cannot set or restore it. Catches even
        memory-mapped writes that update no timestamps.
    -   **file id** — the file was replaced by a different file (new MFT
        record), even with identical size and restored timestamps.
    -   **WAL sidecar** — in SQLite WAL mode a committed write lands in
        `<db>-wal` before any checkpoint touches the main file; a non-empty
        wal contributes its size/ctime/USN to the stamp.
    -   **format version / path** — index schema bumps and database
        switches.

    Reads of the file never change the stamp (verified), so an untouched
    database never triggers a needless rebuild. On non-NTFS volumes the
    ctime/USN fields degrade gracefully and the stamp falls back to
    size+mtime behavior. The stamp is stored human-readable in a version
    file next to the index and written only when a build fully completes —
    an interrupted build leaves a missing/blank version file and is treated
    as stale and rebuilt on the next run. The same stamp logic is a shared
    utility, also used by the full-text-search index so any DB change
    rebuilds both.

-   **Startup vs. running.** At startup each index checks its stamp once and
    builds if stale (~0.2 ms, negligible). Because the service is long-lived,
    a **file-system watcher** on the database's folder covers in-place
    updates that happen *while it runs*: it watches the directory (not the
    single file, so SQLite's `-wal` sidecar writes are seen). The watcher is
    event-driven (no polling, no idle CPU) and costs ~10 KB of memory.

    It **never reacts mid-write.** A DB update — especially a full
    re-download/replace — is a long operation with many writes and pauses;
    rebuilding partway through would waste work or index a half-written file.
    So after activity it waits a **generous quiet window** (default 2 min),
    then *confirms the file has actually stopped changing* (its stamp is
    unchanged across the window) before doing anything. If the file moved
    during the wait, it waits another full window — repeatedly — until the
    file settles, bounded by a hard cap (default 15 min) so a continuously
    slow-written file still eventually rebuilds. Only on settle does each
    index recompute its stamp and rebuild if it genuinely changed — a
    spurious event that changed nothing costs one cheap stamp read.

-   **Database switch (Otzaria ↔ Zayit).** The DB *path* is a user setting
    that the host application can change directly (outside the search
    service) while the service is running. The service's caches (catalog,
    schema probes, index instances, the file watcher's folder) are all
    per-process, so the correct response to a path switch is a **clean
    service restart**, not in-place invalidation: an event-driven watch on
    the settings store (Windows: `RegNotifyChangeKeyValue` — one parked
    thread, no polling) re-resolves the path on every settings write and
    restarts the service only when it actually changed. The fresh process
    re-resolves everything; because both index change-stamps *include the
    DB path*, both indexes detect the mismatch and reindex automatically —
    and switching back to a database whose index stamp still matches costs
    nothing.
-   Rebuild runs on a dedicated background thread and never blocks the
    UI or searches.
-   Searches remain available while rebuilding. The index is built **in
    place** with a reader that can see documents as they are added
    (Lucene: a near-real-time reader off the live writer; other engines:
    periodic commits + reader reopen) — so partial results appear
    immediately instead of waiting for the whole build to finish.

## Search Behavior

-   Contains-all (AND) logic only: every query token must match one of the
    indexed fields (per token: `FullTocPath` OR `CatalogPath` OR `Author`),
    using exact term matches against the analyzed tokens.
-   No TF/IDF, BM25, boosting, fuzzy, wildcard, proximity, phrase, or
    relevance ranking. Engine relevance scores are ignored entirely.
-   Matching is never limited — every matching document is found, counted,
    and ordered. Only **materialization** is bounded (below).
-   Ordering is determined only by the tie-breakers below.
-   A new search supersedes and cancels the previous in-flight one
    (latest-wins); superseded responses are flagged so the client retries.

### Materialization Cap (performance only)

A search has two costs: *matching* (walking the inverted index to produce
document ids — cheap, tens of milliseconds even for 100k+ hits) and
*materializing* (reading each matched document's stored fields off disk —
the dominant cost, roughly linear in documents read: a broad one-word query
that matches ~125k documents takes ~10 s to materialize them all).

Because ordering depends only on `Level` and `TreeOrder`, those two values
are **also** written as numeric doc-values (column-stored, cheap random
access — independent of the compressed stored-fields blob). Search then:

1.  Collects **all** matching document ids with their `Level`/`TreeOrder`
    from doc-values — uncapped, no stored-field reads.
2.  Sorts that full set by (Level, TreeOrder).
3.  Materializes (reads stored fields for) only the ordered top **N**
    (currently 1000). The token-order discard runs on this window.

The cap is a pure performance bound: matching, counting, and ordering are
uncapped and exact; only how many already-ordered documents get turned into
full result objects is limited. On the current DB the worst-case broad
query drops from ~10 s to a few hundred milliseconds; typical multi-word
queries are a few tens of milliseconds. Raise N (or add lazy paging) only
if a real use case needs to display beyond the first thousand ordered hits.

### Fuzzy Fallback (zero-results only)

When the exact search returns **no results**, it is retried once with fuzzy
matching, under strict limits:

-   Fuzzy terms are tried on the `CatalogPath` and `Author` fields **only —
    never on the TOC path**, where a one-letter edit is a different
    chapter or verse (יב vs יג).
-   Only query tokens of **3 or more characters** participate in fuzzy
    matching; shorter tokens must still match exactly somewhere.
-   Edit distance: 1 for tokens of 3–5 characters, 2 for longer tokens.
-   Exact matching on all fields remains available for every token, and
    everything downstream (ordering, the token-order discard rule) is
    unchanged — a fuzzy-matched token is not in the TOC path, so it is
    automatically exempt from the order rule.

Example: `תבך בראשית פרק ב` (misspelled תנך) finds `בראשית / פרק ב` through
the catalog field; `בראשית פרק ב פסוק צט` (nonexistent verse) still returns
nothing, because TOC-path tokens never fuzz.

## Text Normalization

The exact same normalization pipeline must run during indexing and
searching (a custom analyzer/tokenizer implemented once and used for both).

### Pipeline

1.  Canonical normalization
2.  Daf (amud) normalization
3.  Strip all non-word characters
4.  Tokenization

### Canonical Normalization

Token-based. Normalize to the canonical token `שולחן`:

-   שלחן
-   שולחן
-   שו"ע / שו״ע / שו''ע
-   ש"ע / ש״ע / ש''ע

(Each abbreviation in its ASCII-quote, Hebrew-gershayim, and doubled-ASCII-
apostrophe spellings.)

This normalization **must occur before** punctuation is stripped
(the quote character is the signal; bare `שוע` is deliberately not mapped).

### Daf Normalization

Before stripping punctuation, a token immediately following `דף` that ends
with an amud mark expands:

-   `דף יד:` → `דף יד עמוד ב`
-   `דף יד.` → `דף יד עמוד א`

Applies to indexed TOC text and to queries alike, so `פסחים דף י:` and
`פסחים דף י עמוד ב` are the same query.

### Character Filtering

After normalization, strip all non-word characters (anything that is not a
letter or digit) from both indexed text and search queries. Tokens are the
whitespace-separated units that remain; empty tokens are dropped.

## Indexed Fields

Three indexed text fields (all analyzed with the pipeline above) plus
stored-only metadata:

| Field | Content | Indexed | Stored | Notes |
| --- | --- | :---: | :---: | --- |
| `FullTocPath` | book title + " / " + TOC segments | Yes | Yes | The display path, the search text, **and** the reference text for the Query Token Order rule. Book-title documents hold just the title. |
| `CatalogPath` | full folder-hierarchy (category) path | Yes | Yes | Matchable, order-exempt. Never part of the displayed path. |
| `Author` | author names | Yes | Yes | Matchable, order-exempt. |
| `BookId` | book id | No | Yes | Used to locate the book. |
| `LineIndex` | line number within the book | No | Yes | Navigation target (−1 = none). |
| `Level` | 0 = book title, 1+ = TOC depth | No | Yes | First tie-breaker. |
| `TreeOrder` | catalog book position + original TOC order | No | Yes | Second tie-breaker. |

The book title needs no separate field — `FullTocPath` begins with it.
The UI currently resolves the author tag and category line from the
client-side catalog store by `BookId`; the stored `CatalogPath`/`Author`
values are available for consumers without that store.

Document kinds: one document per book title (Level 0, pointing at the
book's first line), one per TOC entry, one per alternative-structure TOC
entry (parshiot/aliyot etc.), and one per generated Tanach verse (below).

### Level Is Computed, Never Read From the Database

The Seforim DB's `tocEntry.level` column is inconsistent — TOCs start at
level 0 for some books, 1 or 2 for others. Do **not** use it. `Level` is
computed as parent-chain depth during indexing: book document = 0, root
entries = 1, each child +1. This gives every book the same baseline.

### Redundant Title Roots

Most books' root TOC entry merely repeats the book title (on the current
DB: ~86% are exact duplicates), which would render paths like
"בראשית / בראשית / פרק א". Such roots are dropped and their children
re-parented to the top. Detection is fuzzy, not exact-match only: both
texts are compared as word sets after stripping quote-like characters
(Hebrew geresh/gershayim, ASCII quote **and apostrophe** — titles write
ש"ע as ש''ע — curly quotes, maqaf, hyphen); the shorter set must be a
subset of the longer with a length ratio ≥ 0.6. Genuinely structural roots
(חלק א, a distinct work name) are kept. Avoid hardcoded book-id exception
lists — ids shift between database versions.

### Talmud Daf/Amud Hierarchy

The Seforim DB stores Talmud pages as flat sibling entries `דף ב.` and
`דף ב:`. Indexed as-is, the amud normalization flattens the mark into the
entry's own tokens (`דף ב:` → `דף ב עמוד ב`) — and the injected amud letters
א/ב then collide with real daf/siman/verse letters *at the same level*: a
query like `שבת ב` would match every `דף X:` in the tractate through its
עמוד-ב token, ranked equally with the real דף ב.

The indexer therefore restructures such entries: each `דף X.`/`דף X:` pair
becomes a synthetic parent **`דף X`** with children **`עמוד א`** and
**`עמוד ב`** one level deeper:

```text
שבת / דף ב             (level 1 — navigates to the עמוד א line by default)
שבת / דף ב / עמוד א    (level 2)
שבת / דף ב / עמוד ב    (level 2)
```

The bare parent carries no amud token, so it is what a plain `שבת דף ב`
query surfaces first; amud-letter collisions can only appear at the amud
level, below every real daf-level match. Queries with an explicit amud —
`שבת דף ב:` or `שבת דף ב עמוד ב` — resolve to the matching child.

### Alternative TOC Structures

Besides the primary table of contents (`tocEntry`), many books have one or
more **alternative structures** (`alt_toc_structure` + `alt_toc_entry`) —
a different way to navigate the same text, e.g. parashah/aliyah divisions
of a chumash, or daf divisions alongside a chapter TOC.

Each alternative structure's entries are indexed as **ordinary documents**,
identical in every way to primary TOC documents:

-   One document per `alt_toc_entry`, with the same field set (`FullTocPath`,
    `CatalogPath`, `Author`, `BookId`, `LineIndex`, `Level`, `TreeOrder`),
    the same normalization pipeline, the same computed-depth `Level`, the
    same redundant-title-root stripping, and the same tie-breakers.
-   Entries are grouped and processed per structure; a structure's owning
    book supplies the title (path root), catalog path, and author. Its
    `TreeOrder` continues the owning book's sequence, so alternative-TOC
    hits sort within their book after the primary TOC entries.
-   The path is built exactly like a primary TOC path (book title + " / " +
    root→leaf segments), so a search such as `בראשית נח עליה א` resolves to
    `בראשית / נח / עליה א`.

Because they are ordinary documents, alternative-TOC entries need no special
handling anywhere in search, ordering, or the token-order rule — they simply
participate as more paths to match.

## Folder Hierarchy Indexing

The catalog category tree is indexed (the `CatalogPath` field) so users can
search by the library structure itself, not just by book titles and TOC
entries.

For example, if a book is stored under:

```text
שולחן ערוך
└── אורח חיים
```

then searches such as:

-   `שולחן ערוך אורח חיים`
-   `אורח חיים`

will correctly find the book and its TOC entries, even when those terms are
not part of the book title or the TOC itself (e.g. a book like `פרי מגדים`
whose title carries neither `שולחן` nor `ערוך`).

The folder hierarchy is a separate indexed field — it never appears in the
displayed TOC path, and it never participates in the Query Token Order rule.

## Result Ordering

1.  TOC Level (ascending)
2.  TreeOrder (ascending — catalog book position, then original TOC order)
3.  Query Token Order (a discard rule, applied before sorting)

### How TreeOrder Is Built

`TreeOrder` is a single 64-bit value composed of two parts:

```text
TreeOrder = (bookRank << 24) | perBookSequence
```

**Book rank** — the book's position in the catalog tree, computed once per
build by a depth-first walk that mirrors the catalog UI exactly:

1.  Load categories in display order (`ORDER BY level, orderIndex` when the
    `orderIndex` column exists, else by `level`) and nest them by
    `parentId`, preserving load order among siblings.
2.  At every level, custom entries (negative ids) sort after regular ones.
3.  Books whose category id is unknown go under a synthetic root appended
    after all real roots.
4.  Walk the tree depth-first — a node's own books first, then its child
    categories — numbering each book as it is encountered (0, 1, 2, …).

**Per-book sequence** — a counter that starts at 0 for each book and
increments for every document written for it, in build order:

1.  `0` — the book-title document.
2.  Regular TOC entries, in original TOC order (entry id order).
3.  Alternative-structure TOC entries, structure by structure.
4.  Generated Tanach verse entries, in chapter/verse scan order.

The shift by 24 bits gives each book room for ~16.7M documents, far above
any real book. Sorting by the combined value therefore orders results by
catalog shelf position first and by the original order within the book
second — with no per-query computation: the value is precomputed at build
time and stored on every document.

### Query Token Order

Defined by the `FullTocPath` field **only**:

1.  From the query tokens, keep those that are present among the hit's
    path tokens. Tokens that matched only via `CatalogPath` or `Author`
    are excluded from the test by construction.
2.  If fewer than two tokens remain, there is nothing to order — the hit
    counts as in order.
3.  Otherwise the remaining tokens must appear in the path as an ordered
    subsequence, in the order the user typed them.
4.  Within a (Level, book) group that contains **both** an in-order and an
    out-of-order hit, the out-of-order hits are **discarded**. Groups with
    no in-order hit are kept untouched.

Examples:

-   `תנך בראשית ד יד` — `תנך` is a catalog term and exempt; `בראשית ד יד`
    must be in order → `בראשית / פרק ד / פסוק יד` is kept and
    `בראשית / פרק יד / פסוק ד` is discarded.
-   `משנה תורה` and `תורה משנה` — title/catalog word order can never differ
    between siblings of the same book, so the rule never fires: both
    queries return identical results.

This heuristic is only a final tie-breaker and must not act as a general
relevance score.

## Tanach Verse TOC Generation

The Seforim database contains chapter TOC entries for the 24 books of
Tanach but not verse-level entries.

Maintain a static list of the Tanach book titles exactly as they appear in
the database (the traditional 24 books are stored as 39 titles: שמואל,
מלכים and דברי הימים split in two, תרי עשר as twelve, and תהילים spelled
with a yud).

After the normal index has been fully built and committed:

1.  Scan only the Tanach books, chapter by chapter (each TOC entry owns
    the lines up to the next entry).
2.  Detect verse markers such as `(א)`, `(ב)`, `(ג)` in the line text
    (HTML tags stripped first). A marker only counts when its gematria
    value equals the **next expected verse number** for the current
    chapter — this rejects parasha markers `(פ)`/`(ס)` and any quoted
    parenthesized text. The counter resets at every TOC entry.
3.  Create synthetic verse TOC entries: path = `<chapter path> / פסוק
    <letters>`, Level = chapter level + 1, LineIndex = the marker's line.
4.  Index these entries using the same analyzer, fields, and tie-breaking
    rules as all other documents.
5.  Commit the completed additions; the live reader picks them up on its
    next refresh.

This second pass exists only to compensate for the missing verse TOC
data and should not slow down the primary indexing process. On the current
database it yields ~23,200 verse documents — matching the canonical Tanach
verse count.

## Search Examples

All examples below were verified against the current database. "Top results"
means the first hits after ordering (Level, then TreeOrder); results are
always uncapped — only the leading hits are shown.

### Chapter search

Query: `בראשית פרק יב`

```text
בראשית / פרק יב                          (level 1)
תרגום אונקלוס על בראשית / פרק יב         (level 1)
תרגום יונתן על בראשית / פרק יב           (level 1)
רש"י על בראשית / פרק יב                  (level 1)
…every book on the בראשית shelf, in catalog order
```

### Generated Tanach verse

Query: `בראשית ד יד` (no פרק/פסוק words needed)

```text
בראשית / פרק ד / פסוק יד                 (level 2 — generated verse doc)
…the same verse in each commentary that has it, in catalog order
```

### Catalog term + token-order discard

Query: `תנך בראשית ד יד`

-   `תנך` matches only the `CatalogPath` field → exempt from the order rule.
-   `בראשית ד יד` must appear in the TOC path in typed order →
    `בראשית / פרק ד / פסוק יד` is returned; `בראשית / פרק יד / פסוק ד`
    is **discarded** (an in-order sibling exists in the same book+level).

Query: `שמות יד ד` (reversed)

```text
רש"י על שמות / פרק יד / פסוק ד           (in typed order — kept)
רשב"ם על שמות / פרק ד / פסוק יד          (kept although out of order —
                                          רשב"ם has no comment on 14:4,
                                          so there is no in-order sibling)
```

### Title word order never filters

`משנה תורה הלכות שבת` and `תורה משנה הלכות שבת` return the **identical**
result set — title/catalog words cannot differ in order between documents
of the same book, so the discard rule never fires on them.

### Amud (daf) equivalence

`פסחים דף י:` and `פסחים דף י עמוד ב` are the same query, resolving to the
עמוד ב child:

```text
פסחים / דף י / עמוד ב                    (level 2)
רש"י על פסחים / דף י / עמוד ב
…
```

`פסחים דף י.` targets the עמוד א children; the bare `פסחים דף י` surfaces
the level-1 `פסחים / דף י` parents first (each navigating to its עמוד א
line):

```text
פסחים / דף י                             (level 1)
רש"י על פסחים / דף י
תוספות על פסחים / דף י
…
```

### שולחן ערוך spelling variants

`שלחן ערוך אורח חיים סימן ב`, `שולחן ערוך אורח חיים סימן ב`,
`שו"ע אורח חיים סימן ב`, `ש''ע אורח חיים סימן ב` all return the same set:

```text
שולחן ערוך, אורח חיים / סימן ב           (level 1)
נתיב חיים על שלחן ערוך אורח חיים / סימן ב
…
פרי מגדים על אורח חיים / משבצות זהב / סימן ב   (matched via CatalogPath —
                                          its title has neither שולחן nor ערוך)
```

Bare `שוע` (no quote mark) is deliberately unmapped and returns nothing.

### Alternative TOC structures

Query: `בראשית נח עליה א`

```text
בראשית / נח / עליה א                     (level 2 — alt-structure entry)
תרגום אונקלוס על בראשית / נח / עליה א
```

### Single word

Query: `בראשית` — book-title documents (level 0) come first, the חומש
בראשית leading by catalog position, followed by level-1 TOC entries of
books whose path or shelf contains the term.

### No partial words

Query: `בראשי` returns nothing — matching is exact-term only (no prefix
or fuzzy matching), by design.

## Reference Figures (current database, Lucene.NET 4.8 implementation)

-   ~1.39M documents (books + TOC + alt-TOC structures + Tanach verses)
-   Index size on disk: ~45 MB
-   Full rebuild: ~20–30s, searchable throughout
-   Typical multi-word search: a few tens of milliseconds; broad one-word
    queries (100k+ matches): a few hundred milliseconds with the
    materialization cap (was ~10 s uncapped)
