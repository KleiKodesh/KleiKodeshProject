# commentary

Commentary display, filtering, and navigation for the book view. All commentary-related logic is contained here.

## Components

**CommentaryView.vue** - main commentary display grouped by book. Renders commentary groups with headers and content. Handles scroll restoration and group rendering.

**CommentaryHeader.vue** - header for a commentary book group with connection type selector and navigation buttons.

**CommentaryHeaderNav.vue** - previous/next section navigation within a commentary book.

**useCommentaryPanelSlot.ts** - the per-panel factory. The book view runs one panel per `CommentarySlot` ('bottom', 'side', 'side-left'); this assembles everything one panel owns - pin, filter tree state + check-tree scope, scroll lifecycle, in-panel search, render cache, divider fraction - on top of the one shared `useCommentary` fetch. See `../README.md` for the per-panel vs shared table.

**CommentaryPanelHost.vue** - renders one panel: binds a `CommentaryPanel` slot object plus the shared line-level props to `CommentaryView`, so `BookViewPage` does not repeat a thirty-prop block once per panel. For the two SIDE slots it also hosts that panel's filter dropdown, clipped to its own column; the bottom panel's runs the full body height and so is rendered by `BookViewPage` instead.

**CommentaryTreePanel.vue** - the filter tree for toggling individual commentary books on/off. One instance per commentary panel, so its `scopeKey` is fixed by construction (it used to be a single shared instance re-pointed between panels, kept honest only by a `key`). Uses `buildCommentaryTree` from `useCommentary.ts` to render the tree in normal mode. In search mode, renders a flat result list using `SegmentSearchTree` — matches query words across the full path (sectionLabel / subSectionLabel / bookTitle). When a search is active, emits an effective hidden set that merges the user's explicit hidden set with all groups outside the search results, so the commentary view automatically shows only what is both checked and in the search results.

**CommentaryTreeSectionNode.vue** - single node in the commentary filter tree.

## Composables

**useCommentary.ts** - the reactive composable shell. Owns all Vue state (`groups`, `staticFilterGroups`, `loading`, etc.), the `load` watcher, and `ensureStaticFilterGroupsLoaded`. Exports the `CommentaryGroup`, `CommentaryLine`, and `CommentaryBookEntry` interfaces plus the re-exported constants from `commentaryConnectionTypes.ts`. Delegates all data fetching and transformation to `commentaryGroupBuilder.ts`.

**commentaryConnectionTypes.ts** - all connection type knowledge: the DB→canonical mapping, Hebrew section labels, the reverse label→type lookup, the lazy-loaded ID table (`ensureConnectionTypeNamesLoaded`, `getConnectionTypeName`, `getConnectionTypeId`), and derived helpers (`getPrimaryConnectionType`, `getCommentaryConnectionTypeIds`). Nothing in the commentary feature should re-derive connection type logic outside this file.

The canonical connection types are:

```
SOURCE | MESORAH_HASHAS | TARGUM | COMMENTARY | OTHER | REFERENCE
```

DB names map to canonical types as follows:

| DB name | Canonical | Section label |
|---|---|---|
| `SOURCE` | `SOURCE` | מקור |
| `MESORAH_HASHAS` | `REFERENCE` | ציונים |
| `TARGUM` | `TARGUM` | תרגומים |
| `COMMENTARY` | `COMMENTARY` | מפרשים |
| `SUPER_COMMENTARY` | `COMMENTARY` | מפרשים |
| `PARSHANUT` | `COMMENTARY` | מפרשים |
| `MIDRASH` | `COMMENTARY` | מפרשים |
| `REFERENCE` | `REFERENCE` | ציונים |
| `EIN_MISHPAT` | `EIN_MISHPAT` | עין משפט |
| `MISHNAH_IN_TALMUD` | `REFERENCE` | ציונים |
| `OTHER` / unknown | `OTHER` | קשרים |

`SOURCE`, `TARGUM`, `COMMENTARY`, and `EIN_MISHPAT` are **static filter types** — checked by default in the filter panel. `OTHER` and `REFERENCE` are unchecked by default.

`EIN_MISHPAT` is its own canonical type (not merged into `REFERENCE`) because its links are forward-direction and must be kept in the forward query path. `SOURCE` is the only type skipped in the forward query — its forward links are unreliable, so it is fetched via a reverse lookup instead. `TARGUM` is an ordinary forward link and travels the same path as `COMMENTARY`.

**commentaryGroupBuilder.ts** - all data fetching and group building. Contains `buildCommentaryGroupsFromEntries`, `buildCommentaryGroupsFromCombined`, `fetchSourceEntriesViaReverseQuery`, `buildStaticCommentaryFilterGroups`, and the category ordering helpers. No Vue reactivity — purely async functions and pure transformations.

**useCommentaryRender.ts** - manages content rendering for commentary lines: diacritics filtering, divine name censoring, search highlighting, and render caching to avoid re-running expensive DOM operations on every render cycle.

**useCommentaryScroll.ts** - manages scroll behavior for commentary: sticky header tracking, scroll position capture/restore, and scroll-to-group navigation. Handles the complex scroll restoration logic when groups reload.

