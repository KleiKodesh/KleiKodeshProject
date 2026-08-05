# Catalog User Folders

Lets the user add a folder from their own computer to the book catalog, so their own documents sit alongside the library. **Not yet implemented** — a first attempt was built and reverted (see *Why the first attempt was reverted*). This document is the design to build against when the feature is picked up.

## Scope

- A toolbar button on the catalog page (next to the three view-mode buttons) opens a folder picker and adds the chosen folder to the catalog.
- Only the folder **path** is persisted. Contents are never cached — the catalog reflects what is on disk right now.
- The folder appears as a top-level catalog entry whose subtree mirrors the folder hierarchy. Its files open in the normal local-file viewers (`/pdf-view`, `/html-view`, `/txt-view`), since they are files on disk, not rows in the seforim DB.
- A delete affordance on the added folder's own row (hover-revealed) removes it from the catalog. Nothing on disk is touched. Sub-folders inside it get no delete button.
- Must work identically in dev (service RPC) and hosted (WebView2 bridge).
- File types are DocumentLocator's indexed set — the 12 extensions in `MftCrawler.AllowedExtensions`.

## Loading: lazy, per level

**This is the part the first attempt got wrong and the main thing to get right.**

Load **one directory level at a time, on expand**. Do not walk the whole hierarchy up front. The user sees what they navigate to and nothing more:

- Adding a folder scans **only its immediate children** (the files and sub-folder names directly inside it) — enough to render the folder's own row and its first level.
- Entering or expanding a sub-folder scans **that** directory, again one level deep.
- A directory already visited this session may be re-read on revisit; that is correct and cheap. There is still no cache — "lazy" means *scan less*, not *remember more*.

Consequences to design for:

- `CategoryNode` for a folder directory needs a "not yet loaded" state distinct from "loaded and empty", so the tree view can show an expander before the children are known.
- The tree view (which flattens the whole catalog for `TreeView`) cannot flatten an unloaded folder subtree. Either it loads a level when a folder row expands, or folder subtrees stay collapsed until visited.
- Nothing about the folder half of the tree may be awaited on a path that renders the library. See below.

### Non-blocking requirement

The scan must never be on the critical path of a page load. The first attempt regressed demo-app load time by wiring the re-scan into `booksDataStore.ensureLoaded()`, which is called from ~7 unrelated places (every book open, every commentary panel load, the FTS page, daf-yomi, app boot warmup) — so each of those awaited a recursive disk walk.

Rules:

- The folder scan belongs to the **catalog page**, not to `ensureLoaded()`. `ensureLoaded()` must stay a cheap "is the catalog in memory" gate.
- Fire it un-awaited. The library tree renders immediately; folder rows appear when their level resolves.
- A folder on a disconnected or sleeping drive must degrade to "no rows for that folder", never to a stalled page.

## Search

Folder contents are **not** indexed by the app. Search them through DocumentLocator, which already indexes the filesystem.

- To search within a user folder, **append the folder path to the search string** and hand it to DocumentLocator. The path acts as the scope filter, so results come back restricted to that folder. No separate index, no app-side matching, and it stays correct as the folder changes.
- Do **not** add folder files to the catalog's own in-memory search index, and do not add them to the FTS book list — they have no rows in either.

### Result ordering

Results from a user folder are **level 0** results (the same rank as a book title match, not a TOC-entry match), and are ordered **after books**:

```
1. book results          (seforim DB)
2. user-folder results   (level 0, from DocumentLocator, scoped by folder path)
3. TOC results           (deeper levels)
```

This matches the existing rule that custom entries sort after DB entries.

## Data model notes

Whatever shape the rebuild takes, these held up and are worth keeping:

- **Negative ids for custom entries.** The catalog already treats negative ids as user-defined and sorts them last (`bookCatalogTree.ts`). `-1` (ROOT) and `-999999` (the orphaned-books bucket) are taken; allocate below them.
- **A file-backed row needs its own marker** (an absolute path on the row) so every consumer can tell it apart from a DB book. Its presence is what routes a click to the local-file viewer instead of `/book-view`.
- **The delete affordance keys off "is this the added folder's own root"**, not "is this custom" — a marker set only on the top-level node, never on nested directories.
- **Match persisted folders by path, not by node id**, anywhere state outlives a re-scan (breadcrumbs, the remove handler). Ids are re-minted on every scan; paths are stable.

## Consumers that must be checked

Adding file-backed rows to the shared books store reaches well beyond the catalog page. The first attempt found these the hard way — re-check each:

| Consumer | Requirement |
|---|---|
| Home search dropdown / address bar | A folder file must open in the local-file viewer. Routing it by `bookId` to `/book-view` renders an empty book. The dropdown's emit must carry enough to tell a file from a book. |
| Daf-yomi navigation | Takes the top `filterBooksByWords` candidate and queries its TOC. Must exclude file-backed rows or a filename can outrank the real tractate and hijack the button. |
| FTS filter panel + filters | Sizes its "everything is selected" set from the book list; that test guards a hot streaming fast path. Must count DB books only, or the fast path silently turns off and the tree lists checkboxes that can never match. |
| Commentary metadata pass | Walks up to a `parentId == null` root to derive a book's period. A reachable folder node stamps the *folder's name* as the period — an unrecognised era in the FTS chronological sort. Exclude folder subtrees from the map, not just from the loop. |
| Catalog TOC heuristics | File-backed rows have no TOC rows; drop them **before** the candidate cap or they push real books out. |
| Catalog search index | Stores book *indices* in a typed array. Once the book count is user-controlled it is no longer bounded by the DB — a `Uint16Array` wraps silently past 65535 and maps tokens to the wrong book. |
| `treeOrder` | A global catalog-order rank used as a final tiebreak. Folder books must be numbered *past* the DB books, or they collide with the first DB books and sort first. |

## Concurrency

Scans are asynchronous and user actions overlap them (add, then quickly remove). Guard with a generation token so a slower earlier scan cannot overwrite a newer result — otherwise an in-flight add lands after a remove and resurrects the folder. Any list built across an `await` (e.g. "which folders were truncated") must be accumulated locally and published once, or two overlapping scans interleave into one mixed list.

## Why the first attempt was reverted

It worked end-to-end and was verified live (add → scan → render → open a file → remove, in dev with the real service), but it took the wrong shape in two ways:

1. **Eager full-hierarchy scan.** It walked the entire folder tree on every catalog visit, bounded only by caps of 20 000 files / depth 12. Correct, and it satisfied "no caching", but it did far more work than the user's view ever needed.
2. **Wired into `ensureLoaded()`.** That put a disk walk on ~7 hot paths unrelated to the catalog and measurably slowed app load.

The design above fixes both: scan one level, on demand, off the critical path. The consumer table and the data-model notes are the salvage from that attempt — they were found by auditing and by live testing, and they will apply to any rebuild.
