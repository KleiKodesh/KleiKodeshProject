export interface LineItem {
  id: number
  lineIndex: number
  /** null = not loaded. Never rendered: the scroller asks for a range before mounting it. */
  content: string | null
}

/**
 * The book's lines: one Int32 id per line (0 = not known yet) and the text of only the
 * lines near the reader (see useLines). A 190k-line book costs 760 KB of ids.
 *
 * After a version swap the old text moves to `staleTexts`: still shown (rows keep their
 * height), but not counted as loaded, so the next ensureLines fetches the new text.
 */
export class LinesTable {
  private ids: Int32Array
  private texts = new Map<number, string>()
  private staleTexts = new Map<number, string>()
  // Built on the first id lookup, dropped on every id write.
  private indexById: Map<number, number> | null = null

  constructor(count = 0) {
    this.ids = new Int32Array(count)
  }

  get length(): number {
    return this.ids.length
  }

  /** Lines whose current text is loaded. */
  get loadedCount(): number {
    return this.texts.size
  }

  growTo(count: number) {
    if (count <= this.ids.length) return
    const next = new Int32Array(count)
    next.set(this.ids)
    this.ids = next
  }

  at(index: number): LineItem | undefined {
    if (index < 0 || index >= this.ids.length) return undefined
    return { id: this.idAt(index), lineIndex: index, content: this.contentAt(index) }
  }

  /** The line's id, or its negative placeholder (-(index+1)) while unknown. */
  idAt(index: number): number {
    return this.ids[index] || -(index + 1)
  }

  hasId(index: number): boolean {
    return this.ids[index]! > 0
  }

  contentAt(index: number): string | null {
    return this.texts.get(index) ?? this.staleTexts.get(index) ?? null
  }

  /** The line's text is loaded under the current version. */
  hasText(index: number): boolean {
    return this.texts.has(index)
  }

  setText(index: number, id: number, text: string) {
    this.setId(index, id)
    this.texts.set(index, text)
    this.staleTexts.delete(index)
  }

  setId(index: number, id: number) {
    if (id <= 0 || this.ids[index] === id) return
    this.ids[index] = id
    this.indexById = null
  }

  /** A version swap: every loaded text becomes stale (shown until replaced). */
  markAllStale() {
    for (const [index, text] of this.texts) this.staleTexts.set(index, text)
    this.texts.clear()
  }

  /** Releases the text of every line `keep` rejects; returns the released lines' ids. */
  releaseTextsExcept(keep: (index: number) => boolean): number[] {
    const released: number[] = []
    for (const store of [this.texts, this.staleTexts]) {
      for (const index of store.keys()) {
        if (keep(index)) continue
        store.delete(index)
        released.push(this.idAt(index))
      }
    }
    return released
  }

  /** The ids of [from, to), clamped; unknown ones as their negative placeholders. */
  idsIn(from: number, to: number): number[] {
    const ids: number[] = []
    for (let i = Math.max(0, from); i < Math.min(this.ids.length, to); i++) ids.push(this.idAt(i))
    return ids
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
}
