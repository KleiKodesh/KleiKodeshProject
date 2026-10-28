/**
 * Full-text-search snippets, built ON DEMAND in the page.
 *
 * Every hit carries its snippet DESCRIPTOR — the raw window and highlight ranges over the
 * MATCHED LINE's text, located once by the search's own check (see FtsHit in
 * SeforimDbFtsModels.cs). Only the rows the results list mounts fetch their texts
 * (ftsSnippetTexts, one batched request) and have their HTML built here; both are kept in
 * small LRUs. A hit flagged SNIPPET_FLAG_SHORT is fetched with its neighbour lines too, and
 * gets them as plain context around the highlighted window — never part of the descriptor,
 * so nothing is re-located and no offset shifts.
 *
 * buildSnippetHtml is a PORT of FtsLib's SnippetBuilder.RenderFromRaw + AppendRawStripped
 * and must produce the very same string for the window — keep the two in step.
 */
import { shallowRef } from 'vue'
import { ftsSnippetTexts, type FtsSnippetText } from '@/webview-host/seforimApi'
import {
  SNIPPET_FLAG_LEADING_ELLIPSIS, SNIPPET_FLAG_SHORT, SNIPPET_FLAG_TRAILING_ELLIPSIS, SNIPPET_MARK_STRIDE,
} from './fullTextSearchTypes'
import type { FullTextSearchResult } from './fullTextSearchTypes'

/**
 * source[from..to) with tags and {X} paragraph markers (X a Hebrew letter) stripped;
 * entities pass through as-is. A `from` that lands mid-tag is detected by scanning
 * backwards, exactly as the C# AppendRawStripped does.
 */
function stripRaw(source: string, from: number, to: number): string {
  let out = ''
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
  return out
}

/** Rebuilds the snippet window's HTML from its descriptor and the matched line's text. */
export function buildSnippetHtml(source: string, hit: FullTextSearchResult): string {
  const snapStart = hit.snippetStart
  const snapEnd = Math.min(hit.snippetEnd, source.length)
  const marks = hit.snippetMarks ?? []
  const flags = hit.snippetFlags ?? 0
  let out = flags & SNIPPET_FLAG_LEADING_ELLIPSIS ? '…' : ''
  const append = (from: number, to: number) => { out += stripRaw(source, from, Math.min(to, snapEnd)) }

  // Marks are (start, length, termId) triples; the term id is not rendered.
  let pos = snapStart
  for (let m = 0; m + 1 < marks.length; m += SNIPPET_MARK_STRIDE) {
    const start = marks[m]!
    const end = start + marks[m + 1]!
    append(pos, start)
    out += '<mark>'
    append(start, end)
    out += '</mark>'
    pos = end
  }
  append(pos, snapEnd)
  if (flags & SNIPPET_FLAG_TRAILING_ELLIPSIS) out += '…'
  return out
}

/**
 * Plain context for a short window: the last `words` words of `before` and the first
 * `words` of `after`, stripped like the window, with "…" where the context is cut. A side
 * the window itself already cut (its own "…") gets no context.
 */
function withContext(windowHtml: string, text: FtsSnippetText, hit: FullTextSearchResult, words: number): string {
  const flags = hit.snippetFlags ?? 0
  let html = windowHtml
  if (text.before && !(flags & SNIPPET_FLAG_LEADING_ELLIPSIS)) {
    const all = stripRaw(text.before, 0, text.before.length).split(/\s+/).filter(Boolean)
    const kept = all.slice(-words)
    if (kept.length) html = (kept.length < all.length ? '…' : '') + kept.join(' ') + ' ' + html
  }
  if (text.after && !(flags & SNIPPET_FLAG_TRAILING_ELLIPSIS)) {
    const all = stripRaw(text.after, 0, text.after.length).split(/\s+/).filter(Boolean)
    const kept = all.slice(0, words)
    if (kept.length) html = html + ' ' + kept.join(' ') + (kept.length < all.length ? '…' : '')
  }
  return html
}

// ── The search the snippets belong to ─────────────────────────────────────────────────

/** Words of context per side — the search's setting. Set by useFullTextSearch per search. */
let contextWords = 8

export function setSnippetContextWords(words: number): void {
  contextWords = words
}

function isShort(hit: FullTextSearchResult): boolean {
  return ((hit.snippetFlags ?? 0) & SNIPPET_FLAG_SHORT) !== 0
}

/** The cached text this hit can render from, or undefined when it must be fetched. */
function cachedTextFor(hit: FullTextSearchResult): CachedText | undefined {
  const text = snippetTexts.get(hit.lineId)
  return text && (text.withContext || !isShort(hit)) ? text : undefined
}

