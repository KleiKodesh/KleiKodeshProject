import { ref, computed, nextTick, watch, type Ref } from 'vue'
import { useEventListener, unrefElement, type MaybeElementRef } from '@vueuse/core'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import type { ContextMenuItem, ContextMenuTextItem } from '@/components/ContextMenu.vue'

/** Marks a container that has its own selection toolbar; the app-wide fallback stays out of it. */
export const SELECTION_TOOLBAR_OWNER_ATTRIBUTE = 'data-selection-toolbar'

/** How many icon items of the context menu become toolbar buttons (the rest sit under "more"). */
export const SELECTION_TOOLBAR_MAX_BUTTONS = 4

const GAP_PX = 6
const VIEWPORT_MARGIN_PX = 4

// Keys that end a keyboard selection (Shift+arrows, Shift+Home/End, Ctrl+A).
const SELECTION_KEYS = new Set(['Shift', 'ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End', 'PageUp', 'PageDown', 'a', 'A'])

/** The context menu's icon text items - the toolbar's buttons, in menu order. */
export function selectionToolbarButtons(items: ContextMenuItem[]): ContextMenuTextItem[] {
  return items
    .filter((item): item is ContextMenuTextItem => (item.type === undefined || item.type === 'text') && !!(item as ContextMenuTextItem).icon)
    .slice(0, SELECTION_TOOLBAR_MAX_BUTTONS)
}

/**
 * A floating bar above the text selection (a "bubble menu"): shows when a mouse or keyboard
 * selection ends inside `container`, hides when the selection collapses, on scroll, Escape,
 * a press outside the bar, or when a right-click menu opens.
 *
 * `container` null = the app-wide fallback: any page selection outside a container that has its
 * own toolbar, outside editable fields.
 */
export function useSelectionToolbar(options: {
  container: MaybeElementRef | null
  toolbar: Ref<HTMLElement | null>
  /** True = do not show now (e.g. the view is in an editing mode). */
  isBlocked?: () => boolean
  /** Runs right before the bar shows, e.g. to build items for this selection. */
  prepare?: () => void
}) {
  const hostWindow = useHostWindow()
  const visible = ref(false)
  const placed = ref(false)
  const left = ref(0)
  const top = ref(0)
  let savedRange: Range | null = null

  const style = computed(() => ({
    left: `${left.value}px`,
    top: `${top.value}px`,
    visibility: placed.value ? ('visible' as const) : ('hidden' as const),
  }))

  function containerElement(): HTMLElement | null {
    return options.container ? ((unrefElement(options.container) as HTMLElement | null | undefined) ?? null) : null
  }

  // A view with its own toolbar marks its container, so the fallback leaves it alone.
  if (options.container) {
    watch(containerElement, (element, previous) => {
      previous?.removeAttribute(SELECTION_TOOLBAR_OWNER_ATTRIBUTE)
      element?.setAttribute(SELECTION_TOOLBAR_OWNER_ATTRIBUTE, '')
    }, { immediate: true, flush: 'post' })
  }

  /** The current selection's range when this bar serves it, else null. */
  function servedRange(): Range | null {
    const selection = hostWindow.getSelection()
    if (!selection || selection.rangeCount === 0 || selection.isCollapsed) return null
    if (!selection.toString().trim()) return null
    const range = selection.getRangeAt(0)
    const node = range.commonAncestorContainer
    const element = node instanceof Element ? node : node.parentElement
    if (!element) return null
    if (options.container) {
      const container = containerElement()
      return container && container.contains(node) ? range : null
    }
    if (element.closest(`[${SELECTION_TOOLBAR_OWNER_ATTRIBUTE}], input, textarea, select, [contenteditable]:not([contenteditable="false"])`)) return null
    return range
  }

  function hide(): void {
    visible.value = false
    placed.value = false
  }

  async function showForSelection(): Promise<void> {
    if (options.isBlocked?.()) return hide()
    const range = servedRange()
    if (!range) return hide()
    const rects = Array.from(range.getClientRects()).filter((rect) => rect.width > 0 && rect.height > 0)
    if (rects.length === 0) return hide()
    // A selection scrolled wholly out of view gets no bar.
    if (rects.every((rect) => rect.bottom < 0 || rect.top > hostWindow.innerHeight)) return hide()
    savedRange = range.cloneRange()
    options.prepare?.()
    placed.value = false
    visible.value = true
    await nextTick()
    const bar = options.toolbar.value
    if (!bar) return
    const { width, height } = bar.getBoundingClientRect()
    // Above the first line of the selection, centered on it; no room → below the last line.
    const firstLine = rects.reduce((best, rect) => (rect.top < best.top ? rect : best))
    const lastLine = rects.reduce((best, rect) => (rect.bottom > best.bottom ? rect : best))
    const maxLeft = hostWindow.innerWidth - VIEWPORT_MARGIN_PX - width
    const centered = firstLine.left + firstLine.width / 2 - width / 2
    left.value = Math.max(VIEWPORT_MARGIN_PX, Math.min(centered, maxLeft))
    const above = firstLine.top - GAP_PX - height
    const below = lastLine.bottom + GAP_PX
    const maxTop = hostWindow.innerHeight - VIEWPORT_MARGIN_PX - height
    top.value = above >= VIEWPORT_MARGIN_PX ? above : Math.max(VIEWPORT_MARGIN_PX, Math.min(below, maxTop))
    placed.value = true
  }

  function isInsideBar(target: EventTarget | null): boolean {
    return target instanceof Node && !!options.toolbar.value?.contains(target)
  }

  // The selection is final once the button is released (one frame later in every engine).
  useEventListener(hostWindow.document, 'mouseup', (event: MouseEvent) => {
    if (event.button !== 0 || isInsideBar(event.target)) return
    hostWindow.requestAnimationFrame(() => void showForSelection())
  })
  useEventListener(hostWindow.document, 'keyup', (event: KeyboardEvent) => {
    if (SELECTION_KEYS.has(event.key)) void showForSelection()
  })
  useEventListener(hostWindow.document, 'keydown', (event: KeyboardEvent) => {
    if (event.key === 'Escape' && visible.value) hide()
  })
  useEventListener(hostWindow.document, 'selectionchange', () => {
    if (!visible.value) return
    const selection = hostWindow.getSelection()
    if (!selection || selection.isCollapsed) hide()
  })
  useEventListener(hostWindow.document, 'pointerdown', (event: PointerEvent) => {
    if (visible.value && !isInsideBar(event.target)) hide()
  }, { capture: true })
  useEventListener(hostWindow.document, 'scroll', () => { if (visible.value) hide() }, { capture: true, passive: true })
  useEventListener(hostWindow.document, 'contextmenu', () => { if (visible.value) hide() }, { capture: true })

  /** Puts back the selection the bar was shown for, so an action reads exactly that text. */
  function restoreSelection(): void {
    if (!savedRange) return
    const selection = hostWindow.getSelection()
    if (!selection) return
    selection.removeAllRanges()
    selection.addRange(savedRange)
  }

  return { visible, style, hide, restoreSelection }
}
