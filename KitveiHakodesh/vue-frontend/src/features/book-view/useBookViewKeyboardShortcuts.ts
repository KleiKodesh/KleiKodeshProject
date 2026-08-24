/**
 * Keyboard shortcuts for the book view.
 *
 * Handles two categories:
 * - Ctrl+±/0: zoom. When focus is inside a scroller, the scroller's own
 *   useZoomHandler takes over (but we still preventDefault to block the browser).
 *   When focus is elsewhere (toolbar, etc.) we zoom both panels together.
 * - Ctrl+Left / Ctrl+Right: section navigation (RTL — Left = next, Right = previous).
 */
import { inject } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useBookViewStore } from '@/stores/bookViewStore'
import { usePaneNavigation } from '@/composables/usePaneNavigation'

// Only `$el` is used here (see below) — the scroller API is not part of this
// composable's contract.
type LinesContentInstance = Record<string, unknown>

type CommentaryViewInstance = Record<string, unknown>

export function useBookViewKeyboardShortcuts(
  linesContentRef: () => (LinesContentInstance & { $el?: HTMLElement }) | null,
  /** Every commentary scroller in this pane — one per commentary panel. */
  commentaryViewRefs: Array<() => (CommentaryViewInstance & { $el?: HTMLElement }) | null>,
  hasToc: () => boolean,
  navigateToAdjacentTocSection: (direction: 'next' | 'previous') => void,
) {
  const bookViewStore = useBookViewStore()
  const paneNavigation = usePaneNavigation()
  const paneId = inject<1 | 2>('paneId', 1)

  useEventListener(window, 'keydown', (event: KeyboardEvent) => {
    // Only respond when split view is off, or this pane is the focused one.
    if (bookViewStore.splitViewEnabled && bookViewStore.focusedPaneId !== paneId) return
    // Only respond when the active tab in this pane is a book view.
    if (paneNavigation.activeTab.route !== '/book-view') return
    const ctrl = event.ctrlKey || event.metaKey
    if (!ctrl) return

    const isZoomIn = event.code === 'Equal' || event.code === 'NumpadAdd'
    const isZoomOut = event.code === 'Minus' || event.code === 'NumpadSubtract'
    const isReset = event.code === 'Digit0' || event.code === 'Numpad0'
    const isNextSection = event.code === 'ArrowLeft'
    const isPreviousSection = event.code === 'ArrowRight'

    if (!isZoomIn && !isZoomOut && !isReset && !isNextSection && !isPreviousSection) return

    const focused = document.activeElement
    const linesRoot = linesContentRef()?.$el
    // The commentary check is structural (the slot attribute on each panel's
    // root) rather than $el-based: a stray comment beside a template root turns
    // the component into a fragment and $el into a comment node, which silently
    // breaks contains() - that exact regression double-zoomed the panels once.
    const focusInScroller =
      focused != null &&
      ((linesRoot != null && linesRoot.contains(focused)) ||
        (focused instanceof Element && focused.closest('[data-commentary-slot]') != null))

    if (isNextSection || isPreviousSection) {
      if (!hasToc()) return
      event.preventDefault()
      navigateToAdjacentTocSection(isNextSection ? 'next' : 'previous')
      return
    }

    // Prevent the browser from applying its own page zoom regardless of focus.
    event.preventDefault()

    // If a scroller owns focus, its own useZoomHandler handles the zoom — skip.
    if (focusInScroller) return

    // Zoom this pane's own tab — the no-arg forms target pane 1's active tab,
    // which is the wrong book when these shortcuts fire in pane 2.
    const tab = paneNavigation.activeTab
    if (isZoomIn) bookViewStore.zoomIn(tab.id, tab.bookId)
    else if (isZoomOut) bookViewStore.zoomOut(tab.id, tab.bookId)
    else bookViewStore.resetZoom(tab.id, tab.bookId)
  })
}
