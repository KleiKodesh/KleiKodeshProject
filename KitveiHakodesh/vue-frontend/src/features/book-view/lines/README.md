# lines

Virtual-scrolled main text display for the book view. Handles line rendering, selection, and persistence.

## Components

**BookViewLinesContent.vue** - virtual-scrolled main text. Handles line selection, scroll position persistence, and communicates the selected line to the commentary panel. Any change to how lines are rendered or selected belongs here.

## Composables

**useBookViewLinesTable.ts** - paginated line fetching in chunks of 200. Pre-allocates placeholder slots for correct virtualizer height. Use `prioritise(lineIndex)` to move a chunk to the front of the queue when the user jumps to a specific position.

**useBookViewLineRenderer.ts** - line content rendering with diacritics filtering, divine name censoring, and search highlighting. Caches rendered HTML per line to avoid re-running expensive transformations on every render cycle.

**useBookViewLineCopyMenu.ts** - context menu for copying selected lines with optional source attribution. Handles both partial selections and select-all.

## Imports

Import from this subfolder:

```typescript
import BookViewLinesContent from './lines/BookViewLinesContent.vue'
import { useLines } from './lines/useBookViewLinesTable'
import { useBookViewLineRenderer } from './lines/useBookViewLineRenderer'
import { useBookViewLineCopyMenu } from './lines/useBookViewLineCopyMenu'
```
