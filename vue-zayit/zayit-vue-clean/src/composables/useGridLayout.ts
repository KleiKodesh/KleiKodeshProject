import { ref, computed, onMounted, onUnmounted, type Ref } from 'vue'

/**
 * Computes the optimal grid columns for `count` items in a container,
 * picking the column count that produces the least empty cells while
 * keeping each tile at least `minTileWidth` px wide.
 */
export function useGridLayout(
  containerRef: Ref<HTMLElement | null>,
  count: Ref<number>,
  minTileWidth = 68,
  gap = 16,
) {
  const containerWidth = ref(0)

  const cols = computed(() => {
    const w = containerWidth.value
    if (!w || !count.value) return 1

    // max columns that fit physically
    const maxCols = Math.max(1, Math.floor((w + gap) / (minTileWidth + gap)))
    const n = count.value

    let bestCols = 1
    let bestScore = Infinity

    for (let c = 1; c <= maxCols; c++) {
      const rows = Math.ceil(n / c)
      const empty = rows * c - n
      // prefer fewer empty cells; break ties by preferring more columns (wider tiles)
      const score = empty * 100 - c
      if (score < bestScore) {
        bestScore = score
        bestCols = c
      }
    }

    return bestCols
  })

  const tileWidth = computed(() => {
    if (!cols.value || !containerWidth.value) return minTileWidth
    return Math.floor((containerWidth.value - gap * (cols.value - 1)) / cols.value)
  })

  let ro: ResizeObserver | null = null

  onMounted(() => {
    if (!containerRef.value) return
    ro = new ResizeObserver(([entry]) => {
      if (entry) containerWidth.value = entry.contentRect.width
    })
    ro.observe(containerRef.value)
  })

  onUnmounted(() => ro?.disconnect())

  return { cols, tileWidth }
}