**useCommentaryTocPaths.ts** - fetches and caches TOC paths for commentary groups asynchronously. Keyed by bookId — resolved after groups load, never blocks rendering.

**useCommentaryCopy.ts** - mirrors the book view copy logic for multi-line commentary selections. Provides the same three copy modes (block, source at end, source at start). Selection extraction uses `[data-line-id]` attributes on `.line` elements and counts stripped (diacritic-removed) character offsets, matching the same offset model used by the highlight storage layer.

**useCommentaryNavigation.ts** - next/prev section navigation for a commentary panel. One instance per panel (see `useCommentaryPanelSlot.ts`), so navigating reopens and re-pins only the panel whose header button was pressed.

**useCommentarySearch.ts** - commentary search against a flat index.

**useCommentaryTreeSearch.ts** - search logic for the commentary filter tree. Matches query words across the full path using `SegmentSearchTree`.

**useCommentaryHighlights.ts** - manages user highlights for all commentary books visible in either commentary panel (one shared instance over the union of every panel's groups). Loads highlights lazily per commentary bookId as groups become visible. Supports apply/clear with overlap rules, persisted to `user_settings.db`.

**useCommentaryNotes.ts** - manages user notes for all commentary books visible in either commentary panel (one shared instance over the union of every panel's groups). Lazy viewport-driven loading by commentary bookId, with create/update/delete mutations.

## Utilities

**commentaryNavigation.ts** - commentary section navigation helpers (next/prev section, TOC-aware).

**commentaryTreeTypes.ts** - TypeScript types for the commentary filter tree.

**DEBUG_NOTES.md** - debug investigation notes for the pinned commentary scroll bugs. Kept for future reference if similar issues resurface.

## Imports from parent book-view

When importing commentary components or composables from the parent `book-view/` folder, use relative imports:

```typescript
import CommentaryView from './commentary/CommentaryView.vue'
import { useCommentary } from './commentary/useCommentary'
```

Never import from the old flat paths — all commentary code is now in this subfolder.

## Data Structures

**`CommentaryGroup`** — one book's lines under one section:
```typescript
interface CommentaryGroup {
  bookId: number
  bookTitle: string
  path: string
  connectionTypes: string[]   // canonical names, e.g. ['COMMENTARY']
  lines: CommentaryLine[]
  category?: string           // e.g. 'ראשונים', 'אחרונים'
  sectionLabel?: string       // e.g. 'מפרשים' (from CONNECTION_TYPE_SECTION_LABELS)
  subSectionLabel?: string    // e.g. 'ראשונים' (same as category for COMMENTARY groups)
}
```

**`CommentaryVisibilityItem`** — one row in the filter panel:
```typescript
interface CommentaryVisibilityItem {
  bookId: number
  sectionLabel: string       // e.g. 'מפרשים'
  subSectionLabel: string    // e.g. 'ראשונים', or ''
  bookTitle: string
  isChecked: boolean         // user toggle
  isInSearchResults: boolean // filter search match (default true when no search active)
}
// isVisible = isChecked && isInSearchResults
```

**`CommentaryTreeState`** — persisted filter panel state:
```typescript
interface CommentaryTreeState {
  searchQuery: string
  tokens: string[]
  visibilityList: CommentaryVisibilityItem[]
}
```

**`CommentaryBookEntry`** — intermediate build structure used inside `commentaryGroupBuilder.ts`:
```typescript
interface CommentaryBookEntry {
  bookId: number
  bookTitle: string
  connectionTypes: string[]
  lines: CommentaryLine[]
  category: string
  treeOrder: number
  primaryConnectionType: string
}
```

## Data Flow

1. `ensureConnectionTypeNamesLoaded()` fetches the `connection_type` table and populates the id↔name maps.
2. `useCommentary.load()` fires two queries in parallel: the forward commentary query — which carries `TARGUM` along with `COMMENTARY` and the rest — and `fetchSourceEntriesViaReverseQuery`. Only `SOURCE` needs a reverse lookup, because its forward-direction DB links are unreliable.
3. `buildCommentaryGroupsFromCombined()` merges all entries, skips any forward-query rows that canonicalize to `SOURCE` (already covered by the reverse query), and passes the combined list to `buildCommentaryGroupsFromEntries()`.
4. `buildCommentaryGroupsFromEntries()` groups by canonical connection type and emits ordered `CommentaryGroup[]`: SOURCE → TARGUM → COMMENTARY (by category) → EIN_MISHPAT → OTHER (by category) → REFERENCE.
5. `ensureStaticFilterGroupsLoaded()` is called lazily when the filter panel first opens. It runs the same forward + reverse queries without line content to build the full book list for the filter tree. The result promise is cached per book at MODULE level (survives tab switches) — the link-table scans behind it cost seconds on heavily-linked books, and re-running them on every remount was a major part of the slow-tab-return bug.
6. `CommentaryTreePanel.vue` + `useCommentaryTreeSearch.ts` build a hierarchical tree from `visibilityList` and support token-based search (Enter or `@` commits a token; results are a union across all tokens).
7. `CommentaryView.vue` filters `groups` through `visibilityList` (`isChecked && isInSearchResults`) before rendering.
