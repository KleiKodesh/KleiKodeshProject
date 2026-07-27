/**
 * Windowed live preview ("הצג עוד") for full-text-search results.
 *
 * Toggling a result swaps its clamped snippet for a fixed-height scrollable
 * window over the book, seeded around the matched line. Scrolling toward
 * either edge lazily loads a chunk of lines in that direction — the whole
 * book is reachable but only the visited window is ever in memory.
 *
 * Highlighting reuses the book view's highlightFromSnippet — the same
 * diacritic/entity-aware walk that marks the snippet's matched terms anywhere
 * in the loaded lines.
 */
import { reactive } from 'vue'
import { getLineContents, getLineIndexFromLineId, getLinesPaged, getWordLinkAnchorsForLines, type WordLinkAnchor } from '@/webview-host/seforimApi'
import { highlightFromSnippet } from '@/features/book-view/lines/useBookViewLineRenderer'
import { applyWordLinkAnchors } from '@/features/book-view/lines/wordLinkAnchors'
import type { FullTextSearchResult } from './fullTextSearchTypes'

export interface PreviewLine {
  id: number
  lineIndex: number
  html: string
}

export interface PreviewState {
  loading: boolean
  /** Matched line's 0-based index within its book (row offset in getLinesPaged). */
  lineIndex: number
  /** Loaded window bounds — inclusive line indexes. */
  lo: number
  hi: number
  atStart: boolean
  atEnd: boolean
  lines: PreviewLine[]
  /** Preview scroll position, preserved across virtual-list unmount/remount. */
  scrollTop: number
}

// Seed window around the matched line + how many lines each edge-load adds.
// The seed is deliberately much larger than a chunk: the scrollbar thumb size is
// clientHeight/scrollHeight, so the bigger the already-loaded window, the less the
// thumb visibly shrinks when a chunk lands (live-tuned — see the 2026-07-20 rework).
const SEED_ABOVE = 12
const SEED_BELOW = 28
const CHUNK = 10

export function useFullTextSearchPreview() {
  // Keyed by lineId — stable across streaming flushes and re-sorts of the results array.
  const previews = reactive(new Map<number, PreviewState>())

  const previewOf = (result: FullTextSearchResult) => previews.get(result.lineId)

  function clearPreviews() {
    previews.clear()
  }

  async function fetchLines(result: FullTextSearchResult, lo: number, count: number): Promise<PreviewLine[]> {
    const rows = await getLinesPaged(result.bookId, count, lo)
    // Word-level link anchors for the fetched window — one batched call, [] on
    // schema-v1 DBs (probe short-circuits inside seforimApi). Best-effort: the
    // preview renders fine without them.
    const anchorsByLine = await fetchAnchors(rows.map((row) => row.id))
    return rows.map((row) => ({
      id: row.id,
      lineIndex: row.lineIndex,
      html: highlightFromSnippet(spliceAnchors(row.content ?? '', anchorsByLine.get(row.id)), result.snippet),
    }))
  }

  async function fetchAnchors(lineIds: number[]): Promise<Map<number, WordLinkAnchor[]>> {
    const byLine = new Map<number, WordLinkAnchor[]>()
    if (!lineIds.length) return byLine
    try {
      for (const row of await getWordLinkAnchorsForLines(lineIds)) {
        let list = byLine.get(row.lineId)
        if (!list) byLine.set(row.lineId, (list = []))
        list.push(row)
      }
    } catch { /* best-effort */ }
    return byLine
  }

  function spliceAnchors(content: string, anchors: WordLinkAnchor[] | undefined): string {
    return anchors?.length ? applyWordLinkAnchors(content, anchors) : content
  }

  async function togglePreview(result: FullTextSearchResult) {
    if (previews.has(result.lineId)) {
      // Close = dispose. The window is cheap to rebuild, so nothing is kept —
      // the next toggle loads a fresh seed around the matched line.
      previews.delete(result.lineId)
      return
    }

    const st: PreviewState = reactive({
      loading: true,
      lineIndex: -1,
      lo: 0,
      hi: -1,
      atStart: false,
      atEnd: false,
      lines: [],
      scrollTop: 0,
    })
    previews.set(result.lineId, st)
    try {
      const idxRows = await getLineIndexFromLineId(result.lineId)
      st.lineIndex = idxRows[0]?.lineIndex ?? -1
      if (st.lineIndex >= 0) {
        const lo = Math.max(0, st.lineIndex - SEED_ABOVE)
        const count = st.lineIndex + SEED_BELOW - lo + 1
        const lines = await fetchLines(result, lo, count)
        st.lines = lines
        st.lo = lo
        st.hi = lines.length ? lines[lines.length - 1]!.lineIndex : lo - 1
        st.atStart = lo === 0
        st.atEnd = lines.length < count
      }
      if (!st.lines.length) {
        // Line not reachable through the line table (e.g. custom books) — fall
        // back to a fixed single-line window over the full matched line.
        const contents = await getLineContents([result.lineId])
        const content = contents[0]?.content
        const anchorsByLine = content != null ? await fetchAnchors([result.lineId]) : new Map<number, WordLinkAnchor[]>()
        st.lines = [{
          id: result.lineId,
          lineIndex: st.lineIndex,
          html: content != null
            ? highlightFromSnippet(spliceAnchors(content, anchorsByLine.get(result.lineId)), result.snippet)
            : result.snippet,
        }]
        st.atStart = true
        st.atEnd = true
      }
    } catch (err) {
      console.error('[useFullTextSearchPreview] open failed:', err)
      previews.delete(result.lineId) // let the user retry
    } finally {
      st.loading = false
    }
  }

  /** Prepend up to CHUNK lines above the window. Returns how many lines were added. */
  async function loadAbove(result: FullTextSearchResult): Promise<number> {
    const st = previews.get(result.lineId)
    if (!st || st.loading || st.atStart) return 0
    st.loading = true
    try {
      const lo = Math.max(0, st.lo - CHUNK)
      const count = st.lo - lo
      if (count <= 0) {
        st.atStart = true
        return 0
      }
      const lines = await fetchLines(result, lo, count)
      st.lines = [...lines, ...st.lines]
      st.lo = lo
      st.atStart = lo === 0
      return lines.length
    } catch (err) {
      console.error('[useFullTextSearchPreview] loadAbove failed:', err)
      return 0
    } finally {
      st.loading = false
    }
  }

  /** Append up to CHUNK lines below the window. Returns how many lines were added. */
  async function loadBelow(result: FullTextSearchResult): Promise<number> {
    const st = previews.get(result.lineId)
    if (!st || st.loading || st.atEnd) return 0
    st.loading = true
    try {
      const lines = await fetchLines(result, st.hi + 1, CHUNK)
      if (lines.length) {
        st.lines = [...st.lines, ...lines]
        st.hi = lines[lines.length - 1]!.lineIndex
      }
      if (lines.length < CHUNK) st.atEnd = true
      return lines.length
    } catch (err) {
      console.error('[useFullTextSearchPreview] loadBelow failed:', err)
      return 0
    } finally {
      st.loading = false
    }
  }

  return { previews, previewOf, togglePreview, loadAbove, loadBelow, clearPreviews }
}
