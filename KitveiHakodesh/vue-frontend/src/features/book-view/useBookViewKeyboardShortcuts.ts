/**
 * Keyboard shortcuts for the book view.
 *
 * Handles two categories:
 * - Ctrl+±/0: zoom. When focus is inside a scroller, the scroller's own
 *   useZoomHandler takes over (but we still preventDefault to block the browser).
 *   When focus is elsewhere (toolbar, etc.) we zoom both panels together.
 * - Ctrl+Left / Ctrl+Right: section navigation (RTL — Left = next, Right = previous).
 */
import { useEventListener } from '@vueuse/core'
import { useBookViewStore } from '@/stores/bookViewStore'
import { storeToRefs } from 'pinia'

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
  const { isBookViewActive } = storeToRefs(bookViewStore)

  useEventListener(window, 'keydown', (event: KeyboardEvent) => {
    if (!isBookViewActive.value) return
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
