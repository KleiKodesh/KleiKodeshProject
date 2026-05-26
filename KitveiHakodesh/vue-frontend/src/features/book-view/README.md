# book-view

Main book reader. Split pane with text above and commentary below, shared side panel for tools, search bar, and toolbar.

## Top-Level Components

**BookViewPage.vue** - top-level orchestrator. The right place to add new panels or cross-cutting book-view behavior.

**BookViewToolbar.vue** - zoom, search, TOC toggle, and bottom panel toggle. Add new toolbar actions here.

**BookViewSplitPane.vue** - thin wrapper around `SplitPane` for the text/commentary split. Do not add logic here.

**BookViewSidePanel.vue** - shared side-panel shell for book-view tools such as TOC and commentary filters.

**BookViewSearchBar.vue** - inline search bar. Query input, mode selection, and match navigation.

## Lines Subfolder

All line-display logic lives in `lines/`. See `lines/README.md` for details.

Import line components from the subfolder:

```typescript
import BookViewLinesContent from './lines/BookViewLinesContent.vue'
import { useLines } from './lines/useBookViewLinesTable'
```

## TOC Subfolder

All table-of-contents logic lives in `toc/`. See `toc/README.md` for details.

Import TOC components from the subfolder:

```typescript
import BookViewTocTree from './toc/BookViewTocTree.vue'
import { useToc } from './toc/useBookViewToc'
```

## Commentary Subfolder

All commentary-related components, composables, and utilities live in `commentary/`. See `commentary/README.md` for details.

Import commentary components from the subfolder:

```typescript
import CommentaryView from './commentary/CommentaryView.vue'
import { useCommentary } from './commentary/useCommentary'
```

## Composables

**useBookViewSearch.ts** - in-book content search, line-based.


