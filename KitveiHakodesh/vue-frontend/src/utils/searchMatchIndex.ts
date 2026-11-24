/**
 * The results of a find-in-text search (the book view, its commentary panels, FastDocView),
 * kept as ROWS, not as occurrences: the rows that match, ascending, and a running count of
 * occurrences before each row. Match number k is found by binary search over that count.
 *
 * WHY THIS SHAPE - do not replace it with a list of one object per occurrence. That is what it
 * replaced (2026-11-10, after comparing with Zayit's find-in-book, which keeps the same two
 * arrays): a common word in a large book is tens of thousands of occurrences, a one-letter
 * query hundreds of thousands, and an object per occurrence was that many allocations on every
 * search and every keystroke that refined it, for data that is fully described by "these rows,
 * this many times each". Here the memory follows the matching ROWS, the total is one read,
 * "match k" and "the first match at or after row r" are binary searches, and the row set the
 * highlighter needs (`has`) is the same arrays.
 *
 * Immutable: a new search makes a new index, so a Vue shallowRef holding one changes exactly
 * when the results do.
 */
export class SearchMatchIndex {
  /** The matching rows (line index, flat index or block), strictly ascending. */
  readonly rows: Int32Array
  /** firstOccurrence[i] = occurrences before rows[i]; one longer than rows, its last entry the total. */
  readonly firstOccurrence: Int32Array

  private constructor(rows: Int32Array, firstOccurrence: Int32Array) {
    this.rows = rows
    this.firstOccurrence = firstOccurrence
  }

  static readonly EMPTY = new SearchMatchIndex(new Int32Array(0), new Int32Array(1))

  /**
   * From (row, count) pairs in any order; a row given twice keeps its LAST count, and rows with
   * a count of 0 are left out.
   */
  static fromRowCounts(rows: ArrayLike<number>, counts: ArrayLike<number>): SearchMatchIndex {
    // The usual case - the service answers in reading order, every count positive - is
    // copied straight in, with no map and no sort.
    if (isStrictlyAscendingWithCounts(rows, counts)) {
      if (!rows.length) return SearchMatchIndex.EMPTY
      const outRows = Int32Array.from(rows)
      const first = new Int32Array(rows.length + 1)
      for (let i = 0; i < rows.length; i++) first[i + 1] = first[i]! + counts[i]!
      return new SearchMatchIndex(outRows, first)
    }
    const countByRow = new Map<number, number>()
    for (let i = 0; i < rows.length; i++) countByRow.set(rows[i]!, counts[i]!)
    const sorted = [...countByRow.keys()].filter((row) => countByRow.get(row)! > 0).sort((a, b) => a - b)
    if (!sorted.length) return SearchMatchIndex.EMPTY
    const outRows = new Int32Array(sorted.length)
    const first = new Int32Array(sorted.length + 1)
    for (let i = 0; i < sorted.length; i++) {
      outRows[i] = sorted[i]!
      first[i + 1] = first[i]! + countByRow.get(sorted[i]!)!
    }
    return new SearchMatchIndex(outRows, first)
  }

  /** Every occurrence, in reading order. */
  get total(): number {
    return this.firstOccurrence[this.rows.length]!
  }

  /** How many times row `row` matches; 0 when it does not. */
  countOf(row: number): number {
    const i = this.rowPosition(row)
    return i < 0 ? 0 : this.firstOccurrence[i + 1]! - this.firstOccurrence[i]!
  }

  /** Whether row `row` holds a match - what the highlighter marks. */
  has(row: number): boolean {
    return this.rowPosition(row) >= 0
  }

  /** Match number `k` (0-based) as its row and which occurrence in that row; null when out of range. */
  at(k: number): { row: number; occurrence: number } | null {
    if (k < 0 || k >= this.total) return null
    // The last row whose first occurrence is at or before k.
    let low = 0
    let high = this.rows.length - 1
    while (low < high) {
      const middle = (low + high + 1) >> 1
      if (this.firstOccurrence[middle]! <= k) low = middle
      else high = middle - 1
    }
    return { row: this.rows[low]!, occurrence: k - this.firstOccurrence[low]! }
  }

  /** The match number of (row, occurrence), or -1 when that is not a match. */
  indexOf(row: number, occurrence: number): number {
    const i = this.rowPosition(row)
    if (i < 0) return -1
    const k = this.firstOccurrence[i]! + occurrence
    return occurrence >= 0 && k < this.firstOccurrence[i + 1]! ? k : -1
  }

  /** The first match at or after row `row`; `total` when there is none. */
  firstAtOrAfter(row: number): number {
    const i = this.lowerBound(row)
    return this.firstOccurrence[i]!
  }

  /** The last match strictly before row `row`; -1 when there is none. */
  lastBefore(row: number): number {
    return this.firstAtOrAfter(row) - 1
  }

  /** The rows of both, each row with `newer`'s count where both have it - for a search that arrives in parts. */
  mergedWith(newer: SearchMatchIndex): SearchMatchIndex {
    if (!this.rows.length) return newer
    if (!newer.rows.length) return this
    const rows: number[] = []
    const counts: number[] = []
    for (const index of [this, newer]) {
      for (let i = 0; i < index.rows.length; i++) {
        rows.push(index.rows[i]!)
        counts.push(index.firstOccurrence[i + 1]! - index.firstOccurrence[i]!)
      }
    }
    return SearchMatchIndex.fromRowCounts(rows, counts)
  }

  /** The position of the first row >= `row` in `rows`. */
  private lowerBound(row: number): number {
    let low = 0
    let high = this.rows.length
    while (low < high) {
      const middle = (low + high) >> 1
      if (this.rows[middle]! < row) low = middle + 1
      else high = middle
    }
    return low
  }

  /** The position of `row` in `rows`, or -1. */
  private rowPosition(row: number): number {
    const i = this.lowerBound(row)
    return i < this.rows.length && this.rows[i] === row ? i : -1
  }
}

/** Rows already strictly ascending, each with a positive count - the shape the service answers in. */
function isStrictlyAscendingWithCounts(rows: ArrayLike<number>, counts: ArrayLike<number>): boolean {
  for (let i = 0; i < rows.length; i++) {
    if (!(counts[i]! > 0)) return false
    if (i > 0 && rows[i]! <= rows[i - 1]!) return false
  }
  return true
}
