import { onLongPress, type MaybeElementRef } from '@vueuse/core'

/**
 * True when the document currently holds a real (non-collapsed, non-whitespace)
 * text selection. Read on demand at the moment of a gesture — deliberately NOT
 * wired to `selectionchange`, so it adds no listeners and no reactivity churn.
 *
 * Used to keep text-selection gestures from being hijacked into something else:
 * a click that changes the commentary line, or a long-press that pops the context
 * menu over the text the user is in the middle of selecting.
 */
export function hasActiveTextSelection(): boolean {
  const selection = window.getSelection()
  return !!selection && !selection.isCollapsed && selection.toString().trim() !== ''
}

// A deliberate press should feel deliberate. 500ms (the VueUse default) trips on a
// normal lingering click; 600ms reads as an intentional long-press.
const LONG_PRESS_DELAY = 600

/**
 * Selection-aware long-press → context menu, shared by the book-view lines pane and
 * the txt view. Wraps VueUse's onLongPress with three guards against accidental
 * triggering:
 *   - Mouse presses are ignored entirely. A mouse already opens the context menu on
 *     right-click (@contextmenu), so long-press-with-a-mouse only misfires on a slow
 *     click — this is the real "too sensitive" on desktop. Touch and pen keep it,
 *     since they have no right-click. (An empty/unknown pointerType keeps long-press,
 *     so nothing regresses if WebView2 reports no type.)
 *   - VueUse's own distanceThreshold (default 10px) cancels a drag-select.
 *   - hasActiveTextSelection() cancels the case a drag can't catch — a tiny (<10px)
 *     or handle-driven selection that leaves the pointer stationary.
 *
 * onTrigger receives the originating pointer event; the caller decides where/whether
 * to show the menu (e.g. the lines pane also rejects presses on the RTL scrollbar).
 */
export function useContextMenuLongPress(
  target: MaybeElementRef,
  onTrigger: (event: PointerEvent) => void,
) {
  onLongPress(
    target,
    (event) => {
      if (event.pointerType === 'mouse') return
      if (hasActiveTextSelection()) return
      onTrigger(event)
    },
    { delay: LONG_PRESS_DELAY },
  )
}
