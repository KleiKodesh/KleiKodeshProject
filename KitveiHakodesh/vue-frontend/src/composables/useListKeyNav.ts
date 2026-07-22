import { ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import { wantsNewTab } from '@/composables/useOpenInNewTab'
import type { Ref } from 'vue'

// SCROLL DEBUGGING NOTE (HomeSearchDropdown, 2026):
// If scrollIntoView stops working past a certain item in a scrollable list,
// the cause is likely the container sitting inside a scrollable page ancestor.
// scrollIntoView({ block: 'nearest' }) walks up the DOM and may scroll the
// *page* instead of the container, which then gets its scrollTop reset by
// the page's own scroll management (frame=1 in rAF polling).
// The fix: render the dropdown via <Teleport to="body"> with position:fixed,
// so the only scrollable ancestor is the container itself.
// This issue only affects dropdowns rendered inside a scrollable page — plain
// list components on their own page are not affected.

export function useListKeys(
  containerEl: Ref<HTMLElement | null>,
  getCount: () => number,
  onActivate?: (index: number, openInNewTab: boolean, event: KeyboardEvent) => void,
  options?: { itemSelector?: string },
) {
  const selector = options?.itemSelector ?? '[data-nav-item]'
  const focusedIndex = ref(-1)
  const containerFocused = ref(false)

  function getItems(): NodeListOf<HTMLElement> | HTMLElement[] {
    return containerEl.value?.querySelectorAll<HTMLElement>(selector) ?? []
  }

  function scrollItemIntoView(index: number) {
    const items = getItems()
    const el = items[index]
    if (el) el.scrollIntoView({ block: 'nearest' })
  }

  function moveTo(index: number) {
    const count = getCount()
    if (!count) return
    const clamped = Math.max(0, Math.min(count - 1, index))
    focusedIndex.value = clamped
    scrollItemIntoView(clamped)
  }

  useEventListener(containerEl, 'focus', () => {
    containerFocused.value = true
  })

  useEventListener(containerEl, 'blur', () => {
    containerFocused.value = false
  })

  useEventListener(containerEl, 'keydown', (e: KeyboardEvent) => {
    const count = getCount()
    if (!count) return

    if (e.code === 'ArrowDown') {
      e.preventDefault()
      moveTo(focusedIndex.value < 0 ? 0 : focusedIndex.value + 1)
    } else if (e.code === 'ArrowUp') {
      e.preventDefault()
      moveTo(focusedIndex.value <= 0 ? 0 : focusedIndex.value - 1)
    } else if (e.code === 'Enter' || e.code === 'Space') {
      if (focusedIndex.value >= 0 && onActivate) {
        e.preventDefault()
        // Ctrl/⌘+Enter opens the focused item in a new tab (mirrors Ctrl+click).
        onActivate(focusedIndex.value, wantsNewTab(e), e)
      }
    } else if (e.code === 'Home') {
      e.preventDefault()
      moveTo(0)
    } else if (e.code === 'End') {
      e.preventDefault()
      moveTo(count - 1)
    }
  })

  return { focusedIndex, containerFocused }
}
