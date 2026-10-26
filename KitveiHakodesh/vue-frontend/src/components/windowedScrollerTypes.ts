/**
 * The public surface of WindowedScroller.vue, for the composables that drive it
 * (scroll restore, navigation) — they take the component instance as a getter
 * and must not import the `.vue` file.
 */

export interface WindowedScrollerScrollToOptions {
  /**
   * Pixels added to the row's top when landing. Negative leaves a gap above the
   * row (a landing sliver, a search bar inset); positive restores a position that
   * was partway down the row.
   */
  offsetPx?: number
  /** 'end' puts the row's bottom at the viewport's bottom — the End key. */
  align?: 'start' | 'end'
}

export interface WindowedScrollerVisibleRow {
  index: number
  /** How far the viewport's top sits below the row's top. Negative when the row starts below it. */
  offsetWithinRow: number
}

export interface WindowedScrollerApi {
  /** The native scroll container holding the rows — focus, keys and pointer listeners go here. */
  scrollerElement: HTMLElement | null
  /** The index-based scrollbar's track. */
  scrollbarTrackElement: HTMLElement | null
  /** Rows currently mounted in flow: [start, end). Pinned rows outside it are mounted too, out of flow. */
  renderedRange: { start: number; end: number }
  /**
   * Lands the row at the top of the viewport. Exact: when the row is not mounted the
   * window is rebuilt around it first, then the scroll is set from its real position.
   * Resolves once the scroll is set.
   */
  scrollToIndex(index: number, options?: WindowedScrollerScrollToOptions): Promise<void>
  /**
   * The first row showing below the viewport's top plus `topInsetPx`. `fully` asks for
   * the first row whose top is below that line, not merely any part of it.
   */
  readFirstVisible(topInsetPx?: number, fully?: boolean): WindowedScrollerVisibleRow | null
  isRowRendered(index: number): boolean
  isRowFullyVisible(index: number): boolean
  rowElement(index: number): HTMLElement | null
}
