# book-view

Main book reader. Split pane with text above and commentary below, TOC side panel, floating search bar, and toolbar.

**BookViewPage.vue** — top-level orchestrator. The right place to add new panels or cross-cutting book-view behavior.

**BookViewToolbar.vue** — zoom, search, TOC toggle, and bottom panel toggle. Add new toolbar actions here.

**BookViewSplitPane.vue** — thin wrapper around `SplitPane` for the text/commentary split. Do not add logic here.

**BookViewLinesContent.vue** — virtual-scrolled main text. Handles line selection and communicates the selected line to the commentary panel. Any change to how lines are rendered or selected belongs here.

**BookViewSearchBar.vue** — floating in-book search bar. Query input, mode selection, and match navigation.

**BookViewTocTree.vue** — TOC side panel with main and alternate structures.

**BookViewTocTreeSection.vue** — section header separating main and alternate TOC structures.

**CommentaryView.vue** — commentary display grouped by book.

**CommentaryHeader.vue** — header for a commentary book group with connection type selector and navigation.

**CommentaryHeaderNav.vue** — previous/next section navigation within a commentary book.

**CommentaryTreePanel.vue** — TOC tree for the commentary.

**CommentaryTreeViewNode.vue** — single node in the commentary TOC tree.

**CommentaryTypeDropdown.vue** — dropdown for selecting the commentary connection type.

**CommentaryPanel.vue** — container holding `CommentaryView` and `CommentaryTreePanel`.

**useToc.ts** — loads TOC entries and alternate structures for a book. Builds an entry-id-to-breadcrumb path map. Use `getActiveTocEntry` and `getTocPath` rather than computing paths manually.

**useLinesTable.ts** — paginated line fetching in chunks of 200. Pre-allocates placeholder slots for correct virtualizer height. Use `prioritise(lineIndex)` to move a chunk to the front of the queue when the user jumps to a specific position.

**useCommentary.ts** — fetches linked commentary for a selected line or range, groups by connection type and category. Returns `CommentaryGroup[]`. All commentary data fetching goes through here.

**useBookViewSearch.ts** — in-book content search, line-based.

**useCommentarySearch.ts** — commentary search against a flat index.
