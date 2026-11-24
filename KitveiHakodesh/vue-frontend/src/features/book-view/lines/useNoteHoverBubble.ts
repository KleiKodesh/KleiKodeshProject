/**
 * Hovering a user-note marker opens the note editor bubble itself (no separate
 * read-only preview).
 *
 * A hover-opened bubble is temporary: it closes shortly after the pointer leaves
 * both the marker and the bubble. The moment the user clicks, focuses or types in
 * it — or clicks the marker — it becomes pinned and then closes like a clicked
 * bubble does (click outside, Esc, Ctrl+Enter).
 */
import { onScopeDispose, ref, type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'

const OPEN_DELAY_MS = 250
const CLOSE_DELAY_MS = 250
const MARKER_SELECTOR = '[data-note-id]'
const BUBBLE_SELECTOR = '.note-bubble'

export function useNoteHoverBubble(
  scrollerEl: Ref<HTMLElement | null>,
  options: {
    /** Opens the bubble for this marker (looks the note up). */
    open: (marker: HTMLElement) => void
    close: () => void
    isOpen: () => boolean
  },
) {
  const hostWindow = useHostWindow()
  /** True while the open bubble was opened by hover and not yet used. */
  const hoverOpened = ref(false)
  let openTimer: ReturnType<typeof setTimeout> | null = null
  let closeTimer: ReturnType<typeof setTimeout> | null = null

  function cancelOpen() {
    if (openTimer !== null) clearTimeout(openTimer)
    openTimer = null
  }
  function cancelClose() {
    if (closeTimer !== null) clearTimeout(closeTimer)
    closeTimer = null
  }
  function scheduleClose() {
    if (!hoverOpened.value) return
    cancelClose()
    closeTimer = setTimeout(() => {
      closeTimer = null
      if (!hoverOpened.value) return
      hoverOpened.value = false
      options.close()
    }, CLOSE_DELAY_MS)
  }
  function pin() {
    hoverOpened.value = false
    cancelClose()
  }

  function markerOf(target: EventTarget | null): HTMLElement | null {
    const el = (target as HTMLElement | null)?.closest?.(MARKER_SELECTOR) as HTMLElement | null
    return el && scrollerEl.value?.contains(el) ? el : null
  }
  function inBubble(target: EventTarget | null): boolean {
    return !!(target as HTMLElement | null)?.closest?.(BUBBLE_SELECTOR)
  }

  useEventListener(scrollerEl, 'mouseover', (event: MouseEvent) => {
    const marker = markerOf(event.target)
    if (!marker) return
    cancelClose()
    // A pinned bubble the user is working in is never replaced by a hover.
    if (options.isOpen() && !hoverOpened.value) return
    cancelOpen()
    openTimer = setTimeout(() => {
      openTimer = null
      if (options.isOpen() && !hoverOpened.value) return
      hoverOpened.value = true
      options.open(marker)
    }, OPEN_DELAY_MS)
  })

  useEventListener(scrollerEl, 'mouseout', (event: MouseEvent) => {
    const marker = markerOf(event.target)
    if (!marker || marker.contains(event.relatedTarget as Node | null)) return
    cancelOpen()
    if (!inBubble(event.relatedTarget)) scheduleClose()
  })

  // The bubble is teleported to body: watch it at document level.
  useEventListener(hostWindow.document, 'mouseover', (event: MouseEvent) => {
    if (inBubble(event.target)) cancelClose()
  })
  useEventListener(hostWindow.document, 'mouseout', (event: MouseEvent) => {
    if (!inBubble(event.target) || inBubble(event.relatedTarget)) return
    if (markerOf(event.relatedTarget)) return
    scheduleClose()
  })
  useEventListener(hostWindow.document, 'pointerdown', (event: PointerEvent) => {
    if (inBubble(event.target)) pin()
  }, { capture: true })
  useEventListener(hostWindow.document, 'keydown', (event: KeyboardEvent) => {
    if (inBubble(event.target)) pin()
  }, { capture: true })

  // Clicking the marker opens the bubble for real (the click handler does the open).
  useEventListener(scrollerEl, 'click', (event: MouseEvent) => {
    if (markerOf(event.target)) {
      cancelOpen()
      pin()
    }
  }, { capture: true })

  useEventListener(scrollerEl, 'scroll', () => {
    cancelOpen()
    if (hoverOpened.value) {
      hoverOpened.value = false
      cancelClose()
      options.close()
    }
  }, { passive: true })

  onScopeDispose(() => {
    cancelOpen()
    cancelClose()
  })

  return { hoverOpened }
}
