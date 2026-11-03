/**
 * The block plan of a WindowedScroller in flow mode: which rows flow inline, as
 * spans of one running paragraph, and which stand as blocks of their own.
 *
 * Why blocks: text prepended to the FRONT of a paragraph re-wraps every visual
 * line after it, through the viewport — the reader's words would jump, and no
 * scroll correction can undo a change in the text's own shape. So in flow mode the
 * window only ever grows at the top by whole blocks, and a block boundary is never
 * invented: it is a landmark row (a heading, by the caller's reckoning — a TOC
 * entry) or the stretch between two landmarks.
 *
 * A stretch flows when its weight (the rows' character counts) is under
 * FLOW_MAX_WEIGHT. A longer one has no natural break anywhere — the library holds
 * stretches of a million characters (measured 2026-11-03 on every 10th book: a
 * stretch's p90 is 52k characters, p99 244k) — and would be a single block of
 * dozens of screens to mount and re-wrap. Those stay a block per row, the
 * line-per-row layout the reader knows; everything else flows.
 */

/** The weight above which a stretch falls back to a block per row. */
export const FLOW_MAX_WEIGHT = 40_000
/** A row's weight when the list gives none: a median line (measured 150). */
const FALLBACK_ROW_WEIGHT = 150

export interface WindowedScrollerFlowPlan {
  /** Whether the row flows inline, within its stretch. */
  isFlowRow(index: number): boolean
  /** The [start, end) of the block holding the row: the row itself, or its flowing stretch. */
  blockRange(index: number): readonly [number, number]
}

/** Every row its own block — the rows mode, and flow mode with nothing to plan from. */
export const ROWS_PLAN: WindowedScrollerFlowPlan = {
  isFlowRow: () => false,
  blockRange: (index) => [index, index + 1],
}

export function buildFlowPlan(
  count: number,
  landmarkTiers: readonly (readonly number[])[],
  rowWeights: ArrayLike<number> | null | undefined,
): WindowedScrollerFlowPlan {
  if (count <= 0) return ROWS_PLAN
  const marks = [...new Set(landmarkTiers.flat())]
    .filter((index) => index >= 0 && index < count)
    .sort((first, second) => first - second)
  const weights = rowWeights && rowWeights.length === count ? rowWeights : null
  const weightOf = (index: number) => (weights ? weights[index]! : FALLBACK_ROW_WEIGHT)

  // Stretch k lies between marks[k - 1] and marks[k]; stretch 0 starts at row 0 and
  // the last one runs to the end. Whether each flows is decided once, here.
  const stretchCount = marks.length + 1
  const flows = new Uint8Array(stretchCount)
  for (let k = 0; k < stretchCount; k++) {
    const start = k === 0 ? 0 : marks[k - 1]! + 1
    const end = k < marks.length ? marks[k]! : count
    let weight = 0
    for (let index = start; index < end && weight <= FLOW_MAX_WEIGHT; index++) weight += weightOf(index)
    flows[k] = weight <= FLOW_MAX_WEIGHT ? 1 : 0
  }

  /** The number of marks at or before `index` — the stretch after them, or the mark itself. */
  function marksUpTo(index: number): number {
    let low = 0
    let high = marks.length
    while (low < high) {
      const middle = (low + high) >> 1
      if (marks[middle]! <= index) low = middle + 1
      else high = middle
    }
    return low
  }

  function stretchOf(index: number): { k: number; start: number; end: number } | null {
    const k = marksUpTo(index)
    if (k > 0 && marks[k - 1] === index) return null // a landmark row
    return {
      k,
      start: k === 0 ? 0 : marks[k - 1]! + 1,
      end: k < marks.length ? marks[k]! : count,
    }
  }

  return {
    isFlowRow(index) {
      const stretch = stretchOf(index)
      return stretch != null && flows[stretch.k] === 1
    },
    blockRange(index) {
      const stretch = stretchOf(index)
      if (!stretch || flows[stretch.k] !== 1) return [index, index + 1]
      return [stretch.start, stretch.end]
    },
  }
}
