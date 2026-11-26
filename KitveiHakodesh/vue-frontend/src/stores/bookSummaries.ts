/**
 * A book's title and catalog path by id, asked for only for the books actually shown
 * (getBookSummaries) and kept in a small LRU - in place of the whole catalog held in memory.
 *
 * The readers are reactive and self-fetching: a book not held yet reads empty and is asked
 * for; every id asked for in the same tick goes in one request; the reader re-evaluates when
 * it lands. `requestBookTitles(ids)` warms a list's rows ahead and can be awaited.
 */
import { shallowRef } from 'vue'
import { getBookSummaries, type BookSummary } from '@/webview-host/seforimApi'

/** Books held. Distinct books on screen are few; this is far above that. */
const CAP = 2000
/** Ids per request; the service binds one parameter per id, under SQLite's 999 limit. */
const BATCH = 500

const summaries = new Map<number, BookSummary | null>()
/** Ids asked for this tick (not sent yet) or on their way, with the request that brings them. */
const pending = new Map<number, Promise<void>>()
let queued: number[] = []
let flush: Promise<void> | null = null
const version = shallowRef(0)

function remember(id: number, summary: BookSummary | null) {
  summaries.delete(id)
  summaries.set(id, summary)
  if (summaries.size > CAP) summaries.delete(summaries.keys().next().value as number)
}

async function send(ids: number[]): Promise<void> {
  for (let i = 0; i < ids.length; i += BATCH) {
    const batch = ids.slice(i, i + BATCH)
    try {
      const found = new Map((await getBookSummaries(batch)).map((row) => [row.id, row]))
      // A book the library lacks is held as null so it is not asked for again.
      for (const id of batch) remember(id, found.get(id) ?? null)
      version.value++
    } catch (err) {
      // No re-render on failure: a reader re-asking at once would loop while the DB is
      // down. The next render asks again, or refreshBookSummaries() once it is up.
      console.error('[bookSummaries] fetch failed:', err)
    } finally {
      for (const id of batch) pending.delete(id)
    }
  }
}

/** Fetches the books among these that are not held or already asked for. */
export function requestBookTitles(ids: Iterable<number>): Promise<void> {
  const waits = new Set<Promise<void>>()
  for (const id of ids) {
    if (!(id > 0) || summaries.has(id)) continue
    const held = pending.get(id)
    if (held) {
      waits.add(held)
      continue
    }
    queued.push(id)
    flush ??= Promise.resolve().then(() => {
      const ids = queued
      queued = []
      flush = null
      return send(ids)
    })
    pending.set(id, flush)
    waits.add(flush)
  }
  return Promise.all(waits).then(() => {})
}

/** Makes every reader look again - for books asked for before the database was ready. */
export function refreshBookSummaries(): void {
  version.value++
}

/** The held summary (touched), or undefined after asking for it. Reactive. */
function summaryOf(id: number): BookSummary | null | undefined {
  void version.value
  if (summaries.has(id)) {
    const summary = summaries.get(id)!
    remember(id, summary)
    return summary
  }
  void requestBookTitles([id])
  return undefined
}

/** The book's title; '' until fetched or for a book the library lacks. */
export function bookTitle(id: number): string {
  return summaryOf(id)?.title ?? ''
}

/** The book's catalog categories, root first; [] until fetched or for a book the library lacks. */
export function bookCategoryPath(id: number): string[] {
  return summaryOf(id)?.categoryPath ?? []
}

/** The book's authors, ', '-joined; '' until fetched or when it has none. */
export function bookAuthors(id: number): string {
  return summaryOf(id)?.authors ?? ''
}

/** What a result row shows of a book. */
export interface SearchBook {
  readonly id: number
  readonly title: string
  readonly authors: string
  /** The catalog path, ' / '-joined. */
  readonly parentPath: string
}

/**
 * A search result's book for a row: its fields read from the summary cache WHEN the row renders,
 * so only the rows a windowed list actually shows are ever fetched (batched, one request per
 * tick) - never the whole result set - and the page holds no catalog. Reactive: a field reads
 * '' until its summary arrives, then the row updates.
 */
export function summaryBook(id: number): SearchBook {
  return {
    id,
    get title() { return bookTitle(id) },
    get authors() { return bookAuthors(id) },
    get parentPath() { return bookCategoryPath(id).join(' / ') },
  }
}
