import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import type { HomeView } from '@/stores/settingsStore'

/** Widest the search bar and card collection ever get. */
const MAX_WIDTH = 720

// Column width and gap per view; must match .home-cards in HomePage.vue.
const GRID: Partial<Record<HomeView, { column: number; gap: number }>> = {
  cards: { column: 200, gap: 8 },
  tiles: { column: 72, gap: 6 },
}

/** Width of the whole columns that fit in `available` — shared by the search bar
 *  and the card grid, so the bar always lines up with the grid's edges. The list
 *  view has no columns and just takes the cap. */
export function useHomeContentWidth(
  available: MaybeRefOrGetter<number>,
  view: MaybeRefOrGetter<HomeView>,
) {
  return computed(() => {
    // Not measured yet (first frame): fall back to the cap rather than a 0-wide bar.
    const width = Math.min(toValue(available) || MAX_WIDTH, MAX_WIDTH)
    const grid = GRID[toValue(view)]
    if (!grid) return width
    const columns = Math.max(1, Math.floor((width + grid.gap) / (grid.column + grid.gap)))
    return Math.min(width, columns * grid.column + (columns - 1) * grid.gap)
  })
}
