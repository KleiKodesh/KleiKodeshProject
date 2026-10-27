/**
 * Where the KEYBOARD CARET sits in the lines pane.
 *
 * Caret browsing (F7 — Chromium's own; there is no app code behind it) navigates and
 * selects from the DOCUMENT's caret, the collapsed `window.getSelection()`. Clicking a
 * line puts that caret where the reader clicked, which is why the arrows follow the
 * text during normal reading.
 *
 * An OPENING never clicks. It lands the scroller on a line and hands the viewport
 * focus — and focus is not a caret: with none inside the focused element the browser
 * picks its own starting point, and in a WindowedScroller that is the start of the
 * MOUNTED WINDOW, which a landing builds ROWS_ABOVE_ON_REBUILD rows above the target
 * and then grows upward by a screenful more (useWindowedScrollerWindow's `fill`
 * extends the top side until a full edge of content sits above the viewport). So the
 * view showed the line that was asked for while the caret sat some dozens of lines
 * higher, and the first arrow key moved from THERE: the book opened in the right place
 * and the cursor then jumped to another one.
 *
 * Opening therefore places the caret itself, on the line it opened. Collapsed, so
 * nothing in the app reads it as a selection — hasActiveTextSelection,
 * useSelectAllInContainer and useVirtualizedTextSelection all ignore a collapsed one —
 * and invisible until the reader turns caret browsing on.
 */
import { domPositionFromStrippedOffset } from '@/utils/lineTextOffsets'
import { hasActiveTextSelection } from '@/composables/useContextMenuLongPress'

/**
 * Collapses the document selection onto the start of `row`'s text.
 *
 * `row` is a scroller row element (`WindowedScrollerApi.rowElement`); the text is the
 * `.line` inside it, so a row without one — not rendered, or a placeholder — is left
 * alone.
 *
 * A real selection the reader is holding wins, wherever it is. The selection is
 * document-wide: at the moment a book opens, text that is still selected belongs to
 * something still on screen — the other split pane, a commentary panel — and wiping it
 * to seat a caret would cost them their selection for a caret they may never use. A
 * collapsed caret (or none at all) carries no such intent and is replaced.
 */
export function placeCaretAtRowStart(row: HTMLElement | null): void {
  const lineElement = row?.querySelector('.line') as HTMLElement | null
  if (!lineElement) return
  if (hasActiveTextSelection()) return
  const selection = window.getSelection()
  if (!selection) return

  const position = domPositionFromStrippedOffset(lineElement, 0)
  if (!position) return
  const range = document.createRange()
  range.setStart(position.node, position.offset)
  range.collapse(true)
  selection.removeAllRanges()
  selection.addRange(range)
}
