/**
 * Full-text-search snippets, built ON DEMAND in the page.
 *
 * The service no longer sends snippet HTML per hit (a high-frequency word is hundreds of
 * thousands of hits, and their HTML was most of the payload, the IndexedDB cache and the
 * page's memory). Each hit carries a DESCRIPTOR instead — which lines, which raw window,
 * which highlight ranges (see FtsHit in SeforimDbFtsModels.cs). Only the rows the
 * results list mounts fetch their line texts (getLineContents, batched) and have
 * their HTML rebuilt here, and both are kept in small LRUs.
 *
 * buildSnippetHtml is a PORT of FtsLib's SnippetBuilder.RenderFromRaw + AppendRawStripped
 * and must produce the very same string the service used to send — keep the two in step.
 */
import { shallowRef } from 'vue'
import { ftsSnippets, getLineContents } from '@/webview-host/seforimApi'
import { SNIPPET_FLAG_LEADING_ELLIPSIS, SNIPPET_FLAG_TRAILING_ELLIPSIS, SNIPPET_MARK_STRIDE } from './fullTextSearchTypes'
import type { FullTextSearchResult } from './fullTextSearchTypes'

/** Rebuilds the snippet HTML from its descriptor and the joined source text. */
export function buildSnippetHtml(source: string, hit: FullTextSearchResult): string {
  const snapStart = hit.snippetStart
  const snapEnd = Math.min(hit.snippetEnd, source.length)
  const marks = hit.snippetMarks ?? []
  const flags = hit.snippetFlags ?? 0
  let out = flags & SNIPPET_FLAG_LEADING_ELLIPSIS ? '…' : ''

  // AppendRawStripped: source[from..to) with tags and {X} paragraph markers (X a Hebrew
  // letter) stripped; entities pass through as-is. A `from` that lands mid-tag is
  // detected by scanning backwards, exactly as the C# does.
  const appendStripped = (from: number, to: number) => {
    if (to > snapEnd) to = snapEnd
    let inTag = false
    for (let k = from - 1; k >= 0; k--) {
      const ch = source[k]
      if (ch === '>') break
      if (ch === '<') { inTag = true; break }
    }
    for (let i = from; i < to; i++) {
      const c = source[i]!
      if (inTag) { if (c === '>') inTag = false; continue }
      if (c === '<') { inTag = true; continue }
      if (c === '{' && i + 2 < to && source[i + 2] === '}') {
        const inner = source.charCodeAt(i + 1)
        if (inner >= 0x05d0 && inner <= 0x05ea) { i += 2; continue }
      }
      out += c
    }
  }

  // Marks are (start, length, termId) triples; the term id is not rendered.
  let pos = snapStart
  for (let m = 0; m + 1 < marks.length; m += SNIPPET_MARK_STRIDE) {
    const start = marks[m]!
    const end = start + marks[m + 1]!
    appendStripped(pos, start)
    out += '<mark>'
    appendStripped(start, end)
    out += '</mark>'
    pos = end
  }
  appendStripped(pos, snapEnd)
  if (flags & SNIPPET_FLAG_TRAILING_ELLIPSIS) out += '…'
  return out
}

// ── The search the snippets belong to ─────────────────────────────────────────────────

/**
 * What asking the service for a hit's snippet takes: the query as it was SENT (word
 * options applied) and the options the stream ran with, plus the search's term table —
 * the service numbers a response's matched terms on its own, and each mark's term id is
 * mapped onto the search's table so matchedTermsOf keeps reading the hits uniformly.
 * Set by useFullTextSearch for every search it starts or restores.
 */
export interface SnippetSource {
  query: string
  requireOrdered: boolean
  contextWords: number
  /** The search's id for a matched term, appending it when first seen. */
  termIdOf: (term: string) => number
}

let snippetSource: SnippetSource | null = null

export function setSnippetSource(source: SnippetSource | null): void {
  snippetSource = source
}

/** Whether the hit carries its snippet descriptor yet (the stream sends it for the first hits only). */
function hasDescriptor(hit: FullTextSearchResult): boolean {
  return hit.snippetLineIds.length > 0
}

/**
 * Asks the service for these hits' snippet descriptors and writes them onto the hits
 * (the one place a hit changes after it streamed), seeding their line texts too. Hits
 * the service no longer matches keep an empty descriptor and render as an empty
 * snippet. Rejects on a failed request, so the rows are not mounted empty.
 */
async function fetchDescriptors(hits: FullTextSearchResult[]): Promise<void> {
  const source = snippetSource
  if (!source) return
  const byLineId = new Map(hits.map((hit) => [hit.lineId, hit]))
  const response = await ftsSnippets(source.query, [...byLineId.keys()], source.requireOrdered, source.contextWords)
  const termIds = response.terms.map(source.termIdOf)
  for (const row of response.hits) {
    const hit = byLineId.get(row.lineId)
    if (!hit) continue
    const marks = row.snippetMarks.slice()
    for (let i = SNIPPET_MARK_STRIDE - 1; i < marks.length; i += SNIPPET_MARK_STRIDE) marks[i] = termIds[marks[i]!] ?? marks[i]!
    hit.snippetLineIds = row.snippetLineIds
    hit.snippetStart = row.snippetStart
    hit.snippetEnd = row.snippetEnd
    hit.snippetMarks = marks
    hit.snippetFlags = row.snippetFlags
  }
  seedLineTexts(response.lineTexts)
}

