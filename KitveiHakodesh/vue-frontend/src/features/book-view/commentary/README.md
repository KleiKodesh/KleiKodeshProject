# commentary

Commentary display, filtering, and navigation for the book view. All commentary-related logic is contained here.

## Components

**CommentaryView.vue** - main commentary display grouped by book. Renders commentary groups with headers and content. Handles scroll restoration and group rendering.

**CommentaryHeader.vue** - header for a commentary book group with connection type selector and navigation buttons.

**CommentaryHeaderNav.vue** - previous/next section navigation within a commentary book.

**CommentaryTreePanel.vue** - side-panel content for toggling individual commentary books on/off. Uses `buildCommentaryTree` from `useCommentary.ts` to render the tree in normal mode. In search mode, renders a flat result list using `SegmentSearchTree` — matches query words across the full path (sectionLabel / subSectionLabel / bookTitle). When a search is active, emits an effective hidden set that merges the user's explicit hidden set with all groups outside the search results, so the commentary view automatically shows only what is both checked and in the search results.

**CommentaryTreeSectionNode.vue** - single node in the commentary filter tree.

## Composables

**useCommentary.ts** - the reactive composable shell. Owns all Vue state (`groups`, `staticFilterGroups`, `loading`, etc.), the `load` watcher, and `ensureStaticFilterGroupsLoaded`. Exports the `CommentaryGroup`, `CommentaryLine`, and `CommentaryBookEntry` interfaces plus the re-exported constants from `commentaryConnectionTypes.ts`. Delegates all data fetching and transformation to `commentaryGroupBuilder.ts`.

**commentaryConnectionTypes.ts** - all connection type knowledge: the DB→canonical mapping, Hebrew section labels, the reverse label→type lookup, the lazy-loaded ID table (`ensureConnectionTypeNamesLoaded`, `getConnectionTypeName`, `getConnectionTypeId`), and derived helpers (`getPrimaryConnectionType`, `getCommentaryConnectionTypeIds`, `getTargumConnectionTypeIds`). Nothing in the commentary feature should re-derive connection type logic outside this file.

**commentaryGroupBuilder.ts** - all data fetching and group building. Contains `buildCommentaryGroupsFromEntries`, `buildCommentaryGroupsFromCombined`, `fetchSourceEntriesViaReverseQuery`, `fetchTargumEntriesViaReverseQuery`, `buildStaticCommentaryFilterGroups`, and the category ordering helpers. No Vue reactivity — purely async functions and pure transformations.

**useCommentaryRender.ts** - manages content rendering for commentary lines: diacritics filtering, divine name censoring, search highlighting, and render caching to avoid re-running expensive DOM operations on every render cycle.

**useCommentaryScroll.ts** - manages scroll behavior for commentary: sticky header tracking, scroll position capture/restore, and scroll-to-group navigation. Handles the complex scroll restoration logic when groups reload.

**useCommentaryTocPaths.ts** - fetches and caches TOC paths for commentary groups asynchronously. Keyed by bookId — resolved after groups load, never blocks rendering.

**useCommentaryCopy.ts** - mirrors the book view copy logic for multi-line commentary selections. Provides the same three copy modes (block, source at end, source at start). Selection extraction uses `[data-line-id]` attributes on `.line` elements and counts stripped (diacritic-removed) character offsets, matching the same offset model used by the highlight storage layer.

**useCommentaryNavigation.ts** - next/prev section navigation for the commentary panel.

**useCommentarySearch.ts** - commentary search against a flat index.

**useCommentaryTreeSearch.ts** - search logic for the commentary filter tree. Matches query words across the full path using `SegmentSearchTree`.

**useCommentaryHighlights.ts** - manages user highlights for all commentary books visible in the commentary panel. Loads highlights lazily per commentary bookId as groups become visible. Supports apply/clear with overlap rules, persisted to `user_settings.db`.

**useCommentaryNotes.ts** - manages user notes for all commentary books visible in the commentary panel. Lazy viewport-driven loading by commentary bookId, with create/update/delete mutations.

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
