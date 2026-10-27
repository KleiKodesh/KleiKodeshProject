import { ref } from 'vue'
import { wantsNewTab } from '@/composables/useOpenInNewTab'
import type { Ref } from 'vue'

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
//                           up enters at the last, per the APG pattern). One
//                           visual ROW at a time when getColumnsPerRow is given.
//   ArrowLeft / ArrowRight — GRIDS ONLY (getColumnsPerRow given), and only once
//                           something is highlighted: step within the row.
//                           Until Down/Up has entered the grid they stay the
//                           input's caret keys. Lists leave them alone always.
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
  /**
   * Brings item `index` into view, REPLACING the containerElement lookup above.
   *
   * For a WINDOWED list, whose rows are not all in the DOM: there the nth matched
   * element is not the nth item (the DOM holds only the rows around the viewport),
   * and the highlighted item may have no element at all — so the list's own
   * scroller has to do the landing, and a querySelectorAll index would silently
   * scroll to the wrong row. Implement it with 'nearest' semantics (do nothing
   * while the item is already fully visible), like the DOM path it stands in for.
   */
  scrollIntoView?: (index: number) => void
  /** Item selector inside containerElement (default '[data-nav-item]'). */
  itemSelector?: string
  /**
   * For tile grids: how many items one ArrowDown/ArrowUp step skips (one visual
   * row). Lists omit it (defaults to 1).
   */
  getColumnsPerRow?: () => number
}

/**
 * Column count for a wrapping tile grid, for `getColumnsPerRow`.
 *
 * Counted off the rendered tiles — those sharing the first one's `offsetTop` are
 * its row — rather than derived from an assumed tile-plus-gap width, so it is
 * exact at any width and any gap and there is no constant to keep in sync with
 * the CSS. Lives here because `getColumnsPerRow` is the only thing that consumes
 * it, and every tile grid needs the same answer.
 */
export function countGridColumns(
  container: HTMLElement | null | undefined,
  itemSelector = '[data-nav-item]',
): number {
  const tiles = container?.querySelectorAll<HTMLElement>(itemSelector)
  if (!tiles?.length) return 1
  const firstRowTop = tiles[0]!.offsetTop
  let columns = 0
  while (columns < tiles.length && tiles[columns]!.offsetTop === firstRowTop) columns++
  return Math.max(1, columns)
}

const PAGE_STEP = 10

// Grids move by column: Down past the bottom of a column wraps to the top of
// the next column, and Up past the top wraps to the bottom of the previous one.
function stepDown(index: number, rowStep: number, count: number, isGrid: boolean): number {
  if (!isGrid || index + rowStep < count) return index + rowStep
  const nextColumn = (index % rowStep) + 1
  return nextColumn < rowStep && nextColumn < count ? nextColumn : index
}

function stepUp(index: number, rowStep: number, count: number, isGrid: boolean): number {
  if (!isGrid || index - rowStep >= 0) return index - rowStep
  const previousColumn = index - 1
  if (previousColumn < 0) return index
  // Bottom item of the previous column.
  let bottom = previousColumn
  while (bottom + rowStep < count) bottom += rowStep
  return bottom
}

export function useInputListNavigation(options: UseInputListNavigationOptions) {
  const itemSelector = options.itemSelector ?? '[data-nav-item]'

  /** Highlighted item index, -1 for none. Writable — click handlers assign it. */
  const activeIndex = ref(-1)

  function scrollItemIntoView(index: number) {
    if (options.scrollIntoView) {
      options.scrollIntoView(index)
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


  /**
   * Handle a keydown from the paired input. Returns true when the event was
   * consumed (caller should stop; its own handling runs otherwise).
   */
  function onKeydown(event: KeyboardEvent): boolean {
    const count = options.getCount()
    if (!count) return false

    const ctrl = event.ctrlKey || event.metaKey
    // Alt+Left/Right is the title-bar back/forward shortcut. Its window
    // listener is capture-phase and stops propagation, so Alt+Arrow never
    // reaches here in practice; excluding Alt keeps this handler correct on
    // its own rather than relying on that.
    const modified = ctrl || event.altKey
    const isGrid = !!options.getColumnsPerRow
    const rowStep = Math.max(1, options.getColumnsPerRow?.() ?? 1)

    if (event.code === 'ArrowDown' && !modified) {
      event.preventDefault()
      moveTo(activeIndex.value < 0 ? 0 : stepDown(activeIndex.value, rowStep, count, isGrid))
      return true
    }
    if (event.code === 'ArrowUp' && !modified) {
      event.preventDefault()
      moveTo(activeIndex.value < 0 ? count - 1 : stepUp(activeIndex.value, rowStep, count, isGrid))
      return true
    }
    // Left/right step WITHIN a row, and so exist only for grids — a list has one
    // item per row, and in a text field these are the caret keys, which the user
    // needs to edit the query they are typing. `getColumnsPerRow` is what says
    // "this is a grid", so it gates them.
    //
    // Even in a grid they stay caret keys until the user has ENTERED the grid
    // with ArrowDown/ArrowUp (activeIndex >= 0): before that the field is
    // plainly being edited, and stealing left/right would make the caret
    // unmovable. Callers reset activeIndex to -1 on a new query, which hands
    // the keys back to the caret.
    //
    // The page is RTL: the visually NEXT tile is to the LEFT, so ArrowLeft
    // advances and ArrowRight goes back. (Reading `dir` off the container would
    // handle both directions, but this app is RTL throughout and an untested
    // LTR branch is worse than an honest assumption.)
    if (
      isGrid &&
      activeIndex.value >= 0 &&
      (event.code === 'ArrowLeft' || event.code === 'ArrowRight') &&
      !modified
    ) {
      const step = event.code === 'ArrowLeft' ? 1 : -1
      event.preventDefault()
      moveTo(activeIndex.value + step)
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
      moveTo(0)
      return true
    }
    if (ctrl && event.code === 'End') {
      event.preventDefault()
      moveTo(count - 1)
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
