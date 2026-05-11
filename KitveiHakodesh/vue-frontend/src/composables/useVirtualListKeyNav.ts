import { ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import type { Ref } from 'vue'
import type { Virtualizer } from '@tanstack/vue-virtual'

export function useVirtualListKeys(
  scrollerEl: Ref<HTMLElement | null>,
  getVirtualizer: () => Virtualizer<Element, Element>,
  getCount: () => number,
  onActivate?: (index: number) => void,
) {
  const focusedIndex = ref(-1)
  const containerFocused = ref(false)
  // Track whether focus arrived via pointer so we skip the scroll-to-top
  // that is only appropriate for keyboard-initiated focus.
  let focusFromPointer = false

  useEventListener(scrollerEl, 'pointerdown', () => {
    focusFromPointer = true
  })

  useEventListener(scrollerEl, 'focus', () => {
    containerFocused.value = true
    const fromPointer = focusFromPointer
    focusFromPointer = false
    if (!fromPointer && focusedIndex.value < 0 && getCount() > 0) {
      focusedIndex.value = 0
      getVirtualizer().scrollToIndex(0, { align: 'auto' })
    }
  })

  useEventListener(scrollerEl, 'blur', () => {
    containerFocused.value = false
  })

  useEventListener(scrollerEl, 'keydown', (e: KeyboardEvent) => {
    const count = getCount()
    if (!count) return

    const ctrl = e.ctrlKey || e.metaKey

    if (e.code === 'ArrowDown' && !ctrl) {
      e.preventDefault()
      const next = focusedIndex.value < count - 1 ? focusedIndex.value + 1 : focusedIndex.value
      focusedIndex.value = next
      getVirtualizer().scrollToIndex(next, { align: 'auto' })
    } else if (e.code === 'ArrowUp' && !ctrl) {
      e.preventDefault()
      const prev = focusedIndex.value > 0 ? focusedIndex.value - 1 : 0
      focusedIndex.value = prev
      getVirtualizer().scrollToIndex(prev, { align: 'auto' })
    } else if (e.code === 'Enter' || e.code === 'Space') {
      if (focusedIndex.value >= 0 && onActivate) {
        e.preventDefault()
        onActivate(focusedIndex.value)
      }
    } else if (ctrl && e.code === 'Home') {
      e.preventDefault()
      focusedIndex.value = 0
      getVirtualizer().scrollToIndex(0, { align: 'start' })
      requestAnimationFrame(() => {
        if (scrollerEl.value) scrollerEl.value.scrollTop = 0
      })
    } else if (ctrl && e.code === 'End') {
      e.preventDefault()
      focusedIndex.value = count - 1
      getVirtualizer().scrollToIndex(count - 1, { align: 'end' })
      requestAnimationFrame(() => {
        if (scrollerEl.value) scrollerEl.value.scrollTop = scrollerEl.value.scrollHeight
      })
    }
  })

  return { focusedIndex, containerFocused }
}
