# commentary

Commentary display, filtering, and navigation for the book view. All commentary-related logic is contained here.

## Components

**CommentaryView.vue** - main commentary display grouped by book. Renders commentary groups with headers and content. Handles scroll restoration and group rendering.

**CommentaryHeader.vue** - header for a commentary book group with connection type selector and navigation buttons.

**CommentaryHeaderNav.vue** - previous/next section navigation within a commentary book.

**CommentaryTreePanel.vue** - side-panel content for toggling individual commentary books on/off. Uses `buildCommentaryTree` from `useCommentary.ts` to render the tree in normal mode. In search mode, renders a flat result list using `SegmentSearchTree` — matches query words across the full path (sectionLabel / subSectionLabel / bookTitle). When a search is active, emits an effective hidden set that merges the user's explicit hidden set with all groups outside the search results, so the commentary view automatically shows only what is both checked and in the search results.

**CommentaryTreeSectionNode.vue** - single node in the commentary filter tree.

## Composables

**useCommentary.ts** - fetches linked commentary for a selected line or range, groups by connection type and category, and prepares the commentary-filter data. Returns live `groups` for the commentary panel plus `filterGroups` for the filter tree. All commentary data fetching goes through here.

**useCommentaryRender.ts** - manages content rendering for commentary lines: diacritics filtering, divine name censoring, search highlighting, and render caching to avoid re-running expensive DOM operations on every render cycle.

**useCommentaryScroll.ts** - manages scroll behavior for commentary: sticky header tracking, scroll position capture/restore, and scroll-to-group navigation. Handles the complex scroll restoration logic when groups reload.

**useCommentaryTocPaths.ts** - fetches and caches TOC paths for commentary groups asynchronously. Keyed by bookId — resolved after groups load, never blocks rendering.

**useCommentaryCopy.ts** - mirrors the book view copy logic for multi-line commentary selections. Provides the same three copy modes (block, source at end, source at start). Selection extraction uses `[data-line-id]` attributes on `.line` elements and counts stripped (diacritic-removed) character offsets, matching the same offset model used by the highlight storage layer.

**useCommentaryNavigation.ts** - next/prev section navigation for the commentary panel.

**useCommentarySearch.ts** - commentary search against a flat index.

**useCommentaryTreeSearch.ts** - search logic for the commentary filter tree. Matches query words across the full path using `SegmentSearchTree`.

## Utilities

**commentaryNavigation.ts** - commentary section navigation helpers (next/prev section, TOC-aware).

**commentaryTreeTypes.ts** - TypeScript types for the commentary filter tree.

## Imports from parent book-view

When importing commentary components or composables from the parent `book-view/` folder, use relative imports:

```typescript
import CommentaryView from './commentary/CommentaryView.vue'
import { useCommentary } from './commentary/useCommentary'
```

Never import from the old flat paths — all commentary code is now in this subfolder.
