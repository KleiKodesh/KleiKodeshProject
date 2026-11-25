/** Copying a PDF.js text-layer selection: its text with word gaps intact, and Word-friendly RTL HTML. */

/**
 * Builds RTL Word-friendly HTML from a plain-text PDF selection. The PDF.js text
 * layer is a soup of absolutely-positioned spans, so we copy the visible text
 * (not its markup) and wrap each line in an RTL div — this is what pastes cleanly
 * into Word, matching the book view's copy behaviour.
 */
export function selectionToHtml(text: string): string {
  const lines = text
    .split(/\r?\n/)
    .map((l) => l.trim())
    .filter((l) => l.length > 0)
  if (lines.length === 0) return ''
  return lines.map((l) => `<div dir="rtl">${escapeHtml(l)}</div>`).join('')
}

function escapeHtml(s: string): string {
  return s
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
}

/**
 * Extracts the selected text from the PDF.js text layer with word gaps intact.
 *
 * sel.toString() is NOT usable here: in OCR'd PDFs the text layer is one span per WORD
 * with no space characters anywhere — the gaps are purely visual (absolute positioning).
 * toString() therefore concatenates whole rows into a single merged word (verified live:
 * a row of 8 word-spans came back with zero spaces). Acrobat copies the same document
 * fine because it reconstructs spacing from glyph geometry; we do the DOM equivalent —
 * walk the selected spans, clip the first/last by the range offsets, and join with a
 * space at span boundaries (and a newline when the row's top offset changes).
 *
 * PDFs whose text layer DOES embed real spaces are unaffected: joining adds a space only
 * when the boundary has none.
 */
export function selectionToText(win: Window): string {
  const sel = win.getSelection()
  if (!sel || sel.rangeCount === 0 || sel.isCollapsed) return ''
  const range = sel.getRangeAt(0)

  // Word spans only — NOT every descendant span, and NOT structural wrappers. Two
  // nesting patterns to survive:
  //  - The find bar's highlighter nests a <span class="highlight"> INSIDE the word span
  //    it matched; that child also intersects the range, so counting it too would emit
  //    the matched word twice → exclude spans that sit inside another word span.
  //  - Tagged PDFs (includeMarkedContent in the bundled viewer) wrap word spans in
  //    <span class="markedContent"> containers. Those wrappers are NOT word spans: they
  //    hold many words and their geometry doesn't track visual rows — and treating them
  //    as ancestors would exclude every real word span. So markedContent spans are
  //    dropped from the candidates AND skipped when testing for a word-span ancestor.
  const WORD_SPAN = '.textLayer span:not(.markedContent)'
  const spans = (Array.from(win.document.querySelectorAll(WORD_SPAN)) as HTMLElement[])
    .filter((el) => !el.parentElement?.closest(WORD_SPAN))
    .filter((el) => range.intersectsNode(el))
  if (spans.length === 0) return sel.toString()

  /**
   * The part of a span's text actually inside the range (first/last spans may be cut).
   * Clipped via a sub-range clamped to the selection rather than by slicing textContent
   * with the raw range offsets: an offset is only a CHARACTER offset when its container
   * is a text node (for an element container it's a child index), and a find-highlighted
   * span holds several text nodes, making a whole-textContent slice wrong either way.
   * Range.toString() handles both cases and reads nested markup's text exactly once.
   */
  function clippedText(el: HTMLElement): string {
    const sub = win.document.createRange()
    sub.selectNodeContents(el)
    if (sub.compareBoundaryPoints(Range.START_TO_START, range) < 0) {
      sub.setStart(range.startContainer, range.startOffset)
    }
    if (sub.compareBoundaryPoints(Range.END_TO_END, range) > 0) {
      sub.setEnd(range.endContainer, range.endOffset)
    }
    return sub.toString()
  }

  /**
   * Same visual row ⇔ rendered tops within half a line height. Measured from
   * getBoundingClientRect, NOT style.top: PDF.js writes style.top as a PERCENTAGE of the
   * page (rows ~1.5% apart in a live probe) and often sets no inline font-size at all,
   * so no fixed threshold on the style values is unit-safe. Real pixels also make the
   * tolerance self-scaling: OCR word boxes wobble vertically by a pixel or two (exact
   * equality would fabricate a newline between every word), while a genuine line step is
   * at least the line's own height.
   * A change of PAGE is always a line break: viewport tops alone would merge the last
   * row of one page with the same-top first row of its neighbour in the viewer's
   * side-by-side spread modes.
   * Known limitation: 90°/270°-rotated pages lay words of one visual line at different
   * tops, so rotated selections degrade to one word per line rather than merging words.
   */
  function sameRow(rect: DOMRect, prevRect: DOMRect): boolean {
    const tolerance = Math.max(2, Math.min(rect.height, prevRect.height) / 2)
    return Math.abs(rect.top - prevRect.top) <= tolerance
  }

  let out = ''
  let prevRect: DOMRect | null = null
  let prevPage: Element | null = null
  for (const el of spans) {
    const piece = clippedText(el)
    if (!piece) continue
    const rect = el.getBoundingClientRect()
    const pageEl = el.closest('.page')
    if (out.length > 0 && prevRect !== null) {
      if (pageEl !== prevPage || !sameRow(rect, prevRect)) {
        out = out.trimEnd() + '\n'
      } else if (!/\s$/.test(out) && !/^\s/.test(piece)) {
        out += ' '
      }
    }
    out += piece
    prevRect = rect
    prevPage = pageEl
  }
  return out
}
