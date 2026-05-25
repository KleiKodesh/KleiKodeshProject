# toc

Table of Contents display and navigation for the book view. Handles TOC tree rendering, search, and scroll tracking.

## Components

**BookViewTocTree.vue** - main TOC panel component. Renders the primary TOC and alternate TOC structures in a split pane. Handles TOC search and section selection.

**BookViewTocTreeSection.vue** - individual TOC section renderer. Displays a hierarchical tree of TOC entries with search filtering and click navigation.

## Composables

**useBookViewToc.ts** - loads TOC entries and alternate structures for a book. Builds an entry-id-to-breadcrumb path map. Use `getActiveTocEntry` and `getTocPath` rather than computing paths manually.

**useBookViewTocScrollTracking.ts** - tracks whether a programmatic TOC scroll is in progress so that scroll events do not overwrite the active TOC entry while the virtualizer is animating.

## Utilities

**tocSearchUtils.ts** - generic segment-aware search tree for hierarchical node lists. `SegmentSearchTree` matches query words as an ordered subsequence across ancestor path segments. Re-exports `SegmentSearchTree` as `SearchableTree` for backward compatibility.

## Imports

Import from this subfolder:

```typescript
import BookViewTocTree from './toc/BookViewTocTree.vue'
import { useToc } from './toc/useBookViewToc'
import { useBookViewTocScrollTracking } from './toc/useBookViewTocScrollTracking'
import { SearchableTree } from './toc/tocSearchUtils'
```
