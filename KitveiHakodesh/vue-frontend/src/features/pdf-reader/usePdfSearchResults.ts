import { shallowReactive, shallowRef, ref, watch } from 'vue'
import { useThrottleFn } from '@vueuse/core'
import { FindState, type PdfScroller } from './usePdfScroller'
import type { PDFPageProxy } from 'pdfjs-dist'

/** One match of the current search: its page and its place in PDF.js's find controller. */
export interface PdfSearchHit {
  pageIndex: number
  /** The match's index on its page (findController.pageMatches[pageIndex]). */
  matchIndex: number
  /** Offset and length in the page's text as the find controller extracted it (before normalizing). */
  pos: number
  len: number
}

export interface PdfSearchSnippet {
  before: string
  match: string
  after: string
}

/** The find controller's fields this panel reads and sets (pdf_find_controller.js; not in its types). */
interface FindControllerInternals {
  pageMatches: number[][]
  pageMatchesLength: number[][]
  _matchesCountTotal: number
  /** A new search waits to start (its matches still the old search's). */
  _dirtyMatch: boolean
  _highlightMatches: boolean
  _scrollMatches: boolean
  _selected: { pageIdx: number; matchIdx: number }
  _offset: { pageIdx: number | null; matchIdx: number | null; wrapped: boolean }
}

/** A page's text as the find controller's offsets count it, with each text item's place. */
interface PageText {
  text: string
  /** Offsets where a line ends (the item before had hasEOL). */
  eols: number[]
  /** Each item's start offset in `text`, ascending. */
  starts: number[]
  items: { str: string; x: number; y: number; size: number; width: number }[]
}

/**
 * Characters with nothing to show: control and format characters, private-use and unassigned
 * code points, the replacement character and empty-box glyphs. Some PDFs' fonts map their marks
 * to these; drawn, they are boxes.
 */
const GARBAGE = /[\p{Cc}\p{Cf}\p{Co}\p{Cn}\p{Cs}�□▯]/gu
/**
 * A run of 2+ stray quote / punctuation marks with no letter next to it ('''''). Latin and
 * general marks only: Hebrew geresh, gershayim, maqaf and sof pasuq (U+05BE..U+05F4) are kept.
 */
