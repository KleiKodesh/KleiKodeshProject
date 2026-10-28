import type { FastDocViewBlock } from '@/webview-host/fastDocViewApi'

/**
 * The blocks held near the reader's window - never the whole document. Bounded: past
 * `capacity`, the blocks farthest from the last requested range go first, except those
 * inside a retained range (the mounted window, a selection's ends).
 */
export class FastDocViewBlockCache {
  private readonly blocks = new Map<number, FastDocViewBlock>()

  constructor(private readonly capacity: number) {}

  get size(): number {
    return this.blocks.size
  }

  get(index: number): FastDocViewBlock | undefined {
    return this.blocks.get(index)
  }

  has(index: number): boolean {
    return this.blocks.has(index)
  }

  clear(): void {
    this.blocks.clear()
  }

  /** The contiguous spans of [start, end) not in the cache, each as [first, end). */
  missingSpans(start: number, end: number): [number, number][] {
    const spans: [number, number][] = []
    let spanStart = -1
    for (let index = start; index < end; index++) {
      if (this.blocks.has(index)) {
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
    blocks: readonly FastDocViewBlock[],
    focus: readonly [number, number],
    retained: ReadonlyArray<readonly [number, number]> = [],
  ): void {
    for (const block of blocks) this.blocks.set(block.index, block)
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
      excess--
    }
  }
}