// ── On-demand cache ──────────────────────────────────────────────────────────────────

/** Snippet texts by line id. Small: a screenful of rows is at most a few hundred lines. */
const TEXT_CAP = 1500
/** Built snippet HTML by hit. */
const SNIPPET_HTML_CAP = 1500
/** One request's ids; the service binds one parameter per id, well under SQLite's limit. */
const FETCH_BATCH = 200

/** A cached text, and whether it holds neighbour context — a line cached as plain (by an
 *  earlier search, or under another context setting) is a miss for a hit flagged short. */
type CachedText = FtsSnippetText & { withContext: boolean }

const snippetTexts = new Map<number, CachedText>()
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

/**
 * The hit's snippet HTML, or undefined while its text is not loaded (the row shows a
 * placeholder). Reactive: re-evaluates when requested texts arrive.
 */
export function snippetHtmlOf(hit: FullTextSearchResult): string | undefined {
  void snippetsVersion.value
  const cached = snippetHtml.get(hit)
  if (cached !== undefined) {
    touch(snippetHtml, hit, cached, SNIPPET_HTML_CAP)
    return cached
  }
  const text = cachedTextFor(hit)
  if (text === undefined) return undefined
  touch(snippetTexts, hit.lineId, text, TEXT_CAP)
  let html = buildSnippetHtml(text.text, hit)
  if (isShort(hit)) html = withContext(html, text, hit, contextWords)
  touch(snippetHtml, hit, html, SNIPPET_HTML_CAP)
  return html
}

/**
 * Seeds the text cache from texts that rode on a stream frame / result set
 * (FtsStreamChunk.SnippetTexts — the first hits', so the first screen renders without a
 * fetch). Call BEFORE the hits are handed to the list, so its first render already finds
 * them. No version bump: nothing could have observed them missing yet.
 */
export function seedSnippetTexts(texts: readonly FtsSnippetText[] | undefined | null): void {
  if (!texts) return
  // A seeded text with neighbours was read as short; one without may be either, and a short
  // hit then asks once more (a line with no neighbours at all — rare and cheap).
  for (const text of texts) touch(snippetTexts, text.lineId, { ...text, withContext: !!(text.before || text.after) }, TEXT_CAP)
}

/**
 * Resolves once every given hit's snippet can be built — fetching the texts that are
 * missing in one batched request (split only at FETCH_BATCH ids; short hits ask for their
 * neighbour context in the same request) and awaiting those another call already has in
 * flight. The windowed results list calls this BEFORE it mounts a range of rows, so a row
 * is never on screen without its snippet. Rejects when a fetch fails, so the list mounts
 * nothing rather than empty rows.
 */
export async function ensureSnippets(hits: readonly FullTextSearchResult[]): Promise<void> {
  const missing: FullTextSearchResult[] = []
  const waits = new Set<Promise<void>>()
  for (const hit of hits) {
    if (snippetHtml.has(hit) || cachedTextFor(hit)) continue
    const pending = inFlight.get(hit.lineId)
    if (pending) { waits.add(pending); continue }
    if (missing.some((m) => m.lineId === hit.lineId)) continue
    missing.push(hit)
  }
  for (let i = 0; i < missing.length; i += FETCH_BATCH) {
    const batch = missing.slice(i, i + FETCH_BATCH)
    const lineIds: number[] = []
    const shortLineIds: number[] = []
    for (const hit of batch) (isShort(hit) ? shortLineIds : lineIds).push(hit.lineId)
    const fetching = ftsSnippetTexts(lineIds, shortLineIds)
      .then((rows) => {
        const asShort = new Set(shortLineIds)
        for (const row of rows) touch(snippetTexts, row.lineId, { ...row, withContext: asShort.has(row.lineId) }, TEXT_CAP)
        // An id the service did not return has no line (any more): remember it as empty so
        // the row renders what it can instead of asking forever.
        for (const hit of batch) {
          if (!cachedTextFor(hit)) touch(snippetTexts, hit.lineId, { lineId: hit.lineId, text: '', before: '', after: '', withContext: true }, TEXT_CAP)
        }
      })
      .finally(() => {
        for (const hit of batch) if (inFlight.get(hit.lineId) === fetching) inFlight.delete(hit.lineId)
        snippetsVersion.value++
      })
    for (const hit of batch) inFlight.set(hit.lineId, fetching)
    waits.add(fetching)
  }
  if (waits.size) await Promise.all(waits)
}
