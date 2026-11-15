/**
 * "Click works only on the text" helper for any block of flowing text.
 *
 * - `isPointOnText` hit-tests the element's text nodes (not its padding or the empty
 *   space after a short last line). Rects are grown vertically to the full line box so
 *   the leading between lines still counts.
 * - `useTextHitCursor` adds the matching hover feedback: over text the cursor becomes a
 *   hand (pointer), elsewhere it is left alone.
 *
 * Usage:
 *   const { isOffText, onMouseMove } = useTextHitCursor(() => clickable.value)
 *   <div @mousemove="onMouseMove" @click="isOffText($event) || select()" />
 */

export function isPointOnText(el: HTMLElement, x: number, y: number): boolean {
  const lineHeight = parseFloat(getComputedStyle(el).lineHeight) || 0
  const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT)
  const range = document.createRange()
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    range.selectNodeContents(node)
    for (const r of Array.from(range.getClientRects())) {
      if (r.width === 0) continue
      const grow = Math.max(0, (lineHeight - r.height) / 2)
      if (x >= r.left && x <= r.right && y >= r.top - grow && y <= r.bottom + grow) return true
    }
  }
  return false
}

/**
 * @param enabled  Whether the element is clickable right now (e.g. commentary open).
 * @param skip     Elements for which the text rule does not apply (e.g. inline rows).
 */
export function useTextHitCursor(
  enabled: () => boolean = () => true,
  skip: (el: HTMLElement) => boolean = () => false,
) {
  /** True when the event's point is NOT on the currentTarget's text. */
  function isOffText(event: MouseEvent): boolean {
    const el = event.currentTarget as HTMLElement | null
    if (!el || skip(el)) return false
    return !isPointOnText(el, event.clientX, event.clientY)
  }

  function onMouseMove(event: MouseEvent) {
    const el = event.currentTarget as HTMLElement
    if (!enabled() || skip(el) || isOffText(event)) {
      if (el.style.cursor) el.style.cursor = ''
      return
    }
    if (el.style.cursor !== 'pointer') el.style.cursor = 'pointer'
  }

  return { isOffText, onMouseMove }
}
