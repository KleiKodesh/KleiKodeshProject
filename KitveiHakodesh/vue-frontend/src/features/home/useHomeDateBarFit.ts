import { ref, computed, nextTick, type Ref } from 'vue'
import { useResizeObserver } from '@vueuse/core'

/**
 * Keeps the home date bar on a single line by dropping optional items by
 * priority instead of wrapping: the clock goes first (level >= 1), then the
 * nearest-zman (level >= 2). The date and daf yomi are never dropped.
 *
 * `remeasure` must be called whenever the bar's *content* changes (a zman
 * appears, the daf loads, the clock ticks) — a resize alone is handled here.
 */
export function useHomeDateBarFit(
  dateBarRef: Ref<HTMLElement | null>,
  showBarClock: Ref<boolean>,
  hasNextZman: Ref<boolean>,
) {
  const hideLevel = ref(0)

  const showClockInBar = computed(() => showBarClock.value && hideLevel.value < 1)
  const showZmanInBar = computed(() => hasNextZman.value && hideLevel.value < 2)

  let measuring = false

  function measure() {
    const el = dateBarRef.value
    if (!el || measuring) return
    measuring = true
    // Try to show as much as possible, then step down until it fits (or we've
    // dropped everything droppable). Each step re-measures after the DOM updates.
    const step = () => {
      if (!dateBarRef.value) {
        measuring = false
        return
      }
      const overflow = dateBarRef.value.scrollWidth > dateBarRef.value.clientWidth + 1
      if (overflow && hideLevel.value < 2) {
        hideLevel.value++
        nextTick(step)
      } else if (!overflow && hideLevel.value > 0) {
        // Room may have opened up — try restoring one level and see if it still fits.
        const previous = hideLevel.value
        hideLevel.value--
        nextTick(() => {
          if (dateBarRef.value && dateBarRef.value.scrollWidth > dateBarRef.value.clientWidth + 1) {
            hideLevel.value = previous // didn't fit; revert
            measuring = false
          } else {
            step() // fit — keep trying to restore more
          }
        })
      } else {
        measuring = false
      }
    }
    step()
  }

  useResizeObserver(dateBarRef, () => measure())

  return {
    showClockInBar,
    showZmanInBar,
    remeasure: () => nextTick(measure),
  }
}
