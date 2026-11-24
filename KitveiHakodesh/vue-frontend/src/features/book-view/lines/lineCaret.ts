/**
 * The KEYBOARD CARET of the lines pane. Caret browsing (F7) moves from the document's
 * collapsed selection; an opening only focuses the viewport, so without a caret of its
 * own the browser starts from the top of the mounted window — rows above what is shown.
 *
 * So an opening seats the caret at the start of the text visible at the viewport's top.
 * Collapsed, so nothing reads it as a selection, and invisible until caret browsing is on.
 */
import { domPositionFromStrippedOffset } from '@/utils/lineTextOffsets'
import { hasActiveTextSelection } from '@/composables/useContextMenuLongPress'

const MIN_VISIBLE_PX = 24

/**
 * Collapses the document selection onto the first text visible at `viewport`'s top.
 * False when no line is rendered. Call it once the rows are in the DOM (the scroller's `opened`).
 */
export function placeCaretAtViewportStart(viewport: HTMLElement | null | undefined): boolean {
  if (!viewport || hasActiveTextSelection()) return true
  const selection = window.getSelection()
  if (!selection) return true
  const top = viewport.getBoundingClientRect().top
  // First rendered line with a readable part inside the viewport (a sliver of a line
  // cut off at the top is not what the reader sees) — by geometry, so it does not
  // depend on a hit test that needs the line to be painted already.
  const line = [...viewport.querySelectorAll<HTMLElement>('.line')].find((element) => {
    const rect = element.getBoundingClientRect()
    return rect.bottom - top > Math.min(rect.height / 2, MIN_VISIBLE_PX)
  })
  if (!line) return false // nothing rendered yet
  // A line cut off at the top: its start is not what the reader sees, so take the text
  // at the viewport edge; otherwise (or when the hit test finds nothing) the line start.
  const lineRect = line.getBoundingClientRect()
  let at: { node: Node; offset: number } | null = null
  if (lineRect.top < top) {
    const hit = document.caretRangeFromPoint?.(lineRect.left + lineRect.width / 2, top + 2)
    if (hit && line.contains(hit.startContainer)) at = { node: hit.startContainer, offset: hit.startOffset }
  }
  at ??= domPositionFromStrippedOffset(line, 0)
  if (!at) return false
  const range = document.createRange()
  range.setStart(at.node, at.offset)
  range.collapse(true)
  selection.removeAllRanges()
  selection.addRange(range)
  return true
}

/**
 * Whether an opening may take focus: only when nothing else holds it, so a book that
 * opens while the reader is in another pane, the address bar or a dialog stays out.
 */
export function mayTakeFocus(element: HTMLElement | null | undefined): boolean {
  if (!element) return false
  const active = document.activeElement
  return !active || active === document.body || element.contains(active)
}
