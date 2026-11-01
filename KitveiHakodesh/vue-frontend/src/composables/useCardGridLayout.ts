import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import type { CardsViewMode } from '@/stores/settingsStore'

// Column width range and gap per view; the gap must match the grids that use this
// (.home-cards in HomePage.vue, .catalog-cards in the book catalog).
// Columns shrink toward `min` to fit one more, and never grow past `max`.
const GRID: Partial<Record<CardsViewMode, { min: number; max: number; gap: number }>> = {
  cards: { min: 140, max: 200, gap: 8 },
  tiles: { min: 72, max: 72, gap: 6 },
}

/** Column count and the width of those columns in `available` (capped at
 *  `maxWidth`) — shared by a page's search bar and its card grid, so the bar always
 *  lines up with the grid's edges. The list view has no columns and takes the cap. */
export function useCardGridLayout(
  available: MaybeRefOrGetter<number>,
  view: MaybeRefOrGetter<CardsViewMode>,
  maxWidth = Infinity,
) {
  return computed(() => {
    // Not measured yet (first frame): fall back to the cap rather than a 0-wide bar.
    const width = Math.min(toValue(available) || maxWidth, maxWidth)
    const grid = GRID[toValue(view)]
    if (!grid) return { width, columns: 1 }
    const columns = Math.max(1, Math.floor((width + grid.gap) / (grid.min + grid.gap)))
    const column = Math.min(grid.max, (width - (columns - 1) * grid.gap) / columns)
    return { width: Math.min(width, columns * column + (columns - 1) * grid.gap), columns }
  })
}
