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

type LinesContentInstance = {
  scrollToLineIndex: (lineIndex: number, occurrence?: number, forceScroll?: boolean) => void
}

type CommentaryViewInstance = Record<string, unknown>

export function useBookViewKeyboardShortcuts(
  linesContentRef: () => (LinesContentInstance & { $el?: HTMLElement }) | null,
  commentaryViewRef: () => (CommentaryViewInstance & { $el?: HTMLElement }) | null,
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
    const commentaryRoot = commentaryViewRef()?.$el
    const focusInScroller =
      (linesRoot != null && focused != null && linesRoot.contains(focused)) ||
      (commentaryRoot != null && focused != null && commentaryRoot.contains(focused))

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

    if (isZoomIn) bookViewStore.zoomIn()
    else if (isZoomOut) bookViewStore.zoomOut()
    else bookViewStore.resetZoom()
  })
}