const MARK_RUN = /(?<![\p{L}\p{M}\p{N}])["'`´‘-‟′-‷.,;:!?*~^_|\\/\-]{2,}(?![\p{L}\p{M}\p{N}])/gu
/**
 * A snippet piece for display: garbage as white space, white-space runs as one space. Applied
 * to each piece AFTER it is cut at the match's offsets, so the offsets and the mark stay right.
 */
const clean = (s: string) => s.replace(GARBAGE, ' ').replace(MARK_RUN, ' ').replace(/\s+/g, ' ')

/** The item holding offset `pos` (the last one starting at or before it). */
function itemAt(page: PageText, pos: number): number {
  let lo = 0
  let hi = page.starts.length - 1
  while (lo < hi) {
    const mid = (lo + hi + 1) >> 1
    if (page.starts[mid]! <= pos) lo = mid
    else hi = mid - 1
  }
  return lo
}

const copyCache = new WeakMap<PageText, boolean[]>()
/**
 * Per item: it repeats the text of one of the items just before it, in the same font size, at the
 * same place (within 15% of the font size) - the find controller's findDrawnCopies rule.
 */
function copyItemsOf(page: PageText): boolean[] {
  let out = copyCache.get(page)
  if (out) return out
  out = page.items.map((item, i) => {
    if (!item.str.trim()) return false
    const tolerance = Math.max(0.5, item.size * 0.15)
    for (let j = i - 1; j >= 0 && j >= i - COPY_LOOKBACK; j--) {
      const other = page.items[j]!
      if (
        other.str === item.str &&
        Math.abs(other.size - item.size) <= tolerance &&
        Math.abs(other.x - item.x) <= tolerance &&
        Math.abs(other.y - item.y) <= tolerance
      ) return true
    }
    return false
  })
  copyCache.set(page, out)
  return out
}

/** Characters of context on each side of a match. */
const CONTEXT = 40
/** How many items back a copy of the same text drawn at the same place is looked for. */
const COPY_LOOKBACK = 16

/**
 * The search-results side panel's data: every match of the toolbar's current search, read from
 * PDF.js's find controller as it searches page by page, and a snippet of text around each.
 *
 * The controller keeps each match as an offset into the page's ORIGINAL text (it maps the match
 * in its normalized text back through getOriginalIndex), and keeps only the normalized text. So
 * the snippets come from the page's text extracted as #queueExtractText does it (getTextContent
 * with disableNormalization). Its "\n" after each hasEOL item is NOT in the original offsets
 * (normalize() maps it out), so the text here is the items' text only, the line ends kept aside.
 *
 * Copies: some PDFs draw the same text several times a fraction of a point apart (a fake bold).
 * PDF.js's find controller (our fork) already leaves those copies out of its matches; the
 * snippets leave them out too (copyItemsOf, the same rule), so the context is not the same words
 * repeated.
 */
export function usePdfSearchResults(pdf: PdfScroller) {
  const fc = pdf.findController as unknown as FindControllerInternals
  /** Every match of the current search, in document order. */
  const hits = shallowRef<PdfSearchHit[]>([])
  /** The search is still going through pages. */
  const searching = ref(false)
  /** A search is on (a query sent, the bar not closed). */
  const hasQuery = ref(false)
  /** Extracted page texts, by page index. */
  const pageTexts = shallowReactive(new Map<number, PageText>())
  const pending = new Set<number>()
  let textGeneration = 0

  function collect() {
    if (!hasQuery.value || fc._dirtyMatch) {
      hits.value = []
      return
    }
    const out: PdfSearchHit[] = []
    const matches = fc.pageMatches
    const lengths = fc.pageMatchesLength
    for (let p = 0; p < matches.length; p++) {
      const m = matches[p]
      if (!m?.length) continue
      const l = lengths[p]
      for (let i = 0; i < m.length; i++) out.push({ pageIndex: p, matchIndex: i, pos: m[i] ?? 0, len: l?.[i] ?? 0 })
    }
    hits.value = out
  }
  // The controller reports a count after each page with matches: gather at most ~10 times a second.
  const refresh = useThrottleFn(collect, 100, true, false)

  pdf.eventBus.on('find', (e: Record<string, unknown>) => {
    if (e.type === 'highlightallchange') return
    const q = e.query
    hasQuery.value = typeof q === 'string' ? q.trim() !== '' : Array.isArray(q) && q.length > 0
    // A new search: the controller drops the old matches when it starts (after its short delay).
    if (!e.type) hits.value = []
  })
  pdf.eventBus.on('findbarclose', () => {
    hasQuery.value = false
    searching.value = false
    collect()
  })
  pdf.eventBus.on('updatefindmatchescount', () => void refresh())
  pdf.eventBus.on('updatefindcontrolstate', (e: { state: number }) => {
    searching.value = e.state === FindState.PENDING
    void refresh()
  })

  function resetTexts() {
    textGeneration++
    pageTexts.clear()
    pending.clear()
  }
  // Pages moved / deleted / pasted: page indexes change, and so does their text.
  pdf.eventBus.on('pagesedited', () => {
    resetTexts()
    void refresh()
  })
  watch(pdf.pdfDocument, () => {
    resetTexts()
    hasQuery.value = false
    hits.value = []
  })

  async function loadPageText(pageIndex: number) {
    const doc = pdf.pdfDocument.value
    if (!doc || pending.has(pageIndex)) return
    pending.add(pageIndex)
    const generation = textGeneration
    try {
      // The find controller already read this page's text for its hits (our PDF.js fork keeps the
      // last pages' text): reuse it instead of reading the page a second time.
      const kept = (pdf.findController as unknown as { getPageTextContent?: (i: number) => Awaited<ReturnType<PDFPageProxy['getTextContent']>> | null })
        .getPageTextContent?.(pageIndex)
      const content = kept ?? (await (await doc.getPage(pageIndex + 1)).getTextContent({ disableNormalization: true }))
      if (generation !== textGeneration) return
      const out: PageText = { text: '', eols: [], starts: [], items: [] }
      for (const item of content.items) {
        if (!('str' in item)) continue
        out.starts.push(out.text.length)
        const [a = 0, b = 0, , , x = 0, y = 0] = item.transform as number[]
        out.items.push({ str: item.str, x, y, size: Math.hypot(a, b) || 1, width: item.width })
        out.text += item.str
        if (item.hasEOL) out.eols.push(out.text.length)
      }
      pageTexts.set(pageIndex, out)
    } catch {
      if (generation === textGeneration) pageTexts.set(pageIndex, { text: '', eols: [], starts: [], items: [] })
    } finally {
      if (generation === textGeneration) pending.delete(pageIndex)
    }
  }

  /** The text around a hit (null until its page's text is in); line breaks as spaces. */
  function snippetOf(hit: PdfSearchHit): PdfSearchSnippet | null {
    const page = pageTexts.get(hit.pageIndex)
    if (page === undefined) {
      void loadPageText(hit.pageIndex)
      return null
    }
    const { text, eols, starts, items } = page
    const copies = copyItemsOf(page)
    const eolSet = new Set(eols)
    // text[a, b) as shown: copies of text drawn twice left out, a space at each line end,
    // garbage characters as spaces and white-space runs as one (clean).
    const flat = (a: number, b: number, gapBefore = false) => {
      let out = ''
      let prev: PageText['items'][number] | null = null
      // A piece starting right at an item: the gap to the shown item before it counts too (the
      // space between the match and the word after it).
      const first = itemAt(page, a)
      if (gapBefore && starts[first] === a) for (let j = first - 1; j >= 0 && !prev; j--) if (!copies[j]) prev = items[j]!
      for (let k = first; k < items.length && starts[k]! < b; k++) {
        const s0 = starts[k]!
        const item = items[k]!
        const s1 = s0 + item.str.length
        if (!copies[k]) {
          // Some PDFs draw each letter as its own item and leave the spaces out as gaps (the
          // text layer shows them by position): a gap of a fifth of the font size between two
          // items on one line is shown as a space.
          if (prev && s0 >= a && Math.abs(prev.y - item.y) < item.size / 2) {
            const gap = Math.min(Math.abs(prev.x - (item.x + item.width)), Math.abs(item.x - (prev.x + prev.width)))
            if (gap > item.size / 5) out += ' '
          }
          out += text.slice(Math.max(a, s0), Math.min(b, s1))
          prev = item
        }
        if (eolSet.has(s1) && s1 > a && s1 < b) out += ' '
      }
      return clean(out)
    }
    // Context counted in CLEAN text: garbage and copies do not use it up, so read on past them
    // (in steps) until there is CONTEXT shown characters on each side, or the page ends.
    const STEP = 24
    const matchEnd = hit.pos + hit.len
    let start = hit.pos
    while (start > 0 && flat(start, hit.pos).trim().length < CONTEXT) start = Math.max(0, start - STEP)
    let end = matchEnd
    while (end < text.length && flat(matchEnd, end).trim().length < CONTEXT) end = Math.min(text.length, end + STEP)
    // Whole words at the edges where there is room.
    if (start > 0) {
      const space = text.indexOf(' ', start)
      if (space !== -1 && space < hit.pos) start = space + 1
    }
    if (end < text.length) {
      const space = text.lastIndexOf(' ', end)
      if (space > matchEnd) end = space
    }
    return {
      before: (start > 0 ? '…' : '') + flat(start, hit.pos) + (eolSet.has(hit.pos) ? ' ' : ''),
      match: flat(hit.pos, matchEnd),
      after: (eolSet.has(matchEnd) ? ' ' : '') + flat(matchEnd, end, true) + (end < text.length ? '…' : ''),
    }
  }

  /**
   * Make a hit the find controller's selected match and show it, as its own #updateMatch /
   * #updatePage do: the selected and offset positions set (so Next / Previous continue from it),
   * the page shown (linkService.page), the old and new pages' highlights redrawn - the text
   * layer's highlighter then scrolls the selected match into view (scrollMatchIntoView, which
   * acts only while _scrollMatches is set) - and the controller's count sent again.
   */
  function select(hit: PdfSearchHit) {
    const viewer = pdf.viewer.value
    if (!viewer) return
    pdf.jumpHistory.pushCurrentPosition()
    const previousPage = fc._selected.pageIdx
    fc._selected.pageIdx = fc._offset.pageIdx = hit.pageIndex
    fc._selected.matchIdx = fc._offset.matchIdx = hit.matchIndex
    fc._offset.wrapped = false
    fc._highlightMatches = true
    fc._scrollMatches = true
    if (previousPage !== -1 && previousPage !== hit.pageIndex) {
      pdf.eventBus.dispatch('updatetextlayermatches', { source: pdf.findController, pageIndex: previousPage })
    }
    viewer.currentPageNumber = hit.pageIndex + 1
    pdf.eventBus.dispatch('updatetextlayermatches', { source: pdf.findController, pageIndex: hit.pageIndex })
    let raw = hit.matchIndex + 1
    for (let p = 0; p < hit.pageIndex; p++) raw += fc.pageMatches[p]?.length ?? 0
    pdf.eventBus.dispatch('updatefindmatchescount', {
      source: pdf.findController,
      matchesCount: { current: raw, total: fc._matchesCountTotal },
    })
  }

  return { hits, searching, hasQuery, snippetOf, select }
}