// ── On-demand cache ──────────────────────────────────────────────────────────────────

/** Line texts by line id. Small: a screenful of rows is at most a few hundred lines. */
const LINE_TEXT_CAP = 1500
/** Built snippet HTML by hit. */
const SNIPPET_HTML_CAP = 1500
/** getLineContents binds one parameter per id; stay well under SQLite's limit. */
const FETCH_BATCH = 200

const lineTexts = new Map<number, string>()
const snippetHtml = new Map<FullTextSearchResult, string>()
/** The fetch each in-flight line id rides on, so a second caller awaits it instead of asking again. */
const inFlight = new Map<number, Promise<void>>()

/** Bumped whenever fetched texts land — the reactive dependency of snippetHtmlOf. */
const snippetsVersion = shallowRef(0)

function touch<K, V>(map: Map<K, V>, key: K, value: V, cap: number) {
  map.delete(key)
  map.set(key, value)
  if (map.size > cap) map.delete(map.keys().next().value as K)
}

function textOf(lineId: number): string | undefined {
  const text = lineTexts.get(lineId)
  if (text !== undefined) touch(lineTexts, lineId, text, LINE_TEXT_CAP)
  return text
}

/**
 * The hit's snippet HTML, or undefined while its line texts are not loaded (the row
 * shows a placeholder). Reactive: re-evaluates when requested texts arrive.
 */
export function snippetHtmlOf(hit: FullTextSearchResult): string | undefined {
  void snippetsVersion.value
  const cached = snippetHtml.get(hit)
  if (cached !== undefined) {
    touch(snippetHtml, hit, cached, SNIPPET_HTML_CAP)
    return cached
  }
  const ids = hit.snippetLineIds?.length ? hit.snippetLineIds : [hit.lineId]
  const parts: string[] = []
  for (const id of ids) {
    const text = textOf(id)
    if (text === undefined) return undefined
    // The service joins only NON-empty neighbours; a line that came back empty here is one
    // it would have dropped too (or a line since removed — then the window is clamped).
    if (text) parts.push(text)
  }
  const html = buildSnippetHtml(parts.join(' '), hit)
  touch(snippetHtml, hit, html, SNIPPET_HTML_CAP)
  return html
}

/**
 * Seeds the line-text cache from texts that rode on a stream frame / result set
 * (FtsStreamChunk.LineTexts — the first hits' snippet lines, so the first screen renders
 * without a fetch). Keys are line ids; MessagePack delivers int map keys as strings, so
 * both forms are accepted. Call BEFORE the hits are handed to the list, so its first
 * render already finds them. No version bump: nothing could have observed them missing yet.
 */
export function seedLineTexts(texts: Record<string | number, string> | undefined | null): void {
  if (!texts) return
  for (const key in texts) {
    const id = Number(key)
    if (!Number.isInteger(id)) continue
    touch(lineTexts, id, texts[key] ?? '', LINE_TEXT_CAP)
  }
}

/**
 * Resolves once every given hit's snippet can be built — fetching the line texts that
 * are missing in one batched request (split only at FETCH_BATCH ids) and awaiting those
 * another call already has in flight. The windowed results list calls this BEFORE it
 * mounts a range of rows, so a row is never on screen without its snippet. Rejects when
 * a fetch fails, so the list mounts nothing rather than empty rows.
 */
export async function ensureSnippets(hits: readonly FullTextSearchResult[]): Promise<void> {
  // Descriptors first: a hit past the stream's first ones has none until asked for, and
  // which lines its snippet spans is only known once it does.
  const withoutDescriptor = hits.filter((hit) => !snippetHtml.has(hit) && !hasDescriptor(hit))
  if (withoutDescriptor.length) await fetchDescriptors(withoutDescriptor)
  const missing: number[] = []
  const waits = new Set<Promise<void>>()
  for (const hit of hits) {
    if (snippetHtml.has(hit)) continue
    const ids = hit.snippetLineIds?.length ? hit.snippetLineIds : [hit.lineId]
    for (const id of ids) {
      if (lineTexts.has(id)) continue
      const pending = inFlight.get(id)
      if (pending) { waits.add(pending); continue }
      missing.push(id)
    }
  }
  for (let i = 0; i < missing.length; i += FETCH_BATCH) {
    const batch = missing.slice(i, i + FETCH_BATCH)
    const fetching = getLineContents(batch)
      .then((rows) => {
        for (const row of rows) touch(lineTexts, row.id, row.content ?? '', LINE_TEXT_CAP)
        // An id the service did not return has no line (any more): remember it as empty so
        // the row renders what it can instead of asking forever.
        for (const id of batch) if (!lineTexts.has(id)) touch(lineTexts, id, '', LINE_TEXT_CAP)
      })
      .finally(() => {
        for (const id of batch) if (inFlight.get(id) === fetching) inFlight.delete(id)
        snippetsVersion.value++
      })
    for (const id of batch) inFlight.set(id, fetching)
    waits.add(fetching)
  }
  if (waits.size) await Promise.all(waits)
}
