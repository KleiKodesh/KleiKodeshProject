export interface LineItem {
  id: number
  lineIndex: number
  /** null = not fetched yet. Never rendered: the scroller asks for a range before mounting it. */
  content: string | null
}

/**
 * The book's lines, SPARSE: the line count, one Int32 per line for the id (0 = not
 * known yet) and a Map holding only the text that is loaded right now. A 190k-line
 * book costs 760 KB of ids, not one placeholder object per line; the text is the
 * reader's window plus the eviction budget (see useLines).
 *
 * Readers see LineItem rows synthesized on demand: `at(i)` returns `{ id, lineIndex,
 * content }` with a NEGATIVE placeholder id (-(i+1)) while the real one is unknown and
 * `content: null` while the text is not loaded — exactly the row shape of the old array.
 */
export class LinesTable {
  private ids: Int32Array
  private readonly texts = new Map<number, string>()
  // Built on the first id lookup, dropped on every id write.
  private indexById: Map<number, number> | null = null

  constructor(count = 0) {
    this.ids = new Int32Array(count)
  }

  /** The book's line count (the old array's length). */
  get length(): number {
    return this.ids.length
  }

  /** Lines whose text is loaded. */
  get loadedCount(): number {
    return this.texts.size
  }

  /** Extends the table to `count` lines. No-op if already that long. */
  growTo(count: number) {
    if (count <= this.ids.length) return
    const next = new Int32Array(count)
    next.set(this.ids)
    this.ids = next
  }

  at(index: number): LineItem | undefined {
    if (index < 0 || index >= this.ids.length) return undefined
    return { id: this.idAt(index), lineIndex: index, content: this.texts.get(index) ?? null }
  }

  /** The line's id, or its negative placeholder (-(index+1)) while unknown. */
  idAt(index: number): number {
    return this.ids[index] || -(index + 1)
  }

  hasId(index: number): boolean {
    return this.ids[index]! > 0
  }

  contentAt(index: number): string | null {
    return this.texts.get(index) ?? null
  }

  hasText(index: number): boolean {
    return this.texts.has(index)
  }

  setText(index: number, id: number, text: string) {
    this.setId(index, id)
    this.texts.set(index, text)
  }

  setId(index: number, id: number) {
    if (id <= 0 || this.ids[index] === id) return
    this.ids[index] = id
    this.indexById = null
  }

  /** Releases the line's text; its id stays. Returns false when there was none. */
  clearText(index: number): boolean {
    return this.texts.delete(index)
  }

  /** The index of a line by its id, for ids the table has learned; undefined otherwise. */
  indexOfId(id: number): number | undefined {
    if (id <= 0) return undefined
    if (!this.indexById) {
      this.indexById = new Map()
      for (let i = 0; i < this.ids.length; i++) if (this.ids[i]! > 0) this.indexById.set(this.ids[i]!, i)
    }
    return this.indexById.get(id)
  }

  /** Synthesized rows for [from, to), clamped to the table. */
  slice(from: number, to: number = this.ids.length): LineItem[] {
    const start = Math.max(0, from)
    const end = Math.min(this.ids.length, to)
    const rows: LineItem[] = []
    for (let i = start; i < end; i++) rows.push(this.at(i)!)
    return rows
  }

  /** Over every line of the book, synthesized. Whole-book actions use it after ensureAllLines. */
  map<T>(fn: (line: LineItem, index: number) => T): T[] {
    const out: T[] = new Array(this.ids.length)
    for (let i = 0; i < this.ids.length; i++) out[i] = fn(this.at(i)!, i)
    return out
  }

  /** Over the lines whose text is loaded, in index order. */
  forEachLoaded(fn: (line: LineItem & { content: string }) => void) {
    const indexes = Array.from(this.texts.keys()).sort((a, b) => a - b)
    for (const i of indexes) fn({ id: this.idAt(i), lineIndex: i, content: this.texts.get(i)! })
  }
}
