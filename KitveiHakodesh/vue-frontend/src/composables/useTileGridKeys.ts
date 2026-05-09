import { ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import type { Ref } from 'vue'

const TILE_WIDTH = 72 + 20 // tile width + gap (matches .home-grid CSS)

export function useTilesKeys(
  containerEl: Ref<HTMLElement | null>,
  getCount: () => number,
  onActivate?: (index: number) => void,
) {
  const focusedIndex = ref(-1)
  const containerFocused = ref(false)

  function getColumns(): number {
    if (!containerEl.value) return 1
    const grid = containerEl.value.querySelector('.home-grid') as HTMLElement | null
    const measured = grid ?? containerEl.value
    return Math.max(1, Math.floor(measured.clientWidth / TILE_WIDTH))
  }

  function scrollItemIntoView(index: number) {
    const el = containerEl.value?.querySelectorAll<HTMLElement>('[data-nav-item]')[index]
    if (el) el.scrollIntoView({ block: 'nearest' })
  }

  function moveTo(index: number) {
    const count = getCount()
    if (!count) return
    const clamped = Math.max(0, Math.min(count - 1, index))
    focusedIndex.value = clamped
    scrollItemIntoView(clamped)
  }

  useEventListener(containerEl, 'focusin', () => {
    containerFocused.value = true
  })

  useEventListener(containerEl, 'focusout', (e: FocusEvent) => {
    if (!containerEl.value?.contains(e.relatedTarget as Node)) {
      containerFocused.value = false
    }
  })

  useEventListener(containerEl, 'pointerdown', () => {
    focusedIndex.value = -1
  })

  useEventListener(containerEl, 'keydown', (e: KeyboardEvent) => {
    const count = getCount()
    if (!count) return

    const cols = getColumns()
    const cur = focusedIndex.value < 0 ? 0 : focusedIndex.value

    if (e.code === 'ArrowRight') {
      e.preventDefault()
      moveTo(focusedIndex.value < 0 ? 0 : cur - 1)
    } else if (e.code === 'ArrowLeft') {
      e.preventDefault()
      moveTo(focusedIndex.value < 0 ? 0 : cur + 1)
    } else if (e.code === 'ArrowDown') {
      e.preventDefault()
      moveTo(focusedIndex.value < 0 ? 0 : cur + cols)
    } else if (e.code === 'ArrowUp') {
      e.preventDefault()
      moveTo(focusedIndex.value < 0 ? 0 : cur - cols)
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
