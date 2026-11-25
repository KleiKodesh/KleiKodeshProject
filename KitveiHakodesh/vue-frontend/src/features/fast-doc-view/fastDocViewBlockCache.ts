import type { FastDocViewBlock } from '@/webview-host/fastDocViewApi'

/**
 * The blocks held near the reader's window - never the whole document. Bounded: past
 * `capacity`, the blocks farthest from the last requested range go first, except those
 * inside a retained range (the mounted window, a selection's ends).
 */
export class FastDocViewBlockCache<T extends { index: number } = FastDocViewBlock> {
  private readonly blocks = new Map<number, T>()
  /** Blocks still shown but to be fetched again (drawn before the index was final): missing to a fetch, kept for the eye. */
  private readonly stale = new Set<number>()

  constructor(private readonly capacity: number) {}

  get size(): number {
    return this.blocks.size
  }

  get(index: number): T | undefined {
    return this.blocks.get(index)
  }

  has(index: number): boolean {
    return this.blocks.has(index)
  }

  clear(): void {
    this.blocks.clear()
    this.stale.clear()
  }

  /**
   * Marks the blocks matching `predicate` to be fetched again; returns how many. They stay readable
   * (`get`) until the new copy is stored, so a row on screen keeps its content and height meanwhile
   * (dropping it would collapse the row and move the reader).
   */
  markStaleWhere(predicate: (block: T) => boolean): number {
    let marked = 0
    for (const [index, block] of this.blocks) {
      if (!this.stale.has(index) && predicate(block)) {
        this.stale.add(index)
        marked++
      }
    }
    return marked
  }

  /** The contiguous spans of [start, end) not in the cache (or stale), each as [first, end). */
  missingSpans(start: number, end: number): [number, number][] {
    const spans: [number, number][] = []
    let spanStart = -1
    for (let index = start; index < end; index++) {
      if (this.blocks.has(index) && !this.stale.has(index)) {
        if (spanStart >= 0) spans.push([spanStart, index])
        spanStart = -1
      } else if (spanStart < 0) spanStart = index
    }
    if (spanStart >= 0) spans.push([spanStart, end])
    return spans
  }

  /**
   * Adds fetched blocks, then trims to capacity around `focus` (the range just asked for).
   * Blocks inside `retained` ranges are never evicted.
   */
  store(
    blocks: readonly T[],
    focus: readonly [number, number],
    retained: ReadonlyArray<readonly [number, number]> = [],
  ): void {
    for (const block of blocks) {
      this.blocks.set(block.index, block)
      this.stale.delete(block.index)
    }
    if (this.blocks.size <= this.capacity) return
    const isRetained = (index: number) =>
      (index >= focus[0] && index < focus[1]) ||
      retained.some(([start, end]) => index >= start && index < end)
    const distance = (index: number) =>
      index < focus[0] ? focus[0] - index : index >= focus[1] ? index - focus[1] + 1 : 0
    const candidates = [...this.blocks.keys()]
      .filter((index) => !isRetained(index))
      .sort((first, second) => distance(second) - distance(first))
    let excess = this.blocks.size - this.capacity
    for (const index of candidates) {
      if (excess <= 0) break
      this.blocks.delete(index)
      this.stale.delete(index)
      excess--
    }
  }
}
