import { ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import type { Ref } from 'vue'

export function useListKeys(
  containerEl: Ref<HTMLElement | null>,
  getCount: () => number,
  onActivate?: (index: number) => void,
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
        onActivate(focusedIndex.value)
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
