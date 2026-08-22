import { ref } from 'vue'
import { wantsNewTab } from '@/composables/useOpenInNewTab'
import type { Ref } from 'vue'
import type { Virtualizer } from '@tanstack/vue-virtual'

// The combobox keyboard model (W3C APG "Combobox Pattern"): DOM focus stays in
// the text input the whole time, and the arrow keys move a HIGHLIGHT through the
// paired list — a plain reactive index rendered as a class, never element focus.
// The user can keep typing at any moment because the caret never leaves the field.
//
// This is the input-paired counterpart to useListKeys (useListKeyNav.ts), which
// keeps the roving-focus model for standalone lists that own their own focus.
//
// Key map (everything else falls through to the input untouched):
//   ArrowDown / ArrowUp   — move the highlight (down enters at the first item,
//                           up enters at the last, per the APG pattern)
//   PageDown / PageUp     — move the highlight a chunk at a time
//   Ctrl+Home / Ctrl+End  — jump to the first / last item (plain Home/End stay
//                           caret keys, as a text field demands)
//   Enter                 — activate the highlighted item; NOT consumed when
//                           nothing is highlighted, so the field's own Enter
//                           (submit, single-result open) still runs
//   Ctrl/⌘+Enter          — activate in a new tab (mirrors Ctrl+click)
//
// Wire the returned onKeydown to the input's keydown (directly, or forwarded
// from a parent component when the input and the list live in different files).
// It returns true when it consumed the event, so callers can short-circuit
// their own Enter/Escape handling.
//
// The highlight survives input blur on purpose — the pages using this keep the
// list on screen, and a visible "where was I" marker is harmless. Callers reset
// `activeIndex` to -1 when the item collection changes (new query results), so a
// stale index never points at the wrong row.

export interface UseInputListNavigationOptions {
  getCount: () => number
  /** Called on Enter with a highlighted item. */
  onActivate?: (index: number, openInNewTab: boolean, event: KeyboardEvent) => void
  /**
   * Plain scrollable container holding the item elements — highlighted items are
   * kept visible with scrollIntoView. See the SCROLL DEBUGGING NOTE in
   * useListKeyNav.ts: a dropdown inside a scrollable page must be Teleported
   * with position:fixed, or block:'nearest' may scroll the page instead.
   */
  containerElement?: Ref<HTMLElement | null>
  /** Item selector inside containerElement (default '[data-nav-item]'). */
  itemSelector?: string
  /** For @tanstack/vue-virtual lists — used instead of containerElement. */
  getVirtualizer?: () => Virtualizer<Element, Element>
}

const PAGE_STEP = 10

export function useInputListNavigation(options: UseInputListNavigationOptions) {
  const itemSelector = options.itemSelector ?? '[data-nav-item]'

  /** Highlighted item index, -1 for none. Writable — click handlers assign it. */
  const activeIndex = ref(-1)

  function scrollItemIntoView(index: number) {
    if (options.getVirtualizer) {
      options.getVirtualizer().scrollToIndex(index, { align: 'auto' })
      return
    }
    const items = options.containerElement?.value?.querySelectorAll<HTMLElement>(itemSelector)
    items?.[index]?.scrollIntoView({ block: 'nearest' })
  }

  function moveTo(index: number) {
    const count = options.getCount()
    if (!count) return
    const clamped = Math.max(0, Math.min(count - 1, index))
    activeIndex.value = clamped
    scrollItemIntoView(clamped)
  }

  // scrollToIndex on the extremes can land short when measured item heights
  // drift from the estimate — snap the viewport edge afterwards, same as
  // useVirtualScrollerKeys does.
  function jumpToEdge(index: number, edge: 'start' | 'end') {
    if (!options.getVirtualizer) {
      moveTo(index)
      return
    }
    const virtualizer = options.getVirtualizer()
    activeIndex.value = index
    virtualizer.scrollToIndex(index, { align: edge })
    requestAnimationFrame(() => {
      const element = virtualizer.scrollElement as HTMLElement | null
      if (element) element.scrollTop = edge === 'start' ? 0 : element.scrollHeight
    })
  }

  /**
   * Handle a keydown from the paired input. Returns true when the event was
   * consumed (caller should stop; its own handling runs otherwise).
   */
  function onKeydown(event: KeyboardEvent): boolean {
    const count = options.getCount()
    if (!count) return false

    const ctrl = event.ctrlKey || event.metaKey

    if (event.code === 'ArrowDown' && !ctrl) {
      event.preventDefault()
      moveTo(activeIndex.value < 0 ? 0 : activeIndex.value + 1)
      return true
    }
    if (event.code === 'ArrowUp' && !ctrl) {
      event.preventDefault()
      moveTo(activeIndex.value < 0 ? count - 1 : activeIndex.value - 1)
      return true
    }
    if (event.code === 'PageDown') {
      event.preventDefault()
      moveTo(activeIndex.value < 0 ? 0 : activeIndex.value + PAGE_STEP)
      return true
    }
    if (event.code === 'PageUp') {
      event.preventDefault()
      moveTo(activeIndex.value < 0 ? count - 1 : activeIndex.value - PAGE_STEP)
      return true
    }
    if (ctrl && event.code === 'Home') {
      event.preventDefault()
      jumpToEdge(0, 'start')
      return true
    }
    if (ctrl && event.code === 'End') {
      event.preventDefault()
      jumpToEdge(count - 1, 'end')
      return true
    }
    if (event.code === 'Enter' && activeIndex.value >= 0 && activeIndex.value < count) {
      if (!options.onActivate) return false
      event.preventDefault()
      options.onActivate(activeIndex.value, wantsNewTab(event), event)
      return true
    }
    return false
  }

  return { activeIndex, onKeydown }
}
